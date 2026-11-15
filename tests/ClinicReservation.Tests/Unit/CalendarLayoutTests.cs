using ClinicReservation.Modules.Scheduling;

namespace ClinicReservation.Tests.Unit
{
    public class CalendarLayoutTests
    {
        private static CalendarItem At(int id, int hour, int minute, int minutes, bool extra = false) =>
            new(id, 1, new DateTime(2026, 11, 20, hour, minute, 0), minutes, $"P{id}", null, "", true, extra);

        [Fact]
        public void Overlapping_visits_share_the_column_and_others_keep_the_full_width()
        {
            var normal = At(1, 9, 0, 30);
            var extra = At(2, 9, 0, 30, extra: true);
            var later = At(3, 9, 15, 30);   // overlaps both → third lane
            var alone = At(4, 11, 0, 30);
            var items = new List<CalendarItem> { extra, alone, later, normal };

            CalendarQueries.Lay(items);

            Assert.Equal((0, 3), (normal.Lane, normal.Lanes)); // the regular visit keeps the first lane
            Assert.Equal((1, 3), (extra.Lane, extra.Lanes));
            Assert.Equal((2, 3), (later.Lane, later.Lanes));
            Assert.Equal((0, 1), (alone.Lane, alone.Lanes));
        }

        [Fact]
        public void A_lane_is_reused_once_its_visit_has_ended()
        {
            var a = At(1, 9, 0, 60);
            var b = At(2, 9, 0, 30);
            var c = At(3, 9, 30, 30);   // b has ended → c takes b's lane
            CalendarQueries.Lay(new List<CalendarItem> { a, b, c });
            Assert.Equal(new[] { 0, 1, 1 }, new[] { a.Lane, b.Lane, c.Lane });
            Assert.All(new[] { a, b, c }, x => Assert.Equal(2, x.Lanes));
        }
    }
}
