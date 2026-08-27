using Godot;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Timeline;
using PvpGuide.Editor.Features.TopView;
using PvpGuide.Editor.Features.ViewportSync;

namespace PvpGuide.Editor.Scenes.Main;

internal static class ActionLockOnRuntimeProbe
{
    private const string ActorId = "runtime-actor";
    private const string TargetActorId = "runtime-target";
    private const string ActionId = "runtime-actor-action-0001";
    private const string SameTimeActionId = "runtime-actor-action-0002";
    private const string LockOnId = "runtime-actor-lock-on-0001";
    private const double MarkerPadding = 12;

    public static void Run(
        SceneDocument document,
        DocumentSession session,
        TopViewSurface topViewSurface,
        WorldViewProjectionAdapter worldAdapter,
        Node3D actorsRoot,
        ActionTrackSurface actionTrackSurface,
        Button actionAddButton,
        Button actionDeleteButton,
        Control actionInspector,
        Label actionSelectionLabel,
        SpinBox actionTimeInput,
        LineEdit actionKeyInput,
        Button actionApplyButton,
        Label actionErrorLabel,
        LockOnTrackSurface lockOnTrackSurface,
        Button lockOnAddButton,
        Button lockOnDeleteButton,
        Control lockOnInspector,
        Label lockOnSelectionLabel,
        SpinBox lockTimeInput,
        CheckBox lockEnabledInput,
        OptionButton lockTargetInput,
        OptionButton lockModeInput,
        SpinBox lockYawOffsetInput,
        Button lockApplyButton,
        Label lockErrorLabel,
        Label historyErrorLabel,
        Button undoButton,
        Button redoButton,
        Button playPauseButton,
        HSlider timeSlider,
        Label timelineStatus)
    {
        var historyEvents = 0;
        EventHandler historyHandler = (_, _) => historyEvents++;
        session.HistoryChanged += historyHandler;

        try
        {
            RequireInitialState(document, session, topViewSurface, worldAdapter, historyEvents);
            AddTargetActor(document);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 16, 0, 33,
                "target actor 추가");
            Require(worldAdapter.ActorCount == 2,
                "target actor 추가 뒤 WorldView actor 수가 2가 아닙니다.");

            actionKeyInput.Text = "windup";
            actionAddButton.EmitSignal(Button.SignalName.Pressed);
            var addedAction = document.GetActionKeyframe(ActorId, ActionId);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 17, 1, 34,
                "Action Add");
            Require(IsAction(addedAction, 0, "windup") &&
                    session.SelectedActionKeyframeId == ActionId &&
                    session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    timelineStatus.Text == "Action 추가 완료",
                "Action Add signal이 hand-derived frame/selection/status를 만들지 않았습니다.");

            ClickMarker(actionTrackSurface, 0);
            Require(session.SelectedActionKeyframeId == ActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0) &&
                    actionInspector.Visible &&
                    actionSelectionLabel.Text.Contains(ActionId, StringComparison.Ordinal) &&
                    actionSelectionLabel.Text.Contains("0초", StringComparison.Ordinal) &&
                    IsNear(actionTimeInput.Value, 0) && actionKeyInput.Text == "windup",
                "Action marker viewport click이 selection/time/Inspector를 동기화하지 않았습니다.");
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 17, 1, 34,
                "Action marker click");

            SetSpinBoxValue(actionTimeInput, 0.2, "Action time");
            actionKeyInput.Text = "attack";
            actionApplyButton.EmitSignal(Button.SignalName.Pressed);
            var updatedAction = document.GetActionKeyframe(ActorId, ActionId);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 18, 2, 36,
                "Action Apply");
            Require(IsAction(updatedAction, 0.2, "attack") &&
                    session.SelectedActionKeyframeId == ActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    actionErrorLabel.Text.Length == 0,
                "Action Apply 버튼이 time/key를 하나의 mutation으로 적용하지 않았습니다.");

            actionKeyInput.EmitSignal(LineEdit.SignalName.TextSubmitted, "attack");
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 18, 2, 36,
                "Action LineEdit no-op submit");
            Require(actionErrorLabel.Text.Contains("실제 Action 변경", StringComparison.Ordinal),
                "Action LineEdit no-op submit이 revision/history 불변과 안내 문구를 만들지 않았습니다.");

            Require(session.ActiveTimelineTrack == TimelineTrackKind.Action && actionInspector.Visible &&
                    undoButton.IsVisibleInTree() && !undoButton.Disabled,
                "Action track에서 global Undo 버튼이 표시·활성화되지 않았습니다.");
            undoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 19, 3, 38,
                "Action Apply Undo");
            Require(IsAction(document.GetActionKeyframe(ActorId, ActionId), 0, "windup") &&
                    session.SelectedActionKeyframeId == ActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0) &&
                    session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    session.CanRedo && redoButton.IsVisibleInTree() && !redoButton.Disabled,
                "Action track의 Undo가 preimage/selection/time/global Redo를 복원하지 않았습니다.");

            redoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 20, 4, 40,
                "Action Apply Redo");
            Require(IsAction(document.GetActionKeyframe(ActorId, ActionId), 0.2, "attack") &&
                    session.SelectedActionKeyframeId == ActionId &&
                    session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) && !session.CanRedo,
                "Action track의 Redo가 postimage/selection/time을 복원하지 않았습니다.");

            Scrub(timeSlider, 0.75);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 20, 4, 41,
                "Action left-hold scrub");
            Require(IsNear(session.Playback.CurrentTimeSeconds, 0.75) &&
                    session.SelectedActionKeyframeId is null,
                "Action scrub이 0.75초와 exact-time selection 해제를 만들지 않았습니다.");
            RequireActionOverlay(document, topViewSurface, actorsRoot);

            ClickMarker(actionTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 20, 4, 42,
                "Action marker reselection");
            Require(session.SelectedActionKeyframeId == ActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2),
                "Action marker 재선택이 삭제할 frame/time을 복원하지 않았습니다.");

            actionDeleteButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 21, 5, 43,
                "Action Delete");
            Require(document.Actors.Single(actor => actor.ActorId == ActorId).ActionKeyframes.Count == 0 &&
                    session.SelectedActionKeyframeId is null,
                "Action Delete가 선택 frame을 제거하지 않았습니다.");

            Require(lockTargetInput.ItemCount == 2 && lockTargetInput.GetItemText(1) == TargetActorId,
                "Lock-on target OptionButton이 두 번째 actor를 정확히 노출하지 않았습니다.");
            lockEnabledInput.ButtonPressed = true;
            SelectOption(lockTargetInput, 1, "Lock-on target");
            SelectOption(lockModeInput, (int)LockOnTrackingMode.Continuous, "Lock-on Continuous mode");
            SetSpinBoxValue(lockYawOffsetInput, 15, "Lock-on yaw offset");
            lockOnAddButton.EmitSignal(Button.SignalName.Pressed);
            var addedLock = document.GetLockOnKeyframe(ActorId, LockOnId);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 22, 6, 44,
                "Lock-on Add");
            Require(IsLock(addedLock, 0.2, true, TargetActorId, 15, LockOnTrackingMode.Continuous) &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    timelineStatus.Text == "Lock-on 추가 완료",
                "Lock-on Add signal이 enabled/target/continuous/offset을 저장하지 않았습니다.");

            ClickMarker(lockOnTrackSurface, 0.2);
            Require(session.SelectedLockOnKeyframeId == LockOnId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    lockOnInspector.Visible &&
                    lockOnSelectionLabel.Text.Contains(LockOnId, StringComparison.Ordinal) &&
                    lockOnSelectionLabel.Text.Contains("0.2초", StringComparison.Ordinal) &&
                    IsNear(lockTimeInput.Value, 0.2) && lockEnabledInput.ButtonPressed &&
                    lockTargetInput.Selected == 1 &&
                    lockModeInput.Selected == (int)LockOnTrackingMode.Continuous &&
                    IsNear(lockYawOffsetInput.Value, 15),
                "Lock-on marker viewport click이 selection/time/Inspector를 동기화하지 않았습니다.");
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 22, 6, 44,
                "Lock-on marker click");

            SelectOption(lockModeInput, (int)LockOnTrackingMode.Snap, "Lock-on Snap mode");
            SetSpinBoxValue(lockYawOffsetInput, -30, "Lock-on updated yaw offset");
            lockApplyButton.EmitSignal(Button.SignalName.Pressed);
            var updatedLock = document.GetLockOnKeyframe(ActorId, LockOnId);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 23, 7, 45,
                "Lock-on Apply");
            Require(IsLock(updatedLock, 0.2, true, TargetActorId, -30, LockOnTrackingMode.Snap) &&
                    session.SelectedLockOnKeyframeId == LockOnId && lockErrorLabel.Text.Length == 0,
                "Lock-on Apply signal이 mode/offset postimage를 저장하지 않았습니다.");

            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn && lockOnInspector.Visible &&
                    undoButton.IsVisibleInTree() && !undoButton.Disabled,
                "Lock-on track에서 global Undo 버튼이 표시·활성화되지 않았습니다.");
            undoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 24, 8, 46,
                "Lock-on Apply Undo");
            Require(IsLock(
                        document.GetLockOnKeyframe(ActorId, LockOnId),
                        0.2,
                        true,
                        TargetActorId,
                        15,
                        LockOnTrackingMode.Continuous) &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    session.CanRedo && redoButton.IsVisibleInTree() && !redoButton.Disabled,
                "Lock-on track의 Undo가 continuous/offset preimage/global Redo를 복원하지 않았습니다.");

            redoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 25, 9, 47,
                "Lock-on Apply Redo");
            Require(IsLock(
                        document.GetLockOnKeyframe(ActorId, LockOnId),
                        0.2,
                        true,
                        TargetActorId,
                        -30,
                        LockOnTrackingMode.Snap) &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    session.ActiveTimelineTrack == TimelineTrackKind.LockOn && !session.CanRedo,
                "Lock-on track의 Redo가 snap/offset postimage를 복원하지 않았습니다.");

            lockOnDeleteButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 26, 10, 48,
                "Lock-on Delete");
            Require(document.Actors.Single(actor => actor.ActorId == ActorId).LockOnKeyframes.Count == 0 &&
                    session.SelectedLockOnKeyframeId is null,
                "Lock-on Delete가 선택 frame을 제거하지 않았습니다.");

            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    undoButton.IsVisibleInTree() && !undoButton.Disabled,
                "Lock-on Delete 뒤 global Undo 버튼이 Lock-on track에서 활성화되지 않았습니다.");
            undoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 49,
                "Lock-on Delete Undo");
            Require(IsLock(
                        document.GetLockOnKeyframe(ActorId, LockOnId),
                        0.2,
                        true,
                        TargetActorId,
                        -30,
                        LockOnTrackingMode.Snap) &&
                    session.SelectedLockOnKeyframeId == LockOnId && session.CanRedo,
                "Lock-on Delete Undo가 overlay 확인용 frame을 복원하지 않았습니다.");

            Scrub(timeSlider, 0.75);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 50,
                "Lock-on left-hold scrub");
            Require(IsNear(session.Playback.CurrentTimeSeconds, 0.75) &&
                    session.SelectedLockOnKeyframeId is null,
                "Lock-on scrub이 0.75초와 exact-time selection 해제를 만들지 않았습니다.");
            RequireLockOverlay(document, topViewSurface, actorsRoot);

            actionKeyInput.Text = "guarded-action";
            lockEnabledInput.ButtonPressed = true;
            SelectOption(lockTargetInput, 1, "playback-guard Lock-on target");
            SelectOption(
                lockModeInput,
                (int)LockOnTrackingMode.Continuous,
                "playback-guard Lock-on mode");
            SetSpinBoxValue(lockYawOffsetInput, 15, "playback-guard Lock-on offset");
            Require(!actionAddButton.Disabled && !lockOnAddButton.Disabled &&
                    undoButton.IsVisibleInTree() && redoButton.IsVisibleInTree() &&
                    !undoButton.Disabled && !redoButton.Disabled,
                "재생 전 Action/Lock Add와 global Undo/Redo가 모두 가능한 상태가 아닙니다.");
            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(session.Playback.IsPlaying && actionAddButton.Disabled && lockOnAddButton.Disabled &&
                    undoButton.Disabled && redoButton.Disabled,
                "재생 시작이 semantic Add와 global history를 잠그지 않았습니다.");
            actionKeyInput.Text = "guarded-action";
            lockEnabledInput.ButtonPressed = true;
            SelectOption(lockTargetInput, 1, "재생 중 Lock-on target");
            SelectOption(lockModeInput, (int)LockOnTrackingMode.Continuous, "재생 중 Lock-on mode");
            SetSpinBoxValue(lockYawOffsetInput, 15, "재생 중 Lock-on offset");
            actionAddButton.EmitSignal(Button.SignalName.Pressed);
            lockOnAddButton.EmitSignal(Button.SignalName.Pressed);
            undoButton.EmitSignal(Button.SignalName.Pressed);
            redoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 50,
                "semantic Add/global history playback lock");
            Require(timelineStatus.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    historyErrorLabel.Text.Contains("재생 중", StringComparison.Ordinal),
                "재생 중 semantic Add/global history signal이 잠금 사유를 표시하지 않았습니다.");

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(!session.Playback.IsPlaying && !actionAddButton.Disabled && !lockOnAddButton.Disabled &&
                    !undoButton.Disabled && !redoButton.Disabled,
                "재생 해제가 semantic Add/global history 가용성을 복원하지 않았습니다.");
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 50,
                "semantic Add/global history playback unlock");

            ClickMarker(lockOnTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 51,
                "Lock-on playback guard marker");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn && lockOnInspector.Visible &&
                    !lockApplyButton.Disabled && !lockOnDeleteButton.Disabled,
                "재생 전 Lock-on Apply/Delete가 모두 가능한 selected frame을 준비하지 못했습니다.");
            SelectOption(lockModeInput, (int)LockOnTrackingMode.Continuous, "playback-guard Lock Apply mode");
            SetSpinBoxValue(lockYawOffsetInput, 15, "playback-guard Lock Apply offset");

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(session.Playback.IsPlaying && lockApplyButton.Disabled && lockOnDeleteButton.Disabled,
                "재생 시작이 Lock-on Apply/Delete를 잠그지 않았습니다.");
            SelectOption(lockModeInput, (int)LockOnTrackingMode.Continuous, "재생 중 Lock Apply mode");
            SetSpinBoxValue(lockYawOffsetInput, 15, "재생 중 Lock Apply offset");
            lockApplyButton.EmitSignal(Button.SignalName.Pressed);
            lockOnDeleteButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 51,
                "Lock-on Apply/Delete playback lock");
            Require(lockErrorLabel.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    IsLock(
                        document.GetLockOnKeyframe(ActorId, LockOnId),
                        0.2,
                        true,
                        TargetActorId,
                        -30,
                        LockOnTrackingMode.Snap),
                "재생 중 Lock-on Apply/Delete가 잠금 사유와 committed frame을 보존하지 않았습니다.");
            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(!session.Playback.IsPlaying, "Lock-on playback guard 뒤 paused 상태를 복원하지 못했습니다.");

            Scrub(timeSlider, 0.75);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 52,
                "Action playback guard 준비 scrub");
            actionKeyInput.Text = "guarded-action";
            Require(!actionAddButton.Disabled,
                "재생 전 Action Add가 가능한 빈 exact time을 복원하지 못했습니다.");
            actionAddButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 53,
                "playback guard용 Action 준비");
            Require(IsAction(document.GetActionKeyframe(ActorId, ActionId), 0.75, "guarded-action") &&
                    session.ActiveTimelineTrack == TimelineTrackKind.Action && actionInspector.Visible &&
                    !actionApplyButton.Disabled && !actionDeleteButton.Disabled,
                "재생 전 Action Apply/Delete가 모두 가능한 selected frame을 준비하지 못했습니다.");
            SetSpinBoxValue(actionTimeInput, 0.8, "playback-guard Action time");
            actionKeyInput.Text = "guarded-update";

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(session.Playback.IsPlaying && actionApplyButton.Disabled && actionDeleteButton.Disabled,
                "재생 시작이 Action Apply/Delete를 잠그지 않았습니다.");
            SetSpinBoxValue(actionTimeInput, 0.8, "재생 중 Action time");
            actionKeyInput.Text = "guarded-update";
            actionApplyButton.EmitSignal(Button.SignalName.Pressed);
            actionDeleteButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 53,
                "Action Apply/Delete playback lock");
            Require(actionErrorLabel.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    IsAction(document.GetActionKeyframe(ActorId, ActionId), 0.75, "guarded-action"),
                "재생 중 Action Apply/Delete가 잠금 사유와 committed frame을 보존하지 않았습니다.");
            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(!session.Playback.IsPlaying && document.Revision == 28 && historyEvents == 12 &&
                    topViewSurface.ApplyCount == 53 && worldAdapter.ApplyCount == 53,
                "semantic playback guard 검증의 pause가 상태를 변경했습니다.");

            ClickMarker(lockOnTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 54,
                "Action에서 Lock-on cross-track marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    lockOnInspector.Visible && !actionInspector.Visible,
                "Action surface에서 다른 시각 Lock-on surface marker로 전환했을 때 " +
                "Lock-on Inspector만 표시되지 않았습니다.");

            ClickMarker(actionTrackSurface, 0.75);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 55,
                "Lock-on에서 Action cross-track marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    session.SelectedActionKeyframeId == ActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.75) &&
                    actionInspector.Visible && !lockOnInspector.Visible,
                "Lock-on surface에서 다른 시각 Action surface marker로 전환했을 때 " +
                "Action Inspector만 표시되지 않았습니다.");

            Scrub(timeSlider, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 56,
                "same-time semantic marker 준비 scrub");
            actionKeyInput.Text = "same-time-action";
            Require(!actionAddButton.Disabled,
                "Lock-on marker와 같은 0.2초에 Action을 추가할 수 있는 상태가 아닙니다.");
            actionAddButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 57,
                "same-time Action marker 추가");
            var sameTimeAction = document.GetActionKeyframe(ActorId, SameTimeActionId);
            Require(sameTimeAction.Id == SameTimeActionId &&
                    IsNear(sameTimeAction.TimeSeconds, 0.2) &&
                    sameTimeAction.ActionKey == "same-time-action" &&
                    session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    session.SelectedActionKeyframeId == SameTimeActionId &&
                    actionInspector.Visible && !lockOnInspector.Visible,
                "실제 Action Add signal이 Lock-on과 같은 시각의 Action marker를 준비하지 못했습니다. " +
                $"actual=id:{sameTimeAction.Id},time:{sameTimeAction.TimeSeconds},key:{sameTimeAction.ActionKey}," +
                $"active:{session.ActiveTimelineTrack},selection:{session.SelectedActionKeyframeId}," +
                $"actionVisible:{actionInspector.Visible},lockVisible:{lockOnInspector.Visible}");

            ClickMarker(lockOnTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 57,
                "same-time Action에서 Lock-on marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    lockOnInspector.Visible && !actionInspector.Visible,
                "같은 0.2초 Action에서 실제 Lock-on surface marker를 클릭했을 때 " +
                "Lock-on Inspector만 표시되지 않았습니다.");

            ClickMarker(actionTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 57,
                "same-time Lock-on에서 Action marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    session.SelectedActionKeyframeId == SameTimeActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    actionInspector.Visible && !lockOnInspector.Visible,
                "같은 0.2초 Lock-on에서 실제 Action surface marker를 클릭했을 때 " +
                "Action Inspector만 표시되지 않았습니다.");

            GD.Print(
                "ACTION_LOCK_ON_TRACK_READY action_crud=1 lock_crud=1 step_eval=1 selection_sync=1 " +
                "undo_redo=1 playback_lock=1 top_overlay=1 world_overlay=1");
            GD.Print(
                "ACTION_LOCK_ON_PLAYBACK_GUARDS_READY action_add=1 action_apply=1 action_delete=1 " +
                "lock_add=1 lock_apply=1 lock_delete=1 undo=1 redo=1");
        }
        finally
        {
            session.HistoryChanged -= historyHandler;
        }
    }

    private static void RequireInitialState(
        SceneDocument document,
        DocumentSession session,
        TopViewSurface topViewSurface,
        WorldViewProjectionAdapter worldAdapter,
        int historyEvents)
    {
        var actor = document.Actors.Single(candidate => candidate.ActorId == ActorId);
        var transform = actor.TransformKeyframes.Single();
        Require(document.Revision == 15 && historyEvents == 0 &&
                topViewSurface.ApplyCount == 32 && worldAdapter.ApplyCount == 32 &&
                worldAdapter.ActorCount == 1 && document.Actors.Count == 1 &&
                session.SelectedActorId == ActorId && session.SelectedTransformKeyframeId == "runtime-origin" &&
                IsNear(session.Playback.CurrentTimeSeconds, 0) && !session.Playback.IsPlaying &&
                transform.Id == "runtime-origin" && IsNear(transform.TimeSeconds, 0) &&
                transform.Position == new Position3(1, 0, 0) && IsNear(transform.YawDegrees, 0) &&
                actor.ActionKeyframes.Count == 0 && actor.LockOnKeyframes.Count == 0,
            "probe가 transform CRUD의 hand-derived 최종 상태에서 시작하지 않았습니다.");
    }

    private static void AddTargetActor(SceneDocument document)
    {
        document.AddActor(new ActorTrack(
            TargetActorId,
            "Runtime Target",
            "Lock-on 대상",
            [new TransformKeyframe("runtime-target-origin", 0, new Position3(4, 0, 3), 180)],
            [],
            []));

        var actor = document.Actors.Single(candidate => candidate.ActorId == ActorId);
        var transform = actor.TransformKeyframes.Single();
        Require(transform.Id == "runtime-origin" && transform.Position == new Position3(1, 0, 0) &&
                actor.ActionKeyframes.Count == 0 && actor.LockOnKeyframes.Count == 0,
            "target actor 추가가 기존 runtime actor 상태를 변경했습니다.");
    }

    private static void RequireActionOverlay(
        SceneDocument document,
        TopViewSurface topViewSurface,
        Node3D actorsRoot)
    {
        var snapshot = document.CreateSnapshot(0.75);
        var state = snapshot.ActorTimelineStates[ActorId];
        var topOverlay = topViewSurface.DisplayedSemanticOverlays[ActorId];
        var nodes = GetWorldOverlayNodes(actorsRoot);
        Require(state.Action.SourceKeyframeId == ActionId && state.Action.ActionKey == "attack" &&
                state.LockOn.SourceKeyframeId is null &&
                topOverlay.ActionLabel == "행동: attack" && topOverlay.LockBadge is null &&
                topOverlay.LockLine is null &&
                nodes.ActionLabel.Visible && nodes.ActionLabel.Text == "행동: attack" &&
                !nodes.LockBadge.Visible && nodes.LockBadge.Text.Length == 0 && !nodes.LockLine.Visible,
            "0.75초 Action left-hold의 Top/World overlay text/visibility가 다릅니다.");
    }

    private static void RequireLockOverlay(
        SceneDocument document,
        TopViewSurface topViewSurface,
        Node3D actorsRoot)
    {
        var snapshot = document.CreateSnapshot(0.75);
        var state = snapshot.ActorTimelineStates[ActorId];
        var topOverlay = topViewSurface.DisplayedSemanticOverlays[ActorId];
        var nodes = GetWorldOverlayNodes(actorsRoot);
        Require(state.Action.SourceKeyframeId is null && state.Action.ActionKey is null &&
                state.LockOn.SourceKeyframeId == LockOnId && state.LockOn.Enabled &&
                state.LockOn.TargetActorId == TargetActorId && IsNear(state.LockOn.YawOffsetDegrees, -30) &&
                state.LockOn.TrackingMode == LockOnTrackingMode.Snap &&
                topOverlay.ActionLabel is null &&
                topOverlay.LockBadge == "LOCK · runtime-target · SNAP" &&
                topOverlay.LockLine == new SemanticOverlayLine(new Position3(1, 0, 0), new Position3(4, 0, 3)) &&
                !nodes.ActionLabel.Visible && nodes.ActionLabel.Text.Length == 0 &&
                nodes.LockBadge.Visible && nodes.LockBadge.Text == "LOCK · runtime-target · SNAP" &&
                nodes.LockLine.Visible,
            "0.75초 Lock-on left-hold의 Top/World overlay state/text/visibility가 다릅니다.");
    }

    private static (Label3D ActionLabel, Label3D LockBadge, MeshInstance3D LockLine) GetWorldOverlayNodes(
        Node3D actorsRoot)
    {
        var actorRoot = actorsRoot.GetNodeOrNull<Node3D>("Actor_runtime_actor")
            ?? throw new InvalidOperationException("Action/Lock-on probe: runtime actor WorldView root가 없습니다.");
        var overlayRoot = actorRoot.GetNodeOrNull<Node3D>("OverlayRoot")
            ?? throw new InvalidOperationException("Action/Lock-on probe: WorldView OverlayRoot가 없습니다.");
        return (
            overlayRoot.GetNodeOrNull<Label3D>("ActionLabel")
                ?? throw new InvalidOperationException("Action/Lock-on probe: WorldView ActionLabel이 없습니다."),
            overlayRoot.GetNodeOrNull<Label3D>("LockBadge")
                ?? throw new InvalidOperationException("Action/Lock-on probe: WorldView LockBadge가 없습니다."),
            overlayRoot.GetNodeOrNull<MeshInstance3D>("LockLine")
                ?? throw new InvalidOperationException("Action/Lock-on probe: WorldView LockLine이 없습니다."));
    }

    private static void SetSpinBoxValue(SpinBox input, double value, string stage)
    {
        var signalCount = 0;
        var observed = double.NaN;
        void OnValueChanged(double emittedValue)
        {
            signalCount++;
            observed = emittedValue;
        }

        input.ValueChanged += OnValueChanged;
        try
        {
            input.Value = value;
        }
        finally
        {
            input.ValueChanged -= OnValueChanged;
        }

        Require(signalCount == 1 && IsNear(observed, value) && IsNear(input.Value, value),
            $"{stage} SpinBox.ValueChanged가 정확히 한 번 전달되지 않았습니다.");
    }

    private static void SelectOption(OptionButton input, int index, string stage)
    {
        var signalCount = 0;
        long observed = -1;
        void OnItemSelected(long selectedIndex)
        {
            signalCount++;
            observed = selectedIndex;
        }

        input.Select(index);
        input.ItemSelected += OnItemSelected;
        try
        {
            input.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
        finally
        {
            input.ItemSelected -= OnItemSelected;
        }

        Require(signalCount == 1 && observed == index && input.Selected == index,
            $"{stage} OptionButton.ItemSelected가 정확히 한 번 전달되지 않았습니다.");
    }

    private static void Scrub(HSlider timeSlider, double timeSeconds)
    {
        var signalCount = 0;
        var observed = double.NaN;
        void OnValueChanged(double value)
        {
            signalCount++;
            observed = value;
        }

        timeSlider.ValueChanged += OnValueChanged;
        try
        {
            var grabberWidth = timeSlider.GetThemeIcon("grabber").GetWidth();
            var usableWidth = timeSlider.Size.X - grabberWidth;
            Require(usableWidth > 0 && timeSlider.MaxValue > timeSlider.MinValue,
                "Timeline scrub을 위한 HSlider geometry/range가 올바르지 않습니다.");
            var ratio = (timeSeconds - timeSlider.MinValue) /
                (timeSlider.MaxValue - timeSlider.MinValue);
            var localPosition = new Vector2(
                (float)((grabberWidth / 2d) + (usableWidth * ratio)),
                timeSlider.Size.Y / 2);
            PushViewportLeftButton(timeSlider, localPosition, pressed: true);
            PushViewportLeftButton(timeSlider, localPosition, pressed: false);
        }
        finally
        {
            timeSlider.ValueChanged -= OnValueChanged;
        }

        Require(signalCount == 1 && IsNear(observed, timeSeconds) && IsNear(timeSlider.Value, timeSeconds),
            "Timeline scrub ValueChanged가 hand-derived 0.75초로 정확히 한 번 전달되지 않았습니다.");
    }

    private static void ClickMarker(Control surface, double timeSeconds)
    {
        Require(float.IsFinite(surface.Size.X) && surface.Size.X > MarkerPadding * 2 &&
                float.IsFinite(surface.Size.Y) && surface.Size.Y > 0,
            "semantic marker click을 위한 track surface 크기가 올바르지 않습니다.");
        var localPosition = new Vector2(
            (float)(MarkerPadding + (timeSeconds * (surface.Size.X - (MarkerPadding * 2)))),
            surface.Size.Y / 2);
        PushViewportLeftButton(surface, localPosition, pressed: true);
        PushViewportLeftButton(surface, localPosition, pressed: false);
    }

    private static void PushViewportLeftButton(Control control, Vector2 localPosition, bool pressed)
    {
        var viewportPosition = control.GetGlobalRect().Position + localPosition;
        control.GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = pressed ? MouseButtonMask.Left : (MouseButtonMask)0,
            Pressed = pressed,
            Position = viewportPosition,
        }, inLocalCoords: true);
    }

    private static void RequireState(
        SceneDocument document,
        DocumentSession session,
        TopViewSurface topViewSurface,
        WorldViewProjectionAdapter worldAdapter,
        int actualHistoryEvents,
        long expectedRevision,
        int expectedHistoryEvents,
        int expectedApplyCount,
        string stage) =>
        Require(document.Revision == expectedRevision && session.CurrentRevision == expectedRevision &&
                actualHistoryEvents == expectedHistoryEvents &&
                topViewSurface.ApplyCount == expectedApplyCount &&
                worldAdapter.ApplyCount == expectedApplyCount,
            $"{stage}: revision/history/TopView/WorldView apply count가 " +
            $"{expectedRevision}/{expectedHistoryEvents}/{expectedApplyCount}/{expectedApplyCount}와 다릅니다. " +
            $"actual={document.Revision}/{actualHistoryEvents}/{topViewSurface.ApplyCount}/{worldAdapter.ApplyCount}");

    private static bool IsAction(ActionKeyframe frame, double timeSeconds, string actionKey) =>
        frame.Id == ActionId && IsNear(frame.TimeSeconds, timeSeconds) && frame.ActionKey == actionKey;

    private static bool IsLock(
        LockOnKeyframe frame,
        double timeSeconds,
        bool enabled,
        string targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode) =>
        frame.Id == LockOnId && IsNear(frame.TimeSeconds, timeSeconds) && frame.Enabled == enabled &&
        frame.TargetActorId == targetActorId && IsNear(frame.YawOffsetDegrees, yawOffsetDegrees) &&
        frame.TrackingMode == trackingMode;

    private static bool IsNear(double actual, double expected) => Math.Abs(actual - expected) <= 0.0001;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Action/Lock-on runtime 검증 실패: {message}");
        }
    }
}
