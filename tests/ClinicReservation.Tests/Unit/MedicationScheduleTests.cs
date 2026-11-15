using ClinicReservation.Models;

namespace ClinicReservation.Tests.Unit
{
    public class MedicationScheduleTests
    {
        private static readonly DateTime Day = new(2026, 11, 15);

        [Fact]
        public void Slots_follow_the_daily_times_in_order()
        {
            var o = new MedicationOrder { Times = "20:00;08:00", StartAt = Day.AddDays(-1) };
            Assert.Equal(new[] { Day.AddHours(8), Day.AddHours(20) }, o.SlotsOn(Day));
        }

        [Fact]
        public void First_day_skips_doses_before_the_order_but_keeps_one_due_within_the_hour()
        {
            var o = new MedicationOrder { Times = "08:00;14:00;20:00", StartAt = Day.AddHours(14).AddMinutes(30) };
            Assert.Equal(new[] { Day.AddHours(14), Day.AddHours(20) }, o.SlotsOn(Day));
        }

        [Fact]
        public void Stopped_and_finished_orders_have_no_more_slots()
        {
            var stopped = new MedicationOrder { Times = "08:00;20:00", StartAt = Day.AddDays(-2), StoppedAt = Day.AddHours(12) };
            Assert.Equal(new[] { Day.AddHours(8) }, stopped.SlotsOn(Day));

            var finished = new MedicationOrder { Times = "08:00", StartAt = Day.AddDays(-3), EndDate = Day.AddDays(-1) };
            Assert.Empty(finished.SlotsOn(Day));
        }

        [Fact]
        public void As_needed_order_has_no_schedule()
        {
            var prn = new MedicationOrder { Times = null, StartAt = Day.AddDays(-1) };
            Assert.True(prn.IsPrn);
            Assert.Empty(prn.SlotsOn(Day));
        }
    }
}
