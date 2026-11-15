using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Notifications
{
    /// <summary>The SMS page: log, today's numbers, manual reminder, running the reminder round now.</summary>
    public class SmsDesk
    {
        public const int PageSize = 50;

        private readonly IAppDbContext _db;
        private readonly SmsService _sms;
        private readonly ReminderWorker _worker;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public SmsDesk(IAppDbContext db, SmsService sms, ReminderWorker worker, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _sms = sms;
            _worker = worker;
            _audit = audit;
            _user = user;
        }

        public static string StatusLabel(SmsStatus s) => s switch
        {
            SmsStatus.Sent => "Göndərildi",
            SmsStatus.Simulated => "Test rejimi",
            _ => "Xəta"
        };

        public record Page(List<SmsMessage> Rows, int Total, SmsOptions Options, int SentToday, int FailedToday, int Pending, int ConfirmedBySms, int CancelledBySms);

        public async Task<Page> PageAsync(SmsStatus? status, int page, CancellationToken ct = default)
        {
            var query = _db.SmsMessages.Include(m => m.Patient).AsQueryable();
            if (status != null) query = query.Where(m => m.Status == status);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(m => m.CreatedAt).Skip((Math.Max(1, page) - 1) * PageSize).Take(PageSize).ToListAsync(ct);

            var today = ClinicTime.Today;
            var now = ClinicTime.Now;
            var o = _sms.Options;
            return new Page(rows, total, o,
                await _db.SmsMessages.CountAsync(m => m.CreatedAt >= today && m.Status != SmsStatus.Failed, ct),
                await _db.SmsMessages.CountAsync(m => m.CreatedAt >= today && m.Status == SmsStatus.Failed, ct),
                await _db.Reserves.CountAsync(r => r.ReminderSentAt == null && r.AppointmentAt > now
                    && r.AppointmentAt <= now.AddHours(o.ReminderHoursBefore)
                    && (r.Status!.Name == StatusNames.Waiting || r.Status.Name == StatusNames.Confirmed), ct),
                await _db.AuditLogs.CountAsync(a => a.At >= today.AddDays(-30) && a.Action == "Xəstə SMS linki ilə təsdiq etdi", ct),
                await _db.AuditLogs.CountAsync(a => a.At >= today.AddDays(-30) && a.Action == "Xəstə SMS linki ilə ləğv etdi", ct));
        }

        /// <summary>Sends (or re-sends) the reminder for one future, active reservation. Null = not found.</summary>
        public async Task<Result?> SendReminderAsync(int reserveId, CancellationToken ct = default)
        {
            var res = await _db.Reserves.Include(r => r.Status).Include(r => r.Patient).Include(r => r.Doctor)
                .FirstOrDefaultAsync(r => r.Id == reserveId, ct);
            if (res == null) return null;
            if (res.Status?.Name is not (StatusNames.Waiting or StatusNames.Confirmed) || res.AppointmentAt <= ClinicTime.Now)
                return Result.Failure("Xatırlatma yalnız gələcək, aktiv rezervasiyaya göndərilir");

            var msg = await _sms.SendReminderAsync(res, SmsService.KindManual, _user.UserName);
            _audit.Log("SMS göndərildi", AuditEntities.Sms, msg.Id, $"{res.Patient.FullName}, {msg.Phone}: {StatusLabel(msg.Status)}");
            if (msg.Status == SmsStatus.Failed) return Result.Failure($"SMS göndərilmədi: {msg.Error}");
            return Result.Success(msg.Status == SmsStatus.Simulated
                ? "SMS test rejimində qeydə alındı (həqiqətən göndərilmədi — SMS jurnalına baxın)"
                : $"SMS {msg.Phone} nömrəsinə göndərildi");
        }

        /// <summary>Runs the reminder round now instead of waiting for the next cycle.</summary>
        public async Task<Result> RunNowAsync(CancellationToken ct = default)
        {
            var count = await _worker.RunOnceAsync(ct);
            _audit.Log("SMS xatırlatmaları əl ilə işə salındı", AuditEntities.Sms, null, $"{count} SMS");
            return Result.Success(count == 0 ? "Göndəriləcək xatırlatma yoxdur (və ya indi sakit saatlardır)" : $"{count} xatırlatma göndərildi");
        }
    }
}
