namespace PvpGuide.Editor.Features.Timeline;

public sealed record TransformTrackMarker(string Id, double TimeSeconds, double X);

public static class TransformTrackLayout
{
    public static IReadOnlyList<TransformTrackMarker> CreateMarkers(
        double durationSeconds,
        double width,
        double horizontalPadding,
        IEnumerable<(string Id, double TimeSeconds)> keyframes)
    {
        ValidateFiniteNonNegative(durationSeconds, nameof(durationSeconds));
        ValidateFinitePositive(width, nameof(width));
        ValidateFiniteNonNegative(horizontalPadding, nameof(horizontalPadding));
        if (horizontalPadding > width / 2)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalPadding));
        }

        ArgumentNullException.ThrowIfNull(keyframes);
        var usableWidth = width - (2 * horizontalPadding);
        var markers = new List<TransformTrackMarker>();
        foreach (var (id, timeSeconds) in keyframes)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > durationSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(keyframes));
            }

            var x = durationSeconds == 0
                ? horizontalPadding
                : horizontalPadding + (timeSeconds / durationSeconds * usableWidth);
            markers.Add(new TransformTrackMarker(id, timeSeconds, x));
        }

        return markers;
    }

    public static string? HitTest(
        IReadOnlyList<TransformTrackMarker> markers,
        double pointerX,
        double hitRadius)
    {
        ArgumentNullException.ThrowIfNull(markers);
        ValidateFiniteNonNegative(hitRadius, nameof(hitRadius));
        if (!double.IsFinite(pointerX))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerX));
        }

        TransformTrackMarker? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var marker in markers)
        {
            var distance = Math.Abs(marker.X - pointerX);
            if (distance > hitRadius)
            {
                continue;
            }

            if (best is null || distance < bestDistance ||
                (distance == bestDistance &&
                 (marker.TimeSeconds < best.TimeSeconds ||
                  (marker.TimeSeconds == best.TimeSeconds &&
                   string.CompareOrdinal(marker.Id, best.Id) < 0))))
            {
                best = marker;
                bestDistance = distance;
            }
        }

        return best?.Id;
    }

    private static void ValidateFiniteNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateFinitePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
