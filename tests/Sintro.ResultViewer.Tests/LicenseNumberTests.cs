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
        // The planned 7-9 digit licence format must work without a code change.
        Assert.Equal(expected, LicenseNumber.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Normalize_returnsEmptyWhenThereAreNoDigits(string? input) =>
        Assert.Equal(string.Empty, LicenseNumber.Normalize(input));
}
