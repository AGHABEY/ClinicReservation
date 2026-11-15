using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    public record DoctorLoad(int DoctorId, string Doctor, int WorkMinutes, int BookedMinutes, int AttendedMinutes, int Visits, int NoShows, int PastVisits,
        int? AvgWaitMinutes, int? AvgLateMinutes, double? Rating)
    {
        public int BookedPercent => WorkMinutes == 0 ? 0 : (int)Math.Round(BookedMinutes * 100.0 / WorkMinutes);
        public int AttendedPercent => WorkMinutes == 0 ? 0 : (int)Math.Round(AttendedMinutes * 100.0 / WorkMinutes);
        public int NoShowPercent => PastVisits == 0 ? 0 : (int)Math.Round(NoShows * 100.0 / PastVisits);
    }

    public record CountRow(string Label, int Count, string? Note = null);
    public record DiscountRow(string Label, int Lines, decimal Discount, decimal Revenue);

    public class SchedulingReportView
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int? DepartmentId { get; set; }
        public List<Department> Departments { get; set; } = new();

        public int Booked { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public int NoShows { get; set; }
        public int NoShowsAuto { get; set; }
        public int PastVisits { get; set; }
        public int NoShowPercent => PastVisits == 0 ? 0 : (int)Math.Round(NoShows * 100.0 / PastVisits);
        public int CancelPercent => Booked == 0 ? 0 : (int)Math.Round(Cancelled * 100.0 / Booked);

        public List<DoctorLoad> Doctors { get; set; } = new();
        public List<CountRow> CancelReasons { get; set; } = new();
        public List<CountRow> CancelParties { get; set; } = new();
        public List<CountRow> Sources { get; set; } = new();
        public List<CountRow> LeadTimes { get; set; } = new();
        public double? AvgLeadDays { get; set; }
        public int NewPatients { get; set; }
        public int ReturningPatients { get; set; }
        public List<DiscountRow> DiscountsByKind { get; set; } = new();
        public List<DiscountRow> Campaigns { get; set; } = new();
        public decimal ListTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public int? AvgWaitMinutes { get; set; }
        public int Overbooked { get; set; }
        public int FromWaitlist { get; set; }
        public int Referrals { get; set; }
        public int FollowUps { get; set; }
        public double? Rating { get; set; }
        public int Ratings { get; set; }
    }

    /// <summary>
    /// Qəbul hesabatı (F1): how full the doctors were, no-shows and cancellations, booking lead time, new vs returning
    /// patients, where bookings came from, what the discounts cost, how long patients waited, and their scores.
    /// </summary>
    public class SchedulingReportQueries
    {
        private readonly IAppDbContext _db;
        public SchedulingReportQueries(IAppDbContext db) => _db = db;

        public async Task<SchedulingReportView> LoadAsync(DateTime? from, DateTime? to, int? departmentId, CancellationToken ct = default)
        {
            var t = (to ?? ClinicTime.Today).Date;
            var f = (from ?? t.AddDays(-29)).Date;
            if (f > t) (f, t) = (t, f);
            if ((t - f).TotalDays > 366) f = t.AddDays(-366);
            var end = t.AddDays(1);
            var now = ClinicTime.Now;
            var v = new SchedulingReportView
            {
                From = f, To = t, DepartmentId = departmentId,
                Departments = await _db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct)
            };

            var q = _db.Reserves.AsNoTracking().Where(r => r.AppointmentAt >= f && r.AppointmentAt < end);
            if (departmentId != null) q = q.Where(r => r.Doctor != null && r.Doctor.DepartmentId == departmentId);
            var rows = await q.Select(r => new
            {
                r.Id, r.PatientId, r.DoctorId, Status = r.Status!.Name, r.AppointmentAt, r.AddedDate, r.DurationMinutes, r.ArrivedAt, r.StartedAt,
                r.Source, r.NoShowAuto, r.Overbooked, r.FollowUpOfId, Reason = r.CancellationReason != null ? r.CancellationReason.Name : null, r.CancelledBy
            }).ToListAsync(ct);

            var live = rows.Where(r => r.Status != StatusNames.Cancelled).ToList();
            var past = live.Where(r => r.AppointmentAt < now).ToList();
            v.Booked = rows.Count;
            v.Completed = rows.Count(r => r.Status == StatusNames.Completed);
            v.Cancelled = rows.Count(r => r.Status == StatusNames.Cancelled);
            v.NoShows = rows.Count(r => r.Status == StatusNames.NoShow);
            v.NoShowsAuto = rows.Count(r => r.Status == StatusNames.NoShow && r.NoShowAuto);
            v.PastVisits = past.Count;
            v.Overbooked = live.Count(r => r.Overbooked);
            v.FollowUps = live.Count(r => r.FollowUpOfId != null);

            // Cancellations: why and by whom.
            var cancelled = rows.Where(r => r.Status == StatusNames.Cancelled).ToList();
            v.CancelReasons = cancelled.GroupBy(r => r.Reason ?? "Səbəb göstərilməyib").Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList();
            v.CancelParties = cancelled.GroupBy(r => r.CancelledBy == null ? "qeyd olunmayıb" : BookingLabels.Party(r.CancelledBy)).Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList();

            // Where bookings came from; how far ahead they were made.
            v.Sources = rows.GroupBy(r => r.Source).Select(g => new CountRow(BookingLabels.Source(g.Key), g.Count())).OrderByDescending(x => x.Count).ToList();
            var leads = rows.Where(r => r.AddedDate != null && r.AddedDate <= r.AppointmentAt).Select(r => (r.AppointmentAt.Date - r.AddedDate!.Value.Date).TotalDays).ToList();
            v.AvgLeadDays = leads.Count == 0 ? null : Math.Round(leads.Average(), 1);
            v.LeadTimes = new List<CountRow>
            {
                new("Eyni gün", leads.Count(d => d < 1)),
                new("1–3 gün", leads.Count(d => d >= 1 && d <= 3)),
                new("4–7 gün", leads.Count(d => d >= 4 && d <= 7)),
                new("8–14 gün", leads.Count(d => d >= 8 && d <= 14)),
                new("15+ gün", leads.Count(d => d >= 15)),
            };

            // New vs returning: had a completed visit before this one?
            var patientIds = live.Select(r => r.PatientId).Distinct().ToList();
            var firstVisit = await _db.Reserves.AsNoTracking()
                .Where(r => patientIds.Contains(r.PatientId) && r.Status!.Name == StatusNames.Completed)
                .GroupBy(r => r.PatientId).Select(g => new { g.Key, First = g.Min(x => x.AppointmentAt) })
                .ToDictionaryAsync(x => x.Key, x => x.First, ct);
            foreach (var r in live)
            {
                if (firstVisit.TryGetValue(r.PatientId, out var firstAt) && firstAt < r.AppointmentAt) v.ReturningPatients++;
                else v.NewPatients++;
            }

            // Discounts: what each kind cost, and each campaign's uses and revenue.
            var ids = live.Select(r => r.Id).ToList();
            var lines = await _db.ReserveExaminationTypes.AsNoTracking().Where(l => ids.Contains(l.ReserveId))
                .Select(l => new { l.ListPrice, l.Discount, l.Price, l.DiscountKind, l.DiscountRefId }).ToListAsync(ct);
            v.ListTotal = lines.Sum(l => l.ListPrice);
            v.DiscountTotal = lines.Sum(l => l.Discount);
            v.DiscountsByKind = lines.Where(l => l.Discount > 0).GroupBy(l => l.DiscountKind)
                .Select(g => new DiscountRow(DiscountLabels.Kind(g.Key), g.Count(), g.Sum(x => x.Discount), g.Sum(x => x.Price)))
                .OrderByDescending(x => x.Discount).ToList();
            var campaignIds = lines.Where(l => l.DiscountKind is DiscountKind.Campaign or DiscountKind.Promo && l.DiscountRefId != null).Select(l => l.DiscountRefId!.Value).Distinct().ToList();
            var names = await _db.Campaigns.AsNoTracking().Where(c => campaignIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code == null ? c.Name : $"{c.Name} [{c.Code}]", ct);
            v.Campaigns = lines.Where(l => l.DiscountRefId != null && campaignIds.Contains(l.DiscountRefId.Value) && l.DiscountKind is DiscountKind.Campaign or DiscountKind.Promo)
                .GroupBy(l => l.DiscountRefId!.Value)
                .Select(g => new DiscountRow(names.GetValueOrDefault(g.Key, $"#{g.Key}"), g.Count(), g.Sum(x => x.Discount), g.Sum(x => x.Price)))
                .OrderByDescending(x => x.Revenue).ToList();

            // Waiting room: arrival → start of the visit.
            var waits = live.Where(r => r.ArrivedAt != null && r.StartedAt != null && r.StartedAt >= r.ArrivedAt)
                .Select(r => (r.StartedAt!.Value - r.ArrivedAt!.Value).TotalMinutes).ToList();
            v.AvgWaitMinutes = waits.Count == 0 ? null : (int)Math.Round(waits.Average());

            v.FromWaitlist = await _db.WaitlistEntries.CountAsync(w => w.ReserveId != null && ids.Contains(w.ReserveId.Value), ct);
            v.Referrals = await _db.Referrals.CountAsync(x => x.ReserveId != null && ids.Contains(x.ReserveId.Value), ct);

            var ratings = await _db.VisitRatings.AsNoTracking().Where(x => ids.Contains(x.ReserveId)).Select(x => new { x.DoctorId, x.Score }).ToListAsync(ct);
            v.Ratings = ratings.Count;
            v.Rating = ratings.Count == 0 ? null : Math.Round(ratings.Average(x => x.Score), 2);

            // Per doctor: working minutes in the period (hours − break − leave) against what was booked / attended.
            var doctors = await _db.Doctors.AsNoTracking().Include(d => d.WorkHours).Include(d => d.Leaves)
                .Where(d => d.IsActive && (departmentId == null || d.DepartmentId == departmentId)).OrderBy(d => d.FullName).ToListAsync(ct);
            foreach (var d in doctors)
            {
                var work = 0;
                for (var day = f; day <= t; day = day.AddDays(1))
                {
                    if (d.Leaves.Any(l => l.From.Date <= day && l.To.Date >= day)) continue;
                    var h = d.WorkHours.FirstOrDefault(w => w.Day == day.DayOfWeek);
                    if (h == null) continue;
                    var minutes = (h.End - h.Start).TotalMinutes;
                    if (h.BreakStart != null && h.BreakEnd != null) minutes -= Math.Max(0, (h.BreakEnd.Value - h.BreakStart.Value).TotalMinutes);
                    work += (int)Math.Max(0, minutes);
                }
                var mine = live.Where(r => r.DoctorId == d.Id).ToList();
                if (work == 0 && mine.Count == 0) continue;
                var minePast = mine.Where(r => r.AppointmentAt < now).ToList();
                var myWaits = mine.Where(r => r.ArrivedAt != null && r.StartedAt != null && r.StartedAt >= r.ArrivedAt).Select(r => (r.StartedAt!.Value - r.ArrivedAt!.Value).TotalMinutes).ToList();
                var late = mine.Where(r => r.StartedAt != null).Select(r => Math.Max(0, (r.StartedAt!.Value - r.AppointmentAt).TotalMinutes)).ToList();
                var myRatings = ratings.Where(x => x.DoctorId == d.Id).ToList();
                v.Doctors.Add(new DoctorLoad(d.Id, d.FullName, work,
                    mine.Sum(r => r.DurationMinutes),
                    mine.Where(r => r.Status is StatusNames.Completed or StatusNames.InProgress or StatusNames.Arrived).Sum(r => r.DurationMinutes),
                    mine.Count, minePast.Count(r => r.Status == StatusNames.NoShow), minePast.Count,
                    myWaits.Count == 0 ? null : (int)Math.Round(myWaits.Average()),
                    late.Count == 0 ? null : (int)Math.Round(late.Average()),
                    myRatings.Count == 0 ? null : Math.Round(myRatings.Average(x => x.Score), 2)));
            }
            v.Doctors = v.Doctors.OrderByDescending(x => x.BookedPercent).ToList();
            return v;
        }

        /// <summary>The per-doctor table as CSV (semicolon-separated, opens in Excel with Azerbaijani letters).</summary>
        public static string Csv(SchedulingReportView v)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Həkim;İş dəqiqəsi;Yazılış dəq;Doluluq %;Gəlib dəq;Faktiki %;Qəbul;Gəlmədi;Gəlməmə %;Orta gözləmə dəq;Orta gecikmə dəq;Reytinq");
            string Cell(string s) => s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            foreach (var d in v.Doctors)
                sb.AppendLine(string.Join(";", Cell(d.Doctor), d.WorkMinutes, d.BookedMinutes, d.BookedPercent, d.AttendedMinutes, d.AttendedPercent, d.Visits,
                    d.NoShows, d.NoShowPercent, d.AvgWaitMinutes?.ToString() ?? "", d.AvgLateMinutes?.ToString() ?? "",
                    d.Rating?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? ""));
            return sb.ToString();
        }
    }
}
