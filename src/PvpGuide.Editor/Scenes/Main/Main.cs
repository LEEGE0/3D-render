using Godot;
using PvpGuide.Application.Projection;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Inspector;
using PvpGuide.Editor.Features.TopView;
using PvpGuide.Editor.Features.ViewportSync;

namespace PvpGuide.Editor.Scenes.Main;

public partial class Main : Control
{
    private SceneProjectionController? _projectionController;
    private TransformPreviewController? _previewController;
    private TransformInspectorController? _inspectorController;
    private TopViewSurface? _topViewSurface;

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
        var errorLabel = GetNodeOrNull<Label>("InspectorPanel/TransformInspector/ErrorLabel");
        var xInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/XInput");
        var yInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/YInput");
        var zInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/ZInput");
        var yawInput = GetNodeOrNull<SpinBox>("InspectorPanel/TransformInspector/YawInput");
        var applyButton = GetNodeOrNull<Button>("InspectorPanel/TransformInspector/ApplyButton");
        var undoButton = GetNodeOrNull<Button>("InspectorPanel/TransformInspector/UndoButton");
        var redoButton = GetNodeOrNull<Button>("InspectorPanel/TransformInspector/RedoButton");
        if (topViewSurface is null || worldViewportContainer is null || worldViewport is null || worldRoot is null ||
            camera is null || light is null || ground is null || actorsRoot is null ||
            selectedActorLabel is null || errorLabel is null ||
            xInput is null || yInput is null || zInput is null || yawInput is null ||
            applyButton is null || undoButton is null || redoButton is null)
        {
            GD.PushError("기본 편집 UI에 필요한 자식 노드가 없습니다.");
            return;
        }

        GD.Print("PROJECT_RUNTIME_READY");

        var document = new SceneDocument("main-runtime", 1, 30);
        document.AddActor(new ActorTrack(
            "runtime-actor",
            "Runtime Actor",
            "교육용 배우",
            [new TransformKeyframe("runtime-origin", 0, new Position3(0, 0, 0), 0)],
            [],
            []));

        var session = new DocumentSession(document);
        var worldAdapter = new WorldViewProjectionAdapter(actorsRoot);
        topViewSurface.Initialize(session);
        _topViewSurface = topViewSurface;
        _projectionController = new SceneProjectionController(
            session.SnapshotSource,
            topViewSurface,
            worldAdapter);
        _previewController = new TransformPreviewController(
            session,
            topViewSurface,
            worldAdapter);
        _inspectorController = new TransformInspectorController(
            session,
            selectedActorLabel,
            errorLabel,
            xInput,
            yInput,
            zInput,
            yawInput,
            applyButton,
            undoButton,
            redoButton);

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
    }

    public override void _ExitTree()
    {
        _inspectorController?.Dispose();
        _inspectorController = null;
        _topViewSurface?.DetachSession();
        _topViewSurface = null;
        _previewController?.Dispose();
        _previewController = null;
        _projectionController?.Dispose();
        _projectionController = null;
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
            "stale_error_clear=1 inspector_apply_noop=1 collision_nodes=1 final_ui_clean=1");
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
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            adapter.Apply(snapshot);
            var secondNames = temporaryRoot.GetChildren()
                .Select(child => child.Name.ToString())
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
        }
        finally
        {
            temporaryRoot.QueueFree();
        }
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

    private static bool IsPosition(Vector3 actual, float x, float y, float z) =>
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
