using ClinicReservation.Helpers;

namespace ClinicReservation.Tests.Unit
{
    public class FmtTests
    {
        [Theory]
        [InlineData(1, "1-ci")]
        [InlineData(2, "2-ci")]
        [InlineData(3, "3-cü")]
        [InlineData(4, "4-cü")]
        [InlineData(6, "6-cı")]
        [InlineData(9, "9-cu")]
        [InlineData(10, "10-cu")]
        [InlineData(20, "20-ci")]
        [InlineData(40, "40-cı")]
        [InlineData(100, "100-cü")]
        [InlineData(23, "23-cü")]
        public void Ordinal_follows_azerbaijani_vowel_harmony(int n, string expected) =>
            Assert.Equal(expected, Fmt.Ordinal(n));

        [Fact]
        public void Money_uses_decimal_comma_and_manat_sign()
        {
            // The thousands separator comes from the OS's ICU data (az: "."), the decimal comma is fixed.
            var s = Fmt.Money(1250.5m);
            Assert.EndsWith(",50 ₼", s);
            Assert.Equal("1250,50 ₼", new string(s.Where(c => char.IsDigit(c) || c is ',' or ' ' or '₼').ToArray()).Replace(" ", "").Replace("₼", " ₼"));
        }

        [Fact]
        public void Doctor_prefix_is_not_doubled()
        {
            Assert.Equal("Dr. Leyla Məmmədova", Fmt.Doctor("Leyla Məmmədova"));
            Assert.Equal("Dr. Leyla", Fmt.Doctor("Dr. Leyla"));
        }
    }
}
