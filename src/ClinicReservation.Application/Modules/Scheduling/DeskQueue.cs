using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Loyalty;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>Registratura / kassa queue: tickets at the kiosk or the door, called to a window.</summary>
    public class DeskQueue
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;
        private readonly SchedulingOptions _options;

        public DeskQueue(IAppDbContext db, AuditService audit, ICurrentUser user, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _audit = audit;
            _user = user;
            _options = options.Value;
        }

        public string Prefix(DeskKind kind) => (kind == DeskKind.Cashier ? _options.CashierPrefix : _options.ReceptionPrefix).Trim().ToUpperInvariant();

        /// <summary>Today's next ticket of this queue. <paramref name="by"/> = «Kiosk» or the staff member.</summary>
        public async Task<DeskTicket> IssueAsync(DeskKind kind, QueuePriority priority, string? by, CancellationToken ct = default)
        {
            var today = ClinicTime.Today;
            var last = await _db.DeskTickets.Where(t => t.Kind == kind && t.IssuedAt >= today).MaxAsync(t => (int?)t.Number, ct) ?? 0;
            var t = new DeskTicket
            {
                Kind = kind, Number = last + 1, Ticket = $"{Prefix(kind)}-{last + 1:000}", Priority = Enum.IsDefined(priority) ? priority : QueuePriority.None,
                IssuedAt = ClinicTime.Now, IssuedBy = by
            };
            _db.DeskTickets.Add(t);
            await _db.SaveChangesAsync(ct);
            return t;
        }

        /// <summary>
        /// «Növbəti»: the window's current ticket counts as served, and the next waiting one of its queue (urgent,
        /// then other priorities, then by number) is called to it. Null = no such window.
        /// </summary>
        public async Task<(Result Result, DeskTicket? Ticket)?> CallNextAsync(int windowId, CancellationToken ct = default)
        {
            var window = await _db.ServiceWindows.FirstOrDefaultAsync(w => w.Id == windowId && w.IsActive, ct);
            if (window == null) return null;
            var today = ClinicTime.Today;
            var now = ClinicTime.Now;
            foreach (var cur in await _db.DeskTickets.Where(t => t.WindowId == windowId && t.Status == DeskTicketStatus.Called).ToListAsync(ct))
            {
                cur.Status = DeskTicketStatus.Served;
                cur.ServedAt = now;
                cur.ServedBy = _user.UserName;
            }
            var next = await _db.DeskTickets
                .Where(t => t.Kind == window.Kind && t.Status == DeskTicketStatus.Waiting && t.IssuedAt >= today)
                .OrderBy(t => t.Priority == QueuePriority.Urgent ? 0 : t.Priority == QueuePriority.None ? 2 : 1).ThenBy(t => t.Number)
                .FirstOrDefaultAsync(ct);
            if (next != null)
            {
                next.Status = DeskTicketStatus.Called;
                next.WindowId = windowId;
                next.CalledAt = now;
            }
            await _db.SaveChangesAsync(ct);
            if (next == null) return (Result.Failure("Növbədə gözləyən yoxdur"), null);
            _audit.Log("Talon çağırıldı", AuditEntities.Queue, next.Id, $"{next.Ticket} → {window.Name}");
            return (Result.Success($"{next.Ticket} → {window.Name}"), next);
        }

        public async Task<Result?> RecallAsync(int ticketId, CancellationToken ct = default)
        {
            var t = await _db.DeskTickets.Include(x => x.Window).FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return null;
            if (t.Status != DeskTicketStatus.Called) return Result.Failure("Bu talon hazırda çağırılmayıb");
            t.CalledAt = ClinicTime.Now;
            await _db.SaveChangesAsync(ct);
            return Result.Success($"{t.Ticket} təkrar çağırıldı");
        }

        /// <summary>Served, or didn't come to the window (missed).</summary>
        public async Task<Result?> FinishAsync(int ticketId, bool missed, CancellationToken ct = default)
        {
            var t = await _db.DeskTickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return null;
            if (t.Status != DeskTicketStatus.Called) return Result.Failure("Bu talon hazırda çağırılmayıb");
            t.Status = missed ? DeskTicketStatus.Missed : DeskTicketStatus.Served;
            t.ServedAt = ClinicTime.Now;
            t.ServedBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
            return Result.Success(missed ? $"{t.Ticket} — gəlmədi" : $"{t.Ticket} — xidmət edildi");
        }
    }

    public class DeskView
    {
        public List<ServiceWindow> Windows { get; set; } = new();
        public ServiceWindow? Window { get; set; }
        public DeskTicket? Current { get; set; }
        public List<DeskTicket> Waiting { get; set; } = new();
        public int ServedToday { get; set; }
        public int? AverageWaitMinutes { get; set; }
    }

    public class DeskQueries
    {
        private readonly IAppDbContext _db;
        public DeskQueries(IAppDbContext db) => _db = db;

        public async Task<DeskView> WindowAsync(int? windowId, CancellationToken ct = default)
        {
            var v = new DeskView { Windows = await _db.ServiceWindows.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Kind).ThenBy(w => w.Name).ToListAsync(ct) };
            v.Window = v.Windows.FirstOrDefault(w => w.Id == windowId);
            if (v.Window == null) return v;
            var today = ClinicTime.Today;
            v.Current = await _db.DeskTickets.AsNoTracking().FirstOrDefaultAsync(t => t.WindowId == v.Window.Id && t.Status == DeskTicketStatus.Called, ct);
            v.Waiting = await _db.DeskTickets.AsNoTracking()
                .Where(t => t.Kind == v.Window.Kind && t.Status == DeskTicketStatus.Waiting && t.IssuedAt >= today)
                .OrderBy(t => t.Priority == QueuePriority.Urgent ? 0 : t.Priority == QueuePriority.None ? 2 : 1).ThenBy(t => t.Number)
                .Take(50).ToListAsync(ct);
            var served = await _db.DeskTickets.AsNoTracking()
                .Where(t => t.Kind == v.Window.Kind && t.Status == DeskTicketStatus.Served && t.IssuedAt >= today && t.CalledAt != null)
                .Select(t => new { t.IssuedAt, t.CalledAt }).ToListAsync(ct);
            v.ServedToday = served.Count;
            v.AverageWaitMinutes = served.Count == 0 ? null : (int)Math.Round(served.Average(x => (x.CalledAt!.Value - x.IssuedAt).TotalMinutes));
            return v;
        }

        public Task<DeskTicket?> TicketAsync(int id, CancellationToken ct = default) =>
            _db.DeskTickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

        public async Task<int> AheadAsync(DeskTicket t, CancellationToken ct = default) =>
            await _db.DeskTickets.CountAsync(x => x.Kind == t.Kind && x.Status == DeskTicketStatus.Waiting && x.IssuedAt >= ClinicTime.Today && x.Number < t.Number, ct);
    }

    /// <summary>A visit the kiosk found for the patient (name shortened on screen).</summary>
    public record KioskVisit(int ReserveId, DateTime At, string Doctor, string? Room, string PatientShort, bool CanCheckIn, string? Why, string? Ticket);

    /// <summary>
    /// Kiosk: the patient identifies with FİN, the loyalty card barcode or the QR of the SMS link, sees today's
    /// visits and checks in («Gəldim») — gets a ticket, without staff. Only within the visit's time window.
    /// </summary>
    public class Kiosk
    {
        public const string Actor = "Kiosk";
        private readonly IAppDbContext _db;
        private readonly QueueService _queue;
        private readonly LoyaltyQueries _loyalty;
        private readonly AuditService _audit;
        private readonly SchedulingOptions _options;

        public Kiosk(IAppDbContext db, QueueService queue, LoyaltyQueries loyalty, AuditService audit, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _queue = queue;
            _loyalty = loyalty;
            _audit = audit;
            _options = options.Value;
        }

        /// <summary>Patients behind what was typed or scanned (a family card has several).</summary>
        private async Task<List<int>> PatientsAsync(string? input, CancellationToken ct)
        {
            var s = (input ?? "").Trim();
            if (s.Length == 0 || s.Length > 300) return new();
            // SMS link QR: …/r/{token}
            var slash = s.LastIndexOf("/r/", StringComparison.OrdinalIgnoreCase);
            if (slash >= 0)
            {
                var token = s[(slash + 3)..].Split('/', '?', '#')[0];
                return await _db.Reserves.Where(r => r.PatientToken == token).Select(r => r.PatientId).Take(1).ToListAsync(ct);
            }
            if (await _loyalty.CardByNumberAsync(s, ct) is { Card.IsActive: true } card)
                return card.Card.Members.Select(m => m.PatientId).ToList();
            var pin = Patient.NormalizePin(s);
            return await _db.Patients.Where(p => p.PinCode == pin).Select(p => p.Id).ToListAsync(ct);
        }

        public async Task<List<KioskVisit>?> FindAsync(string? input, CancellationToken ct = default)
        {
            var ids = await PatientsAsync(input, ct);
            if (ids.Count == 0) return null;
            var now = ClinicTime.Now;
            var today = now.Date;
            var visits = await _db.Reserves.AsNoTracking().Include(r => r.Patient).Include(r => r.Status).Include(r => r.Doctor).Include(r => r.Room)
                .Where(r => ids.Contains(r.PatientId) && r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1)
                            && r.Status!.Name != StatusNames.Cancelled)
                .OrderBy(r => r.AppointmentAt).ToListAsync(ct);
            return visits.Select(r =>
            {
                var (ok, why) = Window(r, now);
                return new KioskVisit(r.Id, r.AppointmentAt, r.Doctor == null ? "—" : Fmt.Doctor(r.Doctor.FullName), r.Room?.Name,
                    $"{r.Patient.Name} {(r.Patient.Surname.Length > 0 ? r.Patient.Surname[0] + "." : "")}", ok, why, r.QueueTicket);
            }).ToList();
        }

        private (bool Ok, string? Why) Window(Reserve r, DateTime now)
        {
            if (r.Status?.Name is StatusNames.Arrived or StatusNames.InProgress) return (false, "Artıq qeydiyyatdan keçmisiniz");
            if (r.Status?.Name is not (StatusNames.Waiting or StatusNames.Confirmed)) return (false, StatusNames.Label(r.Status?.Name));
            if (now < r.AppointmentAt.AddMinutes(-_options.KioskEarlyMinutes)) return (false, $"Qeydiyyat qəbuldan {_options.KioskEarlyMinutes} dəq əvvəl açılır");
            if (now > r.AppointmentAt.AddMinutes(_options.KioskLateMinutes)) return (false, "Gecikmisiniz — registraturaya yaxınlaşın");
            return (true, null);
        }

        /// <summary>«Gəldim»: the visit must belong to whoever was identified (same input again). Returns the ticket.</summary>
        public async Task<(Result Result, Reserve? Reserve)> CheckInAsync(int reserveId, string? input, CancellationToken ct = default)
        {
            var ids = await PatientsAsync(input, ct);
            var res = await _db.Reserves.Include(r => r.Status).Include(r => r.Patient).FirstOrDefaultAsync(r => r.Id == reserveId, ct);
            if (res == null || !ids.Contains(res.PatientId)) return (Result.Failure("Qəbul tapılmadı"), null);
            var now = ClinicTime.Now;
            if (res.AppointmentAt.Date != now.Date) return (Result.Failure("Bu qəbul bu gün deyil"), null);
            var (ok, why) = Window(res, now);
            if (!ok) return (Result.Failure(why!), res.Status?.Name is StatusNames.Arrived ? res : null);

            res.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Arrived, ct);
            res.ArrivedAt = now;
            _queue.AssignTicket(res, now);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Status dəyişdi", AuditEntities.Reservation, res.Id, $"{res.Patient.FullName}: kioskda «gəldim», talon {res.QueueTicket}", userName: Actor);
            return (Result.Success($"Talonunuz: {res.QueueTicket}", res.Id), res);
        }
    }
}
