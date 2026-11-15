using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Catalog
{
    /// <summary>Həkimlər: profiles, weekly schedules, leaves (audited).</summary>
    public class Doctors
    {
        public static readonly DayOfWeek[] Week =
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        private readonly IAppDbContext _db;
        private readonly IUserDirectory _users;
        private readonly AuditService _audit;
        private readonly ClinicOptions _clinic;

        public Doctors(IAppDbContext db, IUserDirectory users, AuditService audit, IOptions<ClinicOptions> clinic)
        {
            _db = db;
            _users = users;
            _audit = audit;
            _clinic = clinic.Value;
        }

        public async Task<(List<Doctor> Doctors, Dictionary<int, int> TodayCounts)> ListAsync(CancellationToken ct = default)
        {
            var today = ClinicTime.Today;
            var doctors = await _db.Doctors
                .Include(d => d.Department).Include(d => d.Room).Include(d => d.WorkHours).Include(d => d.Leaves)
                .OrderByDescending(d => d.IsActive).ThenBy(d => d.FullName)
                .AsSplitQuery()
                .ToListAsync(ct);
            var counts = await _db.Reserves
                .Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1) && r.DoctorId != null)
                .Where(r => r.Status!.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow)
                .GroupBy(r => r.DoctorId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return (doctors, counts);
        }

        /// <summary>A new doctor's form: clinic default hours on weekdays.</summary>
        public DoctorFormViewModel NewForm() => new()
        {
            SlotMinutes = _clinic.SlotMinutes,
            Days = Week.Select(d => new WorkDayInput
            {
                Day = d,
                Works = d is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                Start = _clinic.WorkStart.ToString(@"hh\:mm"),
                End = _clinic.WorkEnd.ToString(@"hh\:mm"),
                BreakStart = "13:00",
                BreakEnd = "14:00"
            }).ToList()
        };

        public Task<Doctor?> DoctorAsync(int id, CancellationToken ct = default) =>
            _db.Doctors.Include(x => x.WorkHours).Include(x => x.Leaves).FirstOrDefaultAsync(x => x.Id == id, ct);

        public DoctorFormViewModel EditForm(Doctor d) => new()
        {
            Id = d.Id,
            FullName = d.FullName,
            Specialty = d.Specialty,
            Phone = d.Phone,
            DepartmentId = d.DepartmentId,
            RoomId = d.RoomId,
            SlotMinutes = d.SlotMinutes,
            IsActive = d.IsActive,
            UserId = d.UserId,
            Days = Week.Select(day =>
            {
                var w = d.WorkHours.FirstOrDefault(x => x.Day == day);
                return new WorkDayInput
                {
                    Day = day,
                    Works = w != null,
                    Start = (w?.Start ?? _clinic.WorkStart).ToString(@"hh\:mm"),
                    End = (w?.End ?? _clinic.WorkEnd).ToString(@"hh\:mm"),
                    BreakStart = w?.BreakStart?.ToString(@"hh\:mm"),
                    BreakEnd = w?.BreakEnd?.ToString(@"hh\:mm")
                };
            }).ToList()
        };

        public async Task<DoctorFormLists> FormListsAsync(Doctor? doctor, CancellationToken ct = default) => new()
        {
            Departments = await _db.Departments.Where(d => d.IsActive || (doctor != null && d.Id == doctor.DepartmentId)).OrderBy(d => d.Name).ToListAsync(ct),
            Rooms = await _db.Rooms.Where(r => r.IsActive || (doctor != null && r.Id == doctor.RoomId)).OrderBy(r => r.Name).ToListAsync(ct),
            Users = await _users.UsersAsync(ct),
            Leaves = doctor?.Leaves.Where(l => l.To >= ClinicTime.Today.AddDays(-30)).OrderBy(l => l.From).ToList() ?? new()
        };

        /// <summary>Saves the profile and the weekly schedule. Null = editing a doctor that doesn't exist.</summary>
        public async Task<Result?> SaveAsync(DoctorFormViewModel model, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var doctor = model.Id > 0 ? await DoctorAsync(model.Id, ct) : null;
            if (model.Id > 0 && doctor == null) return null;

            var r = new Result();
            foreach (var (f, m) in formErrors) r.Fail(m, f);
            // Parse and validate the weekly schedule.
            var hours = new List<DoctorWorkHours>();
            for (var i = 0; i < model.Days.Count; i++)
            {
                var d = model.Days[i];
                if (!d.Works) continue;
                var label = Fmt.DayName(d.Day);
                if (!TryTime(d.Start, out var start) || !TryTime(d.End, out var end) || end <= start)
                {
                    r.Fail($"{label}: iş saatı düzgün deyil", $"Days[{i}].Start");
                    continue;
                }
                TimeSpan? bs = null, be = null;
                if (!string.IsNullOrWhiteSpace(d.BreakStart) || !string.IsNullOrWhiteSpace(d.BreakEnd))
                {
                    if (!TryTime(d.BreakStart, out var b1) || !TryTime(d.BreakEnd, out var b2) || b2 <= b1 || b1 < start || b2 > end)
                    {
                        r.Fail($"{label}: fasilə iş saatının içində olmalıdır", $"Days[{i}].BreakStart");
                        continue;
                    }
                    bs = b1; be = b2;
                }
                hours.Add(new DoctorWorkHours { Day = d.Day, Start = start, End = end, BreakStart = bs, BreakEnd = be });
            }
            if (model.IsActive && !model.Days.Any(d => d.Works)) r.Fail("Ən azı bir iş günü seçin");
            if (!string.IsNullOrEmpty(model.UserId) && await _db.Doctors.AnyAsync(x => x.UserId == model.UserId && x.Id != model.Id, ct))
                r.Fail("Bu istifadəçi artıq başqa həkimə bağlıdır", nameof(model.UserId));
            if (!r.Ok) return r;

            var isNew = doctor == null;
            doctor ??= new Doctor();
            doctor.FullName = model.FullName!.Trim();
            doctor.Specialty = string.IsNullOrWhiteSpace(model.Specialty) ? null : model.Specialty.Trim();
            doctor.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
            doctor.DepartmentId = model.DepartmentId;
            doctor.RoomId = model.RoomId;
            doctor.SlotMinutes = model.SlotMinutes;
            doctor.IsActive = model.IsActive;
            doctor.UserId = string.IsNullOrEmpty(model.UserId) ? null : model.UserId;
            _db.DoctorWorkHours.RemoveRange(doctor.WorkHours);
            doctor.WorkHours = hours;
            if (isNew) _db.Doctors.Add(doctor);
            await _db.SaveChangesAsync(ct);

            _audit.Log(isNew ? "Həkim əlavə edildi" : "Həkim məlumatları dəyişdirildi", AuditEntities.Doctor, doctor.Id,
                $"{doctor.FullName}; iş günləri: {string.Join(", ", hours.Select(h => $"{Fmt.DayShortName(h.Day)} {h.Start:hh\\:mm}-{h.End:hh\\:mm}"))}");
            return Result.Success(isNew ? $"{doctor.FullName} əlavə edildi" : $"{doctor.FullName} yeniləndi", doctor.Id);
        }

        /// <summary>Adds a leave; warns (not refuses) when active bookings fall on it. Null = no such doctor.</summary>
        public async Task<Result?> AddLeaveAsync(LeaveInput input, bool formValid, CancellationToken ct = default)
        {
            var doctor = await _db.Doctors.FindAsync(new object[] { input.DoctorId }, ct);
            if (doctor == null) return null;
            if (!formValid || input.From > input.To) return Result.Failure("Məzuniyyət tarixləri düzgün deyil");

            var from = input.From!.Value.Date;
            var to = input.To!.Value.Date;
            _db.DoctorLeaves.Add(new DoctorLeave
            {
                DoctorId = doctor.Id, From = from, To = to,
                Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim()
            });
            await _db.SaveChangesAsync(ct);
            _audit.Log("Məzuniyyət əlavə edildi", AuditEntities.Doctor, doctor.Id, $"{doctor.FullName}: {Fmt.Date(from)} – {Fmt.Date(to)}");

            var clashes = await _db.Reserves.CountAsync(r => r.DoctorId == doctor.Id
                && r.AppointmentAt >= from && r.AppointmentAt < to.AddDays(1)
                && ReserveFlow.Active.Contains(r.Status!.Name), ct);
            var result = Result.Success("Məzuniyyət əlavə edildi", doctor.Id);
            if (clashes > 0) result.Warning = $"Diqqət: bu tarixlərdə həkimin {clashes} aktiv rezervasiyası var — onları başqa vaxta keçirin.";
            return result;
        }

        public async Task<Result?> DeleteLeaveAsync(int leaveId, CancellationToken ct = default)
        {
            var leave = await _db.DoctorLeaves.Include(l => l.Doctor).FirstOrDefaultAsync(l => l.Id == leaveId, ct);
            if (leave == null) return null;
            _db.DoctorLeaves.Remove(leave);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Məzuniyyət silindi", AuditEntities.Doctor, leave.DoctorId, $"{leave.Doctor.FullName}: {Fmt.Date(leave.From)} – {Fmt.Date(leave.To)}");
            return Result.Success("Məzuniyyət silindi", leave.DoctorId);
        }

        /// <summary>A deleted login can't stay linked to a doctor.</summary>
        public async Task UnlinkUserAsync(string userId, CancellationToken ct = default)
        {
            foreach (var d in await _db.Doctors.Where(d => d.UserId == userId).ToListAsync(ct)) d.UserId = null;
            await _db.SaveChangesAsync(ct);
        }

        private static bool TryTime(string? s, out TimeSpan t) =>
            TimeSpan.TryParseExact(s ?? "", @"hh\:mm", null, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);
    }
}
