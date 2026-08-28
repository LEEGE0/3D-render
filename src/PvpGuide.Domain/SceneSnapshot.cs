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
        : this(
            documentId,
            revision,
            timeSeconds,
            actorTransforms,
            actorTimelineStates,
            CreateAuthoredFacings(actorTransforms),
            revision)
    {
    }

    public SceneSnapshot(
        string documentId,
        long revision,
        double timeSeconds,
        IReadOnlyDictionary<string, EvaluatedTransform> actorTransforms,
        IReadOnlyDictionary<string, EvaluatedActorTimelineState> actorTimelineStates,
        IReadOnlyDictionary<string, EvaluatedActorFacing> actorFacings)
        : this(documentId, revision, timeSeconds, actorTransforms, actorTimelineStates, actorFacings, revision)
    {
    }

    public SceneSnapshot(
        string documentId,
        long revision,
        double timeSeconds,
        IReadOnlyDictionary<string, EvaluatedTransform> actorTransforms,
        IReadOnlyDictionary<string, EvaluatedActorTimelineState> actorTimelineStates,
        IReadOnlyDictionary<string, EvaluatedActorFacing> actorFacings,
        long motionRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(actorTransforms);
        ArgumentNullException.ThrowIfNull(actorTimelineStates);
        ArgumentNullException.ThrowIfNull(actorFacings);

        var copiedTransforms = new Dictionary<string, EvaluatedTransform>(actorTransforms);
        var copiedFacings = new Dictionary<string, EvaluatedActorFacing>(actorFacings);
        foreach (var actorId in copiedFacings.Keys)
        {
            if (!copiedTransforms.ContainsKey(actorId))
            {
                throw new ArgumentException(
                    $"Facing actor '{actorId}' does not have an authored transform.",
                    nameof(actorFacings));
            }
        }

        foreach (var (actorId, transform) in copiedTransforms)
        {
            if (!copiedFacings.ContainsKey(actorId))
            {
                copiedFacings.Add(actorId, CreateAuthoredFacing(transform));
            }
        }

        DocumentId = documentId;
        Revision = revision;
        MotionRevision = motionRevision;
        TimeSeconds = timeSeconds;
        ActorTransforms = new ReadOnlyDictionary<string, EvaluatedTransform>(
            copiedTransforms);
        ActorTimelineStates = new ReadOnlyDictionary<string, EvaluatedActorTimelineState>(
            new Dictionary<string, EvaluatedActorTimelineState>(actorTimelineStates));
        ActorFacings = new ReadOnlyDictionary<string, EvaluatedActorFacing>(copiedFacings);
    }

    public string DocumentId { get; }

    public long Revision { get; }

    public long MotionRevision { get; }

    public double TimeSeconds { get; }

    public IReadOnlyDictionary<string, EvaluatedTransform> ActorTransforms { get; }

    public IReadOnlyDictionary<string, EvaluatedActorTimelineState> ActorTimelineStates { get; }

    public IReadOnlyDictionary<string, EvaluatedActorFacing> ActorFacings { get; }

    private static Dictionary<string, EvaluatedActorFacing> CreateAuthoredFacings(
        IReadOnlyDictionary<string, EvaluatedTransform> actorTransforms)
    {
        ArgumentNullException.ThrowIfNull(actorTransforms);

        var facings = new Dictionary<string, EvaluatedActorFacing>(actorTransforms.Count);
        foreach (var (actorId, transform) in actorTransforms)
        {
            facings.Add(actorId, CreateAuthoredFacing(transform));
        }

        return facings;
    }

    private static EvaluatedActorFacing CreateAuthoredFacing(EvaluatedTransform transform) =>
        new(transform.YawDegrees, FacingResolutionKind.AuthoredDisabled, null);
}
