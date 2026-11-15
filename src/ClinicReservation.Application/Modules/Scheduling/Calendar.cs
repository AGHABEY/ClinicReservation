using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Scheduling
{
    public record CalendarItem(int Id, int PatientId, DateTime Start, int Minutes, string Patient, string? Status, string Services, bool Movable, bool Overbooked)
    {
        /// <summary>Side-by-side placement when visits overlap (an extra visit, or old data): lane of Lanes.</summary>
        public int Lane { get; set; }
        public int Lanes { get; set; } = 1;
        public DateTime End => Start.AddMinutes(Minutes);
    }

    /// <summary>One column of the calendar: a doctor on a day (day view) or a day of one doctor (week view).</summary>
    public class CalendarColumn
    {
        public int DoctorId { get; set; }
        public DateTime Date { get; set; }
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        /// <summary>Working time that day; null = not working (see <see cref="Off"/>).</summary>
        public TimeSpan? Start { get; set; }
        public TimeSpan? End { get; set; }
        public TimeSpan? BreakStart { get; set; }
        public TimeSpan? BreakEnd { get; set; }
        public string? Off { get; set; }
        public int Step { get; set; } = 30;
        public List<CalendarItem> Items { get; } = new();
    }

    public class CalendarView
    {
        public string Mode { get; set; } = "day";
        public DateTime Date { get; set; }
        public int? DepartmentId { get; set; }
        public int? DoctorId { get; set; }
        public List<CalendarColumn> Columns { get; } = new();
        public List<Department> Departments { get; set; } = new();
        public List<Doctor> Doctors { get; set; } = new();
        /// <summary>Hours shown (covering every column's working time).</summary>
        public TimeSpan From { get; set; } = TimeSpan.FromHours(8);
        public TimeSpan To { get; set; } = TimeSpan.FromHours(20);
    }

    public class CalendarQueries
    {
        private readonly IAppDbContext _db;
        public CalendarQueries(IAppDbContext db) => _db = db;

        /// <summary>
        /// Day: every active doctor (of the department, if chosen) side by side. Week: one doctor, Monday to Sunday.
        /// Cancelled visits are left out; no-shows stay (faded) so the day's history is visible.
        /// </summary>
        public async Task<CalendarView> LoadAsync(string? mode, DateTime? date, int? departmentId, int? doctorId, CancellationToken ct = default)
        {
            var v = new CalendarView
            {
                Mode = mode == "week" ? "week" : "day",
                Date = (date ?? ClinicTime.Today).Date,
                DepartmentId = departmentId,
                DoctorId = doctorId,
                Departments = await _db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct),
                Doctors = await _db.Doctors.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct)
            };

            var doctors = _db.Doctors.AsNoTracking().Include(d => d.WorkHours).Include(d => d.Leaves).Where(d => d.IsActive);
            List<(Doctor Doctor, DateTime Day)> cells;
            if (v.Mode == "week")
            {
                v.DoctorId ??= v.Doctors.FirstOrDefault(d => departmentId == null || d.DepartmentId == departmentId)?.Id;
                var monday = v.Date.AddDays(-(((int)v.Date.DayOfWeek + 6) % 7));
                v.Date = monday;
                var doc = v.DoctorId == null ? null : await doctors.FirstOrDefaultAsync(d => d.Id == v.DoctorId, ct);
                cells = doc == null ? new() : Enumerable.Range(0, 7).Select(i => (doc, monday.AddDays(i))).ToList();
            }
            else
            {
                if (departmentId != null) doctors = doctors.Where(d => d.DepartmentId == departmentId);
                if (doctorId != null) doctors = doctors.Where(d => d.Id == doctorId);
                cells = (await doctors.OrderBy(d => d.FullName).ToListAsync(ct)).Select(d => (d, v.Date)).ToList();
            }
            if (cells.Count == 0) return v;

            var first = cells.Min(c => c.Day);
            var last = cells.Max(c => c.Day).AddDays(1);
            var ids = cells.Select(c => c.Doctor.Id).Distinct().ToList();
            var reserves = await _db.Reserves.AsNoTracking()
                .Include(r => r.Patient).Include(r => r.Status)
                .Include(r => r.ReserveExaminationTypes!).ThenInclude(x => x.ExaminationType)
                .Where(r => r.DoctorId != null && ids.Contains(r.DoctorId.Value) && r.AppointmentAt >= first && r.AppointmentAt < last)
                .Where(r => r.Status == null || r.Status.Name != StatusNames.Cancelled)
                .OrderBy(r => r.AppointmentAt)
                .ToListAsync(ct);

            var now = ClinicTime.Now;
            foreach (var (doctor, day) in cells)
            {
                var col = new CalendarColumn
                {
                    DoctorId = doctor.Id,
                    Date = day,
                    Title = v.Mode == "week" ? Helpers.Fmt.Day(day) : doctor.FullName,
                    Subtitle = v.Mode == "week" ? Helpers.Fmt.Date(day) : doctor.Specialty,
                    Step = Math.Clamp(doctor.SlotMinutes, 5, 240)
                };
                var leave = doctor.Leaves.FirstOrDefault(l => l.From.Date <= day && l.To.Date >= day);
                var hours = doctor.WorkHours.FirstOrDefault(w => w.Day == day.DayOfWeek);
                if (leave != null) col.Off = "Məzuniyyət" + (string.IsNullOrEmpty(leave.Reason) ? "" : $": {leave.Reason}");
                else if (hours == null) col.Off = "İş günü deyil";
                else
                {
                    col.Start = hours.Start;
                    col.End = hours.End;
                    col.BreakStart = hours.BreakStart;
                    col.BreakEnd = hours.BreakEnd;
                }

                foreach (var r in reserves.Where(r => r.DoctorId == doctor.Id && r.AppointmentAt.Date == day))
                    col.Items.Add(new CalendarItem(r.Id, r.PatientId, r.AppointmentAt, Math.Max(5, r.DurationMinutes), r.Patient.FullName, r.Status?.Name,
                        string.Join(", ", r.ReserveExaminationTypes!.Select(x => x.ExaminationType.Name)),
                        Movable: Services.ReserveFlow.CanReschedule(r) && r.AppointmentAt > now,
                        Overbooked: r.Overbooked));
                Lay(col.Items);
                v.Columns.Add(col);
            }

            // Show every column's hours and every visit, on whole hours.
            var starts = v.Columns.Where(c => c.Start != null).Select(c => c.Start!.Value)
                .Concat(v.Columns.SelectMany(c => c.Items).Select(i => i.Start.TimeOfDay)).ToList();
            var ends = v.Columns.Where(c => c.End != null).Select(c => c.End!.Value)
                .Concat(v.Columns.SelectMany(c => c.Items).Select(i => i.Start.TimeOfDay + TimeSpan.FromMinutes(i.Minutes))).ToList();
            if (starts.Count > 0) v.From = TimeSpan.FromHours(Math.Floor(starts.Min().TotalHours));
            if (ends.Count > 0) v.To = TimeSpan.FromHours(Math.Min(24, Math.Ceiling(ends.Max().TotalHours)));
            if (v.To <= v.From) v.To = v.From + TimeSpan.FromHours(1);
            return v;
        }

        /// <summary>Overlapping visits share the column: each group of overlaps gets as many lanes as it needs.</summary>
        public static void Lay(List<CalendarItem> items)
        {
            items.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Overbooked.CompareTo(b.Overbooked));
            var group = new List<CalendarItem>();
            var groupEnd = DateTime.MinValue;
            void Close()
            {
                var lanes = group.Count == 0 ? 1 : group.Max(x => x.Lane) + 1;
                foreach (var x in group) x.Lanes = lanes;
                group.Clear();
            }
            foreach (var item in items)
            {
                // A visit starting after everything in the group has ended opens a new group.
                if (group.Count > 0 && item.Start >= groupEnd) Close();
                var lane = 0;
                while (group.Any(g => g.Lane == lane && g.End > item.Start)) lane++;
                item.Lane = lane;
                group.Add(item);
                groupEnd = group.Max(g => g.End);
            }
            Close();
        }
    }
}
