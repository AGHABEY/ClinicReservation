namespace ClinicReservation.Models
{
    /// <summary>Who goes first in a queue. Urgent before everyone; the others before ordinary tickets.</summary>
    public enum QueuePriority { None = 0, Pregnant = 1, Elderly = 2, Disability = 3, Urgent = 4 }

    public enum DeskKind { Reception = 1, Cashier = 2 }

    public enum DeskTicketStatus { Waiting = 1, Called = 2, Served = 3, Missed = 4 }

    /// <summary>A reception or cashier window («Pəncərə 2»).</summary>
    public class ServiceWindow : BaseModel
    {
        public string Name { get; set; } = "";
        public DeskKind Kind { get; set; } = DeskKind.Reception;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A ticket for the reception / cashier queue (not tied to a doctor): «R-004 → Pəncərə 2».</summary>
    public class DeskTicket : BaseModel
    {
        public DeskKind Kind { get; set; }
        public int Number { get; set; }
        public string Ticket { get; set; } = "";
        public QueuePriority Priority { get; set; }
        public DateTime IssuedAt { get; set; }
        /// <summary>«Kiosk» or the user who printed it.</summary>
        public string? IssuedBy { get; set; }
        public DeskTicketStatus Status { get; set; } = DeskTicketStatus.Waiting;
        public int? WindowId { get; set; }
        public ServiceWindow? Window { get; set; }
        public DateTime? CalledAt { get; set; }
        public DateTime? ServedAt { get; set; }
        public string? ServedBy { get; set; }
    }

    public static class QueueLabels
    {
        public static string Priority(QueuePriority p) => p switch
        {
            QueuePriority.Pregnant => "Hamilə",
            QueuePriority.Elderly => "65+",
            QueuePriority.Disability => "Əlillik",
            QueuePriority.Urgent => "Təcili",
            _ => "Adi"
        };

        public static string Desk(DeskKind k) => k == DeskKind.Cashier ? "Kassa" : "Registratura";

        /// <summary>Queue order: urgent first, then the other priorities, then everyone else.</summary>
        public static int Rank(QueuePriority p) => p switch { QueuePriority.Urgent => 0, QueuePriority.None => 2, _ => 1 };

        public const int ElderlyAge = 65;
    }
}
