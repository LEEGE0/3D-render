using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Timeline;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class SemanticOverlayLayoutTests
{
    [Fact]
    public void Enabled_lock_layout_connects_actor_and_target_centers()
    {
        var overlay = SemanticOverlayLayout.Create(
            actorPosition: new Position3(1, 0, 2),
            targetPosition: new Position3(4, 0, -1),
            actionKey: "attack",
            lockEnabled: true,
            targetActorId: "invader",
            trackingMode: LockOnTrackingMode.Continuous);

        Assert.Equal("행동: attack", overlay.ActionLabel);
        Assert.Equal("LOCK · invader · CONT", overlay.LockBadge);
        Assert.Equal(
            new SemanticOverlayLine(new Position3(1, 0, 2), new Position3(4, 0, -1)),
            overlay.LockLine);
        Assert.Equal(new Position3(4, 0, -1), overlay.TargetMarkerPosition);
    }

    [Fact]
    public void Missing_action_and_disabled_lock_hide_semantic_overlay()
    {
        var overlay = SemanticOverlayLayout.Create(
            actorPosition: new Position3(1, 0, 2),
            targetPosition: null,
            actionKey: null,
            lockEnabled: false,
            targetActorId: "invader",
            trackingMode: LockOnTrackingMode.KeyframeOnly);

        Assert.Null(overlay.ActionLabel);
        Assert.Null(overlay.LockBadge);
        Assert.Null(overlay.LockLine);
        Assert.Null(overlay.TargetMarkerPosition);
    }

    [Fact]
    public void Enabled_lock_without_target_position_fails_explicitly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SemanticOverlayLayout.Create(
            actorPosition: new Position3(1, 0, 2),
            targetPosition: null,
            actionKey: "attack",
            lockEnabled: true,
            targetActorId: "missing",
            trackingMode: LockOnTrackingMode.Snap));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_layout_uses_preview_position_with_committed_semantic_state()
    {
        var snapshot = CreateSnapshot(
            targetActorId: "invader",
            transforms: new Dictionary<string, EvaluatedTransform>(StringComparer.Ordinal)
            {
                ["host"] = new(new Position3(1, 0, 2), 0),
                ["invader"] = new(new Position3(4, 0, -1), 180),
            });

        var overlays = SemanticOverlayLayout.CreateScene(
            snapshot,
            new Dictionary<string, Position3>(StringComparer.Ordinal)
            {
                ["host"] = new Position3(8, 0, 6),
            });

        Assert.Equal("행동: attack", overlays["host"].ActionLabel);
        Assert.Equal(new Position3(8, 0, 6), overlays["host"].LockLine!.Start);
        Assert.Equal(new Position3(4, 0, -1), overlays["host"].LockLine!.End);
    }

    [Fact]
    public void Scene_layout_rejects_enabled_target_missing_from_snapshot_transforms()
    {
        var snapshot = CreateSnapshot(
            targetActorId: "missing",
            transforms: new Dictionary<string, EvaluatedTransform>(StringComparer.Ordinal)
            {
                ["host"] = new(new Position3(1, 0, 2), 0),
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => SemanticOverlayLayout.CreateScene(snapshot));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    private static SceneSnapshot CreateSnapshot(
        string targetActorId,
        IReadOnlyDictionary<string, EvaluatedTransform> transforms) =>
        new(
            "semantic-layout",
            revision: 3,
            timeSeconds: 1.5,
            transforms,
            new Dictionary<string, EvaluatedActorTimelineState>(StringComparer.Ordinal)
            {
                ["host"] = new(
                    new EvaluatedActionState("action", "attack"),
                    new EvaluatedLockOnState(
                        "lock",
                        true,
                        targetActorId,
                        0,
                        LockOnTrackingMode.Continuous)),
            });
}
