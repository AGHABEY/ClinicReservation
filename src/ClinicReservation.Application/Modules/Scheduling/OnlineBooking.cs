using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Notifications;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Scheduling
{
    public class OnlineBookingForm
    {
        public int DoctorId { get; set; }
        public List<int>? ExamIds { get; set; }
        public DateTime? Date { get; set; }
        public string? Time { get; set; }

        [Required(ErrorMessage = "FİN kodu daxil edin")]
        [RegularExpression(@"^\s*[A-Za-z0-9]{7}\s*$", ErrorMessage = "FİN kod 7 simvol olmalıdır")]
        public string? PinCode { get; set; }
        [StringLength(50)] public string? Name { get; set; }
        [StringLength(50)] public string? Surname { get; set; }
        [Required(ErrorMessage = "Telefon nömrəsini yazın")]
        [RegularExpression(@"^\+?[0-9\s\-()]{9,20}$", ErrorMessage = "Telefon nömrəsi düzgün deyil (məs: 050 123 45 67)")]
        public string? Phone { get; set; }
        public DateTime? Birthday { get; set; }
        /// <summary>Bot trap: humans never see or fill it.</summary>
        public string? Website { get; set; }
    }

    public record OnlineDoctor(Doctor Doctor, List<ExaminationType> Exams);

    /// <summary>
    /// Online yazılış: the patient picks a doctor, services and a free time, gets a 6-digit SMS code and confirms.
    /// Protection: code stored hashed, short-lived, few tries, few codes per phone; known FİN needs its card's phone;
    /// active online visits per patient, the booking horizon and lead time are limited; frequent no-shows must call.
    /// </summary>
    public class OnlineBooking
    {
        private readonly IAppDbContext _db;
        private readonly SlotService _slots;
        private readonly SmsService _sms;
        private readonly NoShows _noShows;
        private readonly PriceQueries _prices;
        private readonly AuditService _audit;
        private readonly SchedulingOptions _options;
        public const string Actor = "Onlayn yazılış";

        public OnlineBooking(IAppDbContext db, SlotService slots, SmsService sms, NoShows noShows, PriceQueries prices, AuditService audit, IOptions<SchedulingOptions> options)
        {
            _db = db;
            _slots = slots;
            _sms = sms;
            _noShows = noShows;
            _prices = prices;
            _audit = audit;
            _options = options.Value;
        }

        public SchedulingOptions Options => _options;

        /// <summary>Doctors open for online booking, with the services they can be booked for (their department's or general ones).</summary>
        public async Task<List<OnlineDoctor>> DoctorsAsync(CancellationToken ct = default)
        {
            var doctors = await _db.Doctors.AsNoTracking().Include(d => d.Department)
                .Where(d => d.IsActive && d.OnlineBooking && d.WorkHours.Any()).OrderBy(d => d.Department!.Name).ThenBy(d => d.FullName).ToListAsync(ct);
            var exams = await _db.ExaminationTypes.AsNoTracking().Where(e => e.OnlineBookable).OrderBy(e => e.Name).ToListAsync(ct);
            return doctors.Select(d => new OnlineDoctor(d, exams.Where(e => e.DepartmentId == null || e.DepartmentId == d.DepartmentId).ToList()))
                .Where(x => x.Exams.Count > 0).ToList();
        }

        /// <summary>Free times a patient may pick: within the horizon, not sooner than the lead time.</summary>
        public SlotResult Slots(int doctorId, DateTime date, IReadOnlyCollection<int> examIds)
        {
            var shape = _slots.ShapeOf(examIds);
            var r = _slots.GetSlots(doctorId, date.Date, null, shape.DurationMinutes, shape.EquipmentId);
            var (first, last) = Window();
            var open = new SlotResult { Message = r.Message, SlotMinutes = r.SlotMinutes, RoomId = r.RoomId };
            foreach (var s in r.Slots)
            {
                var at = date.Date + TimeSpan.Parse(s.Time);
                if (s.Available && at >= first && at.Date <= last) open.Slots.Add(s);
            }
            if (open.Slots.Count == 0 && r.Slots.Count > 0) open.Message = "Bu gün üçün onlayn boş vaxt yoxdur — başqa gün seçin";
            return open;
        }

        private (DateTime First, DateTime LastDay) Window() =>
            (ClinicTime.Now.AddMinutes(_options.OnlineMinLeadMinutes), ClinicTime.Today.AddDays(_options.OnlineMaxDaysAhead));

        /// <summary>Checks everything and texts the code. Returns the key of the code page.</summary>
        public async Task<(Result Result, Guid? Key)> StartAsync(OnlineBookingForm f, IEnumerable<(string? Field, string Message)> formErrors, string? ip, CancellationToken ct = default)
        {
            var r = new Result();
            if (!_options.OnlineEnabled) return (r.Fail("Onlayn yazılış hazırda bağlıdır"), null);
            if (!string.IsNullOrEmpty(f.Website)) return (r.Fail("Sorğu qəbul edilmədi"), null);
            foreach (var (field, m) in formErrors) r.Fail(m, field);
            var (patient, check) = await CheckAsync(f, ct);
            foreach (var e in check.Errors) r.Fail(e.Message, e.Field);
            if (!r.Ok) return (r, null);

            var phone = SmsText.NormalizePhone(patient?.Phone ?? f.Phone)!;
            var now = ClinicTime.Now;
            var hourAgo = now.AddHours(-1);
            var recent = await _db.OtpCodes.Where(o => o.Phone == phone && o.CreatedAt >= hourAgo).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
            if (recent.Count >= _options.OtpPerPhonePerHour) return (r.Fail("Bu nömrəyə çox kod göndərilib. Bir saat sonra yenidən cəhd edin və ya zəng edin."), null);
            if (recent.Count > 0 && (now - recent[0].CreatedAt).TotalSeconds < _options.OtpResendSeconds)
                return (r.Fail($"Yeni kod üçün {_options.OtpResendSeconds} saniyə gözləyin"), null);

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var otp = new OtpCode
            {
                Phone = phone, CreatedAt = now, ExpiresAt = now.AddMinutes(_options.OtpMinutes), Ip = ip,
                Payload = JsonSerializer.Serialize(f)
            };
            otp.CodeHash = Hash(code, otp.Key);
            _db.OtpCodes.Add(otp);
            await _db.SaveChangesAsync(ct);
            var sms = await _sms.SendToPhoneAsync(phone, _sms.OtpText(code), SmsService.KindOtp, ct);
            if (sms.Status == SmsStatus.Failed) return (r.Fail("SMS göndərilmədi. Nömrəni yoxlayın və ya zəng edin."), null);
            return (Result.Success($"Kod {Mask(phone)} nömrəsinə göndərildi"), otp.Key);
        }

        /// <summary>The code page: where the code went and the booking it confirms. Null = unknown / used / expired.</summary>
        public async Task<(OtpCode Otp, OnlineBookingForm Form)?> PendingAsync(Guid key, CancellationToken ct = default)
        {
            var otp = await _db.OtpCodes.AsNoTracking().FirstOrDefaultAsync(o => o.Key == key, ct);
            if (otp == null || otp.UsedAt != null || otp.ExpiresAt < ClinicTime.Now || otp.Attempts >= _options.OtpMaxAttempts) return null;
            return (otp, JsonSerializer.Deserialize<OnlineBookingForm>(otp.Payload)!);
        }

        /// <summary>Right code → the visit is booked (time re-checked). Returns the SMS-link token of the new visit.</summary>
        public async Task<(Result Result, string? Token)> ConfirmAsync(Guid key, string? code, CancellationToken ct = default)
        {
            var otp = await _db.OtpCodes.FirstOrDefaultAsync(o => o.Key == key, ct);
            var now = ClinicTime.Now;
            if (otp == null || otp.UsedAt != null) return (Result.Failure("Kod etibarsızdır — yenidən başlayın"), null);
            if (otp.ExpiresAt < now) return (Result.Failure("Kodun vaxtı bitib — yenidən başlayın"), null);
            if (otp.Attempts >= _options.OtpMaxAttempts) return (Result.Failure("Çox səhv cəhd — yenidən başlayın"), null);
            if (string.IsNullOrWhiteSpace(code) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(code.Trim(), otp.Key)), Encoding.ASCII.GetBytes(otp.CodeHash)))
            {
                otp.Attempts++;
                await _db.SaveChangesAsync(ct);
                var left = _options.OtpMaxAttempts - otp.Attempts;
                return (Result.Failure(left > 0 ? $"Kod səhvdir ({left} cəhd qalıb)" : "Çox səhv cəhd — yenidən başlayın"), null);
            }
            otp.UsedAt = now;

            var f = JsonSerializer.Deserialize<OnlineBookingForm>(otp.Payload)!;
            var (patient, check) = await CheckAsync(f, ct);
            if (!check.Ok)
            {
                await _db.SaveChangesAsync(ct);
                return (Result.Failure(check.ErrorText), null);
            }
            var exams = await _db.ExaminationTypes.Include(e => e.Equipment).Where(e => f.ExamIds!.Contains(e.Id)).ToListAsync(ct);
            var shape = SlotService.Shape(exams);
            if (_slots.Validate(f.DoctorId, f.Date!.Value, f.Time!, null, out var start, out var slot, shape) is { } busy)
            {
                await _db.SaveChangesAsync(ct);
                return (Result.Failure(busy + " — başqa vaxt seçin"), null);
            }

            var created = patient == null;
            patient ??= _db.Patients.Add(new Patient
            {
                PinCode = Patient.NormalizePin(f.PinCode), Name = f.Name!.Trim(), Surname = f.Surname!.Trim(), Phone = f.Phone!.Trim(),
                Birthday = f.Birthday!.Value.Date, CreatedAt = now
            }).Entity;
            var quote = await _prices.QuoteAsync(created ? null : patient.Id, f.DoctorId, start, exams, null, null, ct);
            var reserve = new Reserve
            {
                Patient = patient, DoctorId = f.DoctorId, RoomId = slot.RoomId, AppointmentAt = start, DurationMinutes = slot.SlotMinutes,
                EquipmentId = shape.EquipmentId, Source = ReserveSource.Online, AddedDate = now, FollowUpOfId = quote.FollowUpOfId,
                Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Waiting, ct),
                PatientToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                ReserveExaminationTypes = quote.Lines.Select(l => l.ToLine()).ToList()
            };
            _db.Reserves.Add(reserve);
            await _db.SaveChangesAsync(ct);
            if (created) _audit.Log("Xəstə kartı yaradıldı", AuditEntities.Patient, patient.Id, $"{patient.FullName} ({patient.PinCode}) — onlayn", userName: Actor);
            _audit.Log("Rezervasiya yaradıldı", AuditEntities.Reservation, reserve.Id, $"{patient.FullName} — {Fmt.DateTime(start)}, onlayn", userName: Actor);
            return (Result.Success("Qəbulunuz yazıldı", reserve.Id), reserve.PatientToken);
        }

        /// <summary>The form's own rules: doctor, services, time window, the patient (known FİN needs its phone), limits.</summary>
        private async Task<(Patient? Patient, Result Result)> CheckAsync(OnlineBookingForm f, CancellationToken ct)
        {
            var r = new Result();
            var doctor = await _db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == f.DoctorId && d.IsActive && d.OnlineBooking, ct);
            if (doctor == null) r.Fail("Həkim onlayn yazılış üçün açıq deyil");
            var ids = (f.ExamIds ?? new()).Distinct().ToList();
            var exams = await _db.ExaminationTypes.AsNoTracking().Where(e => ids.Contains(e.Id) && e.OnlineBookable).ToListAsync(ct);
            if (exams.Count == 0 || exams.Count != ids.Count) r.Fail("Xidmət seçin", nameof(f.ExamIds));
            else if (doctor != null && exams.Any(e => e.DepartmentId != null && e.DepartmentId != doctor.DepartmentId)) r.Fail("Seçilən xidmət bu həkimə aid deyil", nameof(f.ExamIds));
            var (first, last) = Window();
            if (f.Date == null || string.IsNullOrEmpty(f.Time) || !TimeSpan.TryParseExact(f.Time, @"hh\:mm", null, out var tod)) r.Fail("Vaxt seçin", nameof(f.Time));
            else
            {
                var at = f.Date.Value.Date + tod;
                if (at < first) r.Fail($"Onlayn yazılış qəbuldan ən azı {_options.OnlineMinLeadMinutes / 60} saat əvvəl mümkündür", nameof(f.Time));
                else if (at.Date > last) r.Fail($"Ən çox {_options.OnlineMaxDaysAhead} gün irəli yazılmaq olar", nameof(f.Date));
            }

            var pin = Patient.NormalizePin(f.PinCode);
            var patient = pin.Length == 0 ? null : await _db.Patients.FirstOrDefaultAsync(p => p.PinCode == pin, ct);
            if (patient != null)
            {
                // Someone else's FİN with another phone: not online.
                if (SmsText.NormalizePhone(patient.Phone) != SmsText.NormalizePhone(f.Phone))
                    r.Fail("Bu FİN üçün qeyd olunmuş telefon nömrəsi fərqlidir. Klinikaya zəng edin.", nameof(f.Phone));
                else
                {
                    var active = await _db.Reserves.CountAsync(x => x.PatientId == patient.Id && x.Source == ReserveSource.Online && x.AppointmentAt > ClinicTime.Now
                        && (x.Status!.Name == StatusNames.Waiting || x.Status.Name == StatusNames.Confirmed), ct);
                    if (active >= _options.OnlineMaxActive) r.Fail($"Onlayn ən çox {_options.OnlineMaxActive} aktiv qəbul ola bilər. Əvvəlkiləri SMS linki ilə ləğv edin və ya zəng edin.");
                    if (_noShows.Warning(await _noShows.CountAsync(patient.Id, ct)) != null)
                        r.Fail("Son aylarda bir neçə qəbula gəlməmisiniz — yazılmaq üçün klinikaya zəng edin.");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(f.Name)) r.Fail("Adınızı yazın", nameof(f.Name));
                if (string.IsNullOrWhiteSpace(f.Surname)) r.Fail("Soyadınızı yazın", nameof(f.Surname));
                if (f.Birthday == null || f.Birthday.Value.Date > ClinicTime.Today || f.Birthday.Value.Year < 1900) r.Fail("Doğum tarixini yazın", nameof(f.Birthday));
                if (SmsText.NormalizePhone(f.Phone) == null) r.Fail("Mobil nömrə düzgün deyil", nameof(f.Phone));
            }
            return (patient, r);
        }

        private static string Hash(string code, Guid key) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key:N}:{code}")));

        public static string Mask(string phone) => phone.Length <= 4 ? phone : new string('•', phone.Length - 4) + phone[^4..];
    }
}

namespace ClinicReservation.Modules.Scheduling
{
    public record DoctorRating(int DoctorId, string Doctor, double Average, int Count, int Low);

    /// <summary>Patients' scores of visits: the list for management and averages per doctor.</summary>
    public class RatingQueries
    {
        private readonly IAppDbContext _db;
        public RatingQueries(IAppDbContext db) => _db = db;

        public Task<List<VisitRating>> ListAsync(int? doctorId, int? maxScore, DateTime from, DateTime to, CancellationToken ct = default)
        {
            var q = _db.VisitRatings.AsNoTracking().Include(r => r.Doctor).Include(r => r.Reserve).ThenInclude(x => x.Patient)
                .Where(r => r.CreatedAt >= from && r.CreatedAt < to.AddDays(1));
            if (doctorId != null) q = q.Where(r => r.DoctorId == doctorId);
            if (maxScore != null) q = q.Where(r => r.Score <= maxScore);
            return q.OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync(ct);
        }

        public async Task<List<DoctorRating>> ByDoctorAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
            (await _db.VisitRatings.AsNoTracking().Where(r => r.DoctorId != null && r.CreatedAt >= from && r.CreatedAt < to.AddDays(1))
                .GroupBy(r => new { r.DoctorId, r.Doctor!.FullName })
                .Select(g => new { g.Key.DoctorId, g.Key.FullName, Avg = g.Average(x => (double)x.Score), Count = g.Count(), Low = g.Count(x => x.Score <= 2) })
                .ToListAsync(ct))
            .Select(x => new DoctorRating(x.DoctorId!.Value, x.FullName, Math.Round(x.Avg, 2), x.Count, x.Low))
            .OrderByDescending(x => x.Average).ToList();
    }
}
