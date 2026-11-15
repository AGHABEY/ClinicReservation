using ClinicReservation.Models;
using ClinicReservation.Services;

namespace ClinicReservation.Tests.Unit
{
    public class LabRefTests
    {
        private static LabTest Hgb() => new()
        {
            Code = "HGB", Name = "Hemoqlobin", ResultType = LabResultType.Numeric,
            RefMin = 130, RefMax = 170, RefMinFemale = 120, RefMaxFemale = 150, CriticalMin = 70, CriticalMax = 200
        };

        [Theory]
        [InlineData(Gender.Male, "125", LabFlag.Low)]
        [InlineData(Gender.Female, "125", LabFlag.Normal)]
        [InlineData(Gender.Female, "160", LabFlag.High)]
        [InlineData(Gender.Male, "160", LabFlag.Normal)]
        [InlineData(Gender.Male, "65", LabFlag.CriticalLow)]
        [InlineData(Gender.Female, "205", LabFlag.CriticalHigh)]
        public void Numeric_result_uses_the_patients_range(Gender g, string value, LabFlag expected) =>
            Assert.Equal(expected, LabRef.Evaluate(Hgb(), g, value).Flag);

        [Fact]
        public void Unknown_sex_flags_only_outside_both_ranges()
        {
            Assert.Equal(LabFlag.Normal, LabRef.Evaluate(Hgb(), null, "125").Flag);   // fine for women
            Assert.Equal(LabFlag.Normal, LabRef.Evaluate(Hgb(), null, "165").Flag);   // fine for men
            Assert.Equal(LabFlag.Low, LabRef.Evaluate(Hgb(), null, "115").Flag);
        }

        [Fact]
        public void Comma_decimal_is_accepted() =>
            Assert.Equal(1.015m, LabRef.Evaluate(new LabTest { ResultType = LabResultType.Numeric, RefMin = 1.010m, RefMax = 1.025m }, null, "1,015").Number);

        [Fact]
        public void Choice_other_than_normal_is_abnormal()
        {
            var t = new LabTest { ResultType = LabResultType.Choice, Choices = "Mənfi;Müsbət", RefText = "Mənfi" };
            Assert.Equal(LabFlag.Normal, LabRef.Evaluate(t, null, "mənfi").Flag);
            Assert.Equal(LabFlag.Abnormal, LabRef.Evaluate(t, null, "Müsbət").Flag);
        }

        [Fact]
        public void Reference_text_shows_both_sexes_when_unknown() =>
            Assert.Equal("K: 130 – 170 · Q: 120 – 150", LabRef.Text(Hgb(), null));
    }
}
