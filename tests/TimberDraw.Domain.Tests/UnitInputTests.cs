using Xunit;

namespace TimberDraw.DomainTests
{
    // Characterization: the architectural distance parser behind every distance field in the
    // palette (property grid cells accept "1'2-1/2\"" as well as plain "14.5"; inches out).
    public class UnitInputTests
    {
        [Theory]
        [InlineData("14.5", 14.5)]
        [InlineData("0", 0.0)]
        [InlineData("2'", 24.0)]
        [InlineData("1'2", 14.0)]
        [InlineData("1'2-1/2\"", 14.5)]
        [InlineData("1'-2 1/2", 14.5)]
        [InlineData("1' 2 1/2", 14.5)]
        [InlineData("3/4", 0.75)]
        [InlineData("-3/4", -0.75)]
        [InlineData("-1'6", -18.0)]
        [InlineData("10\"", 10.0)]
        [InlineData(" 8 ", 8.0)]
        public void TryParseDistance_Parses(string input, double expectedInches)
        {
            Assert.True(UnitInput.TryParseDistance(input, out double v));
            Assert.Equal(expectedInches, v, 10);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("garbage")]
        [InlineData("1/0")]      // zero denominator
        [InlineData("1 2 3")]    // three tokens is not a distance
        [InlineData("/2")]
        [InlineData("2/")]
        public void TryParseDistance_Rejects(string input)
        {
            Assert.False(UnitInput.TryParseDistance(input, out _));
        }

        [Fact]
        public void ParseDistance_ThrowsOnGarbage()
        {
            Assert.Throws<System.FormatException>(() => UnitInput.ParseDistance("not a distance"));
        }
    }
}
