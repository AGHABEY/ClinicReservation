using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalManagement.Models;

public class Appointment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public DateTime AppointmentDate { get; set; }
    [ForeignKey("Doctor")]
    public int DoctorId { get; set; }
    [ForeignKey("Patient")]
    public int PatientId { get; set; }

    public string Notes { get; set; }
    public virtual Doctor? Doctor { get; set; }
    public virtual Patient? Patient { get; set; }

    public virtual Payment? Payment { get; set; }
    public virtual Invoice? Invoice { get; set; }
}