using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Services
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

        public SlotResult GetSlots(int doctorId, DateTime date, int? excludeReserveId = null)
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
                .Where(r => r.DoctorId == doctorId || (doctor.RoomId != null && r.RoomId == doctor.RoomId))
                .Select(r => new { r.AppointmentAt, r.DurationMinutes })
                .ToList()
                .Select(r => (Start: r.AppointmentAt, End: r.AppointmentAt.AddMinutes(Math.Max(1, r.DurationMinutes))))
                .ToList();

            var step = TimeSpan.FromMinutes(Math.Clamp(doctor.SlotMinutes, 5, 240));
            var now = ClinicTime.Now;
            for (var t = hours.Start; t + step <= hours.End; t += step)
            {
                var start = day + t;
                var end = start + step;

                if (hours.BreakStart.HasValue && hours.BreakEnd.HasValue
                    && t < hours.BreakEnd.Value && t + step > hours.BreakStart.Value)
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

            result.SlotMinutes = (int)step.TotalMinutes;
            result.RoomId = doctor.RoomId;
            return result;
        }

        /// <summary>Returns an error message, or null when the slot can be booked.</summary>
        public string? Validate(int doctorId, DateTime date, string time, int? excludeReserveId, out DateTime start, out SlotResult slots)
        {
            start = default;
            slots = GetSlots(doctorId, date, excludeReserveId);

            if (!TimeSpan.TryParseExact(time, @"hh\:mm", null, out var tod))
                return "Qəbul saatı düzgün deyil";

            start = date.Date + tod;
            var slot = slots.Slots.FirstOrDefault(s => s.Time == time);
            if (slot == null)
                return slots.Message ?? "Bu saat həkimin iş qrafikinə uyğun deyil";
            if (slot.Past)
                return "Keçmiş vaxta rezervasiya etmək olmaz";
            if (!slot.Available)
                return "Bu saat artıq doludur (həkim və ya kabinet məşğuldur)";
            return null;
        }
    }

    public class SlotResult
    {
        public List<SlotViewModel> Slots { get; } = new();
        public string? Message { get; set; }
        public int SlotMinutes { get; set; }
        public int? RoomId { get; set; }
    }
}
