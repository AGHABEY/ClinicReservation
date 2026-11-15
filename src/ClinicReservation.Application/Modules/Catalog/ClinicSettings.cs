using System.Text.RegularExpressions;
using ClinicReservation.Data;
using ClinicReservation.Helpers;
using ClinicReservation.Models;
using ClinicReservation.Modules.Pharmacy;
using ClinicReservation.Modules.Scheduling;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Catalog
{
    /// <summary>Departments, rooms and the examination catalog (with their lab tests and materials).</summary>
    public class ClinicSettings
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public ClinicSettings(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        // ---------- Departments and rooms ----------

        public async Task<(List<(Department Item, int Doctors)> Departments, List<(Room Item, int Doctors)> Rooms)> DepartmentsPageAsync(CancellationToken ct = default)
        {
            var deps = (await _db.Departments.OrderByDescending(d => d.IsActive).ThenBy(d => d.Name)
                .Select(d => new { Item = d, Doctors = d.Doctors.Count(x => x.IsActive) }).ToListAsync(ct))
                .Select(x => (x.Item, x.Doctors)).ToList();
            var roomDoctors = await _db.Doctors.Where(d => d.IsActive && d.RoomId != null).GroupBy(d => d.RoomId!.Value)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var rooms = (await _db.Rooms.OrderByDescending(r => r.IsActive).ThenBy(r => r.Name).ToListAsync(ct))
                .Select(r => (r, roomDoctors.GetValueOrDefault(r.Id))).ToList();
            return (deps, rooms);
        }

        public async Task<Result> SaveDepartmentAsync(int id, string? name, string? queuePrefix, bool isActive, CancellationToken ct = default)
        {
            name = name?.Trim();
            queuePrefix = string.IsNullOrWhiteSpace(queuePrefix) ? null : queuePrefix.Trim().ToUpperInvariant();
            if (queuePrefix != null && !Regex.IsMatch(queuePrefix, "^[A-Z]{1,3}$")) return Result.Failure("Talon hərfi 1–3 latın hərfi olmalıdır (məs: K)");
            if (string.IsNullOrEmpty(name)) return Result.Failure("Şöbənin adı boş ola bilməz");
            if (await _db.Departments.AnyAsync(d => d.Id != id && d.Name.ToLower() == name.ToLower(), ct)) return Result.Failure($"«{name}» adlı şöbə artıq var");

            var dep = id > 0 ? await _db.Departments.FindAsync(new object[] { id }, ct) : new Department();
            if (dep == null) return Result.Failure("Şöbə tapılmadı");
            var isNew = id == 0;
            dep.Name = name;
            dep.QueuePrefix = queuePrefix;
            dep.IsActive = isNew || isActive;
            if (isNew) _db.Departments.Add(dep);
            await _db.SaveChangesAsync(ct);
            _audit.Log(isNew ? "Şöbə əlavə edildi" : "Şöbə dəyişdirildi", AuditEntities.Catalog, dep.Id, $"{dep.Name}, talon {dep.QueuePrefix ?? QueueService.DefaultPrefix}{(dep.IsActive ? "" : " (deaktiv)")}");
            return Result.Success(isNew ? $"«{name}» şöbəsi əlavə edildi" : "Şöbə yeniləndi", dep.Id);
        }

        public async Task<Result> SaveRoomAsync(int id, string? name, bool isActive, CancellationToken ct = default)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return Result.Failure("Kabinetin adı boş ola bilməz");
            if (await _db.Rooms.AnyAsync(r => r.Id != id && r.Name.ToLower() == name.ToLower(), ct)) return Result.Failure($"«{name}» adlı kabinet artıq var");

            var room = id > 0 ? await _db.Rooms.FindAsync(new object[] { id }, ct) : new Room();
            if (room == null) return Result.Failure("Kabinet tapılmadı");
            var isNew = id == 0;
            room.Name = name;
            room.IsActive = isNew || isActive;
            if (isNew) _db.Rooms.Add(room);
            await _db.SaveChangesAsync(ct);
            _audit.Log(isNew ? "Kabinet əlavə edildi" : "Kabinet dəyişdirildi", AuditEntities.Catalog, room.Id, $"{room.Name}{(room.IsActive ? "" : " (deaktiv)")}");
            return Result.Success(isNew ? $"«{name}» kabineti əlavə edildi" : "Kabinet yeniləndi", room.Id);
        }

        // ---------- Examination types ----------

        public Task<List<ExaminationType>> ExaminationsAsync(CancellationToken ct = default) =>
            _db.ExaminationTypes.Include(e => e.Department).Include(e => e.LabTests).ThenInclude(l => l.LabTest)
                .Include(e => e.Materials).ThenInclude(m => m.StockItem)
                .OrderBy(e => e.Department == null ? "" : e.Department.Name).ThenBy(e => e.Name)
                .AsSplitQuery().ToListAsync(ct);

        public Task<ExaminationType?> ExaminationAsync(int id, CancellationToken ct = default) =>
            _db.ExaminationTypes.Include(e => e.LabTests).Include(e => e.Materials).FirstOrDefaultAsync(e => e.Id == id, ct);

        public async Task<ExamFormLists> ExamFormListsAsync(int? keepDepartmentId, List<int> selectedLabTests, List<MaterialInput> materials, CancellationToken ct = default)
        {
            var itemIds = materials.Where(m => m.StockItemId != null).Select(m => m.StockItemId!.Value).ToList();
            return new ExamFormLists
            {
                Departments = await _db.Departments.Where(d => d.IsActive || d.Id == keepDepartmentId).OrderBy(d => d.Name).ToListAsync(ct),
                LabTests = await _db.LabTests.Where(t => t.IsActive || selectedLabTests.Contains(t.Id)).OrderBy(t => t.Category).ThenBy(t => t.SortOrder).ToListAsync(ct),
                SelectedLabTests = selectedLabTests,
                StockItems = await _db.StockItems.Where(i => i.IsActive || itemIds.Contains(i.Id)).OrderBy(i => i.Name).ThenBy(i => i.Form).ToListAsync(ct)
            };
        }

        /// <summary>
        /// Creates (existing == null) or updates an examination with its lab tests and materials.
        /// Material rows: unknown items and empty rows are dropped, duplicates added up.
        /// </summary>
        public async Task<Result> SaveExaminationAsync(ExaminationType? existing, ExaminationType model, List<int>? labTestIds, List<MaterialInput>? materialRows,
            IEnumerable<(string? Field, string Message)> formErrors, CancellationToken ct = default)
        {
            var r = new Result();
            foreach (var (f, m) in formErrors) r.Fail(m, f);
            var rows = materialRows ?? new();
            if (rows.Any(x => x.StockItemId != null && !(x.Quantity > 0))) r.Fail("Material sətirlərində miqdar 0-dan böyük olmalıdır");
            if (rows.Any(x => x.Quantity > 10000)) r.Fail("Material miqdarı çox böyükdür");
            if (!r.Ok) return r;

            var list = rows.Where(x => x.StockItemId != null && x.Quantity > 0).ToList();
            var itemIds = list.Select(x => x.StockItemId!.Value).Distinct().ToList();
            var knownItems = (await _db.StockItems.Where(i => itemIds.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct)).ToHashSet();
            var materials = list.Where(x => knownItems.Contains(x.StockItemId!.Value))
                .GroupBy(x => x.StockItemId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity!.Value));
            var testIds = labTestIds ?? new();
            var tests = (await _db.LabTests.Where(t => testIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();

            if (existing == null)
            {
                model.Name = model.Name.Trim();
                model.LabTests = tests.Select(id => new ExaminationTypeLabTest { LabTestId = id }).ToList();
                model.Materials = materials.Select(kv => new ExaminationTypeStockItem { StockItemId = kv.Key, Quantity = kv.Value }).ToList();
                _db.ExaminationTypes.Add(model);
                await _db.SaveChangesAsync(ct);
                _audit.Log("Müayinə əlavə edildi", AuditEntities.Catalog, model.Id, $"{model.Name} — {Fmt.Money(model.Price)}");
                return Result.Success($"«{model.Name}» əlavə edildi", model.Id);
            }

            var oldLab = existing.LabTests.Select(l => l.LabTestId).ToHashSet();
            existing.LabTests.RemoveAll(l => !tests.Contains(l.LabTestId));
            foreach (var id in tests.Where(w => !oldLab.Contains(w)))
                existing.LabTests.Add(new ExaminationTypeLabTest { LabTestId = id });
            var labChanged = !oldLab.SetEquals(tests);

            var oldMaterials = existing.Materials.ToDictionary(m => m.StockItemId, m => m.Quantity);
            existing.Materials.RemoveAll(m => !materials.ContainsKey(m.StockItemId));
            foreach (var (itemId, qty) in materials)
            {
                var row = existing.Materials.FirstOrDefault(m => m.StockItemId == itemId);
                if (row == null) existing.Materials.Add(new ExaminationTypeStockItem { StockItemId = itemId, Quantity = qty });
                else row.Quantity = qty;
            }
            var materialsChanged = oldMaterials.Count != materials.Count || oldMaterials.Any(kv => !materials.TryGetValue(kv.Key, out var q) || q != kv.Value);

            var oldPrice = existing.Price;
            existing.Name = model.Name.Trim();
            existing.Description = model.Description;
            existing.Price = model.Price;
            existing.DepartmentId = model.DepartmentId;
            await _db.SaveChangesAsync(ct);
            _audit.Log("Müayinə dəyişdirildi", AuditEntities.Catalog, existing.Id,
                existing.Name + (oldPrice != existing.Price ? $": qiymət {Fmt.Money(oldPrice)} → {Fmt.Money(existing.Price)}" : "")
                + (labChanged ? $"; analizlər: {tests.Count}" : "")
                + (materialsChanged ? $"; materiallar: {materials.Count}" : ""));
            return Result.Success($"«{existing.Name}» yeniləndi", existing.Id);
        }
    }
}
