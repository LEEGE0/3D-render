using System.Collections.ObjectModel;

namespace PvpGuide.Domain.Timeline;

public sealed class MovementTrajectorySet
{
    public MovementTrajectorySet(
        string documentId,
        long revision,
        long motionRevision,
        string samplingPolicyFingerprint,
        IReadOnlyDictionary<string, ActorMovementTrajectory> actors,
        long segmentSteps)
        : this(
            documentId,
            revision,
            motionRevision,
            samplingPolicyFingerprint,
            CopyActors(actors),
            segmentSteps)
    {
    }

    private MovementTrajectorySet(
        string documentId,
        long revision,
        long motionRevision,
        string samplingPolicyFingerprint,
        ReadOnlyDictionary<string, ActorMovementTrajectory> actors,
        long segmentSteps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Revision cannot be negative.");
        }

        if (motionRevision < 0 || motionRevision > revision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(motionRevision),
                "Motion revision must be non-negative and cannot exceed revision.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(samplingPolicyFingerprint);
        if (segmentSteps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentSteps), "Segment step count cannot be negative.");
        }

        DocumentId = documentId;
        Revision = revision;
        MotionRevision = motionRevision;
        SamplingPolicyFingerprint = samplingPolicyFingerprint;
        Actors = actors;
        SegmentSteps = segmentSteps;
    }

    public string DocumentId { get; }

    public long Revision { get; }

    public long MotionRevision { get; }

    public string SamplingPolicyFingerprint { get; }

    public IReadOnlyDictionary<string, ActorMovementTrajectory> Actors { get; }

    public long SegmentSteps { get; }

    public MovementTrajectorySet WithRevision(long revision)
    {
        if (revision < MotionRevision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                "Revision cannot be earlier than the motion revision.");
        }

        return revision == Revision
            ? this
            : new MovementTrajectorySet(
                DocumentId,
                revision,
                MotionRevision,
                SamplingPolicyFingerprint,
                (ReadOnlyDictionary<string, ActorMovementTrajectory>)Actors,
                SegmentSteps);
    }

    private static ReadOnlyDictionary<string, ActorMovementTrajectory> CopyActors(
        IReadOnlyDictionary<string, ActorMovementTrajectory> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        var copiedActors = new Dictionary<string, ActorMovementTrajectory>(StringComparer.Ordinal);
        foreach (var (actorId, trajectory) in actors)
        {
            if (trajectory is null)
            {
                throw new ArgumentException("Trajectory dictionaries cannot contain null values.", nameof(actors));
            }

            if (!string.Equals(actorId, trajectory.ActorId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Trajectory dictionary keys must match actor IDs.", nameof(actors));
            }

            copiedActors.Add(actorId, trajectory);
        }

        return new ReadOnlyDictionary<string, ActorMovementTrajectory>(copiedActors);
    }
}
