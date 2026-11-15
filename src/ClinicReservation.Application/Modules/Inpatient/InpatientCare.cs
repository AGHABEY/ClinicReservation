using System.Globalization;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Inpatient
{
    /// <summary>
    /// Inpatient use cases: what the reception, doctors and nurses do on a stay. Each one checks
    /// the input, applies the change (through InpatientService where there are rules) and audits it.
    /// </summary>
    public class InpatientCare
    {
        private readonly IAppDbContext _db;
        private readonly InpatientService _stays;
        private readonly InpatientQueries _queries;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public InpatientCare(IAppDbContext db, InpatientService stays, InpatientQueries queries, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _stays = stays;
            _queries = queries;
            _audit = audit;
            _user = user;
        }

        /// <summary>Admits a patient (by id or FİN). Field errors use AdmitViewModel property names.</summary>
        public async Task<Result> AdmitAsync(AdmitViewModel form, CancellationToken ct = default)
        {
            if (form.PatientId == null && !string.IsNullOrWhiteSpace(form.PatientPin))
            {
                var pin = Patient.NormalizePin(form.PatientPin);
                form.PatientId = await _db.Patients.Where(p => p.PinCode == pin).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
                if (form.PatientId == null) return Result.Failure($"{pin} FİN-li xəstə tapılmadı — əvvəlcə xəstə kartını yaradın", nameof(form.PatientPin));
            }
            else if (form.PatientId == null)
                return Result.Failure("Xəstənin FİN-ini yazın", nameof(form.PatientPin));
            if (form.BedId == null) return Result.Failure("Çarpayını seçin", nameof(form.BedId));

            var r = _stays.Admit(form.PatientId.Value, form.BedId.Value, form.DoctorId, form.Type,
                Clean(form.IcdCode)?.ToUpperInvariant(), Clean(form.Diagnosis), Clean(form.Note), form.ReserveId, _user.UserName);
            if (!r.Ok) return Result.Failure(r.Error!);

            var a = (await _queries.AdmissionAsync(r.Admission!.Id, ct))!;
            _audit.Log("Stasionara yerləşdirildi", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}: {a.Bed!.Label}, {InpatientLabels.Type(a.Type)}, {a.AdmitIcdCode} {a.AdmitDiagnosis}".Trim());
            var result = Result.Success($"{a.Patient.FullName} yerləşdirildi: {a.Bed.Label} ({a.Number})", a.Id);
            if (InpatientRules.GenderWarning(a.Bed, a.Patient) is { } warn) result.Warning = $"Diqqət: {warn}";
            return result;
        }

        public async Task<Result> MoveAsync(int admissionId, int bedId, CancellationToken ct = default)
        {
            var a = await _queries.AdmissionAsync(admissionId, ct);
            if (a == null) return Result.Failure("Yerləşdirmə tapılmadı");
            var from = a.Bed?.Label;
            var r = _stays.Move(a, bedId, _user.UserName);
            if (!r.Ok) return Result.Failure(r.Error!);

            a = (await _queries.AdmissionAsync(admissionId, ct))!;
            _audit.Log("Çarpayı dəyişdi", AuditEntities.Inpatient, a.Id, $"{a.Number} {a.Patient.FullName}: {from} → {a.Bed!.Label}");
            var result = Result.Success($"{a.Patient.FullName} köçürüldü: {a.Bed.Label}", a.Id);
            if (InpatientRules.GenderWarning(a.Bed, a.Patient) is { } warn) result.Warning = $"Diqqət: {warn}";
            return result;
        }

        /// <param name="formErrors">Errors already found on the form (ranges), reported together with these checks.</param>
        public async Task<Result> ObserveAsync(Admission a, ObservationInput input, IEnumerable<string> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var e in formErrors) r.Fail(e);
            if (!a.IsActive) r.Fail("Xəstə çıxarılıb — yeni qeyd yazılmır");
            var text = Clean(input.Text);
            var hasVitals = input.Temperature != null || input.Pulse != null || input.BpSystolic != null || input.BpDiastolic != null
                || input.RespRate != null || input.SpO2 != null;
            if (input.Kind == ObservationKind.Doctor && text == null) r.Fail("Gündəlik qeydini yazın");
            if (input.Kind == ObservationKind.Nurse && text == null && !hasVitals) r.Fail("Vital göstərici və ya qeyd yazın");
            if ((input.BpSystolic == null) != (input.BpDiastolic == null)) r.Fail("Təzyiqin hər iki rəqəmini yazın (məs. 120 / 80)");
            else if (input.BpSystolic <= input.BpDiastolic) r.Fail("Sistolik təzyiq diastolikdən böyük olmalıdır");
            if (!r.Ok) return r;

            var o = new Observation
            {
                AdmissionId = a.Id, Kind = input.Kind, At = ClinicTime.Now, Author = _user.UserName, Text = text,
                Temperature = input.Temperature, Pulse = input.Pulse, BpSystolic = input.BpSystolic, BpDiastolic = input.BpDiastolic,
                RespRate = input.RespRate, SpO2 = input.SpO2
            };
            _db.Observations.Add(o);
            await _db.SaveChangesAsync(ct);
            _audit.Log(input.Kind == ObservationKind.Doctor ? "Həkim gündəliyi yazıldı" : "Tibb bacısı qeydi yazıldı", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}" + (hasVitals ? $"; {InpatientRules.Vitals(o)}" : ""));
            return Result.Success("Qeyd saxlanıldı", a.Id);
        }

        public async Task<Result> OrderAsync(Admission a, OrderInput input, IEnumerable<string> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var e in formErrors) r.Fail(e);
            if (!a.IsActive) r.Fail("Xəstə çıxarılıb — yeni təyinat yazılmır");

            StockItem? item = null;
            if (input.StockItemId != null)
            {
                item = await _db.StockItems.FirstOrDefaultAsync(i => i.Id == input.StockItemId && i.IsActive, ct);
                if (item == null) r.Fail("Dərman tapılmadı");
            }
            var name = Clean(input.DrugName) ?? item?.DisplayName;
            if (name == null) r.Fail("Dərmanı seçin və ya adını yazın");
            var route = Clean(input.Route);
            if (route != null && !InpatientLabels.Routes.Contains(route)) r.Fail("Yeridilmə yolunu siyahıdan seçin");
            var times = InpatientRules.ParseTimes(input.Times, out var timesError);
            if (timesError != null) r.Fail(timesError);
            if (!r.Ok) return r;

            var now = ClinicTime.Now;
            var order = new MedicationOrder
            {
                AdmissionId = a.Id,
                StockItemId = item?.Id,
                DrugName = name!,
                Dose = Clean(input.Dose),
                QuantityPerDose = item != null ? input.QuantityPerDose : 1,
                Route = route,
                Times = times,
                StartAt = now,
                EndDate = input.Days != null ? now.Date.AddDays(input.Days.Value - 1) : null,
                Note = Clean(input.Note),
                OrderedAt = now,
                OrderedBy = _user.UserName
            };
            _db.MedicationOrders.Add(order);
            await _db.SaveChangesAsync(ct);
            order.StockItem = item;
            _audit.Log("Dərman təyin edildi", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}: {order.Name} {order.Dose} {order.Route} — {(order.IsPrn ? "lazım olduqda" : order.Times)}"
                + (order.EndDate != null ? $", {input.Days} gün" : ""));
            return Result.Success($"{order.Name} təyin edildi", a.Id);
        }

        public async Task<Result?> StopOrderAsync(int admissionId, int orderId, CancellationToken ct = default)
        {
            var o = await _db.MedicationOrders.Include(x => x.StockItem).Include(x => x.Admission).ThenInclude(x => x.Patient)
                .FirstOrDefaultAsync(x => x.Id == orderId && x.AdmissionId == admissionId, ct);
            if (o == null) return null;
            if (o.StoppedAt != null) return Result.Failure("Təyinat artıq dayandırılıb");
            o.StoppedAt = ClinicTime.Now;
            o.StoppedBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Təyinat dayandırıldı", AuditEntities.Inpatient, admissionId, $"{o.Admission.Number} {o.Admission.Patient.FullName}: {o.Name}");
            return Result.Success($"{o.Name} dayandırıldı");
        }

        /// <summary>Medication round: a dose given or not given. Null = no such order; a bad slot string is an error.</summary>
        public async Task<Result?> RecordDoseAsync(int admissionId, int orderId, string? slot, DoseStatus status, string? note, CancellationToken ct = default)
        {
            var o = await _db.MedicationOrders.Include(x => x.StockItem).Include(x => x.Admission).ThenInclude(x => x.Patient)
                .FirstOrDefaultAsync(x => x.Id == orderId && x.AdmissionId == admissionId, ct);
            if (o == null) return null;
            if (!Enum.IsDefined(status)) return Result.Failure("Status düzgün deyil");
            DateTime at = default;
            if (!o.IsPrn && !DateTime.TryParseExact(slot, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
                return Result.Failure("Vaxt düzgün deyil");

            var r = _stays.RecordDose(o, at, status, note, _user.UserName, out var stockWarning);
            if (!r.Ok) return Result.Failure(r.Error!);
            _audit.Log(status == DoseStatus.Given ? "Dərman verildi" : "Dərman verilmədi", AuditEntities.Inpatient, admissionId,
                $"{o.Admission.Number} {o.Admission.Patient.FullName}: {o.Name} {o.Dose}, {(o.IsPrn ? "lazım olduqda" : Fmt.DateTime(at))}"
                + (status == DoseStatus.Skipped ? $" — {note}" : ""));
            var result = Result.Success(status == DoseStatus.Given ? $"{o.Name} — verildi" : $"{o.Name} — verilmədi");
            result.Warning = stockWarning;
            return result;
        }

        public Result Discharge(Admission a, DischargeInput input)
        {
            var r = _stays.Discharge(a, input.Outcome!.Value, Clean(input.IcdCode)?.ToUpperInvariant(), Clean(input.Diagnosis)!,
                Clean(input.Course), Clean(input.Treatment), Clean(input.Recommendations), _user.UserName);
            if (!r.Ok) return Result.Failure(r.Error!);
            var (_, days, _) = InpatientRules.BedDays(a, ClinicTime.Now);
            _audit.Log("Stasionardan çıxarıldı", AuditEntities.Inpatient, a.Id,
                $"{a.Number} {a.Patient.FullName}: {InpatientLabels.Outcome(a.Outcome)}, {a.FinalIcdCode} {a.FinalDiagnosis}, {days} yataq-gün");
            return Result.Success($"{a.Patient.FullName} çıxarıldı. Epikrizi çap edib xəstəyə verin.", a.Id);
        }

        public Result Cancel(Admission a, string? reason)
        {
            var r = _stays.Cancel(a, reason ?? "", _user.UserName);
            if (!r.Ok) return Result.Failure(r.Error!);
            _audit.Log("Yerləşdirmə ləğv edildi", AuditEntities.Inpatient, a.Id, $"{a.Number} {a.Patient.FullName}: {a.CancelReason}");
            return Result.Success($"{a.Number} ləğv edildi, çarpayı boşaldı", a.Id);
        }

        /// <summary>Reading a printed epicrisis is recorded like opening a medical record.</summary>
        public void EpicrisisViewed(Admission a) =>
            _audit.Log("Epikrizə baxıldı", AuditEntities.Inpatient, a.Id, $"{a.Number} {a.Patient.FullName}");

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
