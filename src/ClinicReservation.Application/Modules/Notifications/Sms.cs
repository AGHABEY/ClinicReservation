using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Notifications
{
    public record SmsSendResult(SmsStatus Status, string? Error = null);

    /// <summary>Delivers one SMS. Pick the implementation with Sms:Provider.</summary>
    public interface ISmsSender
    {
        Task<SmsSendResult> SendAsync(string phone, string text, CancellationToken ct = default);
    }

    /// <summary>Test mode: nothing is sent, the message only appears in the SMS log.</summary>
    public class LogSmsSender : ISmsSender
    {
        private readonly ILogger<LogSmsSender> _logger;
        public LogSmsSender(ILogger<LogSmsSender> logger) => _logger = logger;

        public Task<SmsSendResult> SendAsync(string phone, string text, CancellationToken ct = default)
        {
            _logger.LogInformation("SMS (test rejimi) → {Phone}: {Text}", phone, text);
            return Task.FromResult(new SmsSendResult(SmsStatus.Simulated));
        }
    }

    /// <summary>Generic HTTP gateway (GET Url with {phone} and {text}).</summary>
    public class HttpSmsSender : ISmsSender
    {
        private readonly HttpClient _http;
        private readonly SmsOptions _options;

        public HttpSmsSender(HttpClient http, IOptions<SmsOptions> options)
        {
            _http = http;
            _options = options.Value;
        }

        public async Task<SmsSendResult> SendAsync(string phone, string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_options.Http.Url))
                return new SmsSendResult(SmsStatus.Failed, "Sms:Http:Url təyin edilməyib");
            var url = _options.Http.Url
                .Replace("{phone}", Uri.EscapeDataString(phone))
                .Replace("{text}", Uri.EscapeDataString(text));
            try
            {
                using var response = await _http.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    return new SmsSendResult(SmsStatus.Failed, $"HTTP {(int)response.StatusCode}: {Trim(body)}");
                if (!string.IsNullOrEmpty(_options.Http.SuccessContains) && !body.Contains(_options.Http.SuccessContains))
                    return new SmsSendResult(SmsStatus.Failed, $"Provayder cavabı: {Trim(body)}");
                return new SmsSendResult(SmsStatus.Sent);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new SmsSendResult(SmsStatus.Failed, Trim(ex.Message));
            }
        }

        private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
    }

    /// <summary>Builds and sends reservation SMS and writes them to the SMS log.</summary>
    public class SmsService
    {
        public const string KindReminder = "Xatırlatma";
        public const string KindManual = "Əl ilə";
        public const string KindLabResult = "Analiz nəticəsi";
        public const string KindWaitlist = "Gözləmə siyahısı";
        public const string KindFollowUp = "Kontrol qəbulu";
        public const string KindOtp = "Təsdiq kodu";
        public const string KindRating = "Qiymətləndirmə";

        private readonly IAppDbContext _context;
        private readonly ISmsSender _sender;
        private readonly SmsOptions _options;

        public SmsService(IAppDbContext context, ISmsSender sender, IOptions<SmsOptions> options)
        {
            _context = context;
            _sender = sender;
            _options = options.Value;
        }

        public SmsOptions Options => _options;

        public string ConfirmLink(Reserve reserve) =>
            $"{_options.PublicBaseUrl.TrimEnd('/')}/r/{EnsureToken(reserve)}";

        public string ResultsLink(Reserve reserve) => ConfirmLink(reserve) + "/lab";
        public string RatingLink(Reserve reserve) => ConfirmLink(reserve) + "/rate";

        public string OtpText(string code)
        {
            var text = _options.OtpTemplate.Replace("{kod}", code).Replace("{klinika}", _options.ClinicName);
            return _options.AsciiOnly ? SmsText.ToAscii(text) : text;
        }

        /// <summary>Null when rating requests are switched off (empty template). Reserve with Patient.</summary>
        public string? RatingText(Reserve reserve)
        {
            if (string.IsNullOrWhiteSpace(_options.RatingTemplate)) return null;
            var text = _options.RatingTemplate.Replace("{ad}", reserve.Patient.Name).Replace("{klinika}", _options.ClinicName).Replace("{link}", RatingLink(reserve));
            return _options.AsciiOnly ? SmsText.ToAscii(text) : text;
        }

        /// <summary>A text to a phone number with no card yet (the online booking code). Not linked to a patient.</summary>
        public async Task<SmsMessage> SendToPhoneAsync(string phone, string text, string kind, CancellationToken ct = default)
        {
            var message = new SmsMessage { CreatedAt = ClinicTime.Now, Kind = kind, Text = text, SentBy = "Onlayn yazılış" };
            var normalized = SmsText.NormalizePhone(phone);
            if (normalized == null)
            {
                message.Phone = phone;
                message.Status = SmsStatus.Failed;
                message.Error = "Telefon nömrəsi düzgün deyil";
            }
            else
            {
                message.Phone = normalized;
                var result = await _sender.SendAsync(normalized, text, ct);
                message.Status = result.Status;
                message.Error = result.Error;
            }
            // The code itself is never stored.
            if (kind == KindOtp) message.Text = "•••• (təsdiq kodu)";
            _context.SmsMessages.Add(message);
            await _context.SaveChangesAsync(ct);
            return message;
        }

        /// <summary>The reservation must be loaded with Patient.</summary>
        public string LabResultText(Reserve reserve)
        {
            var text = _options.LabResultTemplate
                .Replace("{ad}", reserve.Patient.Name)
                .Replace("{klinika}", _options.ClinicName)
                .Replace("{link}", ResultsLink(reserve));
            return _options.AsciiOnly ? SmsText.ToAscii(text) : text;
        }

        public string WaitlistText(string patientName, DateTime slot, string doctorName)
        {
            var text = _options.WaitlistTemplate
                .Replace("{ad}", patientName)
                .Replace("{tarix}", slot.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
                .Replace("{saat}", slot.ToString("HH:mm", CultureInfo.InvariantCulture))
                .Replace("{hekim}", Fmt.Doctor(doctorName))
                .Replace("{klinika}", _options.ClinicName);
            text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return _options.AsciiOnly ? SmsText.ToAscii(text) : text;
        }

        public string FollowUpText(string patientName, DateTime due, string doctorName)
        {
            var text = _options.FollowUpTemplate
                .Replace("{ad}", patientName)
                .Replace("{tarix}", due.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
                .Replace("{hekim}", string.IsNullOrEmpty(doctorName) ? "Hekiminiz" : Fmt.Doctor(doctorName))
                .Replace("{klinika}", _options.ClinicName);
            text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return _options.AsciiOnly ? SmsText.ToAscii(text) : text;
        }

        /// <summary>The reservation must be loaded with Patient and Doctor.</summary>
        public string ReminderText(Reserve reserve)
        {
            var text = _options.ReminderTemplate
                .Replace("{ad}", reserve.Patient.Name)
                .Replace("{tarix}", reserve.AppointmentAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
                .Replace("{saat}", reserve.AppointmentAt.ToString("HH:mm", CultureInfo.InvariantCulture))
                .Replace("{hekim}", reserve.Doctor == null ? "" : Fmt.Doctor(reserve.Doctor.FullName))
                .Replace("{klinika}", _options.ClinicName)
                .Replace("{link}", ConfirmLink(reserve));
            text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return _options.AsciiOnly ? SmsText.ToAscii(text) : text;
        }

        public Task<SmsMessage> SendReminderAsync(Reserve reserve, string kind, string? sentBy, CancellationToken ct = default) =>
            SendAsync(reserve, ReminderText(reserve), kind, sentBy, ct);

        /// <summary>Sends one SMS to the reservation's patient and writes it to the SMS log.</summary>
        /// <summary>A message to a patient that isn't about one reservation (e.g. a waitlist offer).</summary>
        public async Task<SmsMessage?> SendToPatientAsync(int patientId, string text, string kind, string? sentBy, CancellationToken ct = default)
        {
            var patient = await _context.Patients.FindAsync(new object[] { patientId }, ct);
            if (patient == null) return null;
            var message = new SmsMessage { CreatedAt = ClinicTime.Now, PatientId = patient.Id, Kind = kind, SentBy = sentBy, Text = text };
            var phone = SmsText.NormalizePhone(patient.Phone);
            if (phone == null)
            {
                message.Phone = patient.Phone ?? "";
                message.Status = SmsStatus.Failed;
                message.Error = "Telefon nömrəsi düzgün deyil";
            }
            else
            {
                message.Phone = phone;
                var result = await _sender.SendAsync(phone, text, ct);
                message.Status = result.Status;
                message.Error = result.Error;
            }
            _context.SmsMessages.Add(message);
            await _context.SaveChangesAsync(ct);
            return message;
        }

        public async Task<SmsMessage> SendAsync(Reserve reserve, string text, string kind, string? sentBy, CancellationToken ct = default)
        {
            var message = new SmsMessage
            {
                CreatedAt = ClinicTime.Now,
                ReserveId = reserve.Id,
                PatientId = reserve.PatientId,
                Kind = kind,
                SentBy = sentBy,
                Text = text
            };

            var phone = SmsText.NormalizePhone(reserve.Patient.Phone);
            if (phone == null)
            {
                message.Phone = reserve.Patient.Phone ?? "";
                message.Status = SmsStatus.Failed;
                message.Error = "Telefon nömrəsi düzgün deyil";
            }
            else
            {
                message.Phone = phone;
                var result = await _sender.SendAsync(phone, message.Text, ct);
                message.Status = result.Status;
                message.Error = result.Error;
            }

            if (message.Status != SmsStatus.Failed && kind is KindReminder or KindManual)
                reserve.ReminderSentAt = message.CreatedAt;
            _context.SmsMessages.Add(message);
            await _context.SaveChangesAsync(ct);
            return message;
        }

        public static string EnsureToken(Reserve reserve)
        {
            reserve.PatientToken ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .Replace('+', '-').Replace('/', '_');
            return reserve.PatientToken;
        }
    }

    public static class SmsText
    {
        private static readonly Dictionary<char, string> Map = new()
        {
            ['ə'] = "e", ['Ə'] = "E", ['ı'] = "i", ['İ'] = "I", ['ş'] = "s", ['Ş'] = "S",
            ['ç'] = "c", ['Ç'] = "C", ['ğ'] = "g", ['Ğ'] = "G", ['ö'] = "o", ['Ö'] = "O",
            ['ü'] = "u", ['Ü'] = "U", ['—'] = "-", ['–'] = "-", ['«'] = "\"", ['»'] = "\"", ['₼'] = "AZN"
        };

        public static string ToAscii(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
                sb.Append(Map.TryGetValue(ch, out var r) ? r : ch.ToString());
            return sb.ToString();
        }

        /// <summary>"050 123 45 67" / "+994 50 123 45 67" → "994501234567"; null if it isn't a mobile number.</summary>
        public static string? NormalizePhone(string? phone)
        {
            var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
            if (digits.StartsWith("994") && digits.Length == 12) return digits;
            if (digits.StartsWith("0") && digits.Length == 10) return "994" + digits[1..];
            if (digits.Length == 9) return "994" + digits;
            return null;
        }

        /// <summary>Number of SMS parts the text takes (160/153 Latin, 70/67 Unicode).</summary>
        public static int Segments(string text)
        {
            var unicode = text.Any(c => c > 127);
            var single = unicode ? 70 : 160;
            var multi = unicode ? 67 : 153;
            return text.Length <= single ? 1 : (int)Math.Ceiling(text.Length / (double)multi);
        }
    }

    /// <summary>Every few minutes: sends reminders for visits in the next N hours.</summary>
    public class ReminderWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly SmsOptions _options;
        private readonly ILogger<ReminderWorker> _logger;

        public ReminderWorker(IServiceProvider services, IOptions<SmsOptions> options, ILogger<ReminderWorker> logger)
        {
            _services = services;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.RemindersEnabled)
                return;

            // Let migrations/seed finish first.
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            var interval = TimeSpan.FromMinutes(Math.Max(1, _options.CheckIntervalMinutes));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "SMS xatırlatma işi xəta verdi");
                }
                await Task.Delay(interval, stoppingToken);
            }
        }

        public async Task<int> RunOnceAsync(CancellationToken ct)
        {
            var now = ClinicTime.Now;
            if (InQuietHours(now.TimeOfDay))
                return 0;

            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var sent = 0;
            // With several app instances only one sends a round; the others skip it (no double SMS).
            var ran = await scope.ServiceProvider.GetRequiredService<IDistributedLock>().TryRunAsync(LockKeys.SmsReminders,
                async () => sent = await SendDueAsync(scope.ServiceProvider, context, now, ct), ct);
            if (!ran)
                _logger.LogDebug("SMS xatırlatma: başqa nüsxə artıq işləyir, bu dövrə buraxıldı");
            return sent;
        }

        private async Task<int> SendDueAsync(IServiceProvider sp, IAppDbContext context, DateTime now, CancellationToken ct)
        {
            var sms = sp.GetRequiredService<SmsService>();

            var until = now.AddHours(_options.ReminderHoursBefore);
            var minLead = _options.MinLeadHours;
            var due = await context.Reserves
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Where(r => r.ReminderSentAt == null && r.AppointmentAt > now && r.AppointmentAt <= until)
                .Where(r => r.Status!.Name == StatusNames.Waiting || r.Status.Name == StatusNames.Confirmed)
                .Where(r => r.Patient.Phone != "")
                // Booked at the last minute — a reminder makes no sense.
                .Where(r => r.AddedDate == null || r.AddedDate <= r.AppointmentAt.AddHours(-minLead))
                // A number that already failed for this reservation isn't retried automatically.
                .Where(r => !context.SmsMessages.Any(m => m.ReserveId == r.Id && m.Kind == SmsService.KindReminder && m.Status == SmsStatus.Failed))
                .OrderBy(r => r.AppointmentAt)
                .Take(200)
                .ToListAsync(ct);

            var sent = 0;
            foreach (var reserve in due)
            {
                await sms.SendReminderAsync(reserve, SmsService.KindReminder, null, ct);
                sent++;
            }

            if (sent > 0)
                _logger.LogInformation("{Count} SMS xatırlatma göndərildi", sent);
            return sent;
        }

        private bool InQuietHours(TimeSpan time)
        {
            if (!TimeSpan.TryParse(_options.QuietStart, CultureInfo.InvariantCulture, out var start) ||
                !TimeSpan.TryParse(_options.QuietEnd, CultureInfo.InvariantCulture, out var end) || start == end)
                return false;
            return start < end ? time >= start && time < end : time >= start || time < end;
        }
    }
}
