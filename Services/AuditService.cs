using System.Security.Claims;
using ClinicReservation.Data;
using ClinicReservation.Models;

namespace ClinicReservation.Services
{
    /// <summary>Writes audit entries. Call it after the change itself is saved.</summary>
    public class AuditService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _http;

        public AuditService(AppDbContext context, IHttpContextAccessor http)
        {
            _context = context;
            _http = http;
        }

        public void Log(string action, string? entityType = null, object? entityId = null, string? details = null, string? userName = null)
        {
            var http = _http.HttpContext;
            var user = http?.User;
            _context.AuditLogs.Add(new AuditLog
            {
                At = DateTime.Now,
                UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
                UserName = userName ?? user?.Identity?.Name,
                Action = action,
                EntityType = entityType,
                EntityId = entityId?.ToString(),
                Details = details is { Length: > 1000 } ? details[..1000] : details,
                IpAddress = http?.Connection.RemoteIpAddress?.ToString()
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
        public const string Doctor = "Həkim";
        public const string Catalog = "Sazlama";
        public const string User = "İstifadəçi";
        public const string Role = "Rol";
        public const string Auth = "Giriş";
    }
}
