using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Modules.Pharmacy
{
    /// <summary>Item catalog, warehouses and suppliers.</summary>
    public class StockSettings
    {
        private readonly IAppDbContext _db;
        private readonly AuditService _audit;

        public StockSettings(IAppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        /// <summary>Creates or updates an item. Field errors use the StockItem property names.</summary>
        public async Task<Result> SaveItemAsync(StockItem input, CancellationToken ct = default)
        {
            var name = (input.Name ?? "").Trim();
            var code = Clean(input.Code)?.ToUpperInvariant();
            var unit = Clean(input.Unit) ?? "ədəd";
            var r = new Result();
            if (name.Length == 0) r.Fail("Ad daxil edin", nameof(StockItem.Name));
            else if (name.Length > 200) r.Fail("Ad ən çox 200 simvol", nameof(StockItem.Name));
            if (code is { Length: > 50 }) r.Fail("Kod ən çox 50 simvol", nameof(StockItem.Code));
            else if (code != null && await _db.StockItems.AnyAsync(i => i.Code == code && i.Id != input.Id, ct))
                r.Fail("Bu kodla məhsul artıq var", nameof(StockItem.Code));
            if (unit.Length > 20) r.Fail("Vahid ən çox 20 simvol", nameof(StockItem.Unit));
            if (input.MinQuantity < 0) r.Fail("Mənfi ola bilməz", nameof(StockItem.MinQuantity));
            if (!Enum.IsDefined(input.Kind)) r.Fail("Növü seçin", nameof(StockItem.Kind));
            if (!r.Ok) return r;

            var isNew = input.Id == 0;
            var item = isNew ? new StockItem() : await _db.StockItems.FindAsync(new object[] { input.Id }, ct);
            if (item == null) return Result.Failure("Məhsul tapılmadı");
            item.Name = name;
            item.Code = code;
            item.Kind = input.Kind;
            item.Form = Clean(input.Form);
            item.Unit = unit;
            item.MinQuantity = input.MinQuantity;
            item.Note = Clean(input.Note);
            item.IsActive = input.IsActive;
            if (isNew) _db.StockItems.Add(item);
            await _db.SaveChangesAsync(ct);

            _audit.Log(isNew ? "Məhsul kataloqa əlavə edildi" : "Məhsul dəyişdirildi", AuditEntities.Stock, item.Id,
                $"{item.DisplayName}, vahid {item.Unit}, minimum {StockService.Qty(item.MinQuantity)}");
            return Result.Success(isNew ? $"«{item.DisplayName}» əlavə edildi" : $"«{item.DisplayName}» yeniləndi", item.Id);
        }

        /// <summary>Adds the starter list (only items whose code isn't in the catalog yet).</summary>
        public async Task<Result> ImportDefaultsAsync(CancellationToken ct = default)
        {
            var existing = (await _db.StockItems.Where(i => i.Code != null).Select(i => i.Code!).ToListAsync(ct)).ToHashSet();
            var added = StockCatalog.Defaults().Where(i => !existing.Contains(i.Code!)).ToList();
            _db.StockItems.AddRange(added);
            await _db.SaveChangesAsync(ct);
            _audit.Log("Standart məhsullar əlavə edildi", AuditEntities.Stock, null, $"{added.Count} məhsul");
            return Result.Success(added.Count == 0
                ? "Bütün standart məhsullar artıq kataloqdadır"
                : $"{added.Count} məhsul əlavə edildi. Qalıq yaratmaq üçün mədaxil sənədi ilə qəbul edin.");
        }

        public async Task<Result> SaveWarehouseAsync(int id, string? name, int? departmentId, bool isMain, bool isActive, CancellationToken ct = default)
        {
            name = Clean(name);
            if (name == null || name.Length > 100) return Result.Failure("Anbarın adını yazın (ən çox 100 simvol)");
            var isNew = id == 0;
            var wh = isNew ? new Warehouse() : await _db.Warehouses.FindAsync(new object[] { id }, ct);
            if (wh == null) return Result.Failure("Anbar tapılmadı");
            if (!isActive && await _db.StockBatches.AnyAsync(b => b.WarehouseId == wh.Id && b.Quantity > 0, ct))
                return Result.Failure($"«{wh.Name}» anbarında qalıq var — əvvəlcə köçürün və ya silin");
            if (departmentId != null && await _db.Warehouses.AnyAsync(w => w.DepartmentId == departmentId && w.Id != wh.Id && w.IsActive, ct))
                return Result.Failure("Bu şöbənin artıq anbarı var");
            if (wh.IsMain && !isMain)
                return Result.Failure("Mərkəzi anbarı dəyişmək üçün başqa anbarı mərkəzi edin");

            wh.Name = name;
            wh.DepartmentId = departmentId;
            wh.IsActive = isActive || wh.IsMain || isMain;
            if (isMain && !wh.IsMain)
            {
                foreach (var other in await _db.Warehouses.Where(w => w.IsMain).ToListAsync(ct)) other.IsMain = false;
                wh.IsMain = true;
            }
            if (isNew) _db.Warehouses.Add(wh);
            await _db.SaveChangesAsync(ct);
            _audit.Log(isNew ? "Anbar əlavə edildi" : "Anbar dəyişdirildi", AuditEntities.Stock, wh.Id, wh.Name + (wh.IsMain ? " (mərkəzi)" : ""));
            return Result.Success(isNew ? $"«{wh.Name}» əlavə edildi" : $"«{wh.Name}» yeniləndi", wh.Id);
        }

        public async Task<Result> SaveSupplierAsync(int id, string? name, string? voen, string? phone, string? note, bool isActive, CancellationToken ct = default)
        {
            name = Clean(name);
            voen = Clean(voen);
            if (name == null || name.Length > 200) return Result.Failure("Təchizatçının adını yazın (ən çox 200 simvol)");
            if (voen != null && (voen.Length != 10 || !voen.All(char.IsDigit))) return Result.Failure("VÖEN 10 rəqəmdən ibarət olmalıdır");
            var isNew = id == 0;
            var s = isNew ? new Supplier() : await _db.Suppliers.FindAsync(new object[] { id }, ct);
            if (s == null) return Result.Failure("Təchizatçı tapılmadı");
            s.Name = name;
            s.Voen = voen;
            s.Phone = Clean(phone) is { } p && p.Length <= 32 ? p : null;
            s.Note = Clean(note) is { } n && n.Length <= 500 ? n : null;
            s.IsActive = isActive;
            if (isNew) _db.Suppliers.Add(s);
            await _db.SaveChangesAsync(ct);
            _audit.Log(isNew ? "Təchizatçı əlavə edildi" : "Təchizatçı dəyişdirildi", AuditEntities.Stock, s.Id, s.Name);
            return Result.Success(isNew ? $"«{s.Name}» əlavə edildi" : $"«{s.Name}» yeniləndi", s.Id);
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
