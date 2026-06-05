namespace ClinicReservation.Models
{
    /// <summary>
    /// Application role names. "Staff" (İşçi) can take payments and manage
    /// reservations but cannot see aggregate kassa totals, reports, examinations
    /// or user management — those are Admin-only.
    /// </summary>
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Staff = "Isci";
    }
}
