using ClinicReservation.Data;
using ClinicReservation.Events;
using ClinicReservation.Models;

namespace ClinicReservation.Modules.Notifications
{
    /// <summary>Texts a waiting patient that an earlier time is free.</summary>
    public sealed class SmsOnWaitlistOffered : Events.EventHandler<WaitlistOffered>
    {
        private readonly SmsService _sms;
        private readonly IAppDbContext _db;
        public SmsOnWaitlistOffered(SmsService sms, IAppDbContext db) { _sms = sms; _db = db; }

        protected override async Task<string?> HandleAsync(WaitlistOffered e, CancellationToken ct)
        {
            var patient = await _db.Patients.FindAsync(new object[] { e.PatientId }, ct);
            if (patient == null) return null;
            var text = _sms.WaitlistText(patient.Name, e.Slot, e.DoctorName);
            // Seen twice (worker retry): the same offer is texted once.
            if (_db.SmsMessages.Any(m => m.PatientId == e.PatientId && m.Kind == SmsService.KindWaitlist && m.Text == text && m.Status != SmsStatus.Failed))
                return null;
            var msg = await _sms.SendToPatientAsync(e.PatientId, text, SmsService.KindWaitlist, e.User, ct);
            return msg is { Status: SmsStatus.Failed } ? $"Gözləmə siyahısı SMS-i göndərilmədi: {msg.Error}" : null;
        }
    }

    /// <summary>Texts the patient that their follow-up is due.</summary>
    public sealed class SmsOnFollowUpReminder : Events.EventHandler<FollowUpReminder>
    {
        private readonly SmsService _sms;
        private readonly IAppDbContext _db;
        public SmsOnFollowUpReminder(SmsService sms, IAppDbContext db) { _sms = sms; _db = db; }

        protected override async Task<string?> HandleAsync(FollowUpReminder e, CancellationToken ct)
        {
            var patient = await _db.Patients.FindAsync(new object[] { e.PatientId }, ct);
            if (patient == null) return null;
            var text = _sms.FollowUpText(patient.Name, e.DueDate, e.DoctorName);
            // Seen twice (worker retry): the same reminder is texted once.
            if (_db.SmsMessages.Any(m => m.PatientId == e.PatientId && m.Kind == SmsService.KindFollowUp && m.Text == text && m.Status != SmsStatus.Failed))
                return null;
            var msg = await _sms.SendToPatientAsync(e.PatientId, text, SmsService.KindFollowUp, e.User, ct);
            return msg is { Status: SmsStatus.Failed } ? $"{patient.FullName}: SMS göndərilmədi ({msg.Error})" : null;
        }
    }
}
