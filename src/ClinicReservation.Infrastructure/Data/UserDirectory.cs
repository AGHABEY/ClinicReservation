using ClinicReservation.Modules.Catalog;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Data
{
    public sealed class UserDirectory : IUserDirectory
    {
        private readonly AppDbContext _db;
        public UserDirectory(AppDbContext db) => _db = db;

        public Task<List<UserOption>> UsersAsync(CancellationToken ct = default) =>
            _db.AppUsers.OrderBy(u => u.UserName).Select(u => new UserOption(u.Id, u.UserName!, u.FullName)).ToListAsync(ct);
    }
}
