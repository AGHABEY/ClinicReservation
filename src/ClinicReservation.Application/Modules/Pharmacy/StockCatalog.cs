using ClinicReservation.Models;

namespace ClinicReservation.Modules.Pharmacy
{
    /// <summary>A starter list of common clinic medicines and materials (no stock, no prices).</summary>
    public static class StockCatalog
    {
        public static List<StockItem> Defaults()
        {
            var list = new List<StockItem>();
            void C(string code, string name, string? form, string unit, decimal min) =>
                list.Add(new StockItem { Code = code, Name = name, Form = form, Unit = unit, MinQuantity = min, Kind = StockItemKind.Consumable });
            void M(string code, string name, string? form, string unit, decimal min) =>
                list.Add(new StockItem { Code = code, Name = name, Form = form, Unit = unit, MinQuantity = min, Kind = StockItemKind.Medicine });
            void R(string code, string name, string? form, string unit, decimal min) =>
                list.Add(new StockItem { Code = code, Name = name, Form = form, Unit = unit, MinQuantity = min, Kind = StockItemKind.Reagent });

            // Sərf materialları
            C("SPR-02", "Şpris", "2 ml", "ədəd", 100);
            C("SPR-05", "Şpris", "5 ml", "ədəd", 100);
            C("SPR-10", "Şpris", "10 ml", "ədəd", 50);
            C("VAK-IYN", "Vakutayner iynəsi", "21G", "ədəd", 100);
            C("VAK-EDTA", "Vakutayner sınaq şüşəsi EDTA (bənövşəyi)", "3 ml", "ədəd", 100);
            C("VAK-GEL", "Vakutayner sınaq şüşəsi gel (sarı)", "5 ml", "ədəd", 100);
            C("SID-QAB", "Sidik qabı steril", "100 ml", "ədəd", 50);
            C("ELC-M", "Tibbi əlcək nitril", "M", "cüt", 200);
            C("ELC-L", "Tibbi əlcək nitril", "L", "cüt", 200);
            C("SAL-SPR", "Spirtli salfet", null, "ədəd", 200);
            C("TNZ-STR", "Tənzif salfet steril", "7.5×7.5 sm", "paket", 50);
            C("BNT-STR", "Bint steril", "7 m × 14 sm", "ədəd", 20);
            C("LEY-25", "Leykoplastır", "2.5 sm × 5 m", "rulon", 10);
            C("PLS-INY", "Plastır (iynə yeri)", null, "ədəd", 100);
            C("SIS-INF", "İnfuziya sistemi", null, "ədəd", 30);
            C("KAN-20", "Venoz kanyula", "20G", "ədəd", 30);
            C("MSK-3Q", "Tibbi maska", "3 qatlı", "ədəd", 200);
            C("BAH-01", "Bahil", null, "cüt", 200);
            C("PEL-6090", "Birdəfəlik döşəkcə", "60×90 sm", "ədəd", 50);
            C("EKQ-ELK", "EKQ elektrodu", null, "ədəd", 100);
            C("USM-GEL", "USM geli", "250 ml", "flakon", 5);
            C("TRN-01", "Turna (jqut)", null, "ədəd", 5);

            // Dərmanlar
            M("NACL-250", "Natrium xlorid 0.9%", "250 ml", "flakon", 20);
            M("NACL-5", "Natrium xlorid 0.9%", "ampul 5 ml", "ampul", 50);
            M("ANL-2", "Metamizol (Analgin) 50%", "ampul 2 ml", "ampul", 20);
            M("DKS-4", "Deksametazon 4 mq/ml", "ampul 1 ml", "ampul", 20);
            M("DKL-75", "Diklofenak 75 mq", "ampul 3 ml", "ampul", 20);
            M("XLP-20", "Xloropiramin (Suprastin) 20 mq/ml", "ampul 1 ml", "ampul", 10);
            M("ADR-01", "Epinefrin (Adrenalin) 0.18%", "ampul 1 ml", "ampul", 10);
            M("DRT-40", "Drotaverin 40 mq", "ampul 2 ml", "ampul", 10);
            M("LDK-2", "Lidokain 2%", "ampul 2 ml", "ampul", 20);
            M("PRC-500", "Parasetamol 500 mq", "tablet", "tablet", 50);
            M("YOD-5", "Yod məhlulu 5%", "25 ml", "flakon", 5);
            M("PRK-3", "Hidrogen peroksid 3%", "100 ml", "flakon", 10);
            M("SPR-70", "Etil spirti 70%", "100 ml", "flakon", 10);
            M("XLH-005", "Xlorheksidin 0.05%", "100 ml", "flakon", 10);

            // Reagentlər və testlər
            R("RG-DIL", "Hematoloji dilyuent", "20 L", "kanistr", 1);
            R("RG-LYS", "Hematoloji lizis reagenti", "500 ml", "flakon", 1);
            R("RG-GLU", "Qlükoza reagent dəsti", "2×100 ml", "dəst", 1);
            R("RG-ALT", "ALT reagent dəsti", "2×50 ml", "dəst", 1);
            R("TST-HBS", "HBsAg sürətli test", "kaset", "ədəd", 20);
            R("TST-HCG", "Hamiləlik testi (hCG)", "kaset", "ədəd", 20);
            R("TST-URN", "Sidik test zolağı (10 parametr)", "100 zolaq", "qutu", 1);

            return list;
        }
    }
}
