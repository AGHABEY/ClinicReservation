using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    /// <summary>The home page: today's numbers and appointments (a doctor sees only their own).</summary>
    public class DashboardQueries
    {
        private readonly IAppDbContext _db;
        public DashboardQueries(IAppDbContext db) => _db = db;

        public async Task<DashboardViewModel> TodayAsync(string? userId, bool withMoney, CancellationToken ct = default)
        {
            var today = ClinicTime.Today;
            var tomorrow = today.AddDays(1);
            var dayAfter = today.AddDays(2);

            var myDoctor = userId == null ? null : await _db.Doctors.FirstOrDefaultAsync(d => d.UserId == userId, ct);
            var active = _db.Reserves.Where(r => r.Status == null
                || (r.Status.Name != StatusNames.Cancelled && r.Status.Name != StatusNames.NoShow));
            if (myDoctor != null) active = active.Where(r => r.DoctorId == myDoctor.Id);
            var todayQuery = active.Where(r => r.AppointmentAt >= today && r.AppointmentAt < tomorrow);

            var model = new DashboardViewModel
            {
                MyDoctor = myDoctor,
                TodayCount = await todayQuery.CountAsync(ct),
                WaitingRoomCount = await todayQuery.CountAsync(r => r.Status!.Name == StatusNames.Arrived, ct),
                InProgressCount = await todayQuery.CountAsync(r => r.Status!.Name == StatusNames.InProgress, ct),
                CompletedToday = await todayQuery.CountAsync(r => r.Status!.Name == StatusNames.Completed, ct),
                TomorrowCount = await active.CountAsync(r => r.AppointmentAt >= tomorrow && r.AppointmentAt < dayAfter, ct),
                WaitingReservations = await active.CountAsync(r => r.Status!.Name == StatusNames.Waiting && r.AppointmentAt >= today, ct),
                TodayAppointments = await todayQuery
                    .Include(r => r.Status)
                    .Include(r => r.Patient)
                    .Include(r => r.Doctor)
                    .Include(r => r.Room)
                    .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                    .OrderBy(r => r.AppointmentAt)
                    .ToListAsync(ct)
            };

            if (withMoney)
            {
                // Per-reservation due vs paid (so one patient's overpayment doesn't hide another's debt)
                var balances = await _db.Reserves
                    .Select(r => new
                    {
                        Due = r.Status != null && (r.Status.Name == StatusNames.Cancelled || r.Status.Name == StatusNames.NoShow)
                            ? 0
                            : r.ReserveExaminationTypes!.Sum(x => (decimal?)x.Price) ?? 0,
                        Paid = r.Payments!.Sum(p => (decimal?)p.Amount) ?? 0
                    })
                    .ToListAsync(ct);
                model.TotalIncome = balances.Sum(b => b.Paid);
                model.Outstanding = balances.Sum(b => b.Due - b.Paid > 0 ? b.Due - b.Paid : 0);
                model.TodayIncome = await _db.Payments.Where(p => p.PaidAt >= today && p.PaidAt < tomorrow).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            }
            return model;
        }
    }
}
