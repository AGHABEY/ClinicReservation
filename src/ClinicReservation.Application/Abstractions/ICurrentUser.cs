namespace ClinicReservation.Services
{
    /// <summary>Who is acting right now (the signed-in user in a web request, nobody in a background job).</summary>
    public interface ICurrentUser
    {
        string? UserId { get; }
        string? UserName { get; }
        string? IpAddress { get; }
        /// <summary>Has this permission (Admin has all). Use cases check rules that go beyond the page's own permission.</summary>
        bool Can(string permission);
    }

    /// <summary>For background jobs and tools: no user.</summary>
    public sealed class SystemUser : ICurrentUser
    {
        public string? UserId => null;
        public string? UserName => null;
        public string? IpAddress => null;
        public bool Can(string permission) => false;
    }
}
