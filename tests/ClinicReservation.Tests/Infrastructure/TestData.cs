using ClinicReservation.Data;
using ClinicReservation.Models;

namespace ClinicReservation.Tests.Infrastructure
{
    /// <summary>Small builders for test rows. Every name/code is unique so tests never share data.</summary>
    public static class TestData
    {
        private static int _seq;
        public static string Unique(string prefix) => $"{prefix}{Interlocked.Increment(ref _seq)}{Guid.NewGuid().ToString("N")[..6]}";

        public static Patient Patient(AppDbContext db, Gender? gender = null)
        {
            var p = new Patient
            {
                Name = "Test",
                Surname = Unique("Xəstə"),
                PinCode = Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
                Phone = "+994501234567",
                Birthday = new DateTime(1980, 5, 1),
                Gender = gender,
                CreatedAt = ClinicTime.Now
            };
            db.Patients.Add(p);
            db.SaveChanges();
            return p;
        }

        public static Department Department(AppDbContext db)
        {
            var d = new Department { Name = Unique("Şöbə") };
            db.Departments.Add(d);
            db.SaveChanges();
            return d;
        }

        public static Warehouse Warehouse(AppDbContext db, int? departmentId = null)
        {
            var w = new Warehouse { Name = Unique("Anbar"), DepartmentId = departmentId };
            db.Warehouses.Add(w);
            db.SaveChanges();
            return w;
        }

        public static StockItem Item(AppDbContext db, StockItemKind kind = StockItemKind.Consumable, string unit = "ədəd")
        {
            var i = new StockItem { Name = Unique("Məhsul"), Code = Unique("T"), Kind = kind, Unit = unit };
            db.StockItems.Add(i);
            db.SaveChanges();
            return i;
        }

        public static ExaminationType Exam(AppDbContext db, int? departmentId = null, params (StockItem Item, decimal Qty)[] materials)
        {
            var e = new ExaminationType { Name = Unique("Müayinə"), Price = 20, DepartmentId = departmentId };
            foreach (var (item, qty) in materials)
                e.Materials.Add(new ExaminationTypeStockItem { StockItemId = item.Id, Quantity = qty });
            db.ExaminationTypes.Add(e);
            db.SaveChanges();
            return e;
        }

        public static Reserve Reserve(AppDbContext db, Patient patient, string status, params ExaminationType[] exams)
        {
            var r = new Reserve
            {
                PatientId = patient.Id,
                AppointmentAt = ClinicTime.Today.AddHours(10),
                AddedDate = ClinicTime.Now,
                Status = db.ReserveStatuses.First(s => s.Name == status),
                ReserveExaminationTypes = exams.Select(e => new ReserveExaminationType { ExaminationTypeId = e.Id, Price = e.Price }).ToList()
            };
            db.Reserves.Add(r);
            db.SaveChanges();
            return r;
        }

        /// <summary>Any active cancellation reason (the catalog is seeded).</summary>
        public static int ReasonId(AppDbContext db) => db.CancellationReasons.Where(r => r.IsActive).OrderBy(r => r.Id).Select(r => r.Id).First();

        /// <summary>A doctor working every day 08:00–20:00 in 30-minute slots.</summary>
        public static Doctor Doctor(AppDbContext db, int? departmentId = null)
        {
            var d = new Doctor { FullName = Unique("Həkim "), DepartmentId = departmentId, SlotMinutes = 30 };
            foreach (var day in Enum.GetValues<DayOfWeek>())
                d.WorkHours.Add(new DoctorWorkHours { Day = day, Start = TimeSpan.FromHours(8), End = TimeSpan.FromHours(20) });
            db.Doctors.Add(d);
            db.SaveChanges();
            return d;
        }

        public static Ward Ward(AppDbContext db, int beds, decimal price = 40, int? departmentId = null, WardGender gender = WardGender.Mixed)
        {
            var w = new Ward { Name = Unique("Palata"), DailyPrice = price, DepartmentId = departmentId, Gender = gender };
            for (var i = 1; i <= beds; i++) w.Beds.Add(new Bed { Name = i.ToString() });
            db.Wards.Add(w);
            db.SaveChanges();
            return w;
        }
    }
}
