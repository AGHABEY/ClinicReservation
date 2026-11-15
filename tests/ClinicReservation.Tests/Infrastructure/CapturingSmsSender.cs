using System.Collections.Concurrent;
using ClinicReservation.Models;
using ClinicReservation.Modules.Notifications;

namespace ClinicReservation.Tests.Infrastructure
{
    /// <summary>Like the log sender (status «Simulated»), but keeps every text so tests can read e.g. the booking code.</summary>
    public sealed class CapturingSmsSender : ISmsSender
    {
        public ConcurrentQueue<(string Phone, string Text)> Sent { get; } = new();

        public Task<SmsSendResult> SendAsync(string phone, string text, CancellationToken ct = default)
        {
            Sent.Enqueue((phone, text));
            return Task.FromResult(new SmsSendResult(SmsStatus.Simulated));
        }

        /// <summary>The last text sent to this phone (normalized, e.g. 994501234567).</summary>
        public string? LastTo(string phone) => Sent.Where(s => s.Phone == phone).Select(s => s.Text).LastOrDefault();
    }
}
