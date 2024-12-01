using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalManagement.Models;

public class Payment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int id { get; set; }

    [Required]
    [ForeignKey("Appointment")]
    public int AppointmentId { get; set; }

    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice Invoice { get; set; }

    public Appointment Appointment { get; set; }
}