using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalManagement.Models;

public class Invoice
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int AppointmentId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime InvoiceDate { get; set; }
    public bool Status { get; set; }
    public Appointment Appointment { get; set; }
    public ICollection<InvoiceItem> InvoiceItems { get; set; }

}