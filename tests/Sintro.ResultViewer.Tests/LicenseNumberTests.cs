using Sintro.ResultViewer.Data;

namespace Sintro.ResultViewer.Tests;

public class LicenseNumberTests
{
    [Theory]
    [InlineData("4321", "004321")]      // short input is zero-padded to six
    [InlineData("012345", "012345")]    // a stored value round-trips untouched
    [InlineData("987654", "987654")]
    [InlineData(" 234567 ", "234567")]  // whitespace stripped
    [InlineData("23-45-67", "234567")]  // separators stripped
    public void Normalize_padsAndStripsToSixDigits(string input, string expected) =>
        Assert.Equal(expected, LicenseNumber.Normalize(input));

    [Theory]
    [InlineData("1234567", "1234567")]
    [InlineData("123456789", "123456789")]
    public void Normalize_leavesLongerNumbersAlone(string input, string expected)
    {
        // The planned 7-9 digit licence format must work without a code change,
        // so normalisation imposes no upper bound.
        Assert.Equal(expected, LicenseNumber.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Normalize_returnsEmptyWhenThereAreNoDigits(string? input) =>
        Assert.Equal(string.Empty, LicenseNumber.Normalize(input));

    [Fact]
    public void AreSame_comparesAfterNormalising() =>
        Assert.True(LicenseNumber.AreSame("12345", "012345"));

    [Fact]
    public void AreSame_isFalseForEmptyInput()
    {
        // Two unidentifiable shooters are not the same shooter.
        Assert.False(LicenseNumber.AreSame("", ""));
        Assert.False(LicenseNumber.AreSame(null, "004321"));
    }

    [Theory]
    [InlineData("000000000000000", null)]  // the placeholder many shooters share
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("000000000000123", "000000000000123")]
    public void RfidCard_treatsAllZeroesAsAbsent(string? input, string? expected) =>
        Assert.Equal(expected, RfidCard.Clean(input));
}
