using System.Collections.ObjectModel;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Trajectory;

namespace PvpGuide.Editor.Features.ViewportSync;

public readonly record struct WorldDirection(double X, double Z);

public readonly record struct WorldFacingTransform(
    double ActorRootRotationYRadians,
    double VisualLocalRotationYRadians,
    WorldDirection WorldFacingPositiveX)
{
    public static WorldFacingTransform Create(
        double domainYawDegrees,
        double modelForwardOffsetDegrees)
    {
        if (!double.IsFinite(domainYawDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(domainYawDegrees), "Domain yaw must be finite.");
        }

        if (!double.IsFinite(modelForwardOffsetDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelForwardOffsetDegrees),
                "Model forward offset must be finite.");
        }

        var actorRootRotation = WorldTransformMapper.ToRotationYRadians(domainYawDegrees);
        var visualLocalRotation = WorldTransformMapper.ToRotationYRadians(modelForwardOffsetDegrees);
        var combinedRotation = actorRootRotation + visualLocalRotation;

        return new WorldFacingTransform(
            actorRootRotation,
            visualLocalRotation,
            new WorldDirection(Math.Cos(combinedRotation), -Math.Sin(combinedRotation)));
    }
}

public sealed class WorldTrajectoryMeshGeometry
{
    private readonly ReadOnlyCollection<WorldPosition> _vertices;
    private readonly ReadOnlyCollection<double> _normalizedTimes;

    public WorldTrajectoryMeshGeometry(
        IEnumerable<WorldPosition> vertices,
        IEnumerable<double> normalizedTimes)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(normalizedTimes);

        var copiedVertices = vertices.ToArray();
        var copiedTimes = normalizedTimes.ToArray();
        if (copiedVertices.Length != copiedTimes.Length)
        {
            throw new ArgumentException("Each trajectory vertex must have one normalized time.");
        }

        if (copiedTimes.Any(time => !double.IsFinite(time) || time < 0 || time > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalizedTimes),
                "Normalized trajectory times must be finite and in [0, 1].");
        }

        _vertices = Array.AsReadOnly(copiedVertices);
        _normalizedTimes = Array.AsReadOnly(copiedTimes);
    }

    public IReadOnlyList<WorldPosition> Vertices => _vertices;

    public IReadOnlyList<double> NormalizedTimes => _normalizedTimes;
}

public sealed class WorldTrajectoryGeometry
{
    public const double TrajectoryLiftY = 0.025;
    public const double FacingTickLength = 0.35;

    private WorldTrajectoryGeometry(
        WorldTrajectoryMeshGeometry sharedPath,
        WorldTrajectoryMeshGeometry freeFacingTicks,
        WorldTrajectoryMeshGeometry lockOnFacingTicks)
    {
        SharedPath = sharedPath;
        FreeFacingTicks = freeFacingTicks;
        LockOnFacingTicks = lockOnFacingTicks;
    }

    public WorldTrajectoryMeshGeometry SharedPath { get; }

    public WorldTrajectoryMeshGeometry FreeFacingTicks { get; }

    public WorldTrajectoryMeshGeometry LockOnFacingTicks { get; }

    public static WorldTrajectoryGeometry Create(
        ActorMovementTrajectory trajectory,
        double durationSeconds,
        int? uniformRate)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds),
                "Trajectory duration must be finite and non-negative.");
        }

        foreach (var sample in trajectory.Samples)
        {
            if (sample.TimeSeconds > durationSeconds)
            {
                throw new ArgumentException(
                    "Trajectory sample times cannot exceed the document duration.",
                    nameof(trajectory));
            }
        }

        var copiedTickIndices = TrajectoryTickSelectionPolicy
            .SelectOrderedSampleIndices(trajectory, uniformRate)
            .ToArray();
        ValidateTickSampleIndices(copiedTickIndices, trajectory.Samples.Count);

        var pathVertices = new WorldPosition[trajectory.Samples.Count];
        var pathTimes = new double[trajectory.Samples.Count];
        for (var index = 0; index < trajectory.Samples.Count; index++)
        {
            var sample = trajectory.Samples[index];
            pathVertices[index] = ToLiftedWorldPosition(sample.Position);
            pathTimes[index] = NormalizeTime(sample.TimeSeconds, durationSeconds);
        }

        var freeTickVertices = new WorldPosition[copiedTickIndices.Length * 2];
        var freeTickTimes = new double[freeTickVertices.Length];
        var lockTickVertices = new WorldPosition[copiedTickIndices.Length * 2];
        var lockTickTimes = new double[lockTickVertices.Length];
        for (var selectedIndex = 0; selectedIndex < copiedTickIndices.Length; selectedIndex++)
        {
            var sample = trajectory.Samples[copiedTickIndices[selectedIndex]];
            var vertexIndex = selectedIndex * 2;
            var origin = ToLiftedWorldPosition(sample.Position);
            var normalizedTime = NormalizeTime(sample.TimeSeconds, durationSeconds);

            freeTickVertices[vertexIndex] = origin;
            freeTickVertices[vertexIndex + 1] = CreateTickEnd(origin, sample.FreeYawDegrees);
            freeTickTimes[vertexIndex] = normalizedTime;
            freeTickTimes[vertexIndex + 1] = normalizedTime;

            lockTickVertices[vertexIndex] = origin;
            lockTickVertices[vertexIndex + 1] = CreateTickEnd(origin, sample.LockOnFacing.YawDegrees);
            lockTickTimes[vertexIndex] = normalizedTime;
            lockTickTimes[vertexIndex + 1] = normalizedTime;
        }

        return new WorldTrajectoryGeometry(
            new WorldTrajectoryMeshGeometry(pathVertices, pathTimes),
            new WorldTrajectoryMeshGeometry(freeTickVertices, freeTickTimes),
            new WorldTrajectoryMeshGeometry(lockTickVertices, lockTickTimes));
    }

    private static void ValidateTickSampleIndices(IReadOnlyList<int> indices, int sampleCount)
    {
        for (var index = 0; index < indices.Count; index++)
        {
            if (indices[index] < 0 || indices[index] >= sampleCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(indices),
                    "Tick sample indices must refer to an existing trajectory sample.");
            }

            if (index > 0 && indices[index - 1] >= indices[index])
            {
                throw new ArgumentException(
                    "Tick sample indices must be strictly increasing without duplicates.",
                    nameof(indices));
            }
        }
    }

    private static WorldPosition ToLiftedWorldPosition(PvpGuide.Domain.Position3 source) =>
        new(source.X, source.Y + TrajectoryLiftY, source.Z);

    private static WorldPosition CreateTickEnd(WorldPosition origin, double yawDegrees)
    {
        var facing = WorldFacingTransform.Create(yawDegrees, modelForwardOffsetDegrees: 0)
            .WorldFacingPositiveX;
        return new WorldPosition(
            origin.X + (facing.X * FacingTickLength),
            origin.Y,
            origin.Z + (facing.Z * FacingTickLength));
    }

    private static double NormalizeTime(double timeSeconds, double durationSeconds) =>
        durationSeconds == 0 ? 0 : timeSeconds / durationSeconds;
}

public sealed class WorldTrajectoryPresentation
{
    public WorldTrajectoryPresentation(
        WorldTrajectoryGeometry geometry,
        double currentTimeNormalized)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(currentTimeNormalized) ||
            currentTimeNormalized < 0 ||
            currentTimeNormalized > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentTimeNormalized),
                "Current normalized time must be finite and in [0, 1].");
        }

        Geometry = geometry;
        CurrentTimeNormalized = currentTimeNormalized;
    }

    public WorldTrajectoryGeometry Geometry { get; }

    public double CurrentTimeNormalized { get; }
}
