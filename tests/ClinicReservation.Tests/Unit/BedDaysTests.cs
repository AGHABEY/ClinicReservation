using ClinicReservation.Models;
using ClinicReservation.Services;

namespace ClinicReservation.Tests.Unit
{
    public class BedDaysTests
    {
        private static readonly DateTime D = new(2026, 11, 10, 14, 0, 0);

        private static Admission Stay(params BedStay[] stays)
        {
            var a = new Admission { AdmittedAt = stays[0].From };
            foreach (var s in stays) { s.Bed = new Bed { Name = "1", Ward = new Ward { Name = "P" } }; a.Stays.Add(s); }
            return a;
        }

        [Fact]
        public void Nights_are_counted_per_ward_price()
        {
            var a = Stay(
                new BedStay { From = D, To = D.AddDays(2).AddHours(3), DailyPrice = 40 },   // 2 nights
                new BedStay { From = D.AddDays(2).AddHours(3), To = D.AddDays(5), DailyPrice = 55 }); // 3 nights
            var (_, days, total) = InpatientRules.BedDays(a, D.AddDays(10));
            Assert.Equal(5, days);
            Assert.Equal(2 * 40 + 3 * 55, total);
        }

        [Fact]
        public void Same_day_stay_counts_as_one_day()
        {
            var a = Stay(new BedStay { From = D, To = D.AddHours(5), DailyPrice = 40 });
            var (_, days, total) = InpatientRules.BedDays(a, D.AddHours(6));
            Assert.Equal(1, days);
            Assert.Equal(40, total);
        }

        [Fact]
        public void Open_stay_is_counted_until_now()
        {
            var a = Stay(new BedStay { From = D, To = null, DailyPrice = 30 });
            Assert.Equal(3, InpatientRules.BedDays(a, D.AddDays(3)).Days);
        }
    }
}
