using ClinicReservation.Models;

namespace ClinicReservation.Modules.Lab
{
    /// <summary>
    /// Starter set of common tests with typical adult reference ranges.
    /// Ranges depend on the analyzer and method — every lab must check
    /// them against its own reagents before use.
    /// </summary>
    public static class LabCatalog
    {
        private const string Blood = "Qan";
        private const string Urine = "Sidik";
        private const string Cbc = "Ümumi qan analizi";
        private const string Bio = "Biokimya";
        private const string Hormone = "Hormonlar";
        private const string Ua = "Ümumi sidik analizi";
        private const string Sero = "Seroloji";

        private static LabTest N(string code, string name, string cat, string unit, decimal? min, decimal? max,
            decimal? fMin = null, decimal? fMax = null, decimal? critMin = null, decimal? critMax = null, string sample = Blood) => new()
        {
            Code = code, Name = name, Category = cat, Unit = unit, SampleType = sample, ResultType = LabResultType.Numeric,
            RefMin = min, RefMax = max, RefMinFemale = fMin, RefMaxFemale = fMax, CriticalMin = critMin, CriticalMax = critMax
        };

        private static LabTest C(string code, string name, string cat, string choices, string normal, string sample) => new()
        {
            Code = code, Name = name, Category = cat, SampleType = sample, ResultType = LabResultType.Choice,
            Choices = choices, RefText = normal
        };

        public static List<LabTest> Defaults()
        {
            var list = new List<LabTest>
            {
                N("WBC", "Leykositlər", Cbc, "10⁹/L", 4.0m, 9.0m, critMin: 2.0m, critMax: 30m),
                N("RBC", "Eritrositlər", Cbc, "10¹²/L", 4.0m, 5.0m, 3.9m, 4.7m),
                N("HGB", "Hemoqlobin", Cbc, "q/L", 130m, 160m, 120m, 140m, critMin: 70m, critMax: 200m),
                N("HCT", "Hematokrit", Cbc, "%", 40m, 48m, 36m, 42m),
                N("PLT", "Trombositlər", Cbc, "10⁹/L", 180m, 320m, critMin: 50m, critMax: 1000m),
                N("ESR", "EÇS (eritrositlərin çökmə sürəti)", Cbc, "mm/saat", 2m, 10m, 2m, 15m),

                N("GLU", "Qlükoza", Bio, "mmol/L", 3.3m, 5.5m, critMin: 2.5m, critMax: 25m),
                N("HBA1C", "Qlikozilləşmiş hemoqlobin (HbA1c)", Bio, "%", 4.0m, 6.0m),
                N("UREA", "Sidik cövhəri", Bio, "mmol/L", 2.5m, 8.3m),
                N("CREA", "Kreatinin", Bio, "mkmol/L", 62m, 115m, 53m, 97m),
                N("ALT", "ALT", Bio, "V/L", null, 41m, null, 33m),
                N("AST", "AST", Bio, "V/L", null, 40m, null, 32m),
                N("TBIL", "Ümumi bilirubin", Bio, "mkmol/L", 3.4m, 20.5m),
                N("CHOL", "Ümumi xolesterin", Bio, "mmol/L", null, 5.2m),
                N("TG", "Triqliseridlər", Bio, "mmol/L", null, 1.7m),
                N("CRP", "C-reaktiv zülal (CRP)", Bio, "mq/L", null, 5m),
                N("K", "Kalium", Bio, "mmol/L", 3.5m, 5.1m, critMin: 2.8m, critMax: 6.2m),
                N("NA", "Natrium", Bio, "mmol/L", 136m, 145m, critMin: 120m, critMax: 160m),
                N("FE", "Dəmir", Bio, "mkmol/L", 11.6m, 31.3m, 9.0m, 30.4m),

                N("TSH", "TTH (tireotrop hormon)", Hormone, "mIU/L", 0.4m, 4.0m),
                N("FT4", "Sərbəst T4", Hormone, "pmol/L", 10m, 22m),
                N("VITD", "25-OH Vitamin D", Hormone, "nq/ml", 30m, 100m),

                C("U-COL", "Rəng", Ua, "Saman-sarı;Sarı;Tünd sarı;Qırmızımtıl;Rəngsiz", "Saman-sarı", Urine),
                C("U-CLR", "Şəffaflıq", Ua, "Şəffaf;Az bulanıq;Bulanıq", "Şəffaf", Urine),
                N("U-SG", "Nisbi sıxlıq", Ua, "", 1.010m, 1.025m, sample: Urine),
                C("U-PRO", "Zülal", Ua, "Mənfi;İz;+;++;+++", "Mənfi", Urine),
                C("U-GLU", "Qlükoza (sidikdə)", Ua, "Mənfi;+;++;+++", "Mənfi", Urine),
                N("U-LEU", "Leykositlər (görmə sahəsində)", Ua, "g/s", null, 5m, sample: Urine),
                N("U-RBC", "Eritrositlər (görmə sahəsində)", Ua, "g/s", null, 2m, sample: Urine),

                C("HBSAG", "HBsAg (hepatit B)", Sero, "Mənfi;Müsbət", "Mənfi", Blood),
                C("HCV", "Anti-HCV (hepatit C)", Sero, "Mənfi;Müsbət", "Mənfi", Blood),
            };
            for (var i = 0; i < list.Count; i++)
                list[i].SortOrder = (i + 1) * 10;
            return list;
        }

        public static readonly string[] SampleTypes = { "Qan", "Sidik", "Nəcis", "Yaxma", "Digər" };
    }
}
