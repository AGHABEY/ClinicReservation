namespace ClinicReservation.Services
{
    /// <summary>Who is acting right now (the signed-in user in a web request, nobody in a background job).</summary>
    public interface ICurrentUser
    {
        string? UserId { get; }
        string? UserName { get; }
        string? IpAddress { get; }
    }

    /// <summary>For background jobs and tools: no user.</summary>
    public sealed class SystemUser : ICurrentUser
    {
        public string? UserId => null;
        public string? UserName => null;
        public string? IpAddress => null;
    }
}
