using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalManagement.Models;

public class InvoiceItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [ForeignKey("InvoiceId")]
    public int InvoiceId { get; set; }

    public string Desctiption { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Invoice Invoice { get; set; }
}