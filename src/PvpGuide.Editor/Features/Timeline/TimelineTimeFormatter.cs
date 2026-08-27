namespace PvpGuide.Editor.Features.Timeline;

public static class TimelineTimeFormatter
{
    public static string Format(double currentTimeSeconds, double durationSeconds, int framesPerSecond)
    {
        var frame = (long)Math.Floor(currentTimeSeconds * framesPerSecond);
        return FormattableString.Invariant(
            $"현재 {currentTimeSeconds:F3}초 / 전체 {durationSeconds:F3}초 · 프레임 {frame}");
    }
}
