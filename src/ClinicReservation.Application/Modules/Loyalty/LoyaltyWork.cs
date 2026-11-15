using System.Security.Cryptography;
using ClinicReservation.Data;
using ClinicReservation.Events;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicReservation.Modules.Loyalty
{
    /// <summary>Cards: issue, family members, point corrections, fixed level. Patient-level actions are audited on the patient.</summary>
    public class LoyaltyCards
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public LoyaltyCards(IAppDbContext db, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _audit = audit;
            _user = user;
        }

        public async Task<Result> IssueAsync(int patientId, bool family, CancellationToken ct = default)
        {
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct);
            if (patient == null) return Result.Failure("Xəstə tapılmadı");
            if (await _db.LoyaltyCardMembers.AnyAsync(m => m.PatientId == patientId, ct)) return Result.Failure("Bu xəstənin artıq kartı var");

            string number;
            do number = "MC" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
            while (await _db.LoyaltyCards.AnyAsync(c => c.Number == number, ct));

            var now = ClinicTime.Now;
            var card = new LoyaltyCard { Number = number, HolderPatientId = patientId, IsFamily = family, CreatedAt = now, CreatedBy = _user.UserName };
            card.Members.Add(new LoyaltyCardMember { PatientId = patientId, JoinedAt = now });
            _db.LoyaltyCards.Add(card);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Loyallıq kartı verildi", AuditEntities.Patient, patientId, $"{patient.FullName}: {number}{(family ? " (ailə kartı)" : "")}");
            return Result.Success($"{(family ? "Ailə kartı" : "Kart")} verildi: {number}", card.Id);
        }

        /// <summary>Adds a relative to the card (it becomes a family card): they share points and the level.</summary>
        public async Task<Result?> AddMemberAsync(int cardId, string? pin, CancellationToken ct = default)
        {
            var card = await _db.LoyaltyCards.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == cardId, ct);
            if (card == null) return null;
            var p = Patient.NormalizePin(pin);
            var patient = p.Length == 0 ? null : await _db.Patients.FirstOrDefaultAsync(x => x.PinCode == p, ct);
            if (patient == null) return Result.Failure("Bu FİN ilə xəstə kartı tapılmadı");
            if (await _db.LoyaltyCardMembers.AnyAsync(m => m.PatientId == patient.Id, ct)) return Result.Failure($"{patient.FullName} artıq bir kartın üzvüdür");
            card.IsFamily = true;
            card.Members.Add(new LoyaltyCardMember { PatientId = patient.Id, JoinedAt = ClinicTime.Now });
            await _db.SaveChangesAsync(ct);
            _audit.Log("Ailə kartına üzv əlavə edildi", AuditEntities.Patient, patient.Id, $"{patient.FullName} → {card.Number}");
            return Result.Success($"{patient.FullName} ailə kartına əlavə edildi", card.Id);
        }

        public async Task<Result?> RemoveMemberAsync(int cardId, int patientId, CancellationToken ct = default)
        {
            var m = await _db.LoyaltyCardMembers.Include(x => x.Card).Include(x => x.Patient).FirstOrDefaultAsync(x => x.CardId == cardId && x.PatientId == patientId, ct);
            if (m == null) return null;
            if (m.Card.HolderPatientId == patientId) return Result.Failure("Kart sahibini çıxarmaq olmaz");
            _db.LoyaltyCardMembers.Remove(m);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Ailə kartından üzv çıxarıldı", AuditEntities.Patient, patientId, $"{m.Patient.FullName} ← {m.Card.Number}");
            return Result.Success($"{m.Patient.FullName} kartdan çıxarıldı", cardId);
        }

        /// <summary>Manual correction (gift, mistake); reason required; the balance can't go below zero.</summary>
        public async Task<Result?> AdjustPointsAsync(int cardId, int points, string? note, CancellationToken ct = default)
        {
            var card = await _db.LoyaltyCards.Include(c => c.Holder).FirstOrDefaultAsync(c => c.Id == cardId, ct);
            if (card == null) return null;
            if (points == 0) return Result.Failure("Bal sayını yazın");
            if (string.IsNullOrWhiteSpace(note)) return Result.Failure("Səbəbi yazın");
            var balance = await _db.LoyaltyTransactions.Where(t => t.CardId == cardId).SumAsync(t => (int?)t.Points, ct) ?? 0;
            if (balance + points < 0) return Result.Failure($"Balans mənfi ola bilməz (indi {balance} bal)");
            _db.LoyaltyTransactions.Add(new LoyaltyTransaction
            {
                CardId = cardId, Kind = PointsKind.Adjust, Points = points, At = ClinicTime.Now, By = _user.UserName, Note = note.Trim()
            });
            await _db.SaveChangesAsync(ct);
            _audit.Log("Bonus bal düzəlişi", AuditEntities.Loyalty, card.Id, $"{card.Number} ({card.Holder.FullName}): {points:+#;-#} bal — {note.Trim()}");
            return Result.Success($"{points:+#;-#} bal yazıldı", cardId);
        }

        public async Task<Result?> SetTierAsync(int cardId, int? tierId, CancellationToken ct = default)
        {
            var card = await _db.LoyaltyCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
            if (card == null) return null;
            if (tierId != null && !await _db.LoyaltyTiers.AnyAsync(t => t.Id == tierId, ct)) return Result.Failure("Səviyyə tapılmadı");
            card.TierOverrideId = tierId;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Kart səviyyəsi təyin edildi", AuditEntities.Loyalty, card.Id, $"{card.Number}: {(tierId == null ? "xərcə görə" : $"sabit #{tierId}")}");
            return Result.Success(tierId == null ? "Səviyyə xərcə görə hesablanacaq" : "Kartın səviyyəsi sabitləndi", cardId);
        }

        public async Task<Result?> SetActiveAsync(int cardId, bool active, CancellationToken ct = default)
        {
            var card = await _db.LoyaltyCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
            if (card == null) return null;
            card.IsActive = active;
            await _db.SaveChangesAsync(ct);
            _audit.Log(active ? "Kart aktivləşdirildi" : "Kart bloklandı", AuditEntities.Loyalty, card.Id, card.Number);
            return Result.Success(active ? "Kart aktivdir" : "Kart bloklandı — endirim və bal işləmir", cardId);
        }
    }

    /// <summary>Discount catalogs: card levels, campaigns / promo codes, social discount categories, companies.</summary>
    public class DiscountCatalog
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public DiscountCatalog(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        public async Task<Result> SaveTierAsync(int id, string? name, decimal minSpend, decimal discount, decimal points, bool active, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return Result.Failure("Səviyyənin adını yazın");
            if (minSpend < 0 || discount is < 0 or > 100 || points is < 0 or > 100) return Result.Failure("Rəqəmlər düzgün deyil (faiz 0–100)");
            var t = id == 0 ? new LoyaltyTier() : await _db.LoyaltyTiers.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t == null) return Result.Failure("Səviyyə tapılmadı");
            (t.Name, t.MinSpend, t.DiscountPercent, t.PointsPercent, t.IsActive) = (name.Trim(), minSpend, discount, points, active);
            if (id == 0) _db.LoyaltyTiers.Add(t);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Kart səviyyəsi saxlanıldı", AuditEntities.Loyalty, t.Id, $"{t.Name}: {Fmt.Money(minSpend)}-dan, endirim {discount:0.##}%, bal {points:0.##}%");
            return Result.Success($"«{t.Name}» saxlanıldı", t.Id);
        }

        public async Task<Result> SaveCampaignAsync(CampaignFormViewModel f, IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var (field, m) in formErrors) r.Fail(m, field);
            if (f.Type == DiscountType.Percent && f.Value > 100) r.Fail("Faiz 100-dən çox ola bilməz", nameof(f.Value));
            if (f.StartsOn != null && f.EndsOn != null && f.EndsOn < f.StartsOn) r.Fail("Son tarix başlanğıcdan əvvəl ola bilməz", nameof(f.EndsOn));
            var code = string.IsNullOrWhiteSpace(f.Code) ? null : f.Code.Trim().ToUpperInvariant();
            if (code != null && await _db.Campaigns.AnyAsync(c => c.Code == code && c.Id != f.Id, ct)) r.Fail("Bu kod artıq var", nameof(f.Code));
            if (!r.Ok) return r;

            var c = f.Id == 0 ? new Campaign() : await _db.Campaigns.Include(x => x.Exams).FirstOrDefaultAsync(x => x.Id == f.Id, ct);
            if (c == null) return Result.Failure("Kampaniya tapılmadı");
            c.Name = f.Name!.Trim();
            c.Code = code;
            c.Type = Enum.IsDefined(f.Type) ? f.Type : DiscountType.Percent;
            c.Value = Math.Round(f.Value, 2);
            c.StartsOn = f.StartsOn!.Value.Date;
            c.EndsOn = f.EndsOn!.Value.Date;
            c.DepartmentId = f.DepartmentId;
            c.MaxUses = f.MaxUses;
            c.MaxUsesPerPatient = f.MaxUsesPerPatient;
            c.NewPatientsOnly = f.NewPatientsOnly;
            c.IsActive = f.IsActive;
            c.Note = string.IsNullOrWhiteSpace(f.Note) ? null : f.Note.Trim();
            var wanted = (f.ExamIds ?? new()).Distinct().ToHashSet();
            c.Exams.RemoveAll(x => !wanted.Contains(x.ExaminationTypeId));
            foreach (var id in wanted.Where(id => c.Exams.All(x => x.ExaminationTypeId != id)))
                c.Exams.Add(new CampaignExam { ExaminationTypeId = id });
            if (f.Id == 0) _db.Campaigns.Add(c);
            await _db.SaveChangesAsync(ct);
            _audit.Log(f.Id == 0 ? "Kampaniya yaradıldı" : "Kampaniya dəyişdirildi", AuditEntities.Loyalty, c.Id,
                $"{c.Name}{(c.Code != null ? $" [{c.Code}]" : "")}: {(c.Type == DiscountType.Percent ? $"{c.Value:0.##}%" : Fmt.Money(c.Value))}, {Fmt.Date(c.StartsOn)}–{Fmt.Date(c.EndsOn)}");
            return Result.Success($"«{c.Name}» saxlanıldı", c.Id);
        }

        public async Task<Result> SaveBenefitCategoryAsync(int id, string? name, decimal percent, bool active, string? note, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return Result.Failure("Adını yazın");
            if (percent is <= 0 or > 100) return Result.Failure("Faiz 0–100 olmalıdır");
            var c = id == 0 ? new BenefitCategory() : await _db.BenefitCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c == null) return Result.Failure("Kateqoriya tapılmadı");
            (c.Name, c.Percent, c.IsActive, c.Note) = (name.Trim(), percent, active, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
            if (id == 0) _db.BenefitCategories.Add(c);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Sosial güzəşt kateqoriyası saxlanıldı", AuditEntities.Loyalty, c.Id, $"{c.Name}: {c.Percent:0.##}%");
            return Result.Success($"«{c.Name}» saxlanıldı", c.Id);
        }

        public async Task<Result> SaveCompanyAsync(int id, string? name, string? contractNo, decimal percent, DateTime? from, DateTime? until, string? contact, bool active, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return Result.Failure("Şirkətin adını yazın");
            if (percent is < 0 or > 100) return Result.Failure("Faiz 0–100 olmalıdır");
            if (from != null && until != null && until < from) return Result.Failure("Müqavilənin son tarixi başlanğıcdan əvvəl ola bilməz");
            var c = id == 0 ? new Company() : await _db.Companies.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c == null) return Result.Failure("Şirkət tapılmadı");
            c.Name = name.Trim();
            c.ContractNo = string.IsNullOrWhiteSpace(contractNo) ? null : contractNo.Trim();
            c.DiscountPercent = percent;
            c.ContractFrom = from?.Date;
            c.ContractUntil = until?.Date;
            c.Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();
            c.IsActive = active;
            if (id == 0) _db.Companies.Add(c);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Korporativ müştəri saxlanıldı", AuditEntities.Loyalty, c.Id, $"{c.Name}: {c.DiscountPercent:0.##}%, müqavilə {c.ContractNo}");
            return Result.Success($"«{c.Name}» saxlanıldı", c.Id);
        }
    }

    /// <summary>A patient's social discounts and employer.</summary>
    public class PatientDiscounts
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;
        private readonly ICurrentUser _user;

        public PatientDiscounts(IAppDbContext db, AuditService audit, ICurrentUser user)
        {
            _db = db;
            _audit = audit;
            _user = user;
        }

        public async Task<Result> AddBenefitAsync(int patientId, int categoryId, string? documentNo, DateTime? validUntil, CancellationToken ct = default)
        {
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct);
            var category = await _db.BenefitCategories.FirstOrDefaultAsync(c => c.Id == categoryId && c.IsActive, ct);
            if (patient == null || category == null) return Result.Failure("Xəstə və ya güzəşt kateqoriyası tapılmadı");
            if (string.IsNullOrWhiteSpace(documentNo)) return Result.Failure("Sənədin nömrəsini yazın (vəsiqə, arayış)");
            if (validUntil != null && validUntil.Value.Date < ClinicTime.Today) return Result.Failure("Sənədin müddəti artıq bitib");
            if (await _db.PatientBenefits.AnyAsync(b => b.PatientId == patientId && b.CategoryId == categoryId && b.IsActive, ct))
                return Result.Failure("Bu güzəşt artıq qeyd olunub");
            var b = new PatientBenefit
            {
                PatientId = patientId, CategoryId = categoryId, DocumentNo = documentNo.Trim(), ValidUntil = validUntil?.Date,
                AddedAt = ClinicTime.Now, AddedBy = _user.UserName
            };
            _db.PatientBenefits.Add(b);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Sosial güzəşt əlavə edildi", AuditEntities.Patient, patientId,
                $"{patient.FullName}: {category.Name} ({category.Percent:0.##}%), sənəd {b.DocumentNo}, {(b.ValidUntil == null ? "müddətsiz" : $"{Fmt.Date(b.ValidUntil)}-dək")}");
            return Result.Success($"«{category.Name}» güzəşti əlavə edildi", b.Id);
        }

        public async Task<Result?> RemoveBenefitAsync(int id, CancellationToken ct = default)
        {
            var b = await _db.PatientBenefits.Include(x => x.Patient).Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b == null) return null;
            b.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Sosial güzəşt silindi", AuditEntities.Patient, b.PatientId, $"{b.Patient.FullName}: {b.Category.Name}");
            return Result.Success("Güzəşt silindi", b.PatientId);
        }

        /// <summary>Employer for the corporate discount; null company = not employed by a client company.</summary>
        public async Task<Result> SetCompanyAsync(int patientId, int? companyId, string? employeeNo, CancellationToken ct = default)
        {
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct);
            if (patient == null) return Result.Failure("Xəstə tapılmadı");
            var current = await _db.PatientCompanies.FirstOrDefaultAsync(x => x.PatientId == patientId, ct);
            Company? company = null;
            if (companyId != null && (company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId && c.IsActive, ct)) == null)
                return Result.Failure("Şirkət tapılmadı və ya aktiv deyil");
            if (company == null)
            {
                if (current != null) _db.PatientCompanies.Remove(current);
            }
            else
            {
                current ??= _db.PatientCompanies.Add(new PatientCompany { PatientId = patientId, AddedAt = ClinicTime.Now }).Entity;
                current.CompanyId = company.Id;
                current.EmployeeNo = string.IsNullOrWhiteSpace(employeeNo) ? null : employeeNo.Trim();
            }
            await _db.SaveChangesAsync(ct);
            _audit.Log("İş yeri (korporativ) dəyişdirildi", AuditEntities.Patient, patientId, $"{patient.FullName}: {company?.Name ?? "yoxdur"}");
            return Result.Success(company == null ? "Korporativ müqavilə çıxarıldı" : $"{company.Name} əməkdaşı kimi qeyd olundu", patientId);
        }
    }

    /// <summary>
    /// A payment / refund at the cashier: cash and card ones earn points (by the card level) and count toward the
    /// level; «Bonus bal» ones spend points (a refund gives them back). Idempotent per payment.
    /// </summary>
    public sealed class PointsOnPayment : Events.EventHandler<PaymentRecorded>
    {
        private readonly IAppDbContext _db;
        private readonly LoyaltyQueries _queries;

        public PointsOnPayment(IAppDbContext db, LoyaltyQueries queries)
        {
            _db = db;
            _queries = queries;
        }

        protected override async Task<string?> HandleAsync(PaymentRecorded e, CancellationToken ct)
        {
            if (await _db.LoyaltyTransactions.AnyAsync(t => t.PaymentKey == e.PaymentKey, ct)) return null;
            var view = await _queries.CardForPatientAsync(e.PatientId, ct);
            if (view == null) return e.Method == PaymentMethod.Points ? "Xəstənin loyallıq kartı yoxdur — bal silinmədi" : null;

            var value = _queries.Options.PointValue;
            var tx = new LoyaltyTransaction
            {
                CardId = view.Card.Id, PatientId = e.PatientId, ReserveId = e.ReserveId, PaymentKey = e.PaymentKey, At = ClinicTime.Now, By = e.User
            };
            if (e.Method == PaymentMethod.Points)
            {
                tx.Kind = PointsKind.Spend;
                tx.Points = -(int)Math.Round(e.Amount / value);
                tx.Note = e.Amount >= 0 ? "Ödənişdə istifadə" : "Bal ilə ödənişin qaytarılması";
            }
            else
            {
                if (!view.Card.IsActive) return null;
                tx.Kind = PointsKind.Earn;
                tx.Spend = e.Amount;
                var pct = view.Tier?.PointsPercent ?? 0;
                tx.Points = (int)Math.Round(e.Amount * pct / 100m / value);
                tx.Note = e.Amount >= 0 ? $"Ödəniş {Fmt.Money(e.Amount)}" : $"Qaytarma {Fmt.Money(-e.Amount)}";
            }
            _db.LoyaltyTransactions.Add(tx);
            await _db.SaveChangesAsync(ct);
            return null;
        }
    }
}
