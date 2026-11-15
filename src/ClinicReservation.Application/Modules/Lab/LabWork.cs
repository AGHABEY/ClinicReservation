using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Notifications;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Lab
{
    /// <summary>Lab use cases: take a sample, enter and validate results, reopen, cancel; all audited.</summary>
    public class LabWork
    {
        private readonly IAppDbContext _db;
        private readonly LabService _lab;
        private readonly LabQueries _queries;
        private readonly SmsService _sms;
        private readonly StockService _stock;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public LabWork(IAppDbContext db, LabService lab, LabQueries queries, SmsService sms, StockService stock, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _lab = lab;
            _queries = queries;
            _sms = sms;
            _stock = stock;
            _audit = audit;
            _user = user;
        }

        /// <summary>Result of taking a sample: the new orders (for the label print) and any stock warning.</summary>
        public record CollectOutcome(Result Result, string? LabelIds);

        /// <summary>Takes the sample: one order (tube, barcode) per sample type. Null = no such reservation.</summary>
        public async Task<CollectOutcome?> CollectAsync(int reserveId, CancellationToken ct = default)
        {
            var reserve = await _db.Reserves.Include(r => r.Patient).Include(r => r.Status).FirstOrDefaultAsync(r => r.Id == reserveId, ct);
            if (reserve == null) return null;
            if (StatusNames.IsClosedWithoutVisit(reserve.Status?.Name))
                return new(Result.Failure($"«{StatusNames.Label(reserve.Status?.Name)}» rezervasiyadan nümunə götürülmür"), null);

            var tests = _lab.PendingTests(reserveId);
            if (tests.Count == 0) return new(Result.Failure("Bu rezervasiya üçün götürüləcək analiz yoxdur"), null);

            // One tube per sample type (blood and urine get separate barcodes).
            var orders = tests.GroupBy(t => t.SampleType)
                .Select(g => _lab.CreateOrder(reserve, g, reserve.Patient.Gender, _user.UserName, ClinicTime.Now))
                .ToList();
            foreach (var o in orders)
                _audit.Log("Nümunə götürüldü", AuditEntities.Lab, o.Id, $"{reserve.Patient.FullName}, barkod {o.Barcode}, {o.Results.Count} analiz");

            var result = Result.Success($"Nümunə götürüldü: {string.Join(", ", orders.Select(o => o.Barcode))}");
            // Tubes, needles, reagents of the lab services leave stock when the sample is taken.
            result.Warning = StockService.Warning(_stock.ConsumeForReserve(reserveId, true, _user.UserName));
            return new(result, string.Join(",", orders.Select(o => o.Id)));
        }

        /// <summary>What happened to a result entry: saved, or errors with the typed values kept on the order for redisplay.</summary>
        public record SaveOutcome(Result Result, LabOrder? Order, bool Forbidden = false);

        public async Task<SaveOutcome?> SaveResultsAsync(LabSaveInput input, bool canValidate, CancellationToken ct = default)
        {
            var order = await _queries.OrderAsync(input.OrderId, ct);
            if (order == null) return null;
            if (order.Status != LabOrderStatus.Collected)
                return new(Result.Failure("Təsdiqlənmiş və ya ləğv edilmiş nümunəyə dəyişiklik edilmir"), null);

            var validate = input.Validate == "1";
            if (validate && !canValidate) return new(new Result(), null, Forbidden: true);

            var now = ClinicTime.Now;
            var gender = order.Patient.Gender;
            var r = new Result();
            var changed = 0;
            foreach (var res in order.Results)
            {
                var posted = input.Results.FirstOrDefault(x => x.Id == res.Id);
                var value = string.IsNullOrWhiteSpace(posted?.Value) ? null : posted!.Value!.Trim();
                var t = res.LabTest;

                if (value != null && t.ResultType == LabResultType.Numeric && !LabRef.TryParseNumber(value, out _))
                    r.Fail($"{t.Name}: rəqəm daxil edin");
                else if (value != null && t.ResultType == LabResultType.Choice && t.ChoiceList.Length > 0
                         && !t.ChoiceList.Contains(value, StringComparer.OrdinalIgnoreCase))
                    r.Fail($"{t.Name}: siyahıdan seçin");

                if (value != res.Value)
                {
                    res.Value = value;
                    res.EnteredAt = now;
                    res.EnteredBy = _user.UserName;
                    changed++;
                }
                var (flag, number) = LabRef.Evaluate(t, gender, value);
                res.Flag = flag;
                res.NumericValue = number;
                res.Unit = t.Unit;
                res.Reference = LabRef.Text(t, gender);
            }
            order.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

            if (validate)
            {
                if (order.Results.Any(x => x.Value == null)) r.Fail("Təsdiq üçün bütün nəticələr yazılmalıdır");
                if (order.HasCritical && !input.ConfirmCritical) r.Fail("Kritik dəyər var — yoxlayın və «Kritik dəyəri yoxladım» işarəsini qoyun");
            }
            // Show what was typed; nothing is saved.
            if (!r.Ok) return new(r, order);

            if (validate)
            {
                order.Status = LabOrderStatus.Validated;
                order.ValidatedAt = now;
                order.ValidatedBy = _user.UserName;
            }
            await _db.SaveChangesAsync(ct);

            if (changed > 0)
                _audit.Log("Analiz nəticəsi yazıldı", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode}: {changed} nəticə");
            if (!validate) return new(Result.Success("Nəticələr saxlanıldı", order.Id), order);

            _audit.Log("Analiz nəticəsi təsdiqləndi", AuditEntities.Lab, order.Id,
                $"{order.Patient.FullName}, {order.Barcode}" + (order.HasCritical ? " — KRİTİK DƏYƏR" : order.HasAbnormal ? " — normadan kənar" : ""));

            var message = "Nəticələr təsdiqləndi";
            if (input.SendSms)
            {
                var reserve = order.Reserve;
                reserve.Patient = order.Patient;
                var allDone = !await _db.LabOrders.AnyAsync(o => o.ReserveId == reserve.Id && o.Status == LabOrderStatus.Collected, ct)
                              && _lab.PendingTests(reserve.Id).Count == 0;
                if (allDone)
                {
                    var sms = await _sms.SendAsync(reserve, _sms.LabResultText(reserve), SmsService.KindLabResult, _user.UserName);
                    message += sms.Status switch
                    {
                        SmsStatus.Failed => $". SMS göndərilmədi: {sms.Error}",
                        SmsStatus.Simulated => ". Xəstəyə SMS (test rejimində) qeydə alındı",
                        _ => ". Xəstəyə SMS göndərildi"
                    };
                }
                else message += ". SMS bu xəstənin bütün nümunələri təsdiqlənəndə göndəriləcək";
            }
            return new(Result.Success(message, order.Id), order);
        }

        /// <summary>Re-opens a validated order for correction (reason required, logged). Null = not found.</summary>
        public async Task<Result?> ReopenAsync(int id, string? reason, CancellationToken ct = default)
        {
            var order = await _db.LabOrders.Include(o => o.Patient).FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null) return null;
            if (order.Status != LabOrderStatus.Validated) return Result.Success();
            if (string.IsNullOrWhiteSpace(reason)) return Result.Failure("Düzəliş üçün səbəb yazın");
            order.Status = LabOrderStatus.Collected;
            order.ValidatedAt = null;
            order.ValidatedBy = null;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Analiz təsdiqi geri alındı", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode} — səbəb: {reason.Trim()}");
            return Result.Success("Nəticə düzəliş üçün açıldı. Düzəlişdən sonra yenidən təsdiqləyin.");
        }

        public async Task<Result?> CancelAsync(int id, string? reason, CancellationToken ct = default)
        {
            var order = await _db.LabOrders.Include(o => o.Patient).FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null) return null;
            if (order.Status != LabOrderStatus.Collected) return Result.Failure("Yalnız təsdiqlənməmiş nümunə ləğv edilə bilər");
            order.Status = LabOrderStatus.Cancelled;
            order.CancelledAt = ClinicTime.Now;
            order.Note = string.IsNullOrWhiteSpace(reason) ? order.Note : $"Ləğv: {reason.Trim()}";
            await _db.SaveChangesAsync(ct);
            _audit.Log("Nümunə ləğv edildi", AuditEntities.Lab, order.Id, $"{order.Patient.FullName}, {order.Barcode}" + (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason.Trim()}"));
            return Result.Success("Nümunə ləğv edildi. Analizlər yenidən «Nümunə gözləyir» siyahısına düşdü.");
        }

        /// <summary>Records the patient's sex (needed for some reference ranges).</summary>
        public async Task<bool> SetGenderAsync(int orderId, Gender gender, CancellationToken ct = default)
        {
            var order = await _db.LabOrders.Include(o => o.Patient).FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order == null || !Enum.IsDefined(gender)) return false;
            order.Patient.Gender = gender;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Xəstənin cinsi qeyd edildi", AuditEntities.Patient, order.PatientId, $"{order.Patient.FullName}: {Fmt.Gender(gender)}");
            return true;
        }

        public void OrderViewed(LabOrder o) => _audit.Log("Analiz nəticəsinə baxıldı", AuditEntities.Lab, o.Id, $"{o.Patient.FullName}, {o.Barcode}");
        public void ReportPrinted(LabOrder o) => _audit.Log("Analiz nəticəsi çap edildi", AuditEntities.Lab, o.Id, $"{o.Patient.FullName}, {o.Barcode}");
    }
}
