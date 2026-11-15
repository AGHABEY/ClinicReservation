using ClinicReservation.Modules.Catalog;

namespace ClinicReservation.Services
{
    /// <summary>The logins (kept by ASP.NET Identity in Infrastructure), for linking a doctor profile.</summary>
    public interface IUserDirectory
    {
        Task<List<UserOption>> UsersAsync(CancellationToken ct = default);
    }
}
