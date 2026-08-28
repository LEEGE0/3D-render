using System.Collections.ObjectModel;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Projection;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Trajectory;

namespace PvpGuide.Editor.Features.TopView;

public enum TopViewDrawLayer
{
    SharedPaths,
    FreeFacingTicks,
    LockOnFacingTicks,
    LockLines,
    ActorBodies,
    TargetMarkers,
    Text,
}

[Flags]
public enum TopViewAnchorMarker
{
    None = 0,
    TransformCircle = 1,
    LockOnDiamond = 2,
}

public enum TopViewTickEndpointShape
{
    FreeArrow,
    LockOnBar,
}

public sealed record TrajectoryPathPointGeometry(double TimeSeconds, Position3 Position);

public sealed record TrajectoryFacingTickGeometry(
    double TimeSeconds,
    Position3 Position,
    double YawDegrees,
    TopViewAnchorMarker AnchorMarker,
    TopViewTickEndpointShape EndpointShape);

public sealed class ActorTrajectoryOverlayGeometry
{
    public ActorTrajectoryOverlayGeometry(
        string actorId,
        IEnumerable<TrajectoryPathPointGeometry> sharedPath,
        IEnumerable<TrajectoryFacingTickGeometry> freeFacingTicks,
        IEnumerable<TrajectoryFacingTickGeometry> lockOnFacingTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(sharedPath);
        ArgumentNullException.ThrowIfNull(freeFacingTicks);
        ArgumentNullException.ThrowIfNull(lockOnFacingTicks);

        ActorId = actorId;
        SharedPath = Array.AsReadOnly(sharedPath.ToArray());
        FreeFacingTicks = Array.AsReadOnly(freeFacingTicks.ToArray());
        LockOnFacingTicks = Array.AsReadOnly(lockOnFacingTicks.ToArray());
    }

    public string ActorId { get; }

    public IReadOnlyList<TrajectoryPathPointGeometry> SharedPath { get; }

    public IReadOnlyList<TrajectoryFacingTickGeometry> FreeFacingTicks { get; }

    public IReadOnlyList<TrajectoryFacingTickGeometry> LockOnFacingTicks { get; }
}

public sealed class TrajectoryOverlayGeometry
{
    public TrajectoryOverlayGeometry(
        IReadOnlyDictionary<string, ActorTrajectoryOverlayGeometry> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        Actors = new ReadOnlyDictionary<string, ActorTrajectoryOverlayGeometry>(
            new Dictionary<string, ActorTrajectoryOverlayGeometry>(actors, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, ActorTrajectoryOverlayGeometry> Actors { get; }
}

public sealed record TrajectoryPathPointPresentation(
    TrajectoryPathPointGeometry Geometry,
    double Brightness)
{
    public double TimeSeconds => Geometry.TimeSeconds;

    public Position3 Position => Geometry.Position;
}

public sealed record TrajectoryFacingTickPresentation(
    TrajectoryFacingTickGeometry Geometry,
    double Brightness)
{
    public double TimeSeconds => Geometry.TimeSeconds;

    public Position3 Position => Geometry.Position;

    public double YawDegrees => Geometry.YawDegrees;

    public TopViewAnchorMarker AnchorMarker => Geometry.AnchorMarker;

    public TopViewTickEndpointShape EndpointShape => Geometry.EndpointShape;
}

public sealed class ActorTrajectoryOverlayPresentation
{
    public ActorTrajectoryOverlayPresentation(
        ActorTrajectoryOverlayGeometry geometry,
        double selectionBrightness,
        IEnumerable<TrajectoryPathPointPresentation> sharedPath,
        IEnumerable<TrajectoryFacingTickPresentation> freeFacingTicks,
        IEnumerable<TrajectoryFacingTickPresentation> lockOnFacingTicks)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(sharedPath);
        ArgumentNullException.ThrowIfNull(freeFacingTicks);
        ArgumentNullException.ThrowIfNull(lockOnFacingTicks);
        if (!double.IsFinite(selectionBrightness) || selectionBrightness < 0 || selectionBrightness > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionBrightness),
                "Selection brightness must be finite and in [0, 1].");
        }

        Geometry = geometry;
        SelectionBrightness = selectionBrightness;
        SharedPath = Array.AsReadOnly(sharedPath.ToArray());
        FreeFacingTicks = Array.AsReadOnly(freeFacingTicks.ToArray());
        LockOnFacingTicks = Array.AsReadOnly(lockOnFacingTicks.ToArray());
    }

    public ActorTrajectoryOverlayGeometry Geometry { get; }

    public double SelectionBrightness { get; }

    public IReadOnlyList<TrajectoryPathPointPresentation> SharedPath { get; }

    public IReadOnlyList<TrajectoryFacingTickPresentation> FreeFacingTicks { get; }

    public IReadOnlyList<TrajectoryFacingTickPresentation> LockOnFacingTicks { get; }
}

public sealed class TrajectoryOverlayPresentation
{
    public TrajectoryOverlayPresentation(
        TrajectoryOverlayGeometry geometry,
        IReadOnlyDictionary<string, ActorTrajectoryOverlayPresentation> actors)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(actors);
        Geometry = geometry;
        Actors = new ReadOnlyDictionary<string, ActorTrajectoryOverlayPresentation>(
            new Dictionary<string, ActorTrajectoryOverlayPresentation>(actors, StringComparer.Ordinal));
    }

    public TrajectoryOverlayGeometry Geometry { get; }

    public IReadOnlyDictionary<string, ActorTrajectoryOverlayPresentation> Actors { get; }
}

public sealed record TopViewActorBodyLayout(
    string ActorId,
    Position3 Position,
    double YawDegrees,
    double AuthoredYawDegrees);

public sealed class TopViewTrajectoryDisplay
{
    public TopViewTrajectoryDisplay(
        SceneSnapshot snapshot,
        MovementTrajectorySet displayedTrajectories,
        TrajectoryOverlayGeometry geometry,
        TrajectoryOverlayPresentation presentation,
        IReadOnlyDictionary<string, TopViewActorBodyLayout> actorBodies)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(displayedTrajectories);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(actorBodies);
        Snapshot = snapshot;
        DisplayedTrajectories = displayedTrajectories;
        Geometry = geometry;
        Presentation = presentation;
        ActorBodies = new ReadOnlyDictionary<string, TopViewActorBodyLayout>(
            new Dictionary<string, TopViewActorBodyLayout>(actorBodies, StringComparer.Ordinal));
    }

    public SceneSnapshot Snapshot { get; }

    public MovementTrajectorySet DisplayedTrajectories { get; }

    public TrajectoryOverlayGeometry Geometry { get; }

    public TrajectoryOverlayPresentation Presentation { get; }

    public IReadOnlyDictionary<string, TopViewActorBodyLayout> ActorBodies { get; }
}

public static class TrajectoryOverlayLayout
{
    private const double FutureBrightness = 0.45;
    private const double UnselectedBrightness = 0.35;
    private const double ComparisonEpsilon = 1e-12;

    public static IReadOnlyList<TopViewDrawLayer> DrawLayerOrder { get; } =
        Array.AsReadOnly<TopViewDrawLayer>(
        [
            TopViewDrawLayer.SharedPaths,
            TopViewDrawLayer.FreeFacingTicks,
            TopViewDrawLayer.LockOnFacingTicks,
            TopViewDrawLayer.LockLines,
            TopViewDrawLayer.ActorBodies,
            TopViewDrawLayer.TargetMarkers,
            TopViewDrawLayer.Text,
        ]);

    public static TrajectoryOverlayGeometry CreateGeometry(MovementTrajectorySet trajectories)
    {
        ArgumentNullException.ThrowIfNull(trajectories);
        var actors = new Dictionary<string, ActorTrajectoryOverlayGeometry>(
            trajectories.Actors.Count,
            StringComparer.Ordinal);
        foreach (var (actorId, trajectory) in trajectories.Actors)
        {
            var sharedPath = trajectory.Samples
                .Select(sample => new TrajectoryPathPointGeometry(sample.TimeSeconds, sample.Position))
                .ToArray();
            var tickSampleIndices = TrajectoryTickSelectionPolicy.SelectOrderedSampleIndices(
                trajectory,
                trajectories.UniformRate);
            var freeTicks = tickSampleIndices
                .Select(index => trajectory.Samples[index])
                .Select(sample => new TrajectoryFacingTickGeometry(
                    sample.TimeSeconds,
                    sample.Position,
                    sample.FreeYawDegrees,
                    ToMarker(sample.AnchorKind),
                    TopViewTickEndpointShape.FreeArrow))
                .ToArray();
            var lockTicks = tickSampleIndices
                .Select(index => trajectory.Samples[index])
                .Select(sample => new TrajectoryFacingTickGeometry(
                    sample.TimeSeconds,
                    sample.Position,
                    sample.LockOnFacing.YawDegrees,
                    ToMarker(sample.AnchorKind),
                    TopViewTickEndpointShape.LockOnBar))
                .ToArray();
            actors.Add(
                actorId,
                new ActorTrajectoryOverlayGeometry(actorId, sharedPath, freeTicks, lockTicks));
        }

        return new TrajectoryOverlayGeometry(actors);
    }

    public static TrajectoryOverlayPresentation CreatePresentation(
        TrajectoryOverlayGeometry geometry,
        double currentTimeSeconds,
        string? selectedActorId)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(currentTimeSeconds) || currentTimeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentTimeSeconds),
                "Current time must be finite and non-negative.");
        }

        var actors = new Dictionary<string, ActorTrajectoryOverlayPresentation>(
            geometry.Actors.Count,
            StringComparer.Ordinal);
        foreach (var (actorId, actor) in geometry.Actors)
        {
            var selectionBrightness = selectedActorId is null ||
                string.Equals(actorId, selectedActorId, StringComparison.Ordinal)
                    ? 1.0
                    : UnselectedBrightness;
            actors.Add(
                actorId,
                new ActorTrajectoryOverlayPresentation(
                    actor,
                    selectionBrightness,
                    actor.SharedPath.Select(point => new TrajectoryPathPointPresentation(
                        point,
                        BrightnessAt(point.TimeSeconds, currentTimeSeconds))),
                    actor.FreeFacingTicks.Select(tick => new TrajectoryFacingTickPresentation(
                        tick,
                        BrightnessAt(tick.TimeSeconds, currentTimeSeconds))),
                    actor.LockOnFacingTicks.Select(tick => new TrajectoryFacingTickPresentation(
                        tick,
                        BrightnessAt(tick.TimeSeconds, currentTimeSeconds)))));
        }

        return new TrajectoryOverlayPresentation(geometry, actors);
    }

    public static TopViewTrajectoryDisplay CreateDisplay(
        SceneProjectionFrame frame,
        TopViewTrajectoryDisplay? previous,
        string? selectedActorId,
        TransformPreview? preview)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var geometry = previous is not null &&
            ReferenceEquals(previous.DisplayedTrajectories.Actors, frame.Trajectories.Actors)
                ? previous.Geometry
                : CreateGeometry(frame.Trajectories);
        var presentation = CreatePresentation(geometry, frame.Snapshot.TimeSeconds, selectedActorId);
        var bodies = CreateActorBodies(frame.Snapshot, preview);
        return new TopViewTrajectoryDisplay(
            frame.Snapshot,
            frame.Trajectories,
            geometry,
            presentation,
            bodies);
    }

    public static TopViewTrajectoryDisplay WithSelection(
        TopViewTrajectoryDisplay display,
        string? selectedActorId)
    {
        ArgumentNullException.ThrowIfNull(display);
        return new TopViewTrajectoryDisplay(
            display.Snapshot,
            display.DisplayedTrajectories,
            display.Geometry,
            CreatePresentation(display.Geometry, display.Snapshot.TimeSeconds, selectedActorId),
            display.ActorBodies);
    }

    public static TopViewTrajectoryDisplay WithPreview(
        TopViewTrajectoryDisplay display,
        TransformPreview? preview)
    {
        ArgumentNullException.ThrowIfNull(display);
        return new TopViewTrajectoryDisplay(
            display.Snapshot,
            display.DisplayedTrajectories,
            display.Geometry,
            display.Presentation,
            CreateActorBodies(display.Snapshot, preview));
    }

    private static IReadOnlyDictionary<string, TopViewActorBodyLayout> CreateActorBodies(
        SceneSnapshot snapshot,
        TransformPreview? preview)
    {
        var bodies = new Dictionary<string, TopViewActorBodyLayout>(
            snapshot.ActorTransforms.Count,
            StringComparer.Ordinal);
        foreach (var (actorId, authored) in snapshot.ActorTransforms)
        {
            var position = authored.Position;
            var yawDegrees = snapshot.ActorFacings.TryGetValue(actorId, out var facing)
                ? facing.YawDegrees
                : authored.YawDegrees;
            var authoredYawDegrees = authored.YawDegrees;
            if (preview is not null && string.Equals(preview.ActorId, actorId, StringComparison.Ordinal))
            {
                position = preview.Position;
                yawDegrees = preview.YawDegrees;
                authoredYawDegrees = preview.YawDegrees;
            }

            bodies.Add(
                actorId,
                new TopViewActorBodyLayout(actorId, position, yawDegrees, authoredYawDegrees));
        }

        return bodies;
    }

    private static TopViewAnchorMarker ToMarker(TrajectoryAnchorKind anchorKind)
    {
        var marker = TopViewAnchorMarker.None;
        if ((anchorKind & (TrajectoryAnchorKind.ActorTransform | TrajectoryAnchorKind.ActiveTargetTransform)) != 0)
        {
            marker |= TopViewAnchorMarker.TransformCircle;
        }

        if ((anchorKind & TrajectoryAnchorKind.ActorLockOn) != 0)
        {
            marker |= TopViewAnchorMarker.LockOnDiamond;
        }

        return marker;
    }

    private static double BrightnessAt(double sampleTimeSeconds, double currentTimeSeconds) =>
        sampleTimeSeconds <= currentTimeSeconds + ComparisonEpsilon ? 1.0 : FutureBrightness;
}
