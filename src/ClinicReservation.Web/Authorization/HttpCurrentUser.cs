using System.Security.Claims;
using ClinicReservation.Services;

namespace ClinicReservation.Authorization
{
    /// <summary>ICurrentUser for web requests: the signed-in user and the client IP.</summary>
    public sealed class HttpCurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _http;
        public HttpCurrentUser(IHttpContextAccessor http) => _http = http;

        public string? UserId => _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        public string? UserName => _http.HttpContext?.User.Identity?.Name;
        public string? IpAddress => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        public bool Can(string permission) => _http.HttpContext?.User is { } u && u.Can(permission);
    }
}
