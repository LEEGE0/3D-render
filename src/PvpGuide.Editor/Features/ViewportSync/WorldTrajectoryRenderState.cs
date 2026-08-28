using System.Collections.ObjectModel;
using PvpGuide.Application.Projection;

namespace PvpGuide.Editor.Features.ViewportSync;

public readonly record struct WorldTrajectoryGeometryKey
{
    public WorldTrajectoryGeometryKey(long motionRevision, string samplingPolicyFingerprint)
    {
        if (motionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(motionRevision), "Motion revision cannot be negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(samplingPolicyFingerprint);
        MotionRevision = motionRevision;
        SamplingPolicyFingerprint = samplingPolicyFingerprint;
    }

    public long MotionRevision { get; }

    public string SamplingPolicyFingerprint { get; }
}

public sealed class WorldTrajectoryRenderState
{
    private WorldTrajectoryRenderState(
        WorldTrajectoryGeometryKey geometryKey,
        IReadOnlyDictionary<string, WorldTrajectoryGeometry> actorGeometries,
        double currentTimeNormalized)
    {
        GeometryKey = geometryKey;
        ActorGeometries = actorGeometries;
        CurrentTimeNormalized = currentTimeNormalized;
    }

    public WorldTrajectoryGeometryKey GeometryKey { get; }

    public IReadOnlyDictionary<string, WorldTrajectoryGeometry> ActorGeometries { get; }

    public double CurrentTimeNormalized { get; }

    public static WorldTrajectoryRenderState Create(
        SceneProjectionFrame frame,
        WorldTrajectoryRenderState? previous)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var geometryKey = new WorldTrajectoryGeometryKey(
            frame.Snapshot.MotionRevision,
            frame.SamplingPolicyFingerprint);
        var durationSeconds = ResolveDurationSeconds(frame);
        var currentTimeNormalized = NormalizeCurrentTime(frame.Snapshot.TimeSeconds, durationSeconds);
        if (previous is not null && previous.GeometryKey == geometryKey)
        {
            return new WorldTrajectoryRenderState(
                geometryKey,
                previous.ActorGeometries,
                currentTimeNormalized);
        }

        var actorGeometries = new Dictionary<string, WorldTrajectoryGeometry>(
            frame.Trajectories.Actors.Count,
            StringComparer.Ordinal);
        foreach (var (actorId, trajectory) in frame.Trajectories.Actors)
        {
            actorGeometries.Add(
                actorId,
                WorldTrajectoryGeometry.Create(
                    trajectory,
                    durationSeconds,
                    frame.Trajectories.UniformRate));
        }

        return new WorldTrajectoryRenderState(
            geometryKey,
            new ReadOnlyDictionary<string, WorldTrajectoryGeometry>(actorGeometries),
            currentTimeNormalized);
    }

    private static double ResolveDurationSeconds(SceneProjectionFrame frame)
    {
        var durationSeconds = 0d;
        foreach (var trajectory in frame.Trajectories.Actors.Values)
        {
            if (trajectory.Samples.Count > 0)
            {
                durationSeconds = Math.Max(durationSeconds, trajectory.Samples[^1].TimeSeconds);
            }
        }

        return durationSeconds;
    }

    private static double NormalizeCurrentTime(double currentTimeSeconds, double durationSeconds)
    {
        if (durationSeconds == 0)
        {
            return 0;
        }

        var normalized = currentTimeSeconds / durationSeconds;
        if (!double.IsFinite(normalized) || normalized < 0 || normalized > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentTimeSeconds),
                "Current time must be finite and within the trajectory duration.");
        }

        return normalized;
    }
}
