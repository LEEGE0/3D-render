using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Projection;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Inspector;
using PvpGuide.Editor.Features.Timeline;
using PvpGuide.Editor.Features.TopView;
using PvpGuide.Editor.Features.ViewportSync;

namespace PvpGuide.Editor.Scenes.Main;

public partial class Main : Control
{
    private SceneProjectionController? _projectionController;
    private TransformPreviewController? _previewController;
    private TransformInspectorController? _inspectorController;
    private TimelineController? _timelineController;
    private TopViewSurface? _topViewSurface;
    private TransformTrackSurface? _transformTrackSurface;
    private PlaybackClock? _playback;

    private static readonly string[] RequiredPanels =
    [
        "TopViewPanel",
        "WorldViewPanel",
        "TimelinePanel",
        "InspectorPanel",
    ];

    public override void _Ready()
    {
        foreach (var panelName in RequiredPanels)
        {
            if (GetNodeOrNull<Control>(panelName) is null)
            {
                GD.PushError($"필수 패널이 없습니다: {panelName}");
                return;
            }
        }

        var topViewSurface = GetNodeOrNull<TopViewSurface>("TopViewPanel/TopViewSurface");
        var worldViewportContainer = GetNodeOrNull<SubViewportContainer>("WorldViewPanel/WorldViewportContainer");
        var worldViewport = GetNodeOrNull<SubViewport>("WorldViewPanel/WorldViewportContainer/WorldViewport");
        var worldRoot = GetNodeOrNull<Node3D>("WorldViewPanel/WorldViewportContainer/WorldViewport/WorldRoot");
        var camera = GetNodeOrNull<Camera3D>("WorldViewPanel/WorldViewportContainer/WorldViewport/WorldRoot/Camera3D");
        var light = GetNodeOrNull<DirectionalLight3D>("WorldViewPanel/WorldViewportContainer/WorldViewport/WorldRoot/DirectionalLight3D");
        var ground = GetNodeOrNull<MeshInstance3D>("WorldViewPanel/WorldViewportContainer/WorldViewport/WorldRoot/Ground");
        var actorsRoot = GetNodeOrNull<Node3D>("WorldViewPanel/WorldViewportContainer/WorldViewport/WorldRoot/Actors");
        var selectedActorLabel = GetNodeOrNull<Label>("InspectorPanel/TransformInspector/SelectedActorLabel");
        var selectedKeyframeLabel = GetNodeOrNull<Label>("InspectorPanel/TransformInspector/SelectedKeyframeLabel");
        var errorLabel = GetNodeOrNull<Label>("InspectorPanel/TransformInspector/ErrorLabel");
        var timeInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/TimeInput");
        var xInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/XInput");
        var yInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/YInput");
        var zInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/ZInput");
        var yawInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/YawInput");
        var applyButton = GetNodeOrNull<Button>("InspectorPanel/TransformInspector/ApplyButton");
        var undoButton = GetNodeOrNull<Button>("InspectorPanel/TransformInspector/UndoButton");
        var redoButton = GetNodeOrNull<Button>("InspectorPanel/TransformInspector/RedoButton");
        var playPauseButton = GetNodeOrNull<Button>("TimelinePanel/TimelineControls/PlaybackButtons/PlayPauseButton");
        var stopButton = GetNodeOrNull<Button>("TimelinePanel/TimelineControls/PlaybackButtons/StopButton");
        var transformTrackSurface = GetNodeOrNull<TransformTrackSurface>("TimelinePanel/TimelineControls/TransformTrackSurface");
        var addKeyframeButton = GetNodeOrNull<Button>("TimelinePanel/TimelineControls/KeyframeToolbar/AddKeyframeButton");
        var deleteKeyframeButton = GetNodeOrNull<Button>("TimelinePanel/TimelineControls/KeyframeToolbar/DeleteKeyframeButton");
        var timeSlider = GetNodeOrNull<HSlider>("TimelinePanel/TimelineControls/TimeSlider");
        var currentTimeLabel = GetNodeOrNull<Label>("TimelinePanel/TimelineControls/CurrentTimeLabel");
        var timelineStatus = GetNodeOrNull<Label>("TimelinePanel/TimelineControls/TimelineStatus");
        if (topViewSurface is null || worldViewportContainer is null || worldViewport is null || worldRoot is null ||
            camera is null || light is null || ground is null || actorsRoot is null ||
            selectedActorLabel is null || selectedKeyframeLabel is null || errorLabel is null || timeInput is null ||
            xInput is null || yInput is null || zInput is null || yawInput is null ||
            applyButton is null || undoButton is null || redoButton is null ||
            playPauseButton is null || stopButton is null || transformTrackSurface is null ||
            addKeyframeButton is null || deleteKeyframeButton is null || timeSlider is null ||
            currentTimeLabel is null || timelineStatus is null)
        {
            GD.PushError("타임라인과 기본 편집 UI에 필요한 자식 노드가 없습니다.");
            return;
        }

        GD.Print("PROJECT_RUNTIME_READY");

        var document = new SceneDocument("main-runtime", 1, 30);
        document.AddActor(new ActorTrack(
            "runtime-actor",
            "Runtime Actor",
            "교육용 배우",
            [
                new TransformKeyframe("runtime-origin", 0, new Position3(0, 0, 0), 0),
                new TransformKeyframe("runtime-end", 1, new Position3(5, 2, -4), 90),
            ],
            [],
            []));

        var session = new DocumentSession(document);
        var worldAdapter = new WorldViewProjectionAdapter(actorsRoot);
        try
        {
            _playback = session.Playback;
            _topViewSurface = topViewSurface;
            topViewSurface.Initialize(session);
            _transformTrackSurface = transformTrackSurface;
            transformTrackSurface.Attach(session);
            _projectionController = new SceneProjectionController(
                session.SnapshotSource,
                session.Playback,
                topViewSurface,
                worldAdapter);
            _previewController = new TransformPreviewController(
                session,
                topViewSurface,
                worldAdapter);
            _inspectorController = new TransformInspectorController(
                session,
                selectedActorLabel,
                selectedKeyframeLabel,
                errorLabel,
                timeInput,
                xInput,
                yInput,
                zInput,
                yawInput,
                applyButton,
                undoButton,
                redoButton);
            _timelineController = new TimelineController(
                session,
                playPauseButton,
                stopButton,
                timeSlider,
                currentTimeLabel,
                timelineStatus,
                transformTrackSurface,
                addKeyframeButton,
                deleteKeyframeButton);

            _projectionController.ProjectCurrent();
            GD.Print($"PROJECTION_SYNC_READY revision={document.Revision} top={topViewSurface.ApplyCount} world={worldAdapter.ApplyCount}");

            RunBasicEditingIntegration(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorsRoot,
                xInput,
                yInput,
                zInput,
                yawInput,
                applyButton,
                undoButton,
                redoButton,
                errorLabel);
            GD.Print(
                $"BASIC_EDITING_READY revision={document.Revision} selected={session.SelectedActorId} " +
                "moved=1 undo=1 redo=1 " +
                $"top={topViewSurface.ApplyCount} world={worldAdapter.ApplyCount} actors={worldAdapter.ActorCount}");

            RunTimelinePlaybackIntegration(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorsRoot,
                xInput,
                yInput,
                zInput,
                yawInput,
                timeInput,
                applyButton,
                undoButton,
                redoButton,
                errorLabel,
                playPauseButton,
                stopButton,
                addKeyframeButton,
                deleteKeyframeButton,
                timeSlider,
                currentTimeLabel,
                timelineStatus);
            Callable.From(() => CompleteDeferredRuntimeVerification(() =>
                    RunTimelineKeyframeCrudIntegration(
                        document,
                        session,
                        topViewSurface,
                        worldAdapter,
                        actorsRoot,
                        transformTrackSurface,
                        selectedKeyframeLabel,
                        timeInput,
                        xInput,
                        yInput,
                        zInput,
                        yawInput,
                        applyButton,
                        undoButton,
                        redoButton,
                        playPauseButton,
                        addKeyframeButton,
                        deleteKeyframeButton,
                        timeSlider,
                        errorLabel,
                        timelineStatus)))
                .CallDeferred();
        }
        catch (Exception exception)
        {
            FailRuntimeVerification("ready", exception);
        }
    }

    public override void _Process(double delta) => _playback?.Advance(delta);

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Keycode: Key.Space, Pressed: true, Echo: false })
        {
            _timelineController?.TogglePlayback();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree() => DisposeControllers();

    private void DisposeControllers()
    {
        _timelineController?.Dispose();
        _timelineController = null;
        _inspectorController?.Dispose();
        _inspectorController = null;
        _previewController?.Dispose();
        _previewController = null;
        _projectionController?.Dispose();
        _projectionController = null;
        _transformTrackSurface?.Detach();
        _transformTrackSurface = null;
        _topViewSurface?.DetachSession();
        _topViewSurface = null;
        _playback = null;
    }

    private void CompleteDeferredRuntimeVerification(Action verification)
    {
        var isHeadless = DisplayServer.GetName() == "headless";
        try
        {
            verification();
            if (isHeadless)
            {
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            FailRuntimeVerification("deferred", exception);
        }
    }

    private void FailRuntimeVerification(string phase, Exception exception)
    {
        GD.PushError($"GODOT_RUNTIME_VERIFICATION_FAILED phase={phase}: {exception}");
        try
        {
            DisposeControllers();
        }
        catch (Exception cleanupException)
        {
            GD.PushError($"GODOT_RUNTIME_CLEANUP_FAILED phase={phase}: {cleanupException}");
        }

        if (DisplayServer.GetName() == "headless")
        {
            GetTree().Quit(1);
        }
    }

    private static void RunBasicEditingIntegration(
        SceneDocument document,
        DocumentSession session,
        TopViewSurface topViewSurface,
        WorldViewProjectionAdapter worldAdapter,
        Node3D actorsRoot,
        SpinBox xInput,
        SpinBox yInput,
        SpinBox zInput,
        SpinBox yawInput,
        Button applyButton,
        Button undoButton,
        Button redoButton,
        Label errorLabel)
    {
        Require(document.Revision == 1, "초기 문서 revision이 1이 아닙니다.");
        Require(worldAdapter.ActorCount == 1, "초기 3D 배우 수가 1이 아닙니다.");
        Require(undoButton.Disabled && redoButton.Disabled, "초기 Undo/Redo 버튼 상태가 올바르지 않습니다.");

        var actorRoot = actorsRoot.GetNodeOrNull<Node3D>("Actor_runtime_actor")
            ?? throw new InvalidOperationException("기본 편집 통합 검증 실패: actor ID 기반 3D root가 생성되지 않았습니다.");
        Require(actorRoot.GetNodeOrNull<Node3D>("VisualRoot") is not null, "VisualRoot가 없습니다.");
        Require(actorRoot.GetNodeOrNull<Node3D>("OverlayRoot") is not null, "OverlayRoot가 없습니다.");
        Require(IsPosition(actorRoot.Position, 0, 0, 0), "초기 3D 위치가 committed 문서와 다릅니다.");
        Require(IsNear(actorRoot.Rotation.Y, 0), "초기 3D Yaw가 committed 문서와 다릅니다.");

        var mapper = new TopViewCoordinateMapper(
            Math.Max(topViewSurface.Size.X, 1),
            Math.Max(topViewSurface.Size.Y, 1),
            centerX: 0,
            centerZ: 0,
            pixelsPerUnit: 40);
        var actorCenter = mapper.WorldToScreen(new Position3(0, 0, 0));
        SendLeftButton(topViewSurface, actorCenter, pressed: true);
        SendLeftButton(topViewSurface, actorCenter, pressed: false);
        Require(session.SelectedActorId == "runtime-actor", "탑뷰 몸체 클릭 선택이 실패했습니다.");

        var rotationHandle = mapper.RotationHandlePosition(actorCenter, 0);
        SendLeftButton(topViewSurface, rotationHandle, pressed: true);
        SendLeftMotion(
            topViewSurface,
            new ScreenPoint(actorCenter.X, actorCenter.Y + 28),
            leftButtonPressed: true);
        Require(document.Revision == 1, "회전 preview가 문서 revision을 변경했습니다.");
        Require(IsNear(actorRoot.Rotation.Y, -Math.PI / 2), "회전 preview가 3D에 적용되지 않았습니다.");
        topViewSurface._GuiInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Require(document.Revision == 1, "Escape가 preview를 문서에 확정했습니다.");
        Require(IsNear(actorRoot.Rotation.Y, 0), "Escape 뒤 committed 3D Yaw가 복원되지 않았습니다.");

        SendLeftButton(topViewSurface, actorCenter, pressed: true);
        var movedPointer = new ScreenPoint(actorCenter.X + 40, actorCenter.Y);
        SendLeftMotion(topViewSurface, movedPointer, leftButtonPressed: true);
        Require(document.Revision == 1, "이동 drag preview가 문서 revision을 변경했습니다.");
        Require(IsPosition(actorRoot.Position, 1, 0, 0), "이동 preview가 3D에 적용되지 않았습니다.");
        SendLeftButton(topViewSurface, movedPointer, pressed: false);
        Require(document.Revision == 2, "탑뷰 mouse release가 이동 명령 하나를 확정하지 않았습니다.");
        Require(document.GetTransformKeyframe("runtime-actor", "runtime-origin").Position == new Position3(1, 0, 0),
            "탑뷰 이동이 최초 키프레임에 저장되지 않았습니다.");
        Require(!undoButton.Disabled && redoButton.Disabled,
            "이동 확정 후 HistoryChanged 기반 Undo/Redo 버튼 상태가 올바르지 않습니다.");

        undoButton.EmitSignal(Button.SignalName.Pressed);
        Require(document.Revision == 3, "Undo 버튼이 revision 3을 만들지 않았습니다.");
        Require(document.GetTransformKeyframe("runtime-actor", "runtime-origin").Position == new Position3(0, 0, 0),
            "Undo 버튼이 committed 위치를 복원하지 않았습니다.");
        Require(IsPosition(actorRoot.Position, 0, 0, 0), "Undo가 3D 위치에 반영되지 않았습니다.");
        Require(undoButton.Disabled && !redoButton.Disabled,
            "Undo 뒤 HistoryChanged 기반 버튼 상태가 올바르지 않습니다.");

        redoButton.EmitSignal(Button.SignalName.Pressed);
        Require(document.Revision == 4, "Redo 버튼이 revision 4를 만들지 않았습니다.");
        var finalTransform = document.GetTransformKeyframe("runtime-actor", "runtime-origin");
        Require(finalTransform.Position == new Position3(1, 0, 0) && IsNear(finalTransform.YawDegrees, 0),
            "Redo 버튼이 committed 변환을 다시 적용하지 않았습니다.");
        Require(IsPosition(actorRoot.Position, 1, 0, 0) && IsNear(actorRoot.Rotation.Y, 0),
            "Redo가 3D committed 변환에 반영되지 않았습니다.");
        Require(!undoButton.Disabled && redoButton.Disabled,
            "Redo 뒤 HistoryChanged 기반 버튼 상태가 올바르지 않습니다.");

        var revisionBeforeInvalidInput = document.Revision;
        xInput.Value = 2;
        Require(document.Revision == revisionBeforeInvalidInput, "유효 Inspector preview가 문서 revision을 변경했습니다.");
        Require(IsPosition(actorRoot.Position, 2, 0, 0), "유효 Inspector preview가 3D에 적용되지 않았습니다.");
        Require(!undoButton.Disabled && redoButton.Disabled,
            "유효 Inspector preview가 history 버튼 상태를 변경했습니다.");

        xInput.Value = 1001;
        Require(IsNear(xInput.Value, 1001), "Inspector가 범위 밖 X 입력을 받아들이지 않았습니다.");
        Require(document.Revision == revisionBeforeInvalidInput, "범위 밖 Inspector 입력이 문서를 변경했습니다.");
        Require(document.GetTransformKeyframe("runtime-actor", "runtime-origin").Position == finalTransform.Position,
            "범위 밖 Inspector 입력이 최초 키프레임을 변경했습니다.");
        Require(!string.IsNullOrWhiteSpace(errorLabel.Text), "범위 밖 Inspector 입력 오류가 표시되지 않았습니다.");
        Require(IsPosition(actorRoot.Position, 1, 0, 0),
            "범위 밖 Inspector 입력이 기존 preview를 취소하고 committed 3D를 복원하지 않았습니다.");

        applyButton.EmitSignal(Button.SignalName.Pressed);
        Require(IsNear(xInput.Value, 1001), "범위 밖 Apply가 사용자의 잘못된 입력값을 덮어썼습니다.");
        Require(document.Revision == revisionBeforeInvalidInput, "범위 밖 Apply가 문서 revision을 변경했습니다.");
        Require(IsPosition(actorRoot.Position, 1, 0, 0), "범위 밖 Apply가 preview를 다시 만들었습니다.");
        Require(!string.IsNullOrWhiteSpace(errorLabel.Text), "범위 밖 Apply가 오류 메시지를 지웠습니다.");

        var committedCenter = mapper.WorldToScreen(finalTransform.Position);
        var committedRotationHandle = mapper.RotationHandlePosition(committedCenter, finalTransform.YawDegrees);
        SendLeftButton(topViewSurface, committedRotationHandle, pressed: true);
        SendLeftMotion(
            topViewSurface,
            new ScreenPoint(committedCenter.X, committedCenter.Y + 28),
            leftButtonPressed: true);
        Require(document.Revision == revisionBeforeInvalidInput,
            "유효 TopView preview가 문서 revision을 변경했습니다.");
        Require(IsNear(actorRoot.Rotation.Y, -Math.PI / 2),
            "유효 TopView preview가 3D 회전에 반영되지 않았습니다.");
        Require(string.IsNullOrWhiteSpace(errorLabel.Text),
            "유효 TopView preview가 Inspector의 이전 오류를 지우지 않았습니다.");
        topViewSurface._GuiInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Require(document.Revision == revisionBeforeInvalidInput,
            "유효 TopView preview 취소가 문서 revision을 변경했습니다.");
        Require(IsPosition(actorRoot.Position, 1, 0, 0) && IsNear(actorRoot.Rotation.Y, 0),
            "유효 TopView preview 취소가 committed 3D 변환을 복원하지 않았습니다.");

        xInput.Value = 1;
        applyButton.EmitSignal(Button.SignalName.Pressed);
        session.CancelPreview();
        Require(document.Revision == revisionBeforeInvalidInput, "Inspector no-op Apply가 문서 revision을 변경했습니다.");
        Require(document.GetTransformKeyframe("runtime-actor", "runtime-origin").Position == finalTransform.Position,
            "Inspector no-op Apply가 committed 변환을 변경했습니다.");
        Require(errorLabel.Text.Contains("실제 변환 변경", StringComparison.Ordinal),
            "Inspector no-op Apply가 실제 변경 없음 메시지를 표시하지 않았습니다.");
        Require(!undoButton.Disabled && redoButton.Disabled,
            "Inspector 거부/no-op 뒤 history 버튼 상태가 바뀌었습니다.");
        Require(topViewSurface.ApplyCount == 4 && worldAdapter.ApplyCount == 4,
            "preview 또는 거부 입력이 committed projection count를 변경했습니다.");

        SendLeftButton(topViewSurface, committedRotationHandle, pressed: true);
        SendLeftMotion(
            topViewSurface,
            new ScreenPoint(committedCenter.X, committedCenter.Y + 28),
            leftButtonPressed: true);
        Require(IsNear(actorRoot.Rotation.Y, -Math.PI / 2),
            "최종 UI 정리 preview가 3D에 반영되지 않았습니다.");
        topViewSurface._GuiInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });

        VerifyTemporaryRotationCommit(topViewSurface);
        VerifyTemporaryInspectorPaths(topViewSurface);
        VerifyCollidingActorNodes(actorsRoot);

        Require(string.IsNullOrWhiteSpace(errorLabel.Text),
            "runtime self-test 뒤 Inspector 오류가 남았습니다.");
        RequirePreviewInactive(session);
        Require(document.Revision == 4, "runtime self-test 정리 뒤 revision이 4가 아닙니다.");
        Require(document.GetTransformKeyframe("runtime-actor", "runtime-origin").Position == new Position3(1, 0, 0),
            "runtime self-test 정리 뒤 committed 키프레임이 바뀌었습니다.");
        Require(IsPosition(actorRoot.Position, 1, 0, 0) && IsNear(actorRoot.Rotation.Y, 0),
            "runtime self-test 정리 뒤 3D가 committed 변환과 다릅니다.");
        Require(IsNear(xInput.Value, 1) && IsNear(yInput.Value, 0) &&
                IsNear(zInput.Value, 0) && IsNear(yawInput.Value, 0),
            "runtime self-test 정리 뒤 Inspector 값이 committed 변환과 다릅니다.");
        Require(!undoButton.Disabled && redoButton.Disabled,
            "runtime self-test 정리 뒤 Undo/Redo 버튼 상태가 올바르지 않습니다.");
        Require(topViewSurface.ApplyCount == 4 && worldAdapter.ApplyCount == 4,
            "runtime self-test 정리가 committed projection count를 변경했습니다.");

        GD.Print(
            "BASIC_EDITING_INTEGRATION_READY rotation_preview=1 escape_restore=1 drag_commit=1 " +
            "undo_button=1 redo_button=1 inspector_reject=1 invalid_preview_cancel=1 " +
            "stale_error_clear=1 inspector_apply_noop=1 collision_nodes=1 final_ui_clean=1 " +
            "rotation_commit=1 enter_commit=1 removal_ownership=1");
    }

    private void RunTimelinePlaybackIntegration(
        SceneDocument document,
        DocumentSession session,
        TopViewSurface topViewSurface,
        WorldViewProjectionAdapter worldAdapter,
        Node3D actorsRoot,
        SpinBox xInput,
        SpinBox yInput,
        SpinBox zInput,
        SpinBox yawInput,
        SpinBox timeInput,
        Button applyButton,
        Button undoButton,
        Button redoButton,
        Label errorLabel,
        Button playPauseButton,
        Button stopButton,
        Button addKeyframeButton,
        Button deleteKeyframeButton,
        HSlider timeSlider,
        Label currentTimeLabel,
        Label timelineStatus)
    {
        const double midpointSeconds = 0.5;
        const double activeAdvanceDeltaSeconds = 0.1;
        const double userScrubSeconds = 0.7;
        var actorRoot = actorsRoot.GetNodeOrNull<Node3D>("Actor_runtime_actor")
            ?? throw new InvalidOperationException("타임라인 통합 검증 실패: runtime actor root가 없습니다.");
        var actorBefore = document.Actors.Single(actor => actor.ActorId == "runtime-actor");
        var firstKeyframeBefore = document.GetTransformKeyframe("runtime-actor", "runtime-origin");
        var endKeyframeBefore = document.GetTransformKeyframe("runtime-actor", "runtime-end");
        var revisionBefore = document.Revision;
        var topApplyCountBefore = topViewSurface.ApplyCount;
        var worldApplyCountBefore = worldAdapter.ApplyCount;
        var historyEvents = 0;
        var previewUpdates = 0;
        var previewClears = 0;
        EventHandler historyHandler = (_, _) => historyEvents++;
        EventHandler<TransformPreviewChangedEventArgs> previewHandler = (_, eventArgs) =>
        {
            if (eventArgs.Preview is null)
            {
                previewClears++;
            }
            else
            {
                previewUpdates++;
            }
        };
        session.HistoryChanged += historyHandler;
        session.PreviewChanged += previewHandler;

        try
        {
            Require(IsNear(timeInput.Step, 1d / 30d),
                "TimeInput이 장면의 1/30 프레임 단계를 유지하지 않았습니다.");
            Require(revisionBefore == 4 && topApplyCountBefore == 4 && worldApplyCountBefore == 4,
                "기본 편집 검사가 revision/top/world 4에서 끝나지 않았습니다.");
            Require(actorBefore.TransformKeyframes.Count == 2 &&
                    firstKeyframeBefore.Position == new Position3(1, 0, 0) &&
                    IsNear(firstKeyframeBefore.YawDegrees, 0) &&
                    endKeyframeBefore.Position == new Position3(5, 2, -4) &&
                    IsNear(endKeyframeBefore.YawDegrees, 90),
                "결정적 타임라인의 두 committed 키프레임이 준비되지 않았습니다.");

            xInput.Value = 2;
            Require(previewUpdates > 0 && IsPosition(actorRoot.Position, 2, 0, 0),
                "slider seek 전에 실제 Inspector ValueChanged preview가 표시되지 않았습니다.");
            Require(document.Revision == revisionBefore && historyEvents == 0,
                "Inspector preview가 revision 또는 history를 변경했습니다.");

            timeSlider.Value = midpointSeconds;
            Require(IsNear(session.Playback.CurrentTimeSeconds, midpointSeconds) && !session.Playback.IsPlaying,
                "HSlider ValueChanged가 0.5초 seek와 pause를 적용하지 않았습니다.");
            Require(previewClears == 1,
                "HSlider seek가 활성 preview를 정확히 한 번 취소하지 않았습니다.");
            Require(IsPosition(actorRoot.Position, 3, 1, -2) && IsNear(actorRoot.Rotation.Y, -Math.PI / 4),
                "preview 취소 뒤 hand-derived 0.5초 committed midpoint가 3D에 복원되지 않았습니다.");
            Require(topViewSurface.ApplyCount == topApplyCountBefore + 1 &&
                    worldAdapter.ApplyCount == worldApplyCountBefore + 1,
                "0.5초 seek가 TopView와 WorldView에 같은 projection 전이를 만들지 않았습니다.");
            Require(IsNear(timeSlider.Value, midpointSeconds) && currentTimeLabel.Text.Contains("0.500초", StringComparison.Ordinal),
                "0.5초 seek가 timeline 표시를 갱신하지 않았습니다.");

            session.SelectActor(null);
            var mapper = new TopViewCoordinateMapper(
                Math.Max(topViewSurface.Size.X, 1),
                Math.Max(topViewSurface.Size.Y, 1),
                centerX: 0,
                centerZ: 0,
                pixelsPerUnit: 40);
            var midpointCenter = mapper.WorldToScreen(new Position3(3, 1, -2));
            SendLeftButton(topViewSurface, midpointCenter, pressed: true);
            SendLeftButton(topViewSurface, midpointCenter, pressed: false);
            Require(session.SelectedActorId == "runtime-actor",
                "TopView가 hand-derived midpoint 위치에서 runtime actor를 찾지 못했습니다.");
            VerifyMidpointTopViewYaw(topViewSurface, document.CreateSnapshot(midpointSeconds));

            Require(!session.CanEditSelectedTransform &&
                    session.EditLockReason?.Contains("선택한 키프레임 시각", StringComparison.Ordinal) == true,
                "0.5초 read-only 편집 잠금이 활성화되지 않았습니다.");
            Require(!xInput.Editable && !yInput.Editable && !zInput.Editable && !yawInput.Editable &&
                    applyButton.Disabled && undoButton.Disabled && redoButton.Disabled &&
                    !addKeyframeButton.Disabled && deleteKeyframeButton.Disabled &&
                    timelineStatus.Text.Contains("삭제 불가", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("선택한 키프레임 시각", StringComparison.Ordinal),
                "Inspector/history/timeline UI가 0.5초 편집 잠금을 표시하지 않았습니다.");

            var guardedPointer = new ScreenPoint(midpointCenter.X + 40, midpointCenter.Y);
            SendLeftButton(topViewSurface, midpointCenter, pressed: true);
            SendLeftMotion(topViewSurface, guardedPointer, leftButtonPressed: true);
            SendLeftButton(topViewSurface, guardedPointer, pressed: false);
            xInput.Value = 9;
            applyButton.EmitSignal(Button.SignalName.Pressed);
            Require(errorLabel.Text.Contains("선택한 키프레임 시각", StringComparison.Ordinal) &&
                    IsPosition(actorRoot.Position, 3, 1, -2) && IsNear(actorRoot.Rotation.Y, -Math.PI / 4),
                "중간 시각 TopView 또는 Inspector edit guard가 committed midpoint를 보존하지 못했습니다.");

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(session.Playback.IsPlaying && playPauseButton.Text == "일시정지" &&
                    session.EditLockReason?.Contains("재생 중", StringComparison.Ordinal) == true,
                "PlayPauseButton.Pressed가 재생 상태와 편집 잠금을 갱신하지 않았습니다.");

            var topApplyCountBeforeActiveAdvance = topViewSurface.ApplyCount;
            var worldApplyCountBeforeActiveAdvance = worldAdapter.ApplyCount;
            session.Playback.Advance(activeAdvanceDeltaSeconds);
            Require(session.Playback.IsPlaying && playPauseButton.Text == "일시정지" &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.6) &&
                    IsNear(timeSlider.Value, 0.6) &&
                    currentTimeLabel.Text.Contains("0.600초", StringComparison.Ordinal),
                "재생 중 sub-end Advance가 active 상태와 slider/label 전진을 보존하지 않았습니다.");
            Require(topViewSurface.ApplyCount == topApplyCountBeforeActiveAdvance + 1 &&
                    worldAdapter.ApplyCount == worldApplyCountBeforeActiveAdvance + 1 &&
                    IsPosition(actorRoot.Position, 3.4f, 1.2f, -2.4f) &&
                    IsNear(actorRoot.Rotation.Y, -Math.PI * 0.3),
                "재생 중 sub-end Advance가 TopView와 WorldView에 새 projection을 정확히 한 번 전달하지 않았습니다.");

            var topApplyCountBeforeUserScrub = topViewSurface.ApplyCount;
            var worldApplyCountBeforeUserScrub = worldAdapter.ApplyCount;
            timeSlider.Value = userScrubSeconds;
            Require(!session.Playback.IsPlaying && playPauseButton.Text == "재생" &&
                    IsNear(session.Playback.CurrentTimeSeconds, userScrubSeconds) &&
                    IsNear(timeSlider.Value, userScrubSeconds) &&
                    currentTimeLabel.Text.Contains("0.700초", StringComparison.Ordinal),
                "재생 중 실제 HSlider.ValueChanged scrub이 pause 후 새 시각을 seek하지 않았습니다.");
            Require(topViewSurface.ApplyCount == topApplyCountBeforeUserScrub + 1 &&
                    worldAdapter.ApplyCount == worldApplyCountBeforeUserScrub + 1 &&
                    IsPosition(actorRoot.Position, 3.8f, 1.4f, -2.8f) &&
                    IsNear(actorRoot.Rotation.Y, -Math.PI * 0.35),
                "사용자 slider scrub이 TopView와 WorldView에 새 projection을 정확히 한 번 전달하지 않았습니다.");

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(session.Playback.IsPlaying && IsNear(session.Playback.CurrentTimeSeconds, userScrubSeconds),
                "Space viewport routing 전에 playback을 재개하지 못했습니다.");

            var focusedStopPresses = 0;
            Action focusedStopHandler = () => focusedStopPresses++;
            stopButton.Pressed += focusedStopHandler;
            try
            {
                stopButton.GrabFocus();
                Require(ReferenceEquals(GetViewport().GuiGetFocusOwner(), stopButton),
                    "Space viewport routing probe에서 Stop button이 실제 focus를 얻지 못했습니다.");

                PushKeyInput(GetViewport(), Key.Space, pressed: true);
                PushKeyInput(GetViewport(), Key.Space, pressed: false);
                Require(!session.Playback.IsPlaying && playPauseButton.Text == "재생" &&
                        IsNear(session.Playback.CurrentTimeSeconds, userScrubSeconds) &&
                        focusedStopPresses == 0,
                    "focus된 Stop button보다 먼저 global Space toggle이 처리되지 않았습니다.");

                PushKeyInput(GetViewport(), Key.Space, pressed: true);
                PushKeyInput(GetViewport(), Key.Space, pressed: false);
                Require(session.Playback.IsPlaying && playPauseButton.Text == "일시정지" &&
                        IsNear(session.Playback.CurrentTimeSeconds, userScrubSeconds) &&
                        focusedStopPresses == 0,
                    "global Space viewport routing이 같은 toggle 경로로 재생을 재개하지 않았습니다.");
            }
            finally
            {
                stopButton.Pressed -= focusedStopHandler;
                stopButton.ReleaseFocus();
            }

            Require(topViewSurface.ApplyCount == topApplyCountBefore + 3 &&
                    worldAdapter.ApplyCount == worldApplyCountBefore + 3,
                "같은 시각의 play/pause 전이가 (revision,time) projection을 중복 적용했습니다.");

            session.Playback.Advance(10);
            Require(IsNear(session.Playback.CurrentTimeSeconds, 1) && !session.Playback.IsPlaying,
                "결정적 Advance가 duration에 clamp한 뒤 자동 pause하지 않았습니다.");
            Require(IsPosition(actorRoot.Position, 5, 2, -4) && IsNear(actorRoot.Rotation.Y, -Math.PI / 2) &&
                    topViewSurface.ApplyCount == topApplyCountBefore + 4 &&
                    worldAdapter.ApplyCount == worldApplyCountBefore + 4,
                "end clamp snapshot이 두 view에 마지막 committed 변환으로 동기화되지 않았습니다.");

            stopButton.EmitSignal(Button.SignalName.Pressed);
            Require(IsNear(session.Playback.CurrentTimeSeconds, 0) && !session.Playback.IsPlaying &&
                    IsNear(timeSlider.Value, 0) && playPauseButton.Text == "재생",
                "StopButton.Pressed가 0초 paused 상태를 복원하지 않았습니다.");
            Require(session.CanEditSelectedTransform && session.EditLockReason is null &&
                    xInput.Editable && yInput.Editable && zInput.Editable && yawInput.Editable &&
                    !applyButton.Disabled && !undoButton.Disabled && redoButton.Disabled &&
                    addKeyframeButton.Disabled && !deleteKeyframeButton.Disabled &&
                    timelineStatus.Text.Contains("추가 불가", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("편집 가능", StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(errorLabel.Text),
                "Stop 뒤 최초 키프레임 편집 상태가 복원되지 않았습니다.");
            Require(IsPosition(actorRoot.Position, 1, 0, 0) && IsNear(actorRoot.Rotation.Y, 0) &&
                    IsNear(xInput.Value, 1) && IsNear(yInput.Value, 0) &&
                    IsNear(zInput.Value, 0) && IsNear(yawInput.Value, 0) &&
                    topViewSurface.ApplyCount == topApplyCountBefore + 5 &&
                    worldAdapter.ApplyCount == worldApplyCountBefore + 5,
                "Stop 뒤 두 view와 Inspector가 edited t=0 committed 변환을 복원하지 않았습니다.");

            var actorAfter = document.Actors.Single(actor => actor.ActorId == "runtime-actor");
            Require(document.Revision == revisionBefore && ReferenceEquals(actorBefore, actorAfter) &&
                    ReferenceEquals(firstKeyframeBefore, document.GetTransformKeyframe("runtime-actor", "runtime-origin")) &&
                    ReferenceEquals(endKeyframeBefore, document.GetTransformKeyframe("runtime-actor", "runtime-end")),
                "timeline 조작이 revision 또는 committed keyframe identity를 변경했습니다.");
            Require(historyEvents == 0 && session.CanUndo && !session.CanRedo &&
                    !undoButton.Disabled && redoButton.Disabled,
                "timeline 조작이 Undo/Redo history를 변경했습니다.");

            GD.Print(
                "TIMELINE_PLAYBACK_READY scrub_midpoint=1 top_world_sync=1 revision_unchanged=1 " +
                "history_unchanged=1 preview_cancel=1 edit_guard=1 play_button=1 space_toggle=1 " +
                "end_clamp=1 stop_restore=1");
        }
        finally
        {
            session.HistoryChanged -= historyHandler;
            session.PreviewChanged -= previewHandler;
        }
    }

    private static void RunTimelineKeyframeCrudIntegration(
        SceneDocument document,
        DocumentSession session,
        TopViewSurface topViewSurface,
        WorldViewProjectionAdapter worldAdapter,
        Node3D actorsRoot,
        TransformTrackSurface transformTrackSurface,
        Label selectedKeyframeLabel,
        SpinBox timeInput,
        SpinBox xInput,
        SpinBox yInput,
        SpinBox zInput,
        SpinBox yawInput,
        Button applyButton,
        Button undoButton,
        Button redoButton,
        Button playPauseButton,
        Button addKeyframeButton,
        Button deleteKeyframeButton,
        HSlider timeSlider,
        Label errorLabel,
        Label timelineStatus)
    {
        const string actorId = "runtime-actor";
        const string originId = "runtime-origin";
        const string endId = "runtime-end";
        const string addedId = "runtime-actor-transform-0001";
        var actorRoot = actorsRoot.GetNodeOrNull<Node3D>("Actor_runtime_actor")
            ?? throw new InvalidOperationException("키프레임 CRUD 통합 검증 실패: runtime actor root가 없습니다.");
        var historyEvents = 0;
        var previewClears = 0;
        EventHandler historyHandler = (_, _) => historyEvents++;
        EventHandler<TransformPreviewChangedEventArgs> previewHandler = (_, eventArgs) =>
        {
            if (eventArgs.Preview is null)
            {
                previewClears++;
            }
        };
        session.HistoryChanged += historyHandler;
        session.PreviewChanged += previewHandler;

        try
        {
            Require(document.Revision == 4 && historyEvents == 0 &&
                    topViewSurface.ApplyCount == 9 && worldAdapter.ApplyCount == 9 &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0) &&
                    session.SelectedTransformKeyframeId == originId,
                "CRUD 검사가 timeline playback의 결정적 최종 상태에서 시작하지 않았습니다.");

            timeSlider.Value = 0.5;
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 4,
                expectedTimeSeconds: 0.5,
                expectedPosition: new Position3(3, 1, -2),
                expectedYawDegrees: 45,
                expectedApplyCount: 10,
                "Add 전 0.5초 scrub");
            Require(!addKeyframeButton.Disabled && deleteKeyframeButton.Disabled,
                "0.5초에서 Add/Delete 버튼 가용성이 올바르지 않습니다.");

            addKeyframeButton.EmitSignal(Button.SignalName.Pressed);
            var added = document.GetTransformKeyframe(actorId, addedId);
            Require(document.Revision == 5 && historyEvents == 1 &&
                    IsNear(added.TimeSeconds, 0.5) && IsPosition(added.Position, 3, 1, -2) &&
                    IsNear(added.YawDegrees, 45) &&
                    session.SelectedTransformKeyframeId == addedId && session.CanUndo && !session.CanRedo,
                "Add button signal이 평가 pose 키프레임과 history를 한 번 확정하지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 5,
                expectedTimeSeconds: 0.5,
                expectedPosition: new Position3(3, 1, -2),
                expectedYawDegrees: 45,
                expectedApplyCount: 11,
                "Add 뒤 committed snapshot");
            RequireTopViewBodyHitAt(
                session,
                topViewSurface,
                expectedPosition: new Position3(3, 1, -2),
                pointerOffset: new ScreenPoint(0, 0),
                expectedKeyframeId: addedId,
                "Add pose TopView hit");
            Require(document.Revision == 5 && historyEvents == 1 &&
                    topViewSurface.ApplyCount == 11 && worldAdapter.ApplyCount == 11,
                "Add pose TopView hit이 projection 또는 history를 변경했습니다.");

            ClickTransformMarker(transformTrackSurface, 0);
            Require(session.SelectedTransformKeyframeId == originId && IsNear(session.Playback.CurrentTimeSeconds, 0),
                "기존 marker 실제 click이 origin을 선택하지 않았습니다.");
            ClickTransformMarker(transformTrackSurface, 0.5);
            Require(session.SelectedTransformKeyframeId == addedId && IsNear(session.Playback.CurrentTimeSeconds, 0.5) &&
                    selectedKeyframeLabel.Text.Contains(addedId, StringComparison.Ordinal) &&
                    selectedKeyframeLabel.Text.Contains("0.5초", StringComparison.Ordinal) &&
                    IsNear(timeInput.Value, 0.5) && IsNear(xInput.Value, 3) && IsNear(yInput.Value, 1) &&
                    IsNear(zInput.Value, -2) && IsNear(yawInput.Value, 45),
                "생성 marker 실제 click이 selection과 Inspector ID/time/pose를 동기화하지 않았습니다.");
            Require(topViewSurface.ApplyCount == 13 && worldAdapter.ApplyCount == 13,
                "두 marker click의 seek가 두 view에 같은 횟수로 반영되지 않았습니다.");

            timeInput.Value = 0.6;
            xInput.Value = 3.5;
            yInput.Value = 1.5;
            zInput.Value = -2.5;
            yawInput.Value = 60;
            var revisionBeforeUpdate = document.Revision;
            applyButton.EmitSignal(Button.SignalName.Pressed);
            var updated = document.GetTransformKeyframe(actorId, addedId);
            Require(revisionBeforeUpdate == 5 && document.Revision == 6 && historyEvents == 2 &&
                    IsNear(updated.TimeSeconds, 0.6) && IsPosition(updated.Position, 3.5, 1.5, -2.5) &&
                    IsNear(updated.YawDegrees, 60) &&
                    session.SelectedTransformKeyframeId == addedId &&
                    IsNear(session.Playback.CurrentTimeSeconds, 0.6) &&
                    selectedKeyframeLabel.Text.Contains("0.6초", StringComparison.Ordinal),
                "Apply signal이 time/pose를 하나의 update revision으로 확정하지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 6,
                expectedTimeSeconds: 0.6,
                expectedPosition: new Position3(3.5, 1.5, -2.5),
                expectedYawDegrees: 60,
                expectedApplyCount: 15,
                "원자적 update 뒤 moved-time snapshot");
            RequireTopViewBodyHitAt(
                session,
                topViewSurface,
                expectedPosition: new Position3(3.5, 1.5, -2.5),
                pointerOffset: new ScreenPoint(8, -8),
                expectedKeyframeId: addedId,
                "Update pose TopView hit");
            Require(document.Revision == 6 && historyEvents == 2 &&
                    topViewSurface.ApplyCount == 15 && worldAdapter.ApplyCount == 15,
                "Update pose TopView hit이 projection 또는 history를 변경했습니다.");

            undoButton.EmitSignal(Button.SignalName.Pressed);
            var restoredBeforeUpdate = document.GetTransformKeyframe(actorId, addedId);
            Require(document.Revision == 7 && historyEvents == 3 && session.CanRedo &&
                    IsNear(restoredBeforeUpdate.TimeSeconds, 0.5) &&
                    IsPosition(restoredBeforeUpdate.Position, 3, 1, -2) &&
                    IsNear(restoredBeforeUpdate.YawDegrees, 45),
                "Undo button이 update preimage를 복원하지 않았습니다.");
            ClickTransformMarker(transformTrackSurface, 0.5);
            Require(!redoButton.Disabled && session.SelectedTransformKeyframeId == addedId &&
                    IsNear(timeInput.Value, 0.5) && IsNear(xInput.Value, 3) && IsNear(yInput.Value, 1) &&
                    IsNear(zInput.Value, -2) && IsNear(yawInput.Value, 45),
                "Undo 뒤 restored marker와 Inspector가 preimage를 표시하지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 7,
                expectedTimeSeconds: 0.5,
                expectedPosition: new Position3(3, 1, -2),
                expectedYawDegrees: 45,
                expectedApplyCount: 17,
                "update Undo 뒤 restored snapshot");

            redoButton.EmitSignal(Button.SignalName.Pressed);
            var redoneUpdate = document.GetTransformKeyframe(actorId, addedId);
            Require(document.Revision == 8 && historyEvents == 4 && !session.CanRedo &&
                    IsNear(redoneUpdate.TimeSeconds, 0.6) &&
                    IsPosition(redoneUpdate.Position, 3.5, 1.5, -2.5) &&
                    IsNear(redoneUpdate.YawDegrees, 60),
                "Redo button이 moved-time update를 재적용하지 않았습니다.");
            ClickTransformMarker(transformTrackSurface, 0.6);
            Require(session.SelectedTransformKeyframeId == addedId &&
                    IsNear(timeInput.Value, 0.6) && IsNear(xInput.Value, 3.5) && IsNear(yInput.Value, 1.5) &&
                    IsNear(zInput.Value, -2.5) && IsNear(yawInput.Value, 60),
                "Redo 뒤 moved marker와 Inspector가 postimage를 표시하지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 8,
                expectedTimeSeconds: 0.6,
                expectedPosition: new Position3(3.5, 1.5, -2.5),
                expectedYawDegrees: 60,
                expectedApplyCount: 19,
                "update Redo 뒤 moved snapshot");

            deleteKeyframeButton.EmitSignal(Button.SignalName.Pressed);
            Require(document.Revision == 9 && historyEvents == 5 &&
                    document.Actors.Single(actor => actor.ActorId == actorId).TransformKeyframes.Count == 2 &&
                    session.SelectedTransformKeyframeId == endId && IsNear(session.Playback.CurrentTimeSeconds, 1),
                "Delete button이 선택 keyframe을 삭제하고 가장 가까운 remaining marker를 선택하지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 9,
                expectedTimeSeconds: 1,
                expectedPosition: new Position3(5, 2, -4),
                expectedYawDegrees: 90,
                expectedApplyCount: 21,
                "Delete 뒤 nearest snapshot");

            undoButton.EmitSignal(Button.SignalName.Pressed);
            Require(document.Revision == 10 && historyEvents == 6 && session.CanRedo &&
                    IsNear(document.GetTransformKeyframe(actorId, addedId).TimeSeconds, 0.6),
                "Delete Undo가 삭제된 keyframe을 복원하지 않았습니다.");
            ClickTransformMarker(transformTrackSurface, 0.6);
            Require(session.SelectedTransformKeyframeId == addedId && !redoButton.Disabled,
                "Delete Undo 뒤 복원 marker를 실제 click으로 선택하지 못했습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 10,
                expectedTimeSeconds: 0.6,
                expectedPosition: new Position3(3.5, 1.5, -2.5),
                expectedYawDegrees: 60,
                expectedApplyCount: 23,
                "Delete Undo 뒤 restored snapshot");

            redoButton.EmitSignal(Button.SignalName.Pressed);
            Require(document.Revision == 11 && historyEvents == 7 && !session.CanRedo &&
                    document.Actors.Single(actor => actor.ActorId == actorId).TransformKeyframes.Count == 2 &&
                    document.Actors.Single(actor => actor.ActorId == actorId).TransformKeyframes.All(frame => frame.Id != addedId),
                "Delete Redo가 복원된 keyframe을 재삭제하지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 11,
                expectedTimeSeconds: 0.6,
                expectedPosition: new Position3(3.4, 1.2, -2.4),
                expectedYawDegrees: 54,
                expectedApplyCount: 24,
                "Delete Redo 뒤 interpolated snapshot");

            ClickTransformMarker(transformTrackSurface, 1);
            var revisionBeforeDuplicate = document.Revision;
            var historyBeforeDuplicate = historyEvents;
            addKeyframeButton.EmitSignal(Button.SignalName.Pressed);
            Require(revisionBeforeDuplicate == 11 && document.Revision == revisionBeforeDuplicate &&
                    historyEvents == historyBeforeDuplicate && addKeyframeButton.Disabled &&
                    timelineStatus.Text.Contains("추가 실패", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("이미", StringComparison.Ordinal),
                "duplicate Add signal이 문서/history 불변과 충돌 문구를 보존하지 않았습니다.");

            timeInput.Value = 1.1;
            applyButton.EmitSignal(Button.SignalName.Pressed);
            Require(IsNear(timeInput.Value, 1.1) && document.Revision == 11 && historyEvents == 7 &&
                    errorLabel.Text.Contains("범위", StringComparison.Ordinal) &&
                    topViewSurface.ApplyCount == 25 && worldAdapter.ApplyCount == 25,
                "범위 밖 TimeInput Apply가 입력을 보존한 채 문서/history/snapshot을 거부하지 않았습니다.");
            timeInput.Value = 1;

            xInput.Value = 4.75;
            var endBeforeExternal = document.GetTransformKeyframe(actorId, endId);
            var externalEnd = new TransformKeyframe(endId, 1, new Position3(4.5, 2.5, -3.5), 75);
            Require(document.UpdateTransformKeyframe(actorId, endBeforeExternal, externalEnd),
                "stale preimage 검증용 외부 변경이 적용되지 않았습니다.");
            Require(document.Revision == 12 && historyEvents == 7 &&
                    topViewSurface.ApplyCount == 26 && worldAdapter.ApplyCount == 26,
                "외부 변경이 revision/projection만 한 번 갱신하지 않았습니다.");
            applyButton.EmitSignal(Button.SignalName.Pressed);
            var committedAfterStaleApply = document.GetTransformKeyframe(actorId, endId);
            Require(document.Revision == 12 && historyEvents == 7 &&
                    IsPosition(committedAfterStaleApply.Position, 4.5, 2.5, -3.5) &&
                    IsNear(committedAfterStaleApply.YawDegrees, 75) &&
                    errorLabel.Text.Contains("오래", StringComparison.Ordinal),
                "stale Inspector Apply가 외부 postimage를 변경하거나 history를 만들었습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 12,
                expectedTimeSeconds: 1,
                expectedPosition: new Position3(4.5, 2.5, -3.5),
                expectedYawDegrees: 75,
                expectedApplyCount: 26,
                "stale conflict 뒤 committed snapshot");

            ClickTransformMarker(transformTrackSurface, 0);
            ClickTransformMarker(transformTrackSurface, 1);
            Require(session.SelectedTransformKeyframeId == endId &&
                    IsNear(xInput.Value, 4.5) && IsNear(yInput.Value, 2.5) &&
                    IsNear(zInput.Value, -3.5) && IsNear(yawInput.Value, 75),
                "외부 postimage marker 재선택이 Inspector를 최신 상태로 동기화하지 않았습니다.");

            xInput.Value = 4;
            Require(IsPosition(actorRoot.Position, 4, 2.5f, -3.5f),
                "scrub cancel 전에 실제 SpinBox preview가 표시되지 않았습니다.");
            var previewClearsBeforeScrub = previewClears;
            timeSlider.Value = 0.8;
            Require(previewClears == previewClearsBeforeScrub + 1 &&
                    document.Revision == 12 && historyEvents == 7 &&
                    session.SelectedTransformKeyframeId is null,
                "활성 Inspector preview 뒤 scrub이 preview/selection을 정확히 취소하지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 12,
                expectedTimeSeconds: 0.8,
                expectedPosition: new Position3(3.8, 2, -2.8),
                expectedYawDegrees: 60,
                expectedApplyCount: 29,
                "preview cancel scrub 뒤 committed interpolation");

            ClickTransformMarker(transformTrackSurface, 1);
            deleteKeyframeButton.EmitSignal(Button.SignalName.Pressed);
            Require(document.Revision == 13 && historyEvents == 8 &&
                    document.Actors.Single(actor => actor.ActorId == actorId).TransformKeyframes.Count == 1 &&
                    session.SelectedTransformKeyframeId == originId && IsNear(session.Playback.CurrentTimeSeconds, 0) &&
                    deleteKeyframeButton.Disabled &&
                    session.DeleteTransformKeyframeLockReason?.Contains("마지막", StringComparison.Ordinal) == true,
                "두 번째 Delete가 마지막 keyframe guard 상태를 만들지 않았습니다.");
            RequireProjectedSnapshot(
                document,
                session,
                topViewSurface,
                worldAdapter,
                actorRoot,
                expectedRevision: 13,
                expectedTimeSeconds: 0,
                expectedPosition: new Position3(1, 0, 0),
                expectedYawDegrees: 0,
                expectedApplyCount: 32,
                "마지막 keyframe guard 전 snapshot");

            deleteKeyframeButton.EmitSignal(Button.SignalName.Pressed);
            Require(document.Revision == 13 && historyEvents == 8 &&
                    document.Actors.Single(actor => actor.ActorId == actorId).TransformKeyframes.Count == 1 &&
                    timelineStatus.Text.Contains("삭제 실패", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("마지막", StringComparison.Ordinal) &&
                    topViewSurface.ApplyCount == 32 && worldAdapter.ApplyCount == 32,
                "마지막 keyframe Delete signal이 문서/history/snapshot 불변을 지키지 않았습니다.");

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(session.Playback.IsPlaying && !session.CanEditSelectedTransform &&
                    !session.CanAddTransformKeyframe && !session.CanDeleteSelectedTransformKeyframe &&
                    xInput.Editable == false && applyButton.Disabled &&
                    addKeyframeButton.Disabled && deleteKeyframeButton.Disabled,
                "재생 시작이 CRUD/Inspector UI를 잠그지 않았습니다.");
            addKeyframeButton.EmitSignal(Button.SignalName.Pressed);
            Require(timelineStatus.Text.Contains("추가 실패", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("재생 중", StringComparison.Ordinal),
                "재생 중 Add signal이 playback lock을 표시하지 않았습니다.");
            deleteKeyframeButton.EmitSignal(Button.SignalName.Pressed);
            Require(timelineStatus.Text.Contains("삭제 실패", StringComparison.Ordinal) &&
                    timelineStatus.Text.Contains("재생 중", StringComparison.Ordinal),
                "재생 중 Delete signal이 playback lock을 표시하지 않았습니다.");

            xInput.Value = 9;
            applyButton.EmitSignal(Button.SignalName.Pressed);
            Require(IsNear(xInput.Value, 1) && errorLabel.Text.Contains("재생 중", StringComparison.Ordinal),
                "재생 중 Inspector signal이 committed 입력과 lock 문구를 보존하지 않았습니다.");
            var mapper = new TopViewCoordinateMapper(
                Math.Max(topViewSurface.Size.X, 1),
                Math.Max(topViewSurface.Size.Y, 1),
                centerX: 0,
                centerZ: 0,
                pixelsPerUnit: 40);
            var originCenter = mapper.WorldToScreen(new Position3(1, 0, 0));
            PushViewportLeftButton(topViewSurface, originCenter, pressed: true);
            PushViewportLeftMotion(
                topViewSurface,
                new ScreenPoint(originCenter.X + 40, originCenter.Y),
                leftButtonPressed: true);
            PushViewportLeftButton(
                topViewSurface,
                new ScreenPoint(originCenter.X + 40, originCenter.Y),
                pressed: false);
            Require(document.Revision == 13 && historyEvents == 8 &&
                    IsPosition(document.GetTransformKeyframe(actorId, originId).Position, 1, 0, 0) &&
                    topViewSurface.ApplyCount == 32 && worldAdapter.ApplyCount == 32 &&
                    IsPosition(actorRoot.Position, 1, 0, 0),
                "재생 중 CRUD/TopView/Inspector 실제 signal이 committed 상태를 변경했습니다.");

            playPauseButton.EmitSignal(Button.SignalName.Pressed);
            Require(!session.Playback.IsPlaying && session.CanEditSelectedTransform &&
                    document.Revision == 13 && historyEvents == 8 &&
                    topViewSurface.ApplyCount == 32 && worldAdapter.ApplyCount == 32,
                "재생 해제가 같은 (revision,time) snapshot을 중복 적용하거나 history를 변경했습니다.");
            RequirePreviewInactive(session);

            GD.Print(
                "TIMELINE_KEYFRAME_CRUD_READY add=1 update=1 time_move=1 delete=1 undo=1 redo=1 " +
                "duplicate_reject=1 range_reject=1 min_keyframe_guard=1 stale_conflict=1 " +
                "selection_sync=1 preview_cancel=1 playback_lock=1");
        }
        finally
        {
            session.HistoryChanged -= historyHandler;
            session.PreviewChanged -= previewHandler;
        }
    }

    private static void RequireProjectedSnapshot(
        SceneDocument document,
        DocumentSession session,
        TopViewSurface topViewSurface,
        WorldViewProjectionAdapter worldAdapter,
        Node3D actorRoot,
        long expectedRevision,
        double expectedTimeSeconds,
        Position3 expectedPosition,
        double expectedYawDegrees,
        int expectedApplyCount,
        string stage)
    {
        var snapshot = document.CreateSnapshot(expectedTimeSeconds);
        var evaluated = snapshot.ActorTransforms["runtime-actor"];
        Require(snapshot.Revision == expectedRevision && document.Revision == expectedRevision &&
                IsNear(session.Playback.CurrentTimeSeconds, expectedTimeSeconds) &&
                IsNear(snapshot.TimeSeconds, expectedTimeSeconds) &&
                IsPosition(evaluated.Position, expectedPosition.X, expectedPosition.Y, expectedPosition.Z) &&
                IsNear(evaluated.YawDegrees, expectedYawDegrees) &&
                topViewSurface.ApplyCount == expectedApplyCount &&
                worldAdapter.ApplyCount == expectedApplyCount &&
                IsPosition(actorRoot.Position, (float)expectedPosition.X, (float)expectedPosition.Y, (float)expectedPosition.Z) &&
                IsNear(actorRoot.Rotation.Y, -expectedYawDegrees * Math.PI / 180),
            $"{stage}: revision/time/pose 또는 TopView/WorldView snapshot 의미가 다릅니다.");
    }

    private static void RequireTopViewBodyHitAt(
        DocumentSession session,
        TopViewSurface surface,
        Position3 expectedPosition,
        ScreenPoint pointerOffset,
        string expectedKeyframeId,
        string stage)
    {
        session.SelectActor(null);
        Require(session.SelectedActorId is null && session.SelectedTransformKeyframeId is null,
            $"{stage}: hit 전에 selection이 해제되지 않았습니다.");
        var mapper = new TopViewCoordinateMapper(
            Math.Max(surface.Size.X, 1),
            Math.Max(surface.Size.Y, 1),
            centerX: 0,
            centerZ: 0,
            pixelsPerUnit: 40);
        var expectedCenter = mapper.WorldToScreen(expectedPosition);
        var pointer = new ScreenPoint(
            expectedCenter.X + pointerOffset.X,
            expectedCenter.Y + pointerOffset.Y);
        PushViewportLeftButton(surface, pointer, pressed: true);
        PushViewportLeftButton(surface, pointer, pressed: false);
        Require(session.SelectedActorId == "runtime-actor" &&
                session.SelectedTransformKeyframeId == expectedKeyframeId,
            $"{stage}: hand-derived pose의 실제 body hit이 actor/keyframe을 재선택하지 않았습니다.");
    }

    private static void ClickTransformMarker(TransformTrackSurface surface, double timeSeconds)
    {
        const double horizontalPadding = 12;
        var width = surface.Size.X;
        Require(float.IsFinite(width) && width > horizontalPadding * 2,
            "marker 실제 click을 위한 transform track 폭이 올바르지 않습니다.");
        var markerPosition = new ScreenPoint(
            horizontalPadding + (timeSeconds * (width - (horizontalPadding * 2))),
            surface.Size.Y / 2);
        PushViewportLeftButton(surface, markerPosition, pressed: true);
        PushViewportLeftButton(surface, markerPosition, pressed: false);
    }

    private static void VerifyMidpointTopViewYaw(Control temporaryParent, SceneSnapshot midpointSnapshot)
    {
        const double surfaceWidth = 640;
        const double surfaceHeight = 360;
        const double pixelsPerUnit = 40;
        const double midpointHandleDiagonalOffset = 19.79898987322333; // 28 * cos/sin(45°)
        var midpointTransform = midpointSnapshot.ActorTransforms["runtime-actor"];
        Require(IsNear(midpointSnapshot.TimeSeconds, 0.5) &&
                midpointTransform.Position == new Position3(3, 1, -2) &&
                IsNear(midpointTransform.YawDegrees, 45),
            "TopView yaw probe에 hand-derived midpoint snapshot이 전달되지 않았습니다.");

        var document = CreateTemporaryDocument(
            "midpoint-yaw-runtime",
            "runtime-actor",
            new Position3(3, 1, -2),
            45);
        var session = new DocumentSession(document);
        session.SelectActor("runtime-actor");
        var surface = new TopViewSurface
        {
            Name = "MidpointYawSurface",
            Visible = false,
            Size = new Vector2((float)surfaceWidth, (float)surfaceHeight),
        };
        temporaryParent.AddChild(surface);
        TransformPreview? observedPreview = null;
        EventHandler<TransformPreviewChangedEventArgs> previewHandler = (_, eventArgs) =>
        {
            if (eventArgs.Preview is not null)
            {
                observedPreview = eventArgs.Preview;
            }
        };
        session.PreviewChanged += previewHandler;
        try
        {
            surface.Initialize(session);
            surface.Apply(midpointSnapshot);
            var midpointCenter = new ScreenPoint(
                (surfaceWidth / 2) + (3 * pixelsPerUnit),
                (surfaceHeight / 2) + (-2 * pixelsPerUnit));
            var expectedMidpointHandle = new ScreenPoint(
                midpointCenter.X + midpointHandleDiagonalOffset,
                midpointCenter.Y + midpointHandleDiagonalOffset);

            SendLeftButton(surface, expectedMidpointHandle, pressed: true);
            SendLeftMotion(
                surface,
                new ScreenPoint(midpointCenter.X, midpointCenter.Y + 28),
                leftButtonPressed: true);

            Require(observedPreview is not null &&
                    observedPreview.Position == new Position3(3, 1, -2) &&
                    IsNear(observedPreview.YawDegrees, 90),
                "TopView가 hand-derived 45도 midpoint 방향 핸들을 실제 입력으로 찾지 못했습니다.");
        }
        finally
        {
            session.PreviewChanged -= previewHandler;
            surface.DetachSession();
            surface.QueueFree();
        }
    }

    private static void RequirePreviewInactive(DocumentSession session)
    {
        var beganProbe = false;
        try
        {
            session.BeginPreview();
            beganProbe = true;
        }
        catch (InvalidOperationException)
        {
            // An already-active preview is the failure this probe detects.
        }
        finally
        {
            session.CancelPreview();
        }

        Require(beganProbe, "runtime self-test 뒤 preview가 활성 상태로 남았습니다.");
    }

    private static void VerifyCollidingActorNodes(Node3D actorsRoot)
    {
        var temporaryRoot = new Node3D { Name = "CollisionActors" };
        actorsRoot.AddChild(temporaryRoot);
        try
        {
            var foreignChild = new Node3D { Name = "ForeignChild" };
            temporaryRoot.AddChild(foreignChild);
            var adapter = new WorldViewProjectionAdapter(temporaryRoot);
            var snapshot = new SceneSnapshot(
                "collision-runtime",
                revision: 0,
                timeSeconds: 0,
                new Dictionary<string, EvaluatedTransform>(StringComparer.Ordinal)
                {
                    ["a_b"] = new EvaluatedTransform(new Position3(0, 0, 0), 0),
                    ["a-b"] = new EvaluatedTransform(new Position3(1, 0, 0), 90),
                });

            adapter.Apply(snapshot);
            var firstNames = temporaryRoot.GetChildren()
                .Select(child => child.Name.ToString())
                .Where(name => name.StartsWith("Actor_", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            adapter.Apply(snapshot);
            var secondNames = temporaryRoot.GetChildren()
                .Select(child => child.Name.ToString())
                .Where(name => name.StartsWith("Actor_", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Require(adapter.ActorCount == 2, "sanitize collision actor 두 개가 모두 생성되지 않았습니다.");
            Require(firstNames.Length == 2 && firstNames.Distinct(StringComparer.Ordinal).Count() == 2,
                "sanitize collision actor node 이름이 실제 Godot tree에서 충돌했습니다.");
            Require(firstNames.SequenceEqual(secondNames, StringComparer.Ordinal),
                "collision node 이름이 같은 snapshot 재적용에서 안정적이지 않습니다.");
            Require(firstNames.Contains("Actor_a_b", StringComparer.Ordinal) &&
                    firstNames.Contains("Actor_a_b__0061_002D_0062", StringComparer.Ordinal),
                "collision node 이름이 exact base와 결정적 suffix 계약을 지키지 않았습니다.");

            adapter.Apply(new SceneSnapshot(
                "collision-runtime",
                revision: 1,
                timeSeconds: 0,
                new Dictionary<string, EvaluatedTransform>(StringComparer.Ordinal)));
            Require(adapter.ActorCount == 0, "empty snapshot 뒤 adapter actor dictionary가 비워지지 않았습니다.");
            Require(ReferenceEquals(foreignChild.GetParent(), temporaryRoot) && !foreignChild.IsQueuedForDeletion(),
                "adapter가 소유하지 않은 foreign child를 제거했습니다.");
        }
        finally
        {
            temporaryRoot.QueueFree();
        }
    }

    private static void VerifyTemporaryRotationCommit(Control temporaryParent)
    {
        var document = CreateTemporaryDocument("rotation-commit-runtime", "rotation-actor");
        var session = new DocumentSession(document);
        var historyEvents = 0;
        session.HistoryChanged += (_, _) => historyEvents++;
        var surface = new TopViewSurface
        {
            Name = "RotationCommitSurface",
            Visible = false,
            Size = new Vector2(400, 400),
        };
        temporaryParent.AddChild(surface);
        try
        {
            surface.Initialize(session);
            surface.Apply(document.CreateSnapshot(0));
            var mapper = new TopViewCoordinateMapper(400, 400, 0, 0, 40);
            var center = mapper.WorldToScreen(new Position3(0, 0, 0));
            SendLeftButton(surface, center, pressed: true);
            SendLeftButton(surface, center, pressed: false);
            var handle = mapper.RotationHandlePosition(center, 0);
            var rotatedPointer = new ScreenPoint(center.X, center.Y + 28);
            SendLeftButton(surface, handle, pressed: true);
            SendLeftMotion(surface, rotatedPointer, leftButtonPressed: true);
            SendLeftButton(surface, rotatedPointer, pressed: false);

            var committed = document.GetTransformKeyframe("rotation-actor", "rotation-actor-origin");
            Require(document.Revision == 1 && historyEvents == 1,
                "임시 TopView 회전 release가 revision/history를 한 번 확정하지 않았습니다.");
            Require(IsNear(committed.YawDegrees, 90) && session.CanUndo && !session.CanRedo,
                "임시 TopView 회전 release가 90도 yaw/history를 저장하지 않았습니다.");
        }
        finally
        {
            surface.DetachSession();
            surface.QueueFree();
        }
    }

    private static void VerifyTemporaryInspectorPaths(Control temporaryParent)
    {
        using (var harness = new TemporaryInspectorHarness(temporaryParent, "enter-commit-runtime", "enter-actor"))
        {
            var historyEvents = 0;
            harness.Session.HistoryChanged += (_, _) => historyEvents++;
            harness.XInput.Value = 3;
            harness.XInput.GetLineEdit().EmitSignal(LineEdit.SignalName.TextSubmitted, "3");

            var committed = harness.Document.GetTransformKeyframe("enter-actor", "enter-actor-origin");
            Require(harness.Document.Revision == 1 && historyEvents == 1,
                "Inspector LineEdit Enter가 revision/history를 한 번 확정하지 않았습니다.");
            Require(committed.Position == new Position3(3, 0, 0) &&
                    harness.Session.CanUndo && !harness.Session.CanRedo,
                "Inspector LineEdit Enter가 입력 변환/history를 저장하지 않았습니다.");
        }

        using (var harness = new TemporaryInspectorHarness(temporaryParent, "stale-runtime", "stale-actor"))
        {
            harness.XInput.Value = 3;
            var original = harness.Document.GetTransformKeyframe("stale-actor", "stale-actor-origin");
            var external = new TransformKeyframe(
                original.Id,
                original.TimeSeconds,
                new Position3(2, 0, 0),
                original.YawDegrees);
            Require(harness.Document.ReplaceTransformKeyframe("stale-actor", original, external),
                "stale Inspector probe의 외부 변경을 만들지 못했습니다.");
            harness.ApplyButton.EmitSignal(Button.SignalName.Pressed);

            Require(harness.Document.Revision == 1 &&
                    harness.Document.GetTransformKeyframe("stale-actor", "stale-actor-origin") == external,
                "stale Inspector commit이 외부 committed 상태를 변경했습니다.");
            Require(!harness.Session.CanUndo && !harness.Session.CanRedo,
                "stale Inspector commit이 history를 만들었습니다.");
            Require(harness.ErrorLabel.Text.Contains("오래", StringComparison.Ordinal),
                "stale Inspector commit이 오래된 변경 메시지를 표시하지 않았습니다.");
        }

        using (var harness = new TemporaryInspectorHarness(temporaryParent, "observer-runtime", "observer-actor"))
        {
            harness.XInput.Value = 4;
            harness.Document.Changed += (_, _) => throw new RuntimeChangedObserverException();
            harness.ApplyButton.EmitSignal(Button.SignalName.Pressed);

            var committed = harness.Document.GetTransformKeyframe("observer-actor", "observer-actor-origin");
            Require(harness.Document.Revision == 1 && committed.Position == new Position3(4, 0, 0),
                "observer 예외 뒤 Inspector 변경이 committed 상태에 저장되지 않았습니다.");
            Require(harness.Session.CanUndo && !harness.Session.CanRedo,
                "observer 예외 뒤 Inspector history transition이 완료되지 않았습니다.");
            Require(harness.ErrorLabel.Text.Contains("저장", StringComparison.Ordinal) &&
                    harness.ErrorLabel.Text.Contains("알림", StringComparison.Ordinal),
                "observer 예외 뒤 Inspector가 저장 완료/알림 실패 메시지를 표시하지 않았습니다.");
        }
    }

    private static SceneDocument CreateTemporaryDocument(
        string documentId,
        string actorId,
        Position3? position = null,
        double yawDegrees = 0) =>
        SceneDocument.Create(
            documentId,
            documentId,
            null,
            10,
            30,
            [
                new ActorTrack(
                    actorId,
                    actorId,
                    "교육용 배우",
                    [new TransformKeyframe($"{actorId}-origin", 0, position ?? new Position3(0, 0, 0), yawDegrees)],
                    [],
                    []),
            ]);

    private sealed class TemporaryInspectorHarness : IDisposable
    {
        private readonly Control _root;
        private readonly TransformInspectorController _controller;

        public TemporaryInspectorHarness(Control parent, string documentId, string actorId)
        {
            Document = CreateTemporaryDocument(documentId, actorId);
            Session = new DocumentSession(Document);
            _root = new Control { Name = $"InspectorHarness_{actorId}", Visible = false };
            parent.AddChild(_root);
            var selectedActorLabel = Add(new Label());
            var selectedKeyframeLabel = Add(new Label());
            ErrorLabel = Add(new Label());
            var timeInput = Add(new SpinBox());
            XInput = Add(new SpinBox());
            var yInput = Add(new SpinBox());
            var zInput = Add(new SpinBox());
            var yawInput = Add(new SpinBox());
            ApplyButton = Add(new Button());
            var undoButton = Add(new Button());
            var redoButton = Add(new Button());
            _controller = new TransformInspectorController(
                Session,
                selectedActorLabel,
                selectedKeyframeLabel,
                ErrorLabel,
                timeInput,
                XInput,
                yInput,
                zInput,
                yawInput,
                ApplyButton,
                undoButton,
                redoButton);
            Session.SelectActor(actorId);
        }

        public SceneDocument Document { get; }

        public DocumentSession Session { get; }

        public Label ErrorLabel { get; }

        public SpinBox XInput { get; }

        public Button ApplyButton { get; }

        public void Dispose()
        {
            _controller.Dispose();
            Session.CancelPreview();
            _root.QueueFree();
        }

        private T Add<T>(T control) where T : Control
        {
            _root.AddChild(control);
            return control;
        }
    }

    private sealed class RuntimeChangedObserverException : InvalidOperationException;

    private static void PushViewportLeftButton(Control surface, ScreenPoint position, bool pressed)
    {
        var viewportPosition = surface.GetGlobalRect().Position +
            new Vector2((float)position.X, (float)position.Y);
        surface.GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = pressed ? MouseButtonMask.Left : (MouseButtonMask)0,
            Pressed = pressed,
            Position = viewportPosition,
        }, inLocalCoords: true);
    }

    private static void PushViewportLeftMotion(Control surface, ScreenPoint position, bool leftButtonPressed)
    {
        var viewportPosition = surface.GetGlobalRect().Position +
            new Vector2((float)position.X, (float)position.Y);
        surface.GetViewport().PushInput(new InputEventMouseMotion
        {
            ButtonMask = leftButtonPressed ? MouseButtonMask.Left : (MouseButtonMask)0,
            Position = viewportPosition,
        }, inLocalCoords: true);
    }

    private static void SendLeftButton(TopViewSurface surface, ScreenPoint position, bool pressed) =>
        surface._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
            Position = new Vector2((float)position.X, (float)position.Y),
        });

    private static void SendLeftMotion(TopViewSurface surface, ScreenPoint position, bool leftButtonPressed) =>
        surface._GuiInput(new InputEventMouseMotion
        {
            ButtonMask = leftButtonPressed ? MouseButtonMask.Left : (MouseButtonMask)0,
            Position = new Vector2((float)position.X, (float)position.Y),
        });

    private static void PushKeyInput(Viewport viewport, Key keycode, bool pressed) =>
        viewport.PushInput(new InputEventKey
        {
            Keycode = keycode,
            Pressed = pressed,
            Echo = false,
        }, inLocalCoords: true);

    private static bool IsPosition(Vector3 actual, float x, float y, float z) =>
        IsNear(actual.X, x) && IsNear(actual.Y, y) && IsNear(actual.Z, z);

    private static bool IsPosition(Position3 actual, double x, double y, double z) =>
        IsNear(actual.X, x) && IsNear(actual.Y, y) && IsNear(actual.Z, z);

    private static bool IsNear(double actual, double expected) => Math.Abs(actual - expected) <= 0.0001;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"기본 편집 통합 검증 실패: {message}");
        }
    }
}
