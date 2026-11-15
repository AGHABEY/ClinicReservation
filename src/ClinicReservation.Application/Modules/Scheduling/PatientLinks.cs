using ClinicReservation.Data;
using ClinicReservation.Helpers;
using Microsoft.Extensions.Options;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>
    /// The link in the patient's SMS: confirm or cancel without logging in, and see lab results
    /// after typing the birth date. The random token in the URL is the only key.
    /// </summary>
    public class PatientLinks
    {
        public const string Actor = "Xəstə (SMS linki)";
        private const string LabFailAction = "Onlayn nəticə: səhv doğum tarixi";
        private const int MaxFailuresPerHour = 5;

        private readonly IAppDbContext _db;
        private readonly AuditService _audit;
        private readonly Waitlist _waitlist;
        private readonly SlotService _slots;
        private readonly SchedulingOptions _options;

        public PatientLinks(IAppDbContext db, AuditService audit, Waitlist waitlist, SlotService slots, IOptions<SchedulingOptions> options)
        {
            _waitlist = waitlist;
            _slots = slots;
            _options = options.Value;
            _db = db;
            _audit = audit;
        }

        /// <summary>Only before the visit and while the booking is still open.</summary>
        public static bool CanAnswer(Reserve r) =>
            r.AppointmentAt > ClinicTime.Now && r.Status?.Name is StatusNames.Waiting or StatusNames.Confirmed;

        public Task<Reserve?> ByTokenAsync(string? token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return Task.FromResult<Reserve?>(null);
            return _db.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.Room)
                .FirstOrDefaultAsync(r => r.PatientToken == token, ct);
        }

        public async Task ConfirmAsync(Reserve res, CancellationToken ct = default)
        {
            if (!CanAnswer(res) || res.Status?.Name != StatusNames.Waiting) return;
            res.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Confirmed, ct);
            res.ConfirmDate = ClinicTime.Now;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Xəstə SMS linki ilə təsdiq etdi", AuditEntities.Reservation, res.Id, res.Patient.FullName, userName: Actor);
        }

        public async Task CancelAsync(Reserve res, CancellationToken ct = default)
        {
            if (!CanAnswer(res)) return;
            res.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Cancelled, ct);
            res.CancelDate = ClinicTime.Now;
            res.CancelReason = "Xəstə SMS linki ilə ləğv etdi";
            res.CancelledBy = CancelledBy.Patient;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Xəstə SMS linki ilə ləğv etdi", AuditEntities.Reservation, res.Id, res.Patient.FullName, userName: Actor);
            await _waitlist.OfferFreedAsync(res, ct: ct);
        }

        // ----- Moving the visit (D2) -----

        /// <summary>Why the patient can't move it themselves (null = they can).</summary>
        public string? CannotMove(Reserve res)
        {
            if (!CanAnswer(res)) return "Bu qəbulun vaxtı artıq dəyişdirilə bilməz";
            if (res.RescheduleCount >= _options.RescheduleMax) return $"Vaxtı ən çox {_options.RescheduleMax} dəfə dəyişmək olar — klinikaya zəng edin";
            if (res.AppointmentAt < ClinicTime.Now.AddMinutes(_options.OnlineMinLeadMinutes)) return "Qəbula az vaxt qalıb — dəyişmək üçün klinikaya zəng edin";
            if (res.DoctorId == null) return "Klinikaya zəng edin";
            return null;
        }

        /// <summary>Free times with the same doctor for the same services (the visit's own time not counted as busy).</summary>
        public async Task<SlotResult> MoveSlotsAsync(Reserve res, DateTime date, CancellationToken ct = default)
        {
            var exams = await ExamsOfAsync(res, ct);
            var shape = SlotService.Shape(exams);
            var all = _slots.GetSlots(res.DoctorId!.Value, date.Date, res.Id, shape.DurationMinutes, shape.EquipmentId);
            var first = ClinicTime.Now.AddMinutes(_options.OnlineMinLeadMinutes);
            var last = ClinicTime.Today.AddDays(_options.OnlineMaxDaysAhead);
            var open = new SlotResult { Message = all.Message, SlotMinutes = all.SlotMinutes };
            foreach (var s in all.Slots)
            {
                var at = date.Date + TimeSpan.Parse(s.Time);
                if (s.Available && at >= first && at.Date <= last && at != res.AppointmentAt) open.Slots.Add(s);
            }
            if (open.Slots.Count == 0 && all.Slots.Count > 0) open.Message = "Bu gün üçün boş vaxt yoxdur — başqa gün seçin";
            return open;
        }

        private Task<List<ExaminationType>> ExamsOfAsync(Reserve res, CancellationToken ct) =>
            _db.ExaminationTypes.Include(e => e.Equipment)
                .Where(e => _db.ReserveExaminationTypes.Any(x => x.ReserveId == res.Id && x.ExaminationTypeId == e.Id)).ToListAsync(ct);

        /// <summary>The patient moves the visit: same doctor and services, a free time inside the online window.</summary>
        public async Task<Result> MoveAsync(Reserve res, DateTime? date, string? time, CancellationToken ct = default)
        {
            if (CannotMove(res) is { } why) return Result.Failure(why);
            if (date == null || string.IsNullOrEmpty(time)) return Result.Failure("Yeni vaxtı seçin");
            var exams = await ExamsOfAsync(res, ct);
            var shape = SlotService.Shape(exams);
            if (_slots.Validate(res.DoctorId!.Value, date.Value, time, res.Id, out var start, out var slot, shape) is { } busy) return Result.Failure(busy);
            if (start < ClinicTime.Now.AddMinutes(_options.OnlineMinLeadMinutes) || start.Date > ClinicTime.Today.AddDays(_options.OnlineMaxDaysAhead))
                return Result.Failure("Bu vaxtı seçmək olmaz — klinikaya zəng edin");

            var old = new Reserve { DoctorId = res.DoctorId, AppointmentAt = res.AppointmentAt, PatientId = res.PatientId, DurationMinutes = res.DurationMinutes };
            res.AppointmentAt = start;
            res.RoomId = slot.RoomId;
            res.DurationMinutes = slot.SlotMinutes;
            res.RescheduleCount++;
            // A new time: confirm again, the reminder goes out again.
            if (res.Status?.Name == StatusNames.Confirmed)
            {
                res.Status = await _db.ReserveStatuses.FirstAsync(s => s.Name == StatusNames.Waiting, ct);
                res.ConfirmDate = null;
            }
            res.ReminderSentAt = null;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Xəstə SMS linki ilə vaxtı dəyişdi", AuditEntities.Reservation, res.Id,
                $"{res.Patient.FullName}: {Fmt.DateTime(old.AppointmentAt)} → {Fmt.DateTime(start)}", userName: Actor);
            await _waitlist.OfferFreedAsync(old, ct: ct);
            return Result.Success($"Yeni vaxt: {Fmt.Long(start)}, saat {Fmt.Time(start)}");
        }

        // ----- Rating (D3) -----

        public bool CanRate(Reserve res) =>
            res.Status?.Name == StatusNames.Completed && (res.CompletedAt ?? res.AppointmentAt) >= ClinicTime.Now.AddDays(-_options.RatingDays);

        public Task<VisitRating?> RatingAsync(int reserveId, CancellationToken ct = default) =>
            _db.VisitRatings.FirstOrDefaultAsync(r => r.ReserveId == reserveId, ct);

        public async Task<Result> RateAsync(Reserve res, int score, string? comment, CancellationToken ct = default)
        {
            if (!CanRate(res)) return Result.Failure("Bu qəbulu qiymətləndirmək artıq mümkün deyil");
            if (score is < 1 or > 5) return Result.Failure("1-dən 5-ə qədər ulduz seçin");
            comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            if (comment?.Length > 1000) comment = comment[..1000];
            var now = ClinicTime.Now;
            var rating = await _db.VisitRatings.FirstOrDefaultAsync(r => r.ReserveId == res.Id, ct);
            if (rating == null)
                _db.VisitRatings.Add(rating = new VisitRating { ReserveId = res.Id, PatientId = res.PatientId, DoctorId = res.DoctorId, CreatedAt = now });
            else rating.UpdatedAt = now;
            rating.Score = score;
            rating.Comment = comment;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Xəstə qəbulu qiymətləndirdi", AuditEntities.Reservation, res.Id, $"{res.Patient.FullName}: {score}/5", userName: Actor);
            return Result.Success("Rəyiniz üçün təşəkkür edirik!");
        }

        /// <summary>Validated results after the birth-date check; at most 5 wrong tries an hour per link.</summary>
        public async Task<(List<LabOrder>? Orders, string? Error)> LabResultsAsync(Reserve res, DateTime? birthday, CancellationToken ct = default)
        {
            var since = ClinicTime.Now.AddHours(-1);
            var failures = await _db.AuditLogs.CountAsync(a => a.Action == LabFailAction && a.EntityId == res.Id.ToString() && a.At >= since, ct);
            if (failures >= MaxFailuresPerHour)
                return (null, "Çox sayda səhv cəhd. Bir saat sonra yenidən yoxlayın və ya klinikaya zəng edin.");
            if (birthday?.Date != res.Patient.Birthday.Date)
            {
                _audit.Log(LabFailAction, AuditEntities.Lab, res.Id, res.Patient.FullName, userName: Actor);
                return (null, "Doğum tarixi uyğun gəlmir");
            }

            var orders = await _db.LabOrders
                .Include(o => o.Patient)
                .Include(o => o.Reserve).ThenInclude(r => r.Doctor)
                .Include(o => o.Results).ThenInclude(r => r.LabTest)
                .Where(o => o.ReserveId == res.Id && o.Status == LabOrderStatus.Validated)
                .OrderBy(o => o.CollectedAt)
                .AsSplitQuery()
                .ToListAsync(ct);
            _audit.Log("Xəstə nəticəyə onlayn baxdı", AuditEntities.Lab, res.Id, res.Patient.FullName, userName: Actor);
            return (orders, null);
        }
    }
}
