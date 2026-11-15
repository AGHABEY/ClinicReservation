using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>
    /// Builds a doctor's bookable slots for a day from their weekly hours, breaks
    /// and leave. A slot is taken when the doctor OR their room is already busy.
    /// </summary>
    public class SlotService
    {
        private readonly IAppDbContext _context;

        public SlotService(IAppDbContext context)
        {
            _context = context;
        }

        /// <summary>What a booking of these services needs: total length and the device (if any).</summary>
        public record BookingShape(int DurationMinutes, int? EquipmentId, string? EquipmentName);

        /// <summary>Services' durations add up (none set = one doctor slot); the first device needed is booked.</summary>
        public static BookingShape Shape(IEnumerable<ExaminationType> exams)
        {
            var list = exams.ToList();
            var eq = list.FirstOrDefault(e => e.EquipmentId != null);
            return new BookingShape(list.Sum(e => e.DurationMinutes ?? 0), eq?.EquipmentId, eq?.Equipment?.Name);
        }

        public BookingShape ShapeOf(IEnumerable<int>? examTypeIds)
        {
            var ids = (examTypeIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            return ids.Count == 0 ? new BookingShape(0, null, null)
                : Shape(_context.ExaminationTypes.Include(e => e.Equipment).AsNoTracking().Where(e => ids.Contains(e.Id)).ToList());
        }

        /// <param name="durationMinutes">Length of the visit; 0 = one slot. Rounded up to whole slots.</param>
        /// <param name="equipmentId">Device the visit needs: times it is busy elsewhere are taken too.</param>
        public SlotResult GetSlots(int doctorId, DateTime date, int? excludeReserveId = null, int durationMinutes = 0, int? equipmentId = null)
        {
            var result = new SlotResult();
            var doctor = _context.Doctors
                .Include(d => d.WorkHours)
                .Include(d => d.Leaves)
                .AsNoTracking()
                .FirstOrDefault(d => d.Id == doctorId);

            if (doctor == null || !doctor.IsActive)
            {
                result.Message = "Həkim tapılmadı və ya aktiv deyil";
                return result;
            }

            var day = date.Date;
            var leave = doctor.Leaves.FirstOrDefault(l => l.From.Date <= day && l.To.Date >= day);
            if (leave != null)
            {
                result.Message = $"Həkim {Fmt.Date(leave.From)} – {Fmt.Date(leave.To)} tarixlərində işdə deyil"
                                 + (string.IsNullOrEmpty(leave.Reason) ? "" : $" ({leave.Reason})");
                return result;
            }

            var hours = doctor.WorkHours.FirstOrDefault(w => w.Day == day.DayOfWeek);
            if (hours == null)
            {
                result.Message = "Həkim bu gün işləmir";
                return result;
            }

            var next = day.AddDays(1);
            var busy = _context.Reserves
                .Where(r => r.AppointmentAt >= day && r.AppointmentAt < next)
                .Where(r => r.Status == null || (r.Status.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow))
                .Where(r => excludeReserveId == null || r.Id != excludeReserveId)
                .Where(r => r.DoctorId == doctorId || (doctor.RoomId != null && r.RoomId == doctor.RoomId)
                            || (equipmentId != null && r.EquipmentId == equipmentId))
                .Select(r => new { r.AppointmentAt, r.DurationMinutes })
                .ToList()
                .Select(r => (Start: r.AppointmentAt, End: r.AppointmentAt.AddMinutes(Math.Max(1, r.DurationMinutes))))
                .ToList();

            var step = TimeSpan.FromMinutes(Math.Clamp(doctor.SlotMinutes, 5, 240));
            // A longer visit takes several slots in a row (rounded up to the doctor's grid).
            var need = durationMinutes > step.TotalMinutes
                ? TimeSpan.FromMinutes(Math.Ceiling(durationMinutes / step.TotalMinutes) * step.TotalMinutes)
                : step;
            var now = ClinicTime.Now;
            for (var t = hours.Start; t + need <= hours.End; t += step)
            {
                var start = day + t;
                var end = start + need;

                if (hours.BreakStart.HasValue && hours.BreakEnd.HasValue
                    && t < hours.BreakEnd.Value && t + need > hours.BreakStart.Value)
                    continue;

                result.Slots.Add(new SlotViewModel
                {
                    Time = t.ToString(@"hh\:mm"),
                    Booked = busy.Any(b => b.Start < end && start < b.End) ? 1 : 0,
                    Capacity = 1,
                    Past = start <= now
                });
            }

            if (result.Slots.Count == 0)
                result.Message = "Bu gün üçün qəbul saatı yoxdur";
            else if (!result.Slots.Any(s => s.Available))
                result.Message = "Bu gün üçün boş saat qalmayıb — başqa gün seçin";

            result.SlotMinutes = (int)need.TotalMinutes;
            result.RoomId = doctor.RoomId;
            return result;
        }

        /// <summary>
        /// «Araya salmaq»: a busy time on the doctor's grid can still be booked, within the doctor's daily limit.
        /// The device can't be shared, and past times stay closed. Null = allowed (slots.Overbooked is set).
        /// </summary>
        public string? ValidateOverbook(int doctorId, DateTime date, string time, int? excludeReserveId, out DateTime start, out SlotResult slots,
            BookingShape? shape = null)
        {
            start = default;
            slots = GetSlots(doctorId, date, excludeReserveId, shape?.DurationMinutes ?? 0, null);
            if (!TimeSpan.TryParseExact(time, @"hh\:mm", null, out var tod)) return "Qəbul saatı düzgün deyil";
            start = date.Date + tod;
            var slot = slots.Slots.FirstOrDefault(s => s.Time == time);
            if (slot == null) return slots.Message ?? "Bu saat həkimin iş qrafikinə uyğun deyil";
            if (slot.Past) return "Keçmiş vaxta rezervasiya etmək olmaz";

            var doctor = _context.Doctors.AsNoTracking().First(d => d.Id == doctorId);
            var day = date.Date;
            var next = day.AddDays(1);
            var used = _context.Reserves.Count(r => r.DoctorId == doctorId && r.Overbooked && r.AppointmentAt >= day && r.AppointmentAt < next
                && (excludeReserveId == null || r.Id != excludeReserveId)
                && r.Status!.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow);
            if (doctor.MaxOverbookPerDay <= 0) return "Bu həkimə əlavə qəbul yazılmır (limit 0)";
            if (used >= doctor.MaxOverbookPerDay)
                return $"Həkimin bu gün üçün əlavə qəbul limiti dolub ({used}/{doctor.MaxOverbookPerDay})";

            if (shape?.EquipmentId != null)
            {
                var end = start.AddMinutes(slots.SlotMinutes);
                var s0 = start;
                var deviceBusy = _context.Reserves
                    .Where(r => r.EquipmentId == shape.EquipmentId && r.AppointmentAt >= day && r.AppointmentAt < next
                                && (excludeReserveId == null || r.Id != excludeReserveId)
                                && r.Status!.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow)
                    .Select(r => new { r.AppointmentAt, r.DurationMinutes }).ToList()
                    .Any(r => r.AppointmentAt < end && s0 < r.AppointmentAt.AddMinutes(Math.Max(1, r.DurationMinutes)));
                if (deviceBusy) return $"{shape.EquipmentName ?? "Avadanlıq"} bu saatda məşğuldur — avadanlığa əlavə qəbul olmur";
            }
            slots.Overbooked = !slot.Available;
            return null;
        }

        /// <summary>Returns an error message, or null when the slot can be booked.</summary>
        public string? Validate(int doctorId, DateTime date, string time, int? excludeReserveId, out DateTime start, out SlotResult slots,
            BookingShape? shape = null)
        {
            start = default;
            slots = GetSlots(doctorId, date, excludeReserveId, shape?.DurationMinutes ?? 0, shape?.EquipmentId);

            if (!TimeSpan.TryParseExact(time, @"hh\:mm", null, out var tod))
                return "Qəbul saatı düzgün deyil";

            start = date.Date + tod;
            var slot = slots.Slots.FirstOrDefault(s => s.Time == time);
            if (slot == null)
                return slots.Message ?? "Bu saat həkimin iş qrafikinə uyğun deyil";
            if (slot.Past)
                return "Keçmiş vaxta rezervasiya etmək olmaz";
            if (!slot.Available)
                return shape?.EquipmentId != null
                    ? $"Bu saat artıq doludur (həkim, kabinet və ya {shape.EquipmentName ?? "avadanlıq"} məşğuldur)"
                    : "Bu saat artıq doludur (həkim və ya kabinet məşğuldur)";
            return null;
        }
    }

    public class SlotResult
    {
        public List<SlotViewModel> Slots { get; } = new();
        public string? Message { get; set; }
        public int SlotMinutes { get; set; }
        public int? RoomId { get; set; }
        /// <summary>Set by ValidateOverbook when the chosen time was already taken.</summary>
        public bool Overbooked { get; set; }
    }
}
