using System.ComponentModel.DataAnnotations;
using ClinicReservation.Models;

namespace ClinicReservation.Modules.Catalog
{

    public class DoctorFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ad və soyad daxil edilməlidir")]
        [StringLength(100)]
        public string? FullName { get; set; }

        [StringLength(100)]
        public string? Specialty { get; set; }

        [RegularExpression(@"^\+?[0-9\s\-()]{9,20}$", ErrorMessage = "Telefon nömrəsi düzgün deyil")]
        public string? Phone { get; set; }

        public int? DepartmentId { get; set; }
        public int? RoomId { get; set; }

        [Range(5, 240, ErrorMessage = "Qəbul müddəti 5–240 dəqiqə olmalıdır")]
        public int SlotMinutes { get; set; } = 30;

        public bool IsActive { get; set; } = true;
        public string? UserId { get; set; }

        /// <summary>Seven entries, Monday first.</summary>
        public List<WorkDayInput> Days { get; set; } = new();

        public bool IsEdit => Id > 0;
    }

    public class WorkDayInput
    {
        public DayOfWeek Day { get; set; }
        public bool Works { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? BreakStart { get; set; }
        public string? BreakEnd { get; set; }
    }

    public class LeaveInput
    {
        public int DoctorId { get; set; }

        [Required(ErrorMessage = "Başlanğıc tarixi seçin")]
        public DateTime? From { get; set; }

        [Required(ErrorMessage = "Son tarixi seçin")]
        public DateTime? To { get; set; }

        [StringLength(100)]
        public string? Reason { get; set; }
    }

    /// <summary>A login that can be linked to a doctor profile.</summary>
    public record UserOption(string Id, string UserName, string? FullName);

    /// <summary>Doctor form lists: departments, rooms, logins and the recent leaves.</summary>
    public class DoctorFormLists
    {
        public List<Department> Departments { get; set; } = new();
        public List<Room> Rooms { get; set; } = new();
        public List<UserOption> Users { get; set; } = new();
        public List<DoctorLeave> Leaves { get; set; } = new();
    }

    /// <summary>Examination form lists: departments, lab tests and stock items to pick.</summary>
    public class ExamFormLists
    {
        public List<Department> Departments { get; set; } = new();
        public List<LabTest> LabTests { get; set; } = new();
        public List<int> SelectedLabTests { get; set; } = new();
        public List<StockItem> StockItems { get; set; } = new();
        public List<Equipment> Equipment { get; set; } = new();
    }
}
