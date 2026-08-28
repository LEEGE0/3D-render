using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Projection;

public sealed record SceneProjectionFrame
{
    public SceneProjectionFrame(
        SceneSnapshot snapshot,
        MovementTrajectorySet trajectories,
        string samplingPolicyFingerprint)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(trajectories);
        ArgumentException.ThrowIfNullOrWhiteSpace(samplingPolicyFingerprint);

        if (!string.Equals(snapshot.DocumentId, trajectories.DocumentId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Snapshot and trajectories must belong to the same document.",
                nameof(trajectories));
        }

        if (snapshot.Revision != trajectories.Revision)
        {
            throw new ArgumentException(
                "Snapshot and trajectories must have the same revision.",
                nameof(trajectories));
        }

        if (snapshot.MotionRevision != trajectories.MotionRevision)
        {
            throw new ArgumentException(
                "Snapshot and trajectories must have the same motion revision.",
                nameof(trajectories));
        }

        if (!string.Equals(
                trajectories.SamplingPolicyFingerprint,
                samplingPolicyFingerprint,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Trajectory sampling fingerprint does not match the requested policy.",
                nameof(samplingPolicyFingerprint));
        }

        Snapshot = snapshot;
        Trajectories = trajectories;
        SamplingPolicyFingerprint = samplingPolicyFingerprint;
    }

    public SceneSnapshot Snapshot { get; }

    public MovementTrajectorySet Trajectories { get; }

    public string SamplingPolicyFingerprint { get; }
}
