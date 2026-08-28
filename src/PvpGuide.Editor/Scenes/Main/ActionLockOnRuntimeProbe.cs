using Godot;
using PvpGuide.Application.Projection;
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

            ClickBackground(actionTrackSurface);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 16, 0, 33,
                "빈 Action lane 진입");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    actionInspector.Visible && actionKeyInput.IsVisibleInTree() &&
                    actionAddButton.IsVisibleInTree() && !actionAddButton.Disabled,
                "빈 Action lane viewport click이 첫 Add용 Inspector/input/button을 표시하지 않았습니다.");

            actionKeyInput.Text = "windup";
            var detachActionAddObserver = AttachOneShotChangedFailure(document, "action add observer failed");
            try
            {
                actionAddButton.EmitSignal(Button.SignalName.Pressed);
            }
            finally
            {
                detachActionAddObserver();
            }

            var addedAction = document.GetActionKeyframe(ActorId, ActionId);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 17, 1, 34,
                "Action Add");
            Require(IsAction(addedAction, 0, "windup") &&
                    session.SelectedActionKeyframeId == ActionId &&
                    session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    timelineStatus.Text.Contains(
                        "변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다: action add observer failed",
                        StringComparison.Ordinal),
                "Action Add signal의 mutation-after-observer 상태가 저장·안내되지 않았습니다.");

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

            SetSpinBoxValue(actionTimeInput, 2, "Action out-of-range time");
            actionApplyButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 17, 1, 34,
                "Action range validation");
            Require(actionErrorLabel.Text.Contains("시각은 0초 이상", StringComparison.Ordinal),
                "Action 범위 오류가 별도 한글 안내로 표시되지 않았습니다.");

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
                "Action Apply 버튼이 time/key를 적용하고 이전 오류를 지우지 않았습니다.");

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

            ClickBackground(lockOnTrackSurface);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 21, 5, 43,
                "빈 Lock-on lane 진입");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    lockOnInspector.Visible && lockEnabledInput.IsVisibleInTree() &&
                    lockOnAddButton.IsVisibleInTree() && !lockOnAddButton.Disabled,
                "빈 Lock-on lane viewport click이 첫 Add용 Inspector/input/button을 표시하지 않았습니다.");

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

            SelectOption(lockTargetInput, 0, "invalid Lock-on target");
            lockApplyButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 22, 6, 44,
                "Lock-on target validation");
            Require(lockErrorLabel.Text.Contains("같은 문서의 다른 배우", StringComparison.Ordinal),
                "Lock-on 대상 오류가 별도 한글 안내로 표시되지 않았습니다.");

            SelectOption(lockTargetInput, 1, "restored Lock-on target");
            SetSpinBoxValue(lockYawOffsetInput, 20, "observer Lock-on yaw offset");
            var detachLockApplyObserver = AttachOneShotChangedFailure(document, "lock apply observer failed");
            try
            {
                lockApplyButton.EmitSignal(Button.SignalName.Pressed);
            }
            finally
            {
                detachLockApplyObserver();
            }

            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 23, 7, 45,
                "Lock-on observer Apply");
            Require(IsLock(
                        document.GetLockOnKeyframe(ActorId, LockOnId),
                        0.2,
                        true,
                        TargetActorId,
                        20,
                        LockOnTrackingMode.Continuous) &&
                    lockErrorLabel.Text.Contains(
                        "변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다: lock apply observer failed",
                        StringComparison.Ordinal),
                "Lock-on mutation-after-observer 상태가 저장·안내되지 않았습니다.");

            SelectOption(lockModeInput, (int)LockOnTrackingMode.Snap, "Lock-on Snap mode");
            SetSpinBoxValue(lockYawOffsetInput, -30, "Lock-on updated yaw offset");
            lockApplyButton.EmitSignal(Button.SignalName.Pressed);
            var updatedLock = document.GetLockOnKeyframe(ActorId, LockOnId);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 24, 8, 46,
                "Lock-on Apply");
            Require(IsLock(updatedLock, 0.2, true, TargetActorId, -30, LockOnTrackingMode.Snap) &&
                    session.SelectedLockOnKeyframeId == LockOnId && lockErrorLabel.Text.Length == 0,
                "Lock-on Apply signal이 mode/offset postimage를 저장하지 않았습니다.");

            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn && lockOnInspector.Visible &&
                    undoButton.IsVisibleInTree() && !undoButton.Disabled,
                "Lock-on track에서 global Undo 버튼이 표시·활성화되지 않았습니다.");
            undoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 25, 9, 47,
                "Lock-on Apply Undo");
            Require(IsLock(
                        document.GetLockOnKeyframe(ActorId, LockOnId),
                        0.2,
                        true,
                        TargetActorId,
                        20,
                        LockOnTrackingMode.Continuous) &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    session.CanRedo && redoButton.IsVisibleInTree() && !redoButton.Disabled,
                "Lock-on track의 Undo가 continuous/offset preimage/global Redo를 복원하지 않았습니다.");

            redoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 26, 10, 48,
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
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 27, 11, 49,
                "Lock-on Delete");
            Require(document.Actors.Single(actor => actor.ActorId == ActorId).LockOnKeyframes.Count == 0 &&
                    session.SelectedLockOnKeyframeId is null,
                "Lock-on Delete가 선택 frame을 제거하지 않았습니다.");

            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    undoButton.IsVisibleInTree() && !undoButton.Disabled,
                "Lock-on Delete 뒤 global Undo 버튼이 Lock-on track에서 활성화되지 않았습니다.");
            undoButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 50,
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
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 51,
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
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 51,
                "semantic Add/global history playback lock");
            Require(timelineStatus.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    historyErrorLabel.Text.Contains("재생 중", StringComparison.Ordinal),
                "재생 중 semantic Add/global history signal이 잠금 사유를 표시하지 않았습니다.");

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(!session.Playback.IsPlaying && !actionAddButton.Disabled && !lockOnAddButton.Disabled &&
                    !undoButton.Disabled && !redoButton.Disabled,
                "재생 해제가 semantic Add/global history 가용성을 복원하지 않았습니다.");
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 51,
                "semantic Add/global history playback unlock");

            ClickMarker(lockOnTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 52,
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
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 52,
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
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 28, 12, 53,
                "Action playback guard 준비 scrub");
            actionKeyInput.Text = "guarded-action";
            Require(!actionAddButton.Disabled,
                "재생 전 Action Add가 가능한 빈 exact time을 복원하지 못했습니다.");
            actionAddButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 54,
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
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 54,
                "Action Apply/Delete playback lock");
            Require(actionErrorLabel.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("재생 중", StringComparison.Ordinal) &&
                    IsAction(document.GetActionKeyframe(ActorId, ActionId), 0.75, "guarded-action"),
                "재생 중 Action Apply/Delete가 잠금 사유와 committed frame을 보존하지 않았습니다.");
            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(!session.Playback.IsPlaying && document.Revision == 29 && historyEvents == 13 &&
                    topViewSurface.ApplyCount == 54 && worldAdapter.ApplyCount == 54,
                "semantic playback guard 검증의 pause가 상태를 변경했습니다.");

            ClickMarker(lockOnTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 55,
                "Action에서 Lock-on cross-track marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    lockOnInspector.Visible && !actionInspector.Visible,
                "Action surface에서 다른 시각 Lock-on surface marker로 전환했을 때 " +
                "Lock-on Inspector만 표시되지 않았습니다.");

            ClickMarker(actionTrackSurface, 0.75);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 56,
                "Lock-on에서 Action cross-track marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    session.SelectedActionKeyframeId == ActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.75) &&
                    actionInspector.Visible && !lockOnInspector.Visible,
                "Lock-on surface에서 다른 시각 Action surface marker로 전환했을 때 " +
                "Action Inspector만 표시되지 않았습니다.");

            Scrub(timeSlider, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 29, 13, 57,
                "same-time semantic marker 준비 scrub");
            actionKeyInput.Text = "same-time-action";
            Require(!actionAddButton.Disabled,
                "Lock-on marker와 같은 0.2초에 Action을 추가할 수 있는 상태가 아닙니다.");
            actionAddButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 30, 14, 58,
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
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 30, 14, 58,
                "same-time Action에서 Lock-on marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    session.SelectedLockOnKeyframeId == LockOnId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    lockOnInspector.Visible && !actionInspector.Visible,
                "같은 0.2초 Action에서 실제 Lock-on surface marker를 클릭했을 때 " +
                "Lock-on Inspector만 표시되지 않았습니다.");

            ClickMarker(actionTrackSurface, 0.2);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 30, 14, 58,
                "same-time Lock-on에서 Action marker 전환");
            Require(session.ActiveTimelineTrack == TimelineTrackKind.Action &&
                    session.SelectedActionKeyframeId == SameTimeActionId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.2) &&
                    actionInspector.Visible && !lockOnInspector.Visible,
                "같은 0.2초 Lock-on에서 실제 Action surface marker를 클릭했을 때 " +
                "Action Inspector만 표시되지 않았습니다.");

            SetSpinBoxValue(actionTimeInput, 0.75, "duplicate Action time");
            actionApplyButton.EmitSignal(Button.SignalName.Pressed);
            RequireState(document, session, topViewSurface, worldAdapter, historyEvents, 30, 14, 58,
                "Action duplicate-time validation");
            Require(actionErrorLabel.Text.Contains(
                    "해당 시각에는 이미 Action 키프레임이 있습니다.",
                    StringComparison.Ordinal),
                "Action 동일 시각 중복이 stale/range와 구분된 한글 안내로 표시되지 않았습니다.");
            SetSpinBoxValue(actionTimeInput, 0.2, "restored Action time");

            RunLockOnMotionProbe(topViewSurface, actorsRoot);

            GD.Print(
                "ACTION_LOCK_ON_TRACK_READY action_crud=1 lock_crud=1 step_eval=1 selection_sync=1 " +
                "undo_redo=1 playback_lock=1 top_overlay=1 world_overlay=1");
            GD.Print(
                "ACTION_LOCK_ON_PLAYBACK_GUARDS_READY action_add=1 action_apply=1 action_delete=1 " +
                "lock_add=1 lock_apply=1 lock_delete=1 undo=1 redo=1");
            GD.Print(
                "ACTION_LOCK_ON_REVIEW_FIXES_READY empty_action_add=1 empty_lock_add=1 " +
                "detailed_errors=1 observer_commit=1");
            GD.Print(
                "LOCK_ON_MOTION_READY snap=1 continuous=1 keyframe_only=1 coincidence=1 " +
                "missing_target=1 shared_frame=1 trajectories=1 cache_reuse=1 nodes_reused=1");
        }
        finally
        {
            session.HistoryChanged -= historyHandler;
        }
    }

    private static void RunLockOnMotionProbe(TopViewSurface existingTopView, Node3D existingActorsRoot)
    {
        const string probeActorId = "probe-host";
        const string probeTargetId = "probe-target";
        var document = CreateLockOnMotionDocument(probeActorId, probeTargetId);
        var session = new DocumentSession(document);
        var probeUiRoot = new Control { Name = "LockOnMotionProbeUi", Visible = false };
        existingTopView.AddChild(probeUiRoot);
        var topView = new TopViewSurface
        {
            Name = "LockOnMotionProbeTopView",
            Size = new Vector2(640, 360),
        };
        probeUiRoot.AddChild(topView);
        var timeSlider = new HSlider
        {
            Name = "LockOnMotionProbeTimeSlider",
            MinValue = 0,
            MaxValue = document.DurationSeconds,
            Step = 1d / document.FramesPerSecond,
            Size = new Vector2(480, 24),
        };
        probeUiRoot.AddChild(timeSlider);

        var probeWorldRoot = new Node3D { Name = "LockOnMotionProbeWorld" };
        existingActorsRoot.AddChild(probeWorldRoot);
        var worldView = new WorldViewProjectionAdapter(probeWorldRoot);
        var source = new CountingProjectionSource(session.ProjectionSource);
        var topConsumer = new RecordingProjectionConsumer(topView);
        var worldConsumer = new RecordingProjectionConsumer(worldView);
        topView.Initialize(session);
        using var projection = new SceneProjectionController(
            source,
            session.Playback,
            topConsumer,
            worldConsumer);

        void OnSliderValueChanged(double timeSeconds)
        {
            session.Playback.Pause();
            session.Playback.Seek(timeSeconds);
        }

        timeSlider.ValueChanged += OnSliderValueChanged;
        try
        {
            projection.ProjectCurrent();
            Require(source.MovementTrajectoryBuildCount == 1,
                "bounded probe의 최초 projection이 궤적을 정확히 한 번 만들지 않았습니다.");
            RequireSharedProjectionFrame(topConsumer, worldConsumer, "initial projection");

            SeekWithSlider(timeSlider, 0.5);
            RequireFacingProjection(
                topConsumer,
                worldConsumer,
                probeWorldRoot,
                probeActorId,
                expectedYawDegrees: 15,
                FacingResolutionKind.SnapTarget,
                "Snap hold");
            var shaderBoundaryNodes = GetTrajectoryNodes(probeWorldRoot, probeActorId);
            var shaderBoundarySharedMesh = shaderBoundaryNodes.SharedPath.Mesh;
            var shaderBoundaryFreeMesh = shaderBoundaryNodes.FreeTicks.Mesh;
            var shaderBoundaryLockMesh = shaderBoundaryNodes.LockTicks.Mesh;
            RequireTrajectoryShaderBoundary(
                shaderBoundaryNodes,
                expectedCurrentTimeNormalized: 0.125,
                "0.5초 shader boundary");

            SeekWithSlider(timeSlider, 1.5);
            RequireFacingProjection(
                topConsumer,
                worldConsumer,
                probeWorldRoot,
                probeActorId,
                expectedYawDegrees: 135,
                FacingResolutionKind.ContinuousTarget,
                "Continuous tracking");
            RequireTopViewTrajectoryPresentation(topView, topConsumer.LastFrame!, probeActorId, 1.5);
            RequireTrajectoryShaderBoundary(
                shaderBoundaryNodes,
                expectedCurrentTimeNormalized: 0.375,
                "1.5초 shader boundary");
            Require(ReferenceEquals(shaderBoundarySharedMesh, shaderBoundaryNodes.SharedPath.Mesh) &&
                    ReferenceEquals(shaderBoundaryFreeMesh, shaderBoundaryNodes.FreeTicks.Mesh) &&
                    ReferenceEquals(shaderBoundaryLockMesh, shaderBoundaryNodes.LockTicks.Mesh),
                "0.5→1.5초 seek가 실제 trajectory mesh identity를 변경했습니다.");

            SeekWithSlider(timeSlider, 2.5);
            RequireFacingProjection(
                topConsumer,
                worldConsumer,
                probeWorldRoot,
                probeActorId,
                expectedYawDegrees: 85,
                FacingResolutionKind.AuthoredKeyframeOnly,
                "KeyframeOnly authored yaw");
            RequireTrajectoryShaderBoundary(
                shaderBoundaryNodes,
                expectedCurrentTimeNormalized: 0.625,
                "2.5초 shader boundary");
            Require(ReferenceEquals(shaderBoundarySharedMesh, shaderBoundaryNodes.SharedPath.Mesh) &&
                    ReferenceEquals(shaderBoundaryFreeMesh, shaderBoundaryNodes.FreeTicks.Mesh) &&
                    ReferenceEquals(shaderBoundaryLockMesh, shaderBoundaryNodes.LockTicks.Mesh),
                "0.5→2.5초 seek가 실제 trajectory mesh identity를 변경했습니다.");

            SeekWithSlider(timeSlider, 3.5);
            var coincidentFacing = topConsumer.LastFrame!.Snapshot.ActorFacings[probeActorId];
            Require(coincidentFacing.ResolutionKind == FacingResolutionKind.CoincidentPrevious &&
                    IsNear(coincidentFacing.YawDegrees, 270),
                "위치 일치 시 이전 유효 방향 fallback이 hand-derived 270도를 보존하지 않았습니다.");
            RequireSharedProjectionFrame(topConsumer, worldConsumer, "coincident seek");

            var trajectoryNodes = GetTrajectoryNodes(probeWorldRoot, probeActorId);
            var actorRoot = probeWorldRoot.GetNodeOrNull<Node3D>("Actor_probe_host")
                ?? throw new InvalidOperationException("bounded probe actor root가 없습니다.");
            var originalNodeCount = CountNodes(probeWorldRoot);
            var originalSharedMesh = trajectoryNodes.SharedPath.Mesh;
            var originalFreeMesh = trajectoryNodes.FreeTicks.Mesh;
            var originalLockMesh = trajectoryNodes.LockTicks.Mesh;
            var originalWorldVertices = ReadWorldVertices(trajectoryNodes.SharedPath)
                .Concat(ReadWorldVertices(trajectoryNodes.FreeTicks))
                .Concat(ReadWorldVertices(trajectoryNodes.LockTicks))
                .ToArray();

            var preview = new PvpGuide.Application.Editing.TransformPreview(
                probeActorId,
                "probe-host-t3.5",
                new Position3(9, 2, -7),
                200);
            topView.ApplyPreview(preview);
            worldView.ApplyPreview(preview);
            Require(IsPosition(actorRoot.Position, 9, 2, -7) &&
                    IsNear(actorRoot.Rotation.Y, -200 * Math.PI / 180),
                "실제 WorldView preview가 actor root 이동·회전을 적용하지 않았습니다.");
            var previewWorldVertices = ReadWorldVertices(trajectoryNodes.SharedPath)
                .Concat(ReadWorldVertices(trajectoryNodes.FreeTicks))
                .Concat(ReadWorldVertices(trajectoryNodes.LockTicks))
                .ToArray();
            RequireVectorSequence(originalWorldVertices, previewWorldVertices,
                "actor preview 전후 world-fixed trajectory vertex");
            topView.ApplyPreview(null);
            worldView.ApplyPreview(null);

            SeekWithSlider(timeSlider, 0.5);
            SeekWithSlider(timeSlider, 2.5);
            SeekWithSlider(timeSlider, 1.5);
            Require(CountNodes(probeWorldRoot) == originalNodeCount &&
                    ReferenceEquals(originalSharedMesh, trajectoryNodes.SharedPath.Mesh) &&
                    ReferenceEquals(originalFreeMesh, trajectoryNodes.FreeTicks.Mesh) &&
                    ReferenceEquals(originalLockMesh, trajectoryNodes.LockTicks.Mesh),
                "반복 seek가 trajectory node/resource identity 또는 node count를 변경했습니다.");
            Require(source.MovementTrajectoryBuildCount == 1,
                "반복 seek가 motion cache를 우회해 궤적을 다시 만들었습니다.");

            var buildCountBeforeAction = source.MovementTrajectoryBuildCount;
            var frameBeforeAction = topConsumer.LastFrame!;
            document.AddActionKeyframe(
                probeActorId,
                new ActionKeyframe("probe-action", 0.25, "runtime-probe"));
            var frameAfterAction = topConsumer.LastFrame!;
            Require(source.MovementTrajectoryBuildCount == buildCountBeforeAction &&
                    ReferenceEquals(originalSharedMesh, trajectoryNodes.SharedPath.Mesh) &&
                    ReferenceEquals(originalFreeMesh, trajectoryNodes.FreeTicks.Mesh) &&
                    ReferenceEquals(originalLockMesh, trajectoryNodes.LockTicks.Mesh),
                "Action-only mutation이 trajectory geometry 또는 mesh resource를 다시 만들었습니다.");
            RequireSharedProjectionFrame(topConsumer, worldConsumer, "Action-only mutation");
            Require(!ReferenceEquals(frameBeforeAction, frameAfterAction) &&
                    frameAfterAction.Snapshot.Revision == document.Revision &&
                    ReferenceEquals(frameBeforeAction.Trajectories.Actors, frameAfterAction.Trajectories.Actors),
                "Action-only mutation이 새 revision의 frame을 Top/World에 전달하지 않았거나 geometry cache를 잃었습니다.");

            var buildCountBeforeMotion = source.MovementTrajectoryBuildCount;
            var frameBeforeMotion = topConsumer.LastFrame!;
            var finalTransform = document.Actors
                .Single(actor => actor.ActorId == probeActorId)
                .TransformKeyframes
                .Single(frame => frame.Id == "probe-host-t4");
            Require(document.ReplaceTransformKeyframe(
                    probeActorId,
                    finalTransform,
                    new TransformKeyframe(
                        finalTransform.Id,
                        finalTransform.TimeSeconds,
                        new Position3(1, 0, 0),
                        140)),
                "motion mutation fixture를 적용하지 못했습니다.");
            var frameAfterMotion = topConsumer.LastFrame!;
            Require(source.MovementTrajectoryBuildCount == buildCountBeforeMotion + 1,
                "motion mutation 뒤 trajectory build count가 정확히 1 증가하지 않았습니다.");
            RequireSharedProjectionFrame(topConsumer, worldConsumer, "motion mutation");
            Require(!ReferenceEquals(frameBeforeMotion, frameAfterMotion) &&
                    frameAfterMotion.Snapshot.Revision == document.Revision &&
                    frameAfterMotion.Snapshot.MotionRevision == document.MotionRevision &&
                    !ReferenceEquals(frameBeforeMotion.Trajectories.Actors, frameAfterMotion.Trajectories.Actors),
                "motion mutation이 새 revision/motion revision frame과 새 geometry payload를 전달하지 않았습니다.");

            RequireWorldRemovalOwnership(
                worldView,
                probeWorldRoot,
                frameAfterMotion,
                removedActorId: probeActorId,
                retainedActorId: probeTargetId);

            RequireMissingTargetBadge(probeUiRoot, probeWorldRoot);
            RequireZeroDurationProjection(probeUiRoot, probeWorldRoot);
        }
        finally
        {
            timeSlider.ValueChanged -= OnSliderValueChanged;
            projection.Dispose();
            session.CancelPreview();
            topView.DetachSession();
            probeUiRoot.QueueFree();
            probeWorldRoot.QueueFree();
        }
    }

    private static SceneDocument CreateLockOnMotionDocument(string actorId, string targetActorId) =>
        SceneDocument.Create(
            "lock-on-motion-runtime",
            "Lock-on motion runtime",
            null,
            durationSeconds: 4,
            framesPerSecond: 30,
            [
                new ActorTrack(
                    actorId,
                    "Probe Host",
                    "교육용 배우",
                    [
                        new TransformKeyframe("probe-host-t0", 0, new Position3(0, 0, 0), 10),
                        new TransformKeyframe("probe-host-t1", 1, new Position3(0, 0, 0), 40),
                        new TransformKeyframe("probe-host-t2", 2, new Position3(0, 0, 0), 70),
                        new TransformKeyframe("probe-host-t3", 3, new Position3(0, 0, 0), 100),
                        new TransformKeyframe("probe-host-t3.5", 3.5, new Position3(0, 0, 0), 115),
                        new TransformKeyframe("probe-host-t4", 4, new Position3(0, 0, 0), 130),
                    ],
                    [],
                    [
                        new LockOnKeyframe("probe-snap", 0, true, targetActorId, 15, LockOnTrackingMode.Snap),
                        new LockOnKeyframe("probe-continuous", 1, true, targetActorId, 0, LockOnTrackingMode.Continuous),
                        new LockOnKeyframe("probe-keyframe-only", 2, true, targetActorId, 0, LockOnTrackingMode.KeyframeOnly),
                        new LockOnKeyframe("probe-coincident", 3, true, targetActorId, 0, LockOnTrackingMode.Continuous),
                    ]),
                new ActorTrack(
                    targetActorId,
                    "Probe Target",
                    "교육용 대상",
                    [
                        new TransformKeyframe("probe-target-t0", 0, new Position3(4, 0, 0), 0),
                        new TransformKeyframe("probe-target-t1", 1, new Position3(0, 0, 4), 0),
                        new TransformKeyframe("probe-target-t2", 2, new Position3(-4, 0, 0), 0),
                        new TransformKeyframe("probe-target-t3", 3, new Position3(0, 0, -4), 0),
                        new TransformKeyframe("probe-target-t3.5", 3.5, new Position3(0, 0, 0), 0),
                        new TransformKeyframe("probe-target-t4", 4, new Position3(4, 0, 0), 0),
                    ],
                    [],
                    []),
            ]);

    private static void SeekWithSlider(HSlider slider, double timeSeconds)
    {
        var signalCount = 0;
        var observed = double.NaN;
        void Observe(double value)
        {
            signalCount++;
            observed = value;
        }

        slider.ValueChanged += Observe;
        try
        {
            slider.Value = timeSeconds;
        }
        finally
        {
            slider.ValueChanged -= Observe;
        }

        Require(signalCount == 1 && IsNear(observed, timeSeconds) && IsNear(slider.Value, timeSeconds),
            $"bounded HSlider seek가 {timeSeconds}초 ValueChanged를 정확히 한 번 전달하지 않았습니다.");
    }

    private static void RequireFacingProjection(
        RecordingProjectionConsumer topConsumer,
        RecordingProjectionConsumer worldConsumer,
        Node3D worldRoot,
        string actorId,
        double expectedYawDegrees,
        FacingResolutionKind expectedResolution,
        string stage)
    {
        RequireSharedProjectionFrame(topConsumer, worldConsumer, stage);
        var frame = topConsumer.LastFrame!;
        var facing = frame.Snapshot.ActorFacings[actorId];
        var actorRoot = worldRoot.GetNodeOrNull<Node3D>("Actor_probe_host")
            ?? throw new InvalidOperationException($"{stage}: WorldView actor root가 없습니다.");
        Require(IsNear(facing.YawDegrees, expectedYawDegrees) &&
                facing.ResolutionKind == expectedResolution &&
                IsNear(actorRoot.Rotation.Y, -expectedYawDegrees * Math.PI / 180),
            $"{stage}: hand-derived facing 또는 exact actor root Rotation.Y가 다릅니다.");
    }

    private static void RequireSharedProjectionFrame(
        RecordingProjectionConsumer topConsumer,
        RecordingProjectionConsumer worldConsumer,
        string stage) =>
        Require(topConsumer.LastFrame is not null &&
                ReferenceEquals(topConsumer.LastFrame, worldConsumer.LastFrame),
            $"{stage}: TopView와 WorldView가 동일 SceneProjectionFrame reference를 받지 않았습니다.");

    private static void RequireTopViewTrajectoryPresentation(
        TopViewSurface topView,
        SceneProjectionFrame frame,
        string actorId,
        double currentTimeSeconds)
    {
        Require(ReferenceEquals(topView.DisplayedTrajectories, frame.Trajectories),
            "TopView DisplayedTrajectories가 controller frame의 실제 trajectory set이 아닙니다.");
        var trajectory = topView.DisplayedTrajectories!.Actors[actorId];
        var snapSample = trajectory.Samples.Single(sample => IsNear(sample.TimeSeconds, 0.5));
        var continuousSample = trajectory.Samples.Single(sample => IsNear(sample.TimeSeconds, 1.5));
        var keyframeOnlySample = trajectory.Samples.Single(sample => IsNear(sample.TimeSeconds, 2.5));
        Require(IsNear(snapSample.FreeYawDegrees, 25) && IsNear(snapSample.LockOnFacing.YawDegrees, 15) &&
                IsNear(continuousSample.FreeYawDegrees, 55) &&
                IsNear(continuousSample.LockOnFacing.YawDegrees, 135) &&
                IsNear(keyframeOnlySample.FreeYawDegrees, 85) &&
                IsNear(keyframeOnlySample.LockOnFacing.YawDegrees, 85),
            "TopView free/Lock-on trajectory samples가 hand-derived yaw를 보존하지 않았습니다.");

        var presentationDisplay = topView.DisplayedTrajectoryPresentation
            ?? throw new InvalidOperationException("TopView 실제 trajectory presentation이 없습니다.");
        Require(ReferenceEquals(presentationDisplay.Geometry, topView.DisplayedTrajectoryGeometry),
            "TopView presentation 진단 seam이 현재 displayed geometry를 가리키지 않습니다.");
        var presentation = presentationDisplay.Actors[actorId];
        var allPresented = presentation.SharedPath
            .Select(point => (point.TimeSeconds, point.Brightness))
            .Concat(presentation.FreeFacingTicks.Select(tick => (tick.TimeSeconds, tick.Brightness)))
            .Concat(presentation.LockOnFacingTicks.Select(tick => (tick.TimeSeconds, tick.Brightness)))
            .ToArray();
        Require(allPresented.Any(item => item.TimeSeconds <= currentTimeSeconds) &&
                allPresented.Any(item => item.TimeSeconds > currentTimeSeconds) &&
                allPresented.Where(item => item.TimeSeconds <= currentTimeSeconds)
                    .All(item => IsNear(item.Brightness, 1)) &&
                allPresented.Where(item => item.TimeSeconds > currentTimeSeconds)
                    .All(item => IsNear(item.Brightness, 0.45)),
            "TopView current/future trajectory presentation 밝기가 1/0.45 계약과 다릅니다.");
    }

    private static TrajectoryNodes GetTrajectoryNodes(Node3D worldRoot, string actorId)
    {
        var overlayRoot = worldRoot.GetNodeOrNull<Node3D>("TrajectoryOverlayRoot")
            ?? throw new InvalidOperationException("실제 TrajectoryOverlayRoot가 없습니다.");
        var containerName = $"Trajectory_{actorId.Replace('-', '_')}";
        var container = overlayRoot.GetNodeOrNull<Node3D>(containerName)
            ?? throw new InvalidOperationException($"실제 trajectory actor container '{containerName}'가 없습니다.");
        return new TrajectoryNodes(
            container.GetNodeOrNull<MeshInstance3D>("SharedTrajectory")
                ?? throw new InvalidOperationException("SharedTrajectory mesh가 없습니다."),
            container.GetNodeOrNull<MeshInstance3D>("FreeFacingTicks")
                ?? throw new InvalidOperationException("FreeFacingTicks mesh가 없습니다."),
            container.GetNodeOrNull<MeshInstance3D>("LockOnFacingTicks")
                ?? throw new InvalidOperationException("LockOnFacingTicks mesh가 없습니다."));
    }

    private static Vector3[] ReadWorldVertices(MeshInstance3D instance)
    {
        var mesh = instance.Mesh
            ?? throw new InvalidOperationException($"{instance.Name}의 mesh resource가 없습니다.");
        var vertices = new List<Vector3>();
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            foreach (var local in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                vertices.Add(instance.GlobalTransform * local);
            }
        }

        Require(vertices.Count > 0, $"{instance.Name}의 실제 mesh vertex가 비어 있습니다.");
        return vertices.ToArray();
    }

    private static void RequireTrajectoryShaderBoundary(
        TrajectoryNodes nodes,
        double expectedCurrentTimeNormalized,
        string stage)
    {
        foreach (var meshInstance in new[] { nodes.SharedPath, nodes.FreeTicks, nodes.LockTicks })
        {
            var actualUniform = ReadCurrentTimeUniform(meshInstance);
            var normalizedTimes = ReadNormalizedMeshTimes(meshInstance);
            Require(actualUniform == expectedCurrentTimeNormalized,
                $"{stage}: {meshInstance.Name}의 current_time_normalized가 " +
                $"{expectedCurrentTimeNormalized}이 아닙니다. actual={actualUniform}");
            Require(normalizedTimes.Any(time => time <= expectedCurrentTimeNormalized) &&
                    normalizedTimes.Any(time => time > expectedCurrentTimeNormalized),
                $"{stage}: {meshInstance.Name} 실제 UV.x에 현재/과거와 미래가 모두 없습니다.");
        }
    }

    private static double[] ReadNormalizedMeshTimes(MeshInstance3D instance)
    {
        var mesh = instance.Mesh
            ?? throw new InvalidOperationException($"{instance.Name}의 mesh resource가 없습니다.");
        var normalizedTimes = new List<double>();
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            normalizedTimes.AddRange(
                arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array().Select(uv => (double)uv.X));
        }

        Require(normalizedTimes.Count > 0, $"{instance.Name}의 실제 mesh UV가 비어 있습니다.");
        return normalizedTimes.ToArray();
    }

    private static void RequireVectorSequence(
        IReadOnlyList<Vector3> expected,
        IReadOnlyList<Vector3> actual,
        string stage)
    {
        Require(expected.Count == actual.Count, $"{stage}: vertex 수가 변경되었습니다.");
        for (var index = 0; index < expected.Count; index++)
        {
            Require(expected[index].IsEqualApprox(actual[index]),
                $"{stage}: {index}번째 world vertex가 변경되었습니다.");
        }
    }

    private static int CountNodes(Node node)
    {
        var count = 1;
        foreach (var child in node.GetChildren())
        {
            count += CountNodes(child);
        }

        return count;
    }

    private static void RequireWorldRemovalOwnership(
        WorldViewProjectionAdapter worldView,
        Node3D worldRoot,
        SceneProjectionFrame fullFrame,
        string removedActorId,
        string retainedActorId)
    {
        Require(worldView.ActorCount == 2 && worldView.TrajectoryActorCount == 2,
            "actor removal 전 WorldView actor/trajectory 수가 2/2가 아닙니다.");

        var overlayRoot = worldRoot.GetNodeOrNull<Node3D>("TrajectoryOverlayRoot")
            ?? throw new InvalidOperationException("actor removal probe의 TrajectoryOverlayRoot가 없습니다.");
        var removedActorRoot = worldRoot.GetNodeOrNull<Node3D>("Actor_probe_host")
            ?? throw new InvalidOperationException("제거할 실제 actor root가 없습니다.");
        var retainedActorRoot = worldRoot.GetNodeOrNull<Node3D>("Actor_probe_target")
            ?? throw new InvalidOperationException("보존할 실제 actor root가 없습니다.");
        var removedTrajectoryRoot = overlayRoot.GetNodeOrNull<Node3D>("Trajectory_probe_host")
            ?? throw new InvalidOperationException("제거할 실제 trajectory container가 없습니다.");
        var retainedTrajectoryRoot = overlayRoot.GetNodeOrNull<Node3D>("Trajectory_probe_target")
            ?? throw new InvalidOperationException("보존할 실제 trajectory container가 없습니다.");
        var foreignChild = new Node3D { Name = "ForeignTrajectoryChild" };
        overlayRoot.AddChild(foreignChild);
        var applyCountBeforeRemoval = worldView.ApplyCount;

        var reducedFrame = CreateRetainedActorFrame(fullFrame, retainedActorId);
        worldView.Apply(reducedFrame);

        Require(worldView.ApplyCount == applyCountBeforeRemoval + 1 &&
                worldView.ActorCount == 1 && worldView.TrajectoryActorCount == 1,
            "actor removal frame이 WorldView actor/trajectory 수를 함께 1/1로 줄이지 않았습니다.");
        Require(removedActorRoot.IsQueuedForDeletion() && removedTrajectoryRoot.IsQueuedForDeletion(),
            "제거 대상 actor/trajectory 소유 node가 QueueFree 대기 상태가 아닙니다.");
        Require(!retainedActorRoot.IsQueuedForDeletion() &&
                !retainedTrajectoryRoot.IsQueuedForDeletion() &&
                ReferenceEquals(retainedActorRoot.GetParent(), worldRoot) &&
                ReferenceEquals(retainedTrajectoryRoot.GetParent(), overlayRoot),
            "유지 대상 actor/trajectory node가 제거되거나 parent가 변경됐습니다.");
        Require(!foreignChild.IsQueuedForDeletion() && ReferenceEquals(foreignChild.GetParent(), overlayRoot),
            "adapter가 소유하지 않은 trajectory overlay child를 제거했습니다.");
        Require(reducedFrame.Snapshot.ActorTransforms.Keys.SequenceEqual([retainedActorId]) &&
                reducedFrame.Trajectories.Actors.Keys.SequenceEqual([retainedActorId]) &&
                removedActorId != retainedActorId,
            "actor removal synthetic frame이 유지 actor 하나만 포함하지 않습니다.");
    }

    private static SceneProjectionFrame CreateRetainedActorFrame(
        SceneProjectionFrame fullFrame,
        string retainedActorId)
    {
        var nextRevision = fullFrame.Snapshot.Revision + 1;
        var nextMotionRevision = fullFrame.Snapshot.MotionRevision + 1;
        var retainedTrajectory = fullFrame.Trajectories.Actors[retainedActorId];
        var snapshot = new SceneSnapshot(
            fullFrame.Snapshot.DocumentId,
            nextRevision,
            fullFrame.Snapshot.TimeSeconds,
            new Dictionary<string, EvaluatedTransform>(StringComparer.Ordinal)
            {
                [retainedActorId] = fullFrame.Snapshot.ActorTransforms[retainedActorId],
            },
            new Dictionary<string, EvaluatedActorTimelineState>(StringComparer.Ordinal)
            {
                [retainedActorId] = fullFrame.Snapshot.ActorTimelineStates[retainedActorId],
            },
            new Dictionary<string, EvaluatedActorFacing>(StringComparer.Ordinal)
            {
                [retainedActorId] = fullFrame.Snapshot.ActorFacings[retainedActorId],
            },
            nextMotionRevision);
        var uniformRate = fullFrame.Trajectories.UniformRate
            ?? throw new InvalidOperationException("actor removal fixture에 uniform rate가 없습니다.");
        var trajectories = new MovementTrajectorySet(
            fullFrame.Trajectories.DocumentId,
            nextRevision,
            nextMotionRevision,
            fullFrame.Trajectories.SamplingPolicyFingerprint,
            uniformRate,
            new Dictionary<string, ActorMovementTrajectory>(StringComparer.Ordinal)
            {
                [retainedActorId] = retainedTrajectory,
            },
            retainedTrajectory.SegmentSteps);
        return new SceneProjectionFrame(
            snapshot,
            trajectories,
            fullFrame.SamplingPolicyFingerprint);
    }

    private static void RequireMissingTargetBadge(Control uiParent, Node3D worldParent)
    {
        const string documentId = "missing-target-runtime";
        const string actorId = "missing-host";
        var topView = new TopViewSurface { Name = "MissingTargetTopView", Size = new Vector2(320, 180) };
        uiParent.AddChild(topView);
        var worldRoot = new Node3D { Name = "MissingTargetWorld" };
        worldParent.AddChild(worldRoot);
        try
        {
            var facing = new EvaluatedActorFacing(
                45,
                FacingResolutionKind.TargetUnavailableFallback,
                "missing-lock");
            var snapshot = new SceneSnapshot(
                documentId,
                revision: 0,
                timeSeconds: 0,
                new Dictionary<string, EvaluatedTransform>(StringComparer.Ordinal)
                {
                    [actorId] = new EvaluatedTransform(new Position3(0, 0, 0), 45),
                },
                new Dictionary<string, EvaluatedActorTimelineState>(StringComparer.Ordinal)
                {
                    [actorId] = new EvaluatedActorTimelineState(
                        new EvaluatedActionState(null, null),
                        new EvaluatedLockOnState(
                            "missing-lock",
                            true,
                            "ghost-target",
                            0,
                            LockOnTrackingMode.Continuous)),
                },
                new Dictionary<string, EvaluatedActorFacing>(StringComparer.Ordinal)
                {
                    [actorId] = facing,
                },
                motionRevision: 0);
            var sample = new MovementTrajectorySample(
                0,
                new Position3(0, 0, 0),
                45,
                facing,
                TrajectoryAnchorKind.ActorLockOn);
            var trajectories = new MovementTrajectorySet(
                documentId,
                revision: 0,
                motionRevision: 0,
                "runtime/missing-target",
                uniformRate: 30,
                new Dictionary<string, ActorMovementTrajectory>(StringComparer.Ordinal)
                {
                    [actorId] = new ActorMovementTrajectory(actorId, [sample], segmentSteps: 0),
                },
                segmentSteps: 0);
            var frame = new SceneProjectionFrame(snapshot, trajectories, trajectories.SamplingPolicyFingerprint);
            var worldView = new WorldViewProjectionAdapter(worldRoot);
            topView.Apply(frame);
            worldView.Apply(frame);

            var topBadge = topView.DisplayedSemanticOverlays[actorId].LockBadge;
            var worldBadge = worldRoot
                .GetNodeOrNull<Label3D>("Actor_missing_host/OverlayRoot/LockBadge");
            Require(topBadge == "LOCK · ghost-target · 대상 없음" &&
                    worldBadge is { Visible: true, Text: "LOCK · ghost-target · 대상 없음" },
                "synthetic missing-target snapshot이 Top/World 대상 없음 badge를 표시하지 않았습니다.");
        }
        finally
        {
            topView.QueueFree();
            worldRoot.QueueFree();
        }
    }

    private static void RequireZeroDurationProjection(Control uiParent, Node3D worldParent)
    {
        const string actorId = "zero-host";
        var document = SceneDocument.Create(
            "zero-duration-runtime",
            "Zero duration runtime",
            null,
            durationSeconds: 0,
            framesPerSecond: 30,
            [new ActorTrack(
                actorId,
                "Zero Host",
                "교육용 배우",
                [new TransformKeyframe("zero-origin", 0, new Position3(0, 0, 0), 0)],
                [],
                [])]);
        var session = new DocumentSession(document);
        var topView = new TopViewSurface { Name = "ZeroDurationTopView", Size = new Vector2(320, 180) };
        uiParent.AddChild(topView);
        var worldRoot = new Node3D { Name = "ZeroDurationWorld" };
        worldParent.AddChild(worldRoot);
        topView.Initialize(session);
        var worldView = new WorldViewProjectionAdapter(worldRoot);
        using var projection = new SceneProjectionController(
            session.ProjectionSource,
            session.Playback,
            topView,
            worldView);
        try
        {
            projection.ProjectCurrent();
            var nodes = GetTrajectoryNodes(worldRoot, actorId);
            Require(!session.Playback.IsPlaying && IsNear(session.Playback.CurrentTimeSeconds, 0) &&
                    !session.Playback.Play(),
                "duration 0 fixture가 paused 0초 상태를 유지하지 않았습니다.");
            Require(MeshUvsAreZero(nodes.FreeTicks) && MeshUvsAreZero(nodes.LockTicks) &&
                    IsNear(ReadCurrentTimeUniform(nodes.SharedPath), 0) &&
                    IsNear(ReadCurrentTimeUniform(nodes.FreeTicks), 0) &&
                    IsNear(ReadCurrentTimeUniform(nodes.LockTicks), 0),
                "duration 0 fixture의 trajectory UV/uniform이 0이 아닙니다.");
        }
        finally
        {
            projection.Dispose();
            topView.DetachSession();
            topView.QueueFree();
            worldRoot.QueueFree();
        }
    }

    private static bool MeshUvsAreZero(MeshInstance3D instance)
    {
        var mesh = instance.Mesh
            ?? throw new InvalidOperationException($"{instance.Name} mesh resource가 없습니다.");
        var uvCount = 0;
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            uvCount += uvs.Length;
            if (uvs.Any(uv => !IsNear(uv.X, 0) || !IsNear(uv.Y, 0)))
            {
                return false;
            }
        }

        return uvCount > 0;
    }

    private static double ReadCurrentTimeUniform(MeshInstance3D instance)
    {
        var material = instance.MaterialOverride as ShaderMaterial
            ?? throw new InvalidOperationException($"{instance.Name}의 ShaderMaterial이 없습니다.");
        return material.GetShaderParameter("current_time_normalized").AsDouble();
    }

    private sealed class CountingProjectionSource(ISceneProjectionSource inner) : ISceneProjectionSource
    {
        public int MovementTrajectoryBuildCount { get; private set; }

        public event EventHandler<SceneDocumentChangedEventArgs> Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public SceneSnapshot CreateSnapshot(double timeSeconds) => inner.CreateSnapshot(timeSeconds);

        public ProjectionSourceMetadata GetProjectionMetadata() => inner.GetProjectionMetadata();

        public TrajectorySamplePlan CreateTrajectorySamplePlan(TrajectorySamplingSettings settings) =>
            inner.CreateTrajectorySamplePlan(settings);

        public MovementTrajectorySet CreateMovementTrajectories(TrajectorySamplePlan plan)
        {
            MovementTrajectoryBuildCount++;
            return inner.CreateMovementTrajectories(plan);
        }
    }

    private sealed class RecordingProjectionConsumer(ISceneProjectionConsumer inner) : ISceneProjectionConsumer
    {
        public SceneProjectionFrame? LastFrame { get; private set; }

        public void Apply(SceneProjectionFrame frame)
        {
            LastFrame = frame;
            inner.Apply(frame);
        }
    }

    private sealed record TrajectoryNodes(
        MeshInstance3D SharedPath,
        MeshInstance3D FreeTicks,
        MeshInstance3D LockTicks);

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

    private static void ClickBackground(Control surface)
    {
        Require(float.IsFinite(surface.Size.X) && surface.Size.X > MarkerPadding * 2 &&
                float.IsFinite(surface.Size.Y) && surface.Size.Y > 0,
            "semantic background click을 위한 track surface 크기가 올바르지 않습니다.");
        var localPosition = new Vector2(surface.Size.X * 0.8f, surface.Size.Y / 2);
        PushViewportLeftButton(surface, localPosition, pressed: true);
        PushViewportLeftButton(surface, localPosition, pressed: false);
    }

    private static Action AttachOneShotChangedFailure(SceneDocument document, string message)
    {
        EventHandler<SceneDocumentChangedEventArgs>? observer = null;
        observer = (_, _) =>
        {
            document.Changed -= observer;
            throw new InvalidOperationException(message);
        };
        document.Changed += observer;
        return () => document.Changed -= observer;
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

    private static bool IsPosition(Vector3 actual, double x, double y, double z) =>
        IsNear(actual.X, x) && IsNear(actual.Y, y) && IsNear(actual.Z, z);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Action/Lock-on runtime 검증 실패: {message}");
        }
    }
}
