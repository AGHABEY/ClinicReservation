namespace ClinicReservation.Models
{
    /// <summary>
    /// Built-in roles. What each role may do is NOT hard-coded: it is a set of
    /// permission claims on the role (editable on the Roles page). Admin always
    /// has every permission so the system can't be locked out.
    /// </summary>
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Staff = "Isci";
        public const string Reception = "Resepsiya";
        public const string Cashier = "Kassir";
        public const string Doctor = "Hekim";
        public const string Manager = "Rehber";

        public static readonly string[] BuiltIn = { Admin, Staff, Reception, Cashier, Doctor, Manager };

        public static string Label(string? role) => role switch
        {
            Admin => "Administrator",
            Staff => "İşçi",
            Reception => "Resepsiya",
            Cashier => "Kassir",
            Doctor => "Həkim",
            Manager => "Rəhbərlik",
            _ => role ?? "—"
        };

        /// <summary>Permissions given to a built-in role the first time it is created.</summary>
        public static string[] DefaultPermissions(string role) => role switch
        {
            Staff => new[]
            {
                Permissions.PatientsView, Permissions.PatientsEdit, Permissions.ReservationsView,
                Permissions.ReservationsManage, Permissions.ReservationsStatus, Permissions.PaymentsTake,
                Permissions.CatalogManage
            },
            Reception => new[]
            {
                Permissions.PatientsView, Permissions.PatientsEdit, Permissions.ReservationsView,
                Permissions.ReservationsManage, Permissions.ReservationsStatus, Permissions.PaymentsTake
            },
            Cashier => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView, Permissions.PaymentsTake,
                Permissions.PaymentsRefund, Permissions.KassaTotals
            },
            Doctor => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView, Permissions.ReservationsStatus
            },
            Manager => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView, Permissions.KassaTotals,
                Permissions.ReportsView, Permissions.AuditView
            },
            _ => Array.Empty<string>()
        };
    }

    public static class Permissions
    {
        public const string ClaimType = "permission";

        public const string PatientsView = "patients.view";
        public const string PatientsEdit = "patients.edit";
        public const string ReservationsView = "reservations.view";
        public const string ReservationsManage = "reservations.manage";
        public const string ReservationsStatus = "reservations.status";
        public const string PaymentsTake = "payments.take";
        public const string PaymentsRefund = "payments.refund";
        public const string KassaTotals = "kassa.totals";
        public const string ReportsView = "reports.view";
        public const string CatalogManage = "catalog.manage";
        public const string UsersManage = "users.manage";
        public const string AuditView = "audit.view";

        public record Info(string Code, string Group, string Label);

        public static readonly Info[] All =
        {
            new(PatientsView, "Xəstələr", "Xəstə kartlarına baxmaq"),
            new(PatientsEdit, "Xəstələr", "Xəstə məlumatlarını dəyişmək"),
            new(ReservationsView, "Rezervasiyalar", "Rezervasiyalara baxmaq"),
            new(ReservationsManage, "Rezervasiyalar", "Yaratmaq, dəyişmək, təsdiq və ləğv etmək"),
            new(ReservationsStatus, "Rezervasiyalar", "Qəbula başlamaq və tamamlamaq"),
            new(PaymentsTake, "Kassa", "Ödəniş qəbul etmək"),
            new(PaymentsRefund, "Kassa", "Geri qaytarma etmək"),
            new(KassaTotals, "Kassa", "Kassa cəmlərini və məbləğləri görmək"),
            new(ReportsView, "Rəhbərlik", "Hesabatlar"),
            new(AuditView, "Rəhbərlik", "Audit jurnalı"),
            new(CatalogManage, "Sazlamalar", "Həkimlər, şöbələr, kabinetlər, müayinələr"),
            new(UsersManage, "Sazlamalar", "İstifadəçilər və rollar"),
        };
    }
}
