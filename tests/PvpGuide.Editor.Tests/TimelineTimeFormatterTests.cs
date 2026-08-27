using System.Globalization;
using PvpGuide.Editor.Features.Timeline;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class TimelineTimeFormatterTests
{
    [Theory]
    [InlineData(0, 10, 30, "현재 0.000초 / 전체 10.000초 · 프레임 0")]
    [InlineData(1.5, 10, 30, "현재 1.500초 / 전체 10.000초 · 프레임 45")]
    [InlineData(10, 10, 30, "현재 10.000초 / 전체 10.000초 · 프레임 300")]
    public void Format_displays_current_total_seconds_and_zero_based_frame(
        double currentTimeSeconds,
        double durationSeconds,
        int framesPerSecond,
        string expected)
    {
        Assert.Equal(expected, TimelineTimeFormatter.Format(
            currentTimeSeconds,
            durationSeconds,
            framesPerSecond));
    }

    [Fact]
    public void Format_uses_invariant_numbers_and_floors_a_fractional_frame()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            Assert.Equal(
                "현재 29.970초 / 전체 60.000초 · 프레임 899",
                TimelineTimeFormatter.Format(29.97, 60, 30));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
