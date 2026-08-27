namespace PvpGuide.Editor.Features.Timeline;

public sealed record StepTrackItem(
    string Id,
    double TimeSeconds,
    string Label,
    bool Emphasized);

public sealed record StepTrackMarker(
    string Id,
    double TimeSeconds,
    double X,
    string Label,
    bool Emphasized);

public sealed record StepTrackSegment(
    string Id,
    double StartTimeSeconds,
    double EndTimeSeconds,
    double StartX,
    double EndX,
    string Label,
    bool Emphasized);

public sealed class StepTrackLane
{
    private readonly IReadOnlyList<StepTrackMarker> _markers;
    private readonly IReadOnlyList<StepTrackSegment> _segments;

    internal StepTrackLane(
        IReadOnlyList<StepTrackMarker> markers,
        IReadOnlyList<StepTrackSegment> segments)
    {
        _markers = markers ?? throw new ArgumentNullException(nameof(markers));
        _segments = segments ?? throw new ArgumentNullException(nameof(segments));
    }

    public IReadOnlyList<StepTrackMarker> Markers => _markers;

    public IReadOnlyList<StepTrackSegment> Segments => _segments;

    public string? HitTest(double pointerX, double hitRadius)
    {
        if (!double.IsFinite(pointerX))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerX));
        }

        ValidateFiniteNonNegative(hitRadius, nameof(hitRadius));
        StepTrackMarker? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var marker in _markers)
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
}

public static class StepTrackLayout
{
    public static StepTrackLane Create(
        double durationSeconds,
        double width,
        double horizontalPadding,
        IReadOnlyList<StepTrackItem> items)
    {
        ValidateFiniteNonNegative(durationSeconds, nameof(durationSeconds));
        ValidateFiniteNonNegative(width, nameof(width));
        ValidateFiniteNonNegative(horizontalPadding, nameof(horizontalPadding));
        ArgumentNullException.ThrowIfNull(items);

        var sortedItems = items
            .Select(ValidateItem)
            .OrderBy(item => item.TimeSeconds)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (sortedItems.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != sortedItems.Length)
        {
            throw new ArgumentException("Step track item IDs must be unique.", nameof(items));
        }

        var padding = Math.Min(horizontalPadding, width / 2d);
        var usableWidth = Math.Max(0, width - (2d * padding));
        double ToX(double timeSeconds) => durationSeconds == 0
            ? padding
            : padding + (timeSeconds / durationSeconds * usableWidth);

        var markers = sortedItems
            .Select(item => new StepTrackMarker(
                item.Id,
                item.TimeSeconds,
                ToX(item.TimeSeconds),
                item.Label,
                item.Emphasized))
            .ToArray();
        var segments = new StepTrackSegment[sortedItems.Length];
        for (var index = 0; index < sortedItems.Length; index++)
        {
            var item = sortedItems[index];
            var endTimeSeconds = index + 1 < sortedItems.Length
                ? Math.Min(sortedItems[index + 1].TimeSeconds, durationSeconds)
                : durationSeconds;
            segments[index] = new StepTrackSegment(
                item.Id,
                item.TimeSeconds,
                endTimeSeconds,
                ToX(item.TimeSeconds),
                ToX(endTimeSeconds),
                item.Label,
                item.Emphasized);
        }

        return new StepTrackLane(
            Array.AsReadOnly(markers),
            Array.AsReadOnly(segments));
    }

    private static StepTrackItem ValidateItem(StepTrackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        ArgumentNullException.ThrowIfNull(item.Label);
        if (!double.IsFinite(item.TimeSeconds) || item.TimeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(item), "Step track item time must be finite and non-negative.");
        }

        return item;
    }

    private static void ValidateFiniteNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
