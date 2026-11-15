using System.Security.Claims;
using ClinicReservation.Models;
using ClinicReservation.Services;

namespace ClinicReservation.Tests.Unit
{
    public class ReserveFlowTests
    {
        private static ClaimsPrincipal User(params string[] permissions) =>
            new(new ClaimsIdentity(permissions.Select(p => new Claim(Permissions.ClaimType, p)), "test"));

        private static Reserve At(string status, DateTime when) => new() { AppointmentAt = when, Status = new ReserveStatus { Name = status } };

        [Fact]
        public void Reception_confirms_but_cannot_complete()
        {
            var user = User(Permissions.ReservationsManage);
            var now = ClinicTime.Now;
            Assert.True(ReserveFlow.IsAllowed(At(StatusNames.Waiting, now.AddHours(2)), StatusNames.Confirmed, user, now));
            Assert.False(ReserveFlow.IsAllowed(At(StatusNames.Arrived, now), StatusNames.Completed, user, now));
        }

        [Fact]
        public void No_show_only_after_the_appointment_time()
        {
            var user = User(Permissions.ReservationsManage);
            var now = ClinicTime.Now;
            Assert.False(ReserveFlow.IsAllowed(At(StatusNames.Confirmed, now.AddMinutes(30)), StatusNames.NoShow, user, now));
            Assert.True(ReserveFlow.IsAllowed(At(StatusNames.Confirmed, now.AddMinutes(-30)), StatusNames.NoShow, user, now));
        }

        [Fact]
        public void Late_arrival_after_no_show_only_the_same_day()
        {
            var user = User(Permissions.ReservationsManage);
            var now = new DateTime(2026, 11, 16, 15, 0, 0);
            Assert.True(ReserveFlow.IsAllowed(At(StatusNames.NoShow, now.AddHours(-3)), StatusNames.Arrived, user, now));
            Assert.False(ReserveFlow.IsAllowed(At(StatusNames.NoShow, now.AddDays(-1)), StatusNames.Arrived, user, now));
            Assert.False(ReserveFlow.IsAllowed(At(StatusNames.NoShow, now.AddHours(-3)), StatusNames.Completed, user, now));
        }

        [Fact]
        public void Completed_visit_has_no_further_steps() =>
            Assert.Empty(ReserveFlow.Actions(At(StatusNames.Completed, ClinicTime.Now), User(Permissions.ReservationsManage, Permissions.ReservationsStatus), ClinicTime.Now));

        [Fact]
        public void Admin_role_can_do_everything()
        {
            var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, AppRoles.Admin) }, "test"));
            Assert.True(ReserveFlow.IsAllowed(At(StatusNames.InProgress, ClinicTime.Now), StatusNames.Completed, admin, ClinicTime.Now));
        }
    }
}
