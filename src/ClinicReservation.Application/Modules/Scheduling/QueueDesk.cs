using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>The doctor's desk and the waiting-hall screen: today's queue, calling patients in, tickets.</summary>
    public class QueueDesk
    {
        /// <summary>How long a call stays in the "called" column of the screen.</summary>
        private static readonly TimeSpan CallVisible = TimeSpan.FromMinutes(10);

        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public QueueDesk(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        /// <summary>The desk page. Null = the doctor doesn't exist.</summary>
        public async Task<WorkspaceViewModel?> DeskAsync(int? doctorId, int? myDoctorId, CancellationToken ct = default)
        {
            doctorId ??= myDoctorId;
            var doctors = await _db.Doctors.Where(d => d.IsActive || d.Id == doctorId).OrderBy(d => d.FullName).ToListAsync(ct);
            if (doctorId == null && doctors.Count == 1) doctorId = doctors[0].Id;

            var model = new WorkspaceViewModel { Doctors = doctors, IsOwnDesk = doctorId != null && doctorId == myDoctorId };
            if (doctorId == null) return model;

            model.Doctor = await _db.Doctors.Include(d => d.Room).Include(d => d.Department).FirstOrDefaultAsync(d => d.Id == doctorId, ct);
            if (model.Doctor == null) return null;
            var today = ClinicTime.Today;
            model.Today = await _db.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Room)
                .Include(r => r.MedicalRecord)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .Where(r => r.DoctorId == doctorId && r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1))
                .Where(r => r.Status!.Name != StatusNames.Cancelled)
                .OrderBy(r => r.AppointmentAt)
                .AsSplitQuery()
                .ToListAsync(ct);
            return model;
        }

        /// <summary>Calls a patient in: shows the ticket and room on the hall screen. Null = not found.</summary>
        public async Task<(Result Result, int? DoctorId)?> CallAsync(int reserveId, CancellationToken ct = default)
        {
            var res = await _db.Reserves.Include(r => r.Status).Include(r => r.Patient).FirstOrDefaultAsync(r => r.Id == reserveId, ct);
            if (res == null) return null;
            if (res.Status?.Name is not (StatusNames.Arrived or StatusNames.InProgress))
                return (Result.Failure("Yalnız gəlmiş xəstəni çağırmaq olar"), res.DoctorId);

            var again = res.CalledAt != null;
            res.CalledAt = ClinicTime.Now;
            await _db.SaveChangesAsync(ct);
            _audit.Log(again ? "Xəstə təkrar çağırıldı" : "Xəstə çağırıldı", AuditEntities.Reservation, res.Id,
                $"{res.Patient.FullName}, talon {res.QueueTicket ?? "—"}");
            return (Result.Success($"{res.QueueTicket ?? res.Patient.FullName} çağırıldı — ekranda göstərilir"), res.DoctorId);
        }

        /// <summary>Calls the first patient in this doctor's queue who hasn't been called yet.</summary>
        public async Task<(Result Result, int? DoctorId)> CallNextAsync(int doctorId, CancellationToken ct = default)
        {
            var next = await QueueService.WaitingFor(_db.Reserves.Where(r => r.DoctorId == doctorId), ClinicTime.Today)
                .Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
            if (next == null) return (Result.Failure("Növbədə gözləyən xəstə yoxdur"), doctorId);
            return (await CallAsync(next.Value, ct))!.Value;
        }

        public Task<List<Department>> ActiveDepartmentsAsync(CancellationToken ct = default) =>
            _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct);

        public async Task<string?> DepartmentNameAsync(int id, CancellationToken ct = default) =>
            (await _db.Departments.FindAsync(new object[] { id }, ct))?.Name;

        /// <summary>What the hall screen shows: recent calls and the waiting tickets — no patient names.</summary>
        public async Task<object> ScreenDataAsync(int? departmentId, CancellationToken ct = default)
        {
            var now = ClinicTime.Now;
            var today = now.Date;
            var query = _db.Reserves.Where(r => r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1) && r.QueueTicket != null);
            if (departmentId != null)
                query = query.Where(r => r.Doctor != null && r.Doctor.DepartmentId == departmentId);

            var since = now - CallVisible;
            var called = await query
                .Where(r => r.CalledAt != null && r.CalledAt >= since
                            && (r.Status!.Name == StatusNames.Arrived || r.Status.Name == StatusNames.InProgress))
                .OrderByDescending(r => r.CalledAt)
                .Take(6)
                .Select(r => new
                {
                    id = r.Id,
                    ticket = r.QueueTicket,
                    room = r.Room != null ? r.Room.Name : (r.Doctor != null && r.Doctor.Room != null ? r.Doctor.Room.Name : null),
                    doctor = r.Doctor != null ? r.Doctor.FullName : null,
                    calledAt = r.CalledAt
                })
                .ToListAsync(ct);

            var waiting = await query
                .Where(r => r.Status!.Name == StatusNames.Arrived && r.CalledAt == null)
                .OrderBy(r => r.Priority == QueuePriority.Urgent ? 0 : r.Priority == QueuePriority.None ? 2 : 1).ThenBy(r => r.ArrivedAt)
                .Take(30)
                .Select(r => new { ticket = r.QueueTicket, doctor = r.Doctor != null ? r.Doctor.FullName : null, since = r.ArrivedAt, r.Priority })
                .ToListAsync(ct);

            // Reception / cashier windows (whole clinic; shown on every screen).
            var deskCalls = await _db.DeskTickets
                .Where(t => t.Status == DeskTicketStatus.Called && t.CalledAt >= since && t.IssuedAt >= today)
                .OrderByDescending(t => t.CalledAt).Take(4)
                .Select(t => new { t.Id, t.Ticket, window = t.Window != null ? t.Window.Name : "", t.CalledAt })
                .ToListAsync(ct);

            return new
            {
                now = now.ToString("HH:mm"),
                called = called.Select(c => new { c.id, c.ticket, c.room, doctor = c.doctor == null ? null : Fmt.Doctor(c.doctor), key = $"{c.id}:{c.calledAt:HHmmss}" })
                    .Concat(deskCalls.Select(d => new { id = -d.Id, ticket = (string?)d.Ticket, room = (string?)d.window, doctor = (string?)null, key = $"d{d.Id}:{d.CalledAt:HHmmss}" }))
                    .ToList(),
                waiting = waiting.Select(w => new { w.ticket, w.doctor, minutes = w.since == null ? 0 : (int)(now - w.since.Value).TotalMinutes,
                    priority = w.Priority == QueuePriority.None ? null : QueueLabels.Priority(w.Priority) })
            };
        }

        /// <summary>The printed ticket and how many are ahead in this doctor's queue.</summary>
        public async Task<(Reserve Reserve, int Ahead)?> TicketAsync(int reserveId, CancellationToken ct = default)
        {
            var res = await _db.Reserves
                .Include(r => r.Patient)
                .Include(r => r.Doctor).ThenInclude(d => d!.Room)
                .Include(r => r.Room)
                .FirstOrDefaultAsync(r => r.Id == reserveId, ct);
            if (res == null) return null;
            var today = ClinicTime.Today;
            var ahead = await _db.Reserves.CountAsync(r => r.DoctorId == res.DoctorId && r.Id != res.Id
                && r.Status!.Name == StatusNames.Arrived && r.ArrivedAt < res.ArrivedAt
                && r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1), ct);
            return (res, ahead);
        }
    }

    public class WorkspaceViewModel
    {
        public List<Doctor> Doctors { get; set; } = new();
        public Doctor? Doctor { get; set; }
        public bool IsOwnDesk { get; set; }
        public List<Reserve> Today { get; set; } = new();

        public Reserve? Current => Today.Where(r => r.Status?.Name == StatusNames.InProgress).OrderBy(r => r.StartedAt).FirstOrDefault();

        public List<Reserve> Queue => Today.Where(r => r.Status?.Name == StatusNames.Arrived)
            .OrderBy(r => r.CalledAt != null).ThenBy(r => QueueLabels.Rank(r.Priority)).ThenBy(r => r.QueueNumber ?? int.MaxValue).ThenBy(r => r.ArrivedAt).ToList();

        public List<Reserve> Upcoming => Today.Where(r => r.Status?.Name is StatusNames.Waiting or StatusNames.Confirmed).ToList();

        public List<Reserve> Done => Today.Where(r => r.Status?.Name is StatusNames.Completed or StatusNames.NoShow)
            .OrderByDescending(r => r.CompletedAt ?? r.AppointmentAt).ToList();

        /// <summary>Average minutes between check-in and the start of the visit, today.</summary>
        public int? AverageWaitMinutes
        {
            get
            {
                var waits = Today.Where(r => r.ArrivedAt != null && r.StartedAt != null)
                    .Select(r => (r.StartedAt!.Value - r.ArrivedAt!.Value).TotalMinutes).ToList();
                return waits.Count == 0 ? null : (int)Math.Round(waits.Average());
            }
        }
    }
}
