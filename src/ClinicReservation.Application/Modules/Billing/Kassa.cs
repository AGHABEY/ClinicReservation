using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Billing
{
    /// <summary>Kassa: what each visit owes, taking payments and refunds (audited).</summary>
    public class Kassa
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public Kassa(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        // filter: unpaid | paid | refund | (empty = all)
        public async Task<KassaListViewModel> ListAsync(string? filter, string? q, CancellationToken ct = default)
        {
            var query = _db.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.ReserveExaminationTypes)
                .Include(r => r.Payments)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(r =>
                    (r.Patient.Name + " " + r.Patient.Surname).ToLower().Contains(term) ||
                    r.Patient.PinCode.ToLower().Contains(term) ||
                    r.Patient.Phone.ToLower().Contains(term));
            }

            var items = (await query.OrderByDescending(r => r.AppointmentAt).Take(500).AsSplitQuery().ToListAsync(ct))
                .Select(r => new KassaItemViewModel { Reserve = r, Total = r.AmountDue, Paid = r.Paid })
                .ToList();
            items = filter switch
            {
                "unpaid" => items.Where(i => i.PaymentStatus is PayStatus.Unpaid or PayStatus.Partial).ToList(),
                "paid" => items.Where(i => i.PaymentStatus == PayStatus.Paid).ToList(),
                "refund" => items.Where(i => i.PaymentStatus == PayStatus.Refund).ToList(),
                _ => items
            };
            return new KassaListViewModel
            {
                Items = items,
                Filter = filter,
                Query = q,
                TotalExpected = items.Sum(i => i.Total),
                TotalCollected = items.Sum(i => i.Paid),
                TotalOutstanding = items.Sum(i => i.Balance > 0 ? i.Balance : 0),
                TotalToRefund = items.Sum(i => i.Balance < 0 ? -i.Balance : 0)
            };
        }

        /// <summary>The payment page of one visit (null = not found); the amount defaults to the open balance.</summary>
        public async Task<PaymentViewModel?> PaymentPageAsync(int reserveId, CancellationToken ct = default)
        {
            var reserve = await LoadAsync(reserveId, ct);
            if (reserve == null) return null;
            var model = Build(reserve);
            model.Amount = Math.Abs(model.Balance);
            return model;
        }

        /// <summary>Takes a payment. Errors use PaymentViewModel field names; the page model comes back for redisplay.</summary>
        public async Task<(Result Result, PaymentViewModel? Page)> PayAsync(PaymentViewModel input, CancellationToken ct = default)
        {
            var reserve = await LoadAsync(input.ReserveId, ct);
            if (reserve == null) return (Result.Failure("Rezervasiya tapılmadı"), null);
            var model = Build(reserve);
            var amount = Math.Round(input.Amount, 2);
            var r = new Result();
            if (StatusNames.IsClosedWithoutVisit(reserve.Status?.Name))
                r.Fail($"«{StatusNames.Label(reserve.Status?.Name)}» rezervasiyaya ödəniş qəbul etmək olmaz");
            else if (amount <= 0)
                r.Fail("Məbləğ 0-dan böyük olmalıdır", nameof(input.Amount));
            else if (amount > model.Balance)
                r.Fail(model.Balance > 0 ? $"Məbləğ qalıq borcdan ({Fmt.Money(model.Balance)}) çox ola bilməz" : "Bu rezervasiya artıq tam ödənilib", nameof(input.Amount));
            if (!Enum.IsDefined(input.Method)) r.Fail("Ödəniş üsulu düzgün deyil", nameof(input.Method));
            if (!r.Ok) return (r, Keep(model, input));

            await AddAsync(reserve.Id, amount, input, ct);
            _audit.Log("Ödəniş qəbul edildi", AuditEntities.Payment, reserve.Id,
                $"{reserve.Patient.FullName}: {Fmt.Money(amount)} ({(input.Method == PaymentMethod.Cash ? "nağd" : "kart")})");
            return (Result.Success($"{Fmt.Money(amount)} ödəniş qeydə alındı", reserve.Id), null);
        }

        /// <summary>Gives money back when more was paid than is due (e.g. the reservation was cancelled).</summary>
        public async Task<(Result Result, PaymentViewModel? Page)> RefundAsync(PaymentViewModel input, CancellationToken ct = default)
        {
            var reserve = await LoadAsync(input.ReserveId, ct);
            if (reserve == null) return (Result.Failure("Rezervasiya tapılmadı"), null);
            var model = Build(reserve);
            var amount = Math.Round(input.Amount, 2);
            var refundable = -model.Balance;
            var r = new Result();
            if (refundable <= 0) r.Fail("Bu rezervasiya üzrə qaytarılacaq məbləğ yoxdur");
            else if (amount <= 0) r.Fail("Məbləğ 0-dan böyük olmalıdır", nameof(input.Amount));
            else if (amount > refundable) r.Fail($"Ən çox {Fmt.Money(refundable)} qaytarmaq olar", nameof(input.Amount));
            if (!Enum.IsDefined(input.Method)) r.Fail("Ödəniş üsulu düzgün deyil", nameof(input.Method));
            if (!r.Ok) return (r, Keep(model, input));

            await AddAsync(reserve.Id, -amount, input, ct);
            _audit.Log("Geri qaytarma", AuditEntities.Payment, reserve.Id,
                $"{reserve.Patient.FullName}: {Fmt.Money(amount)} ({(input.Method == PaymentMethod.Cash ? "nağd" : "kart")})");
            return (Result.Success($"{Fmt.Money(amount)} xəstəyə qaytarıldı", reserve.Id), null);
        }

        private async Task AddAsync(int reserveId, decimal amount, PaymentViewModel input, CancellationToken ct)
        {
            _db.Payments.Add(new Payment
            {
                ReserveId = reserveId,
                Amount = amount,
                Method = input.Method,
                PaidAt = ClinicTime.Now,
                Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
            });
            await _db.SaveChangesAsync(ct);
        }

        private static PaymentViewModel Keep(PaymentViewModel model, PaymentViewModel input)
        {
            model.Amount = input.Amount;
            model.Method = input.Method;
            model.Note = input.Note;
            return model;
        }

        private static PaymentViewModel Build(Reserve reserve)
        {
            var total = reserve.AmountDue;
            var paid = reserve.Paid;
            return new PaymentViewModel
            {
                Reserve = reserve,
                ReserveId = reserve.Id,
                Total = total,
                Paid = paid,
                Balance = total - paid,
                Payments = reserve.Payments?.OrderByDescending(p => p.PaidAt).ToList() ?? new()
            };
        }

        private Task<Reserve?> LoadAsync(int id, CancellationToken ct) =>
            _db.Reserves
                .Include(r => r.Status)
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(rt => rt.ExaminationType)
                .Include(r => r.Payments)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id, ct);
    }
}
