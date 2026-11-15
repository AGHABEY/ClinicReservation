using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>Read side of appointments: the list, one reservation, the form's lists and slots.</summary>
    public class ReservationQueries
    {
        private readonly IAppDbContext _db;
        private readonly SlotService _slots;

        public ReservationQueries(IAppDbContext db, SlotService slots)
        {
            _db = db;
            _slots = slots;
        }

        /// <summary>The doctor profile linked to a login (for "my appointments").</summary>
        public Task<int?> DoctorIdForUserAsync(string? userId, CancellationToken ct = default) =>
            userId == null ? Task.FromResult<int?>(null)
                : _db.Doctors.Where(d => d.UserId == userId).Select(d => (int?)d.Id).FirstOrDefaultAsync(ct);

        public Task<List<Doctor>> AllDoctorsAsync(CancellationToken ct = default) =>
            _db.Doctors.OrderBy(d => d.FullName).ToListAsync(ct);

        // period: today | tomorrow | week | upcoming (default) | past | all
        public Task<List<Reserve>> ListAsync(string period, string? status, int? doctorId, string? q, CancellationToken ct = default)
        {
            var today = ClinicTime.Today;
            var query = _db.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.Room)
                .AsQueryable();

            query = period switch
            {
                "today" => query.Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1)),
                "tomorrow" => query.Where(r => r.AppointmentAt >= today.AddDays(1) && r.AppointmentAt < today.AddDays(2)),
                "week" => query.Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(7)),
                "past" => query.Where(r => r.AppointmentAt < today),
                "all" => query,
                _ => query.Where(r => r.AppointmentAt >= today)
            };
            if (!string.IsNullOrEmpty(status) && StatusNames.All.Contains(status))
                query = query.Where(r => r.Status!.Name == status);
            if (doctorId != null)
                query = query.Where(r => r.DoctorId == doctorId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(r =>
                    (r.Patient.Name + " " + r.Patient.Surname).ToLower().Contains(term) ||
                    r.Patient.PinCode.ToLower().Contains(term) ||
                    r.Patient.Phone.ToLower().Contains(term));
            }
            query = period is "past" or "all"
                ? query.OrderByDescending(r => r.AppointmentAt)
                : query.OrderBy(r => r.AppointmentAt);
            return query.Take(500).ToListAsync(ct);
        }

        /// <summary>A reservation with everything its pages show.</summary>
        public Task<Reserve?> ReserveAsync(int id, CancellationToken ct = default) =>
            _db.Reserves
                .Include(rs => rs.Status)
                .Include(rs => rs.Patient)
                .Include(rs => rs.Doctor).ThenInclude(d => d!.Department)
                .Include(rs => rs.Room)
                .Include(rs => rs.Payments)
                .Include(rs => rs.MedicalRecord)
                .Include(rs => rs.ReserveExaminationTypes!).ThenInclude(rs => rs.ExaminationType)
                .AsSplitQuery()
                .FirstOrDefaultAsync(rs => rs.Id == id, ct);

        public Task<Patient?> PatientByPinAsync(string? pin, CancellationToken ct = default)
        {
            var normalized = Patient.NormalizePin(pin);
            return string.IsNullOrEmpty(normalized) ? Task.FromResult<Patient?>(null)
                : _db.Patients.FirstOrDefaultAsync(p => p.PinCode == normalized, ct);
        }

        public async Task<Patient?> PatientAsync(int id, CancellationToken ct = default) =>
            await _db.Patients.FindAsync(new object[] { id }, ct);

        /// <summary>Lists and free slots for the form; also sets the form's payment/reschedule flags.</summary>
        public async Task<ReserveFormData> FormDataAsync(ReserveFormViewModel model, Reserve? existing, CancellationToken ct = default)
        {
            var data = new ReserveFormData
            {
                ExaminationTypes = await _db.ExaminationTypes.Include(e => e.Department)
                    .OrderBy(e => e.Department == null ? "" : e.Department.Name).ThenBy(e => e.Name).ToListAsync(ct),
                Doctors = await _db.Doctors.Include(d => d.Department)
                    .Where(d => d.IsActive || (existing != null && d.Id == existing.DoctorId))
                    .OrderBy(d => d.Department == null ? "" : d.Department.Name).ThenBy(d => d.FullName)
                    .ToListAsync(ct)
            };

            if (model.DoctorId != null && model.AppointmentDate.HasValue)
                data.Slots = _slots.GetSlots(model.DoctorId.Value, model.AppointmentDate.Value, existing?.Id);

            // Keep the reservation's own time selectable even if it is off the grid
            // (older reservations, or the doctor's hours changed since booking).
            if (existing != null && model.DoctorId == existing.DoctorId && model.AppointmentDate == existing.AppointmentAt.Date)
            {
                var own = existing.AppointmentAt.ToString("HH:mm");
                if (data.Slots.Slots.All(s => s.Time != own))
                {
                    data.Slots.Slots.Add(new SlotViewModel { Time = own, Capacity = 1 });
                    data.Slots.Slots.Sort((a, b) => string.CompareOrdinal(a.Time, b.Time));
                }
            }

            data.LockedPrices = existing?.ReserveExaminationTypes?.ToDictionary(x => x.ExaminationTypeId, x => x.Price) ?? new();
            model.HasPayments = existing?.Payments?.Any() == true;
            model.CanReschedule = existing == null || ReserveFlow.CanReschedule(existing);
            return data;
        }

        /// <summary>Free and taken times of a doctor's day (the form's slot picker).</summary>
        public SlotResult Slots(int doctorId, DateTime date, int? exclude) => _slots.GetSlots(doctorId, date, exclude);
    }
}
