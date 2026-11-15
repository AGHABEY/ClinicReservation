using System.Security.Claims;
using ClinicReservation.Authorization;
using ClinicReservation.Models;

namespace ClinicReservation.Services
{
    /// <summary>
    /// The appointment lifecycle:
    /// Gözləyir → Təsdiqləndi → Gəldi → Qəbulda → Tamamlandı,
    /// with Gəlmədi (after the time has passed) and Ləğv as exits.
    /// </summary>
    public static class ReserveFlow
    {
        public record Step(string To, string Label, string Icon, string Css, string Permission);

        private static readonly Dictionary<string, Step[]> Next = new()
        {
            [StatusNames.Waiting] = new[]
            {
                new Step(StatusNames.Confirmed, "Təsdiqlə", "bi-check2-circle", "btn-success", Permissions.ReservationsManage),
                new Step(StatusNames.Arrived, "Gəldi", "bi-person-check", "btn-ghost", Permissions.ReservationsManage),
                new Step(StatusNames.NoShow, "Gəlmədi", "bi-person-x", "btn-light", Permissions.ReservationsManage),
            },
            [StatusNames.Confirmed] = new[]
            {
                new Step(StatusNames.Arrived, "Gəldi", "bi-person-check", "btn-success", Permissions.ReservationsManage),
                new Step(StatusNames.NoShow, "Gəlmədi", "bi-person-x", "btn-light", Permissions.ReservationsManage),
            },
            [StatusNames.Arrived] = new[]
            {
                new Step(StatusNames.InProgress, "Qəbula başla", "bi-play-circle", "btn-success", Permissions.ReservationsStatus),
                new Step(StatusNames.Completed, "Tamamla", "bi-check2-all", "btn-light", Permissions.ReservationsStatus),
            },
            [StatusNames.InProgress] = new[]
            {
                new Step(StatusNames.Completed, "Tamamla", "bi-check2-all", "btn-success", Permissions.ReservationsStatus),
            },
        };

        /// <summary>Actions the user may take now (no-show only once the time has passed).</summary>
        public static IEnumerable<Step> Actions(Reserve r, ClaimsPrincipal user, DateTime now)
        {
            if (!Next.TryGetValue(r.Status?.Name ?? StatusNames.Waiting, out var steps))
                return Enumerable.Empty<Step>();
            return steps.Where(s => user.Can(s.Permission) && (s.To != StatusNames.NoShow || r.AppointmentAt <= now));
        }

        public static bool IsAllowed(Reserve r, string to, ClaimsPrincipal user, DateTime now) =>
            Actions(r, user, now).Any(s => s.To == to);

        public static bool CanEdit(Reserve r) =>
            r.Status?.Name is null or StatusNames.Waiting or StatusNames.Confirmed or StatusNames.Arrived or StatusNames.InProgress;

        /// <summary>Time/doctor can only move before the patient has arrived.</summary>
        public static bool CanReschedule(Reserve r) =>
            r.Status?.Name is null or StatusNames.Waiting or StatusNames.Confirmed;

        public static bool CanCancel(Reserve r) =>
            r.Status?.Name is null or StatusNames.Waiting or StatusNames.Confirmed or StatusNames.Arrived;

        /// <summary>Statuses that still hold the slot and are "upcoming / today's work".</summary>
        public static readonly string[] Active =
            { StatusNames.Waiting, StatusNames.Confirmed, StatusNames.Arrived, StatusNames.InProgress };
    }
}
