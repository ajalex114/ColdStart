using System;
using static ColdStart.Helpers.FormatHelper;

namespace ColdStart.Tests.Helpers;

public class FormatHelperTests
{
    #region Fmt(long ms)

    [Theory]
    [InlineData(0, "0ms")]
    [InlineData(1, "1ms")]
    [InlineData(500, "500ms")]
    [InlineData(999, "999ms")]
    public void Fmt_BelowOneSecond_ReturnsMilliseconds(long ms, string expected) =>
        Assert.Equal(expected, Fmt(ms));

    [Theory]
    [InlineData(1000, "1.0s")]
    [InlineData(1500, "1.5s")]
    [InlineData(2300, "2.3s")]
    [InlineData(59999, "60.0s")] // 59999 / 1000.0 = 59.999, which is < 60
    public void Fmt_SecondsRange_ReturnsDecimalSeconds(long ms, string expected) =>
        Assert.Equal(expected, Fmt(ms));

    [Theory]
    [InlineData(60000, "1m")]
    [InlineData(90000, "1m 30s")]
    [InlineData(120000, "2m")]
    [InlineData(154000, "2m 34s")]
    public void Fmt_MinutesRange_ReturnsMinutesAndSeconds(long ms, string expected) =>
        Assert.Equal(expected, Fmt(ms));

    [Theory]
    [InlineData(3600000, "60m")]       // exactly 1 hour
    [InlineData(7200000, "120m")]      // 2 hours
    [InlineData(3661000, "61m 1s")]    // 1h 1m 1s
    public void Fmt_LargeValues_ReturnsMinutes(long ms, string expected) =>
        Assert.Equal(expected, Fmt(ms));

    [Fact]
    public void Fmt_NegativeValue_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Fmt(-1));
        Assert.Equal("ms", ex.ParamName);
    }

    [Fact]
    public void Fmt_MinLongNegative_ThrowsArgumentOutOfRangeException() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Fmt(long.MinValue));

    #endregion

    #region FmtMem(double mb)

    [Theory]
    [InlineData(1024.0, "1.0 GB")]
    [InlineData(2048.0, "2.0 GB")]
    [InlineData(1536.0, "1.5 GB")]
    [InlineData(10240.0, "10.0 GB")]
    public void FmtMem_GigabyteRange_ReturnsGB(double mb, string expected) =>
        Assert.Equal(expected, FmtMem(mb));

    [Theory]
    [InlineData(1.0, "1 MB")]
    [InlineData(234.0, "234 MB")]
    [InlineData(1023.0, "1023 MB")]
    [InlineData(1.5, "2 MB")]  // F0 rounds 1.5 to 2
    public void FmtMem_MegabyteRange_ReturnsMB(double mb, string expected) =>
        Assert.Equal(expected, FmtMem(mb));

    [Theory]
    [InlineData(0.0, "0 KB")]
    [InlineData(0.5, "512 KB")]
    [InlineData(0.25, "256 KB")]
    [InlineData(0.001, "1 KB")]
    public void FmtMem_KilobyteRange_ReturnsKB(double mb, string expected) =>
        Assert.Equal(expected, FmtMem(mb));

    [Fact]
    public void FmtMem_ExactlyZero_ReturnsZeroKB() =>
        Assert.Equal("0 KB", FmtMem(0.0));

    [Fact]
    public void FmtMem_NegativeValue_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FmtMem(-0.1));
        Assert.Equal("mb", ex.ParamName);
    }

    [Fact]
    public void FmtMem_NegativeLarge_ThrowsArgumentOutOfRangeException() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FmtMem(-1024.0));

    #endregion

    #region FmtDuration(TimeSpan ts)

    [Theory]
    [InlineData(1, 0, 0, 0, "1d 0h")]
    [InlineData(2, 5, 0, 0, "2d 5h")]
    [InlineData(10, 23, 59, 59, "10d 23h")]
    [InlineData(1, 12, 30, 45, "1d 12h")]
    public void FmtDuration_DaysRange_ReturnsDaysAndHours(int d, int h, int m, int s, string expected) =>
        Assert.Equal(expected, FmtDuration(new TimeSpan(d, h, m, s)));

    [Theory]
    [InlineData(0, 1, 0, 0, "1h 0m")]
    [InlineData(0, 3, 20, 0, "3h 20m")]
    [InlineData(0, 23, 59, 59, "23h 59m")]
    public void FmtDuration_HoursRange_ReturnsHoursAndMinutes(int d, int h, int m, int s, string expected) =>
        Assert.Equal(expected, FmtDuration(new TimeSpan(d, h, m, s)));

    [Theory]
    [InlineData(0, 0, 1, 0, "1m 0s")]
    [InlineData(0, 0, 5, 30, "5m 30s")]
    [InlineData(0, 0, 59, 59, "59m 59s")]
    public void FmtDuration_MinutesRange_ReturnsMinutesAndSeconds(int d, int h, int m, int s, string expected) =>
        Assert.Equal(expected, FmtDuration(new TimeSpan(d, h, m, s)));

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(1, "1s")]
    [InlineData(12, "12s")]
    [InlineData(59, "59s")]
    public void FmtDuration_SecondsRange_ReturnsSeconds(int s, string expected) =>
        Assert.Equal(expected, FmtDuration(TimeSpan.FromSeconds(s)));

    [Fact]
    public void FmtDuration_Zero_ReturnsZeroSeconds() =>
        Assert.Equal("0s", FmtDuration(TimeSpan.Zero));

    #endregion
}
