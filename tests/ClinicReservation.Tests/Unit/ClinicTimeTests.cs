namespace ClinicReservation.Tests.Unit
{
    public class ClinicTimeTests
    {
        [Fact]
        public void Clinic_time_comes_from_utc_and_the_clinic_zone_not_the_server_zone()
        {
            var baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
            ClinicTime.UseTimeZone(baku);
            var expected = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, baku);
            Assert.InRange((ClinicTime.Now - expected).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(5));
            Assert.Equal(ClinicTime.Now.Date, ClinicTime.Today);
        }
    }
}
