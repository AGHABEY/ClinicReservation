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
        public const string Storekeeper = "Anbardar";
        public const string Nurse = "TibbBacisi";

        public static readonly string[] BuiltIn = { Admin, Staff, Reception, Cashier, Doctor, Manager, Lab, Storekeeper, Nurse };

        public static string Label(string? role) => role switch
        {
            Admin => "Administrator",
            Staff => "İşçi",
            Reception => "Resepsiya",
            Cashier => "Kassir",
            Doctor => "Həkim",
            Manager => "Rəhbərlik",
            Lab => "Laborant",
            Storekeeper => "Anbardar / aptek",
            Nurse => "Tibb bacısı",
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
                Permissions.CatalogManage, Permissions.QueueView, Permissions.LabView,
                Permissions.StockView, Permissions.StockIssue, Permissions.InpatientView, Permissions.InpatientManage
            },
            Reception => new[]
            {
                Permissions.PatientsView, Permissions.PatientsEdit, Permissions.ReservationsView,
                Permissions.ReservationsManage, Permissions.ReservationsStatus, Permissions.PaymentsTake,
                Permissions.QueueView, Permissions.QueueCall, Permissions.SmsManage, Permissions.LabView,
                Permissions.InpatientView, Permissions.InpatientManage, Permissions.ReservationsOverbook
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
                Permissions.LabView, Permissions.InpatientView, Permissions.InpatientDoctor
            },
            Nurse => new[]
            {
                Permissions.PatientsView, Permissions.RecordsView, Permissions.LabView,
                Permissions.InpatientView, Permissions.InpatientNurse, Permissions.StockView, Permissions.StockIssue
            },
            Lab => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView,
                Permissions.LabView, Permissions.LabWork, Permissions.LabValidate,
                Permissions.StockView, Permissions.StockIssue
            },
            Storekeeper => new[]
            {
                Permissions.PatientsView, Permissions.StockView, Permissions.StockIssue, Permissions.StockManage
            },
            Manager => new[]
            {
                Permissions.PatientsView, Permissions.ReservationsView, Permissions.KassaTotals,
                Permissions.ReportsView, Permissions.AuditView, Permissions.QueueView, Permissions.StockView,
                Permissions.InpatientView
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
        public const string ReservationsOverbook = "reservations.overbook";
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
        public const string StockView = "stock.view";
        public const string StockIssue = "stock.issue";
        public const string StockManage = "stock.manage";
        public const string InpatientView = "inpatient.view";
        public const string InpatientManage = "inpatient.manage";
        public const string InpatientDoctor = "inpatient.doctor";
        public const string InpatientNurse = "inpatient.nurse";

        public record Info(string Code, string Group, string Label);

        public static readonly Info[] All =
        {
            new(PatientsView, "Xəstələr", "Xəstə kartlarına baxmaq"),
            new(PatientsEdit, "Xəstələr", "Xəstə məlumatlarını dəyişmək"),
            new(ReservationsView, "Rezervasiyalar", "Rezervasiyalara baxmaq"),
            new(ReservationsManage, "Rezervasiyalar", "Yaratmaq, dəyişmək, təsdiq və ləğv etmək"),
            new(ReservationsStatus, "Rezervasiyalar", "Qəbula başlamaq və tamamlamaq"),
            new(ReservationsOverbook, "Rezervasiyalar", "Dolu saata əlavə qəbul yazmaq (araya salmaq, həkimin gündəlik limiti daxilində)"),
            new(QueueView, "Növbə", "Növbə ekranı (gözləmə zalı)"),
            new(QueueCall, "Növbə", "Həkim iş masası, xəstəni çağırmaq"),
            new(RecordsView, "Tibbi kart", "Tibbi qeydlərə və diaqnozlara baxmaq"),
            new(RecordsEdit, "Tibbi kart", "Tibbi qeyd yazmaq, allergiyaları dəyişmək"),
            new(LabView, "Laboratoriya", "Analiz nəticələrinə baxmaq və çap etmək"),
            new(LabWork, "Laboratoriya", "Nümunə götürmək, nəticə yazmaq"),
            new(LabValidate, "Laboratoriya", "Nəticəni təsdiqləmək və xəstəyə göndərmək"),
            new(InpatientView, "Stasionar", "Çarpayı xəritəsinə və xəstəlik tarixinə baxmaq"),
            new(InpatientManage, "Stasionar", "Yerləşdirmək, çarpayını dəyişmək, palataları sazlamaq"),
            new(InpatientDoctor, "Stasionar", "Həkim gündəliyi, dərman təyinatı, çıxarış və epikriz"),
            new(InpatientNurse, "Stasionar", "Vital göstəricilər, tibb bacısı qeydi, dərmanın icrası"),
            new(StockView, "Aptek və anbar", "Qalıqlara, son istifadə tarixlərinə baxmaq"),
            new(StockIssue, "Aptek və anbar", "Material silmək, xəstəyə dərman vermək"),
            new(StockManage, "Aptek və anbar", "Mədaxil, köçürmə, inventarizasiya, məhsul kataloqu, alış qiymətləri"),
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
