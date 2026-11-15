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
        public const string Lab = "Laborant";

        public static readonly string[] BuiltIn = { Admin, Staff, Reception, Cashier, Doctor, Manager, Lab };

        public static string Label(string? role) => role switch
        {
            Admin => "Administrator",
            Staff => "İşçi",
            Reception => "Resepsiya",
            Cashier => "Kassir",
            Doctor => "Həkim",
            Manager => "Rəhbərlik",
            Lab => "Laborant",
            _ => role ?? "—"
        };

        /// <summary>
        /// Permissions given to a built-in role the first time it is created.
        /// A permission added in a later version is also granted once to the
        /// roles listed here (see SeedData), as long as no role has it yet.
        /// </summary>
        public static string[] DefaultPermissions(string role) => role switch
        {
            Staff => new[]
            {
                Permissions.PatientsView, Permissions.PatientsEdit, Permissions.ReservationsView,
                Permissions.ReservationsManage, Permissions.ReservationsStatus, Permissions.PaymentsTake,
                Permissions.CatalogManage, Permissions.QueueView, Permissions.LabView
            },
            Reception => new[]
            {
                Permissions.PatientsView, Permissions.PatientsEdit, Permissions.ReservationsView,
                Permissions.ReservationsManage, Permissions.ReservationsStatus, Permissions.PaymentsTake,
                Permissions.QueueView, Permissions.QueueCall, Permissions.SmsManage, Permissions.LabView
            },
            Cashier => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView, Permissions.PaymentsTake,
                Permissions.PaymentsRefund, Permissions.KassaTotals
            },
            Doctor => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView, Permissions.ReservationsStatus,
                Permissions.QueueView, Permissions.QueueCall, Permissions.RecordsView, Permissions.RecordsEdit,
                Permissions.LabView
            },
            Lab => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView,
                Permissions.LabView, Permissions.LabWork, Permissions.LabValidate
            },
            Manager => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView, Permissions.KassaTotals,
                Permissions.ReportsView, Permissions.AuditView, Permissions.QueueView
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
        public const string QueueView = "queue.view";
        public const string QueueCall = "queue.call";
        public const string RecordsView = "records.view";
        public const string RecordsEdit = "records.edit";
        public const string SmsManage = "sms.manage";
        public const string LabView = "lab.view";
        public const string LabWork = "lab.work";
        public const string LabValidate = "lab.validate";

        public record Info(string Code, string Group, string Label);

        public static readonly Info[] All =
        {
            new(PatientsView, "Xəstələr", "Xəstə kartlarına baxmaq"),
            new(PatientsEdit, "Xəstələr", "Xəstə məlumatlarını dəyişmək"),
            new(ReservationsView, "Rezervasiyalar", "Rezervasiyalara baxmaq"),
            new(ReservationsManage, "Rezervasiyalar", "Yaratmaq, dəyişmək, təsdiq və ləğv etmək"),
            new(ReservationsStatus, "Rezervasiyalar", "Qəbula başlamaq və tamamlamaq"),
            new(QueueView, "Növbə", "Növbə ekranı (gözləmə zalı)"),
            new(QueueCall, "Növbə", "Həkim iş masası, xəstəni çağırmaq"),
            new(RecordsView, "Tibbi kart", "Tibbi qeydlərə və diaqnozlara baxmaq"),
            new(RecordsEdit, "Tibbi kart", "Tibbi qeyd yazmaq, allergiyaları dəyişmək"),
            new(LabView, "Laboratoriya", "Analiz nəticələrinə baxmaq və çap etmək"),
            new(LabWork, "Laboratoriya", "Nümunə götürmək, nəticə yazmaq"),
            new(LabValidate, "Laboratoriya", "Nəticəni təsdiqləmək və xəstəyə göndərmək"),
            new(PaymentsTake, "Kassa", "Ödəniş qəbul etmək"),
            new(PaymentsRefund, "Kassa", "Geri qaytarma etmək"),
            new(KassaTotals, "Kassa", "Kassa cəmlərini və məbləğləri görmək"),
            new(ReportsView, "Rəhbərlik", "Hesabatlar"),
            new(AuditView, "Rəhbərlik", "Audit jurnalı"),
            new(CatalogManage, "Sazlamalar", "Həkimlər, şöbələr, kabinetlər, müayinələr, analiz kataloqu"),
            new(UsersManage, "Sazlamalar", "İstifadəçilər və rollar"),
            new(SmsManage, "Sazlamalar", "SMS jurnalı, xatırlatma göndərmək"),
        };
    }
}
