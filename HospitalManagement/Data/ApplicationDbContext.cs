using HospitalManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Data;

public class ApplicationDbContext:DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        :base(options)
    {
    }

    public DbSet<Doctor> Doctors { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<InvoiceItem> InvoiceItems { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Payment> Payments { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        
        // Payment və Appointment arasındakı bir-birinə bir əlaqə
        builder.Entity<Payment>()
            .HasOne(p => p.Appointment)
            .WithOne(a => a.Payment)
            .HasForeignKey<Payment>(p => p.AppointmentId);

        // Appointment və Invoice arasındakı bir-birinə bir əlaqə
        builder.Entity<Invoice>()
            .HasOne(i => i.Appointment)
            .WithOne(a => a.Invoice)
            .HasForeignKey<Invoice>(i => i.AppointmentId);

        // Invoice və InvoiceItem arasındakı bir-çox əlaqə
        builder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Invoice)
            .WithMany(i => i.InvoiceItems)
            .HasForeignKey(ii => ii.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

}