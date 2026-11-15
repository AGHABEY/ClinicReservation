using ClinicReservation.Data;
using ClinicReservation.Models;

namespace ClinicReservation.Services
{
    /// <summary>Writes audit entries. Call it after the change itself is saved.</summary>
    public class AuditService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUser _user;

        public AuditService(IAppDbContext context, ICurrentUser user)
        {
            _context = context;
            _user = user;
        }

        public void Log(string action, string? entityType = null, object? entityId = null, string? details = null, string? userName = null)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                At = ClinicTime.Now,
                UserId = _user.UserId,
                UserName = userName ?? _user.UserName,
                Action = action,
                EntityType = entityType,
                EntityId = entityId?.ToString(),
                Details = details is { Length: > 1000 } ? details[..1000] : details,
                IpAddress = _user.IpAddress
            });
            _context.SaveChanges();
        }
    }

    /// <summary>Audit entity type names (shown in the audit page filter).</summary>
    public static class AuditEntities
    {
        public const string Patient = "Xəstə";
        public const string Reservation = "Rezervasiya";
        public const string Payment = "Ödəniş";
        public const string MedicalRecord = "Tibbi qeyd";
        public const string Sms = "SMS";
        public const string Lab = "Laboratoriya";
        public const string Stock = "Anbar";
        public const string Inpatient = "Stasionar";
        public const string Waitlist = "Gözləmə siyahısı";
        public const string Doctor = "Həkim";
        public const string Catalog = "Sazlama";
        public const string User = "İstifadəçi";
        public const string Role = "Rol";
        public const string Auth = "Giriş";
    }
}
