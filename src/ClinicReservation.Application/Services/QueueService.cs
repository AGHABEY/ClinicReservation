using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Services
{
    /// <summary>Queue tickets ("A-017") handed out at check-in.</summary>
    public class QueueService
    {
        public const string DefaultPrefix = "A";
        private readonly IAppDbContext _context;

        public QueueService(IAppDbContext context) => _context = context;

        /// <summary>Gives the reservation today's next ticket for its department prefix (once).</summary>
        public void AssignTicket(Reserve reserve, DateTime now)
        {
            if (reserve.QueueNumber != null && reserve.ArrivedAt?.Date == now.Date)
                return;

            var prefix = PrefixFor(reserve.DoctorId);
            var dayStart = now.Date;
            var dayEnd = dayStart.AddDays(1);
            var last = _context.Reserves
                .Where(r => r.Id != reserve.Id && r.QueueNumber != null && r.ArrivedAt >= dayStart && r.ArrivedAt < dayEnd
                            && r.QueueTicket!.StartsWith(prefix + "-"))
                .Max(r => r.QueueNumber) ?? 0;

            reserve.QueueNumber = last + 1;
            reserve.QueueTicket = $"{prefix}-{reserve.QueueNumber:000}";
            reserve.CalledAt = null;
        }

        private string PrefixFor(int? doctorId)
        {
            if (doctorId == null) return DefaultPrefix;
            var prefix = _context.Doctors.Where(d => d.Id == doctorId)
                .Select(d => d.Department == null ? null : d.Department.QueuePrefix)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix.Trim().ToUpperInvariant();
        }

        /// <summary>Patients waiting for a doctor today: not yet called first, then by ticket.</summary>
        public static IQueryable<Reserve> WaitingFor(IQueryable<Reserve> reserves, DateTime today) =>
            reserves.Where(r => r.Status!.Name == StatusNames.Arrived && r.AppointmentAt >= today && r.AppointmentAt < today.AddDays(1))
                .OrderBy(r => r.CalledAt != null).ThenBy(r => r.QueueNumber ?? int.MaxValue).ThenBy(r => r.ArrivedAt);
    }
}
