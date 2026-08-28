using PvpGuide.Domain.Timeline;

namespace PvpGuide.Domain;

public interface ISceneProjectionSource : ISceneSnapshotSource
{
    ProjectionSourceMetadata GetProjectionMetadata();

    TrajectorySamplePlan CreateTrajectorySamplePlan(TrajectorySamplingSettings settings);

    MovementTrajectorySet CreateMovementTrajectories(TrajectorySamplePlan plan);
}
