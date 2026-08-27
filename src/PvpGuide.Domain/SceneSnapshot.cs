using System.Collections.ObjectModel;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Domain;

public readonly record struct Position3
{
    public Position3(double x, double y, double z)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Position components must be finite.");
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Position components must be finite.");
        }

        if (!double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(z), "Position components must be finite.");
        }

        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }
}

public readonly record struct EvaluatedTransform(Position3 Position, double YawDegrees);

public sealed class SceneSnapshot
{
    public SceneSnapshot(
        string documentId,
        long revision,
        double timeSeconds,
        IReadOnlyDictionary<string, EvaluatedTransform> actorTransforms)
        : this(documentId, revision, timeSeconds, actorTransforms, new Dictionary<string, EvaluatedActorTimelineState>())
    {
    }

    public SceneSnapshot(
        string documentId,
        long revision,
        double timeSeconds,
        IReadOnlyDictionary<string, EvaluatedTransform> actorTransforms,
        IReadOnlyDictionary<string, EvaluatedActorTimelineState> actorTimelineStates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(actorTransforms);
        ArgumentNullException.ThrowIfNull(actorTimelineStates);

        DocumentId = documentId;
        Revision = revision;
        TimeSeconds = timeSeconds;
        ActorTransforms = new ReadOnlyDictionary<string, EvaluatedTransform>(
            new Dictionary<string, EvaluatedTransform>(actorTransforms));
        ActorTimelineStates = new ReadOnlyDictionary<string, EvaluatedActorTimelineState>(
            new Dictionary<string, EvaluatedActorTimelineState>(actorTimelineStates));
    }

    public string DocumentId { get; }

    public long Revision { get; }

    public double TimeSeconds { get; }

    public IReadOnlyDictionary<string, EvaluatedTransform> ActorTransforms { get; }

    public IReadOnlyDictionary<string, EvaluatedActorTimelineState> ActorTimelineStates { get; }
}
