using System.Collections.ObjectModel;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Editor.Features.Timeline;

public sealed record SemanticOverlayLine(Position3 Start, Position3 End);

public sealed record SemanticActorOverlay(
    string? ActionLabel,
    string? LockBadge,
    SemanticOverlayLine? LockLine,
    Position3? TargetMarkerPosition);

public static class SemanticOverlayLayout
{
    public static SemanticActorOverlay Create(
        Position3 actorPosition,
        Position3? targetPosition,
        string? actionKey,
        bool lockEnabled,
        string? targetActorId,
        LockOnTrackingMode trackingMode)
    {
        var actionLabel = string.IsNullOrWhiteSpace(actionKey)
            ? null
            : $"행동: {actionKey}";
        if (!lockEnabled)
        {
            return new SemanticActorOverlay(actionLabel, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(targetActorId))
        {
            throw new InvalidOperationException("Enabled lock-on requires a target actor ID.");
        }

        if (targetPosition is null)
        {
            throw new InvalidOperationException(
                $"Enabled lock-on target '{targetActorId}' is missing from actor transforms.");
        }

        var mode = trackingMode switch
        {
            LockOnTrackingMode.Snap => "SNAP",
            LockOnTrackingMode.Continuous => "CONT",
            LockOnTrackingMode.KeyframeOnly => "KEY",
            _ => trackingMode.ToString().ToUpperInvariant(),
        };
        return new SemanticActorOverlay(
            actionLabel,
            $"LOCK · {targetActorId} · {mode}",
            new SemanticOverlayLine(actorPosition, targetPosition.Value),
            targetPosition.Value);
    }

    public static IReadOnlyDictionary<string, SemanticActorOverlay> CreateScene(
        SceneSnapshot snapshot,
        IReadOnlyDictionary<string, Position3>? displayedPositions = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var overlays = new Dictionary<string, SemanticActorOverlay>(
            snapshot.ActorTransforms.Count,
            StringComparer.Ordinal);
        foreach (var (actorId, transform) in snapshot.ActorTransforms)
        {
            snapshot.ActorTimelineStates.TryGetValue(actorId, out var state);
            var actorPosition = GetDisplayedPosition(actorId, transform.Position, displayedPositions);
            Position3? targetPosition = null;
            if (state?.LockOn is { Enabled: true, TargetActorId: { } targetActorId } &&
                snapshot.ActorTransforms.TryGetValue(targetActorId, out var targetTransform))
            {
                targetPosition = GetDisplayedPosition(targetActorId, targetTransform.Position, displayedPositions);
            }

            overlays.Add(actorId, Create(
                actorPosition,
                targetPosition,
                state?.Action.ActionKey,
                state?.LockOn.Enabled ?? false,
                state?.LockOn.TargetActorId,
                state?.LockOn.TrackingMode ?? LockOnTrackingMode.Continuous));
        }

        return new ReadOnlyDictionary<string, SemanticActorOverlay>(overlays);
    }

    private static Position3 GetDisplayedPosition(
        string actorId,
        Position3 committed,
        IReadOnlyDictionary<string, Position3>? displayedPositions) =>
        displayedPositions is not null && displayedPositions.TryGetValue(actorId, out var displayed)
            ? displayed
            : committed;
}
