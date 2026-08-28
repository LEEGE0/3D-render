using System.Collections.ObjectModel;
using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Projection;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Timeline;

namespace PvpGuide.Editor.Features.TopView;

public enum TopViewSemanticDrawLayer
{
    LockLines,
    ActorBodies,
    TargetMarkers,
}

public partial class TopViewSurface : Control, ISceneProjectionConsumer, ITransformPreviewConsumer
{
    private const double PixelsPerUnit = 40;
    private const double MoveThresholdPixels = 3;
    private const float ActorRadiusPixels = 12;

    private static readonly IReadOnlyDictionary<string, SemanticActorOverlay> EmptySemanticOverlays =
        new ReadOnlyDictionary<string, SemanticActorOverlay>(
            new Dictionary<string, SemanticActorOverlay>(StringComparer.Ordinal));

    private static readonly IReadOnlyDictionary<string, ActorDisplayInfo> EmptyDisplayInfos =
        new ReadOnlyDictionary<string, ActorDisplayInfo>(
            new Dictionary<string, ActorDisplayInfo>(StringComparer.Ordinal));

    private readonly Color _gridColor = new("263248");
    private readonly Color _actorColor = new("55aaff");
    private readonly Color _selectedColor = new("ffd166");
    private readonly Color _previewColor = new("7ee787");
    private readonly Color _lockColor = new("ff6b6b");
    private readonly Color _sharedPathColor = new("6ea8fe");
    private readonly Color _freeFacingColor = new("55aaff");
    private readonly Color _lockFacingColor = new("ffd166");
    private DocumentSession? _session;
    private SceneSnapshot? _latestSnapshot;
    private TopViewTrajectoryDisplay? _trajectoryDisplay;
    private IReadOnlyDictionary<string, ActorDisplayInfo> _displayInfos = EmptyDisplayInfos;
    private TransformPreview? _preview;
    private string? _selectedActorId;
    private DragMode _dragMode;
    private ScreenPoint _pressPoint;
    private Position3 _dragStartPosition;
    private double _dragStartYaw;
    private bool _disposed;

    public static IReadOnlyList<TopViewSemanticDrawLayer> SemanticDrawLayerOrder { get; } =
        Array.AsReadOnly<TopViewSemanticDrawLayer>(
        [
            TopViewSemanticDrawLayer.LockLines,
            TopViewSemanticDrawLayer.ActorBodies,
            TopViewSemanticDrawLayer.TargetMarkers,
        ]);

    public int ApplyCount { get; private set; }

    public static IReadOnlyList<TopViewDrawLayer> DrawLayerOrder => TrajectoryOverlayLayout.DrawLayerOrder;

    public MovementTrajectorySet? DisplayedTrajectories => _trajectoryDisplay?.DisplayedTrajectories;

    public TrajectoryOverlayGeometry? DisplayedTrajectoryGeometry => _trajectoryDisplay?.Geometry;

    public IReadOnlyDictionary<string, SemanticActorOverlay> DisplayedSemanticOverlays { get; private set; } =
        EmptySemanticOverlays;

    public void Initialize(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TopViewSurface), "해제된 탑뷰 편집기는 다시 초기화할 수 없습니다.");
        }

        if (_session is not null)
        {
            throw new InvalidOperationException("탑뷰 편집기는 한 번만 초기화할 수 있습니다.");
        }

        _session = session;
        _selectedActorId = session.SelectedActorId;
        session.SelectionChanged += OnSelectionChanged;
        session.EditAvailabilityChanged += OnEditAvailabilityChanged;
        FocusExited += OnFocusExited;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        QueueRedraw();
    }

    public void Apply(SceneProjectionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var snapshot = frame.Snapshot;
        var overlays = SemanticOverlayLayout.CreateScene(snapshot, CreateDisplayedPositions(_preview));
        var trajectoryDisplay = TrajectoryOverlayLayout.CreateDisplay(
            frame,
            _trajectoryDisplay,
            _selectedActorId,
            _preview);
        var displayInfos = CreateDisplayInfos(snapshot);
        _latestSnapshot = snapshot;
        DisplayedSemanticOverlays = overlays;
        _trajectoryDisplay = trajectoryDisplay;
        _displayInfos = displayInfos;
        ApplyCount++;

        if (_selectedActorId is not null && !snapshot.ActorTransforms.ContainsKey(_selectedActorId))
        {
            _session?.SelectActor(null);
        }

        QueueRedraw();
    }

    public void ApplyPreview(TransformPreview? preview)
    {
        var overlays = _latestSnapshot is null
            ? DisplayedSemanticOverlays
            : SemanticOverlayLayout.CreateScene(_latestSnapshot, CreateDisplayedPositions(preview));
        _preview = preview;
        DisplayedSemanticOverlays = overlays;
        if (_trajectoryDisplay is not null)
        {
            _trajectoryDisplay = TrajectoryOverlayLayout.WithPreview(_trajectoryDisplay, preview);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawGrid();
        var display = _trajectoryDisplay;
        if (display is null)
        {
            return;
        }

        var mapper = CreateMapper();
        foreach (var layer in DrawLayerOrder)
        {
            switch (layer)
            {
                case TopViewDrawLayer.SharedPaths:
                    DrawSharedPaths(mapper, display.Presentation);
                    break;
                case TopViewDrawLayer.FreeFacingTicks:
                    DrawFacingTicks(mapper, display.Presentation, lockOn: false);
                    break;
                case TopViewDrawLayer.LockOnFacingTicks:
                    DrawFacingTicks(mapper, display.Presentation, lockOn: true);
                    break;
                case TopViewDrawLayer.LockLines:
                    DrawLockLines(mapper, DisplayedSemanticOverlays);
                    break;
                case TopViewDrawLayer.ActorBodies:
                    DrawActorBodies(mapper, display.ActorBodies);
                    break;
                case TopViewDrawLayer.TargetMarkers:
                    DrawTargetMarkers(mapper, DisplayedSemanticOverlays);
                    break;
                case TopViewDrawLayer.Text:
                    DrawActorText(mapper, display.ActorBodies, DisplayedSemanticOverlays);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported top-view draw layer: {layer}.");
            }
        }
    }

    private void DrawSharedPaths(
        TopViewCoordinateMapper mapper,
        TrajectoryOverlayPresentation presentation)
    {
        foreach (var actor in presentation.Actors.Values)
        {
            for (var index = 1; index < actor.SharedPath.Count; index++)
            {
                var previous = actor.SharedPath[index - 1];
                var current = actor.SharedPath[index];
                var brightness = Math.Min(previous.Brightness, current.Brightness) * actor.SelectionBrightness;
                DrawLine(
                    ToVector2(mapper.WorldToScreen(previous.Position)),
                    ToVector2(mapper.WorldToScreen(current.Position)),
                    WithBrightness(_sharedPathColor, brightness),
                    2,
                    true);
            }
        }
    }

    private void DrawFacingTicks(
        TopViewCoordinateMapper mapper,
        TrajectoryOverlayPresentation presentation,
        bool lockOn)
    {
        foreach (var actor in presentation.Actors.Values)
        {
            var ticks = lockOn ? actor.LockOnFacingTicks : actor.FreeFacingTicks;
            var baseColor = lockOn ? _lockFacingColor : _freeFacingColor;
            var length = lockOn ? 14 : 10;
            var width = lockOn ? 3 : 2;
            foreach (var tick in ticks)
            {
                var centerPoint = mapper.WorldToScreen(tick.Position);
                var center = ToVector2(centerPoint);
                var end = ToVector2(mapper.RotationHandlePosition(centerPoint, tick.YawDegrees, length));
                var color = WithBrightness(baseColor, tick.Brightness * actor.SelectionBrightness);
                DrawLine(center, end, color, width, true);
                DrawTickEndpoint(center, end, tick.EndpointShape, color);

                if (!lockOn && (tick.AnchorMarker & TopViewAnchorMarker.TransformCircle) != 0)
                {
                    DrawArc(center, 5, 0, Mathf.Tau, 16, color, 2, true);
                }

                if (lockOn && (tick.AnchorMarker & TopViewAnchorMarker.LockOnDiamond) != 0)
                {
                    DrawDiamond(center, 4, color);
                }
            }
        }
    }

    private void DrawLockLines(
        TopViewCoordinateMapper mapper,
        IReadOnlyDictionary<string, SemanticActorOverlay> overlays)
    {
        foreach (var overlay in overlays.Values)
        {
            if (overlay.LockLine is not { } lockLine)
            {
                continue;
            }

            var start = ToVector2(mapper.WorldToScreen(lockLine.Start));
            var end = ToVector2(mapper.WorldToScreen(lockLine.End));
            DrawLine(start, end, _lockColor, 2, true);
        }
    }

    private void DrawActorBodies(
        TopViewCoordinateMapper mapper,
        IReadOnlyDictionary<string, TopViewActorBodyLayout> bodies)
    {
        foreach (var (actorId, body) in bodies)
        {
            var centerPoint = mapper.WorldToScreen(body.Position);
            var center = ToVector2(centerPoint);
            var selected = actorId == _selectedActorId;
            var previewing = _preview?.ActorId == actorId;
            var bodyColor = previewing ? _previewColor : selected ? _selectedColor : _actorColor;
            var displayInfo = _displayInfos.TryGetValue(actorId, out var storedDisplayInfo)
                ? storedDisplayInfo
                : new ActorDisplayInfo(actorId, actorId, "알 수 없음");
            var facingEnd = ToVector2(mapper.RotationHandlePosition(centerPoint, body.YawDegrees));

            if (UsesHostileBodyShape(displayInfo.Role))
            {
                DrawColoredPolygon(
                    [
                        center + new Vector2(0, -ActorRadiusPixels),
                        center + new Vector2(ActorRadiusPixels, 0),
                        center + new Vector2(0, ActorRadiusPixels),
                        center + new Vector2(-ActorRadiusPixels, 0),
                    ],
                    bodyColor);
            }
            else
            {
                DrawCircle(center, ActorRadiusPixels, bodyColor);
            }

            DrawLine(center, facingEnd, bodyColor, 3, true);
            if (selected)
            {
                var authoredHandle = ToVector2(mapper.RotationHandlePosition(
                    centerPoint,
                    body.AuthoredYawDegrees));
                DrawCircle(authoredHandle, 5, bodyColor);
            }
        }
    }

    private void DrawActorText(
        TopViewCoordinateMapper mapper,
        IReadOnlyDictionary<string, TopViewActorBodyLayout> bodies,
        IReadOnlyDictionary<string, SemanticActorOverlay> overlays)
    {
        foreach (var (actorId, body) in bodies)
        {
            var center = ToVector2(mapper.WorldToScreen(body.Position));
            var selected = actorId == _selectedActorId;
            var previewing = _preview?.ActorId == actorId;
            var bodyColor = previewing ? _previewColor : selected ? _selectedColor : _actorColor;
            var displayInfo = _displayInfos.TryGetValue(actorId, out var storedDisplayInfo)
                ? storedDisplayInfo
                : new ActorDisplayInfo(actorId, actorId, "알 수 없음");

            DrawString(
                GetThemeDefaultFont(),
                center + new Vector2(17, -10),
                displayInfo.DisplayName,
                HorizontalAlignment.Left,
                -1,
                14,
                bodyColor);
            if (overlays.TryGetValue(actorId, out var overlay) && overlay.ActionLabel is { } actionLabel)
            {
                DrawString(
                    GetThemeDefaultFont(),
                    center + new Vector2(17, 8),
                    actionLabel,
                    HorizontalAlignment.Left,
                    -1,
                    13,
                    bodyColor);
            }

            DrawString(
                GetThemeDefaultFont(),
                center + new Vector2(17, 26),
                $"역할: {displayInfo.Role}",
                HorizontalAlignment.Left,
                -1,
                13,
                bodyColor);

            if (overlay?.LockBadge is { } lockBadge)
            {
                DrawString(
                    GetThemeDefaultFont(),
                    center + new Vector2(17, 44),
                    lockBadge,
                    HorizontalAlignment.Left,
                    -1,
                    12,
                    _lockColor);
            }
        }
    }

    private void DrawTargetMarkers(
        TopViewCoordinateMapper mapper,
        IReadOnlyDictionary<string, SemanticActorOverlay> overlays)
    {
        foreach (var overlay in overlays.Values)
        {
            if (overlay.TargetMarkerPosition is not { } markerPosition)
            {
                continue;
            }

            var marker = ToVector2(mapper.WorldToScreen(markerPosition));
            DrawCircle(marker, 5, _lockColor);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_session is null || _latestSnapshot is null)
        {
            return;
        }

        switch (@event)
        {
            case InputEventKey key when key.Pressed && key.Keycode == Key.Escape:
                CancelDrag();
                AcceptEvent();
                break;
            case InputEventMouseButton button when button.ButtonIndex == MouseButton.Left:
                if (button.Pressed)
                {
                    HandlePress(ToScreenPoint(button.Position));
                    GrabFocus();
                }
                else
                {
                    HandleRelease();
                }

                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragMode != DragMode.None:
                if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
                {
                    CancelDrag();
                }
                else
                {
                    HandleMotion(ToScreenPoint(motion.Position));
                }

                AcceptEvent();
                break;
        }
    }

    public void DetachSession()
    {
        if (_disposed)
        {
            return;
        }

        var session = _session;
        try
        {
            session?.CancelPreview();
        }
        finally
        {
            if (session is not null)
            {
                session.SelectionChanged -= OnSelectionChanged;
                session.EditAvailabilityChanged -= OnEditAvailabilityChanged;
            }

            FocusExited -= OnFocusExited;
            _session = null;
            _latestSnapshot = null;
            _trajectoryDisplay = null;
            _preview = null;
            DisplayedSemanticOverlays = EmptySemanticOverlays;
            _displayInfos = EmptyDisplayInfos;
            _dragMode = DragMode.None;
            _disposed = true;
        }
    }

    private void DrawGrid()
    {
        for (var x = 0f; x <= Size.X; x += (float)PixelsPerUnit)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), _gridColor, 1);
        }

        for (var y = 0f; y <= Size.Y; y += (float)PixelsPerUnit)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), _gridColor, 1);
        }

        DrawLine(new Vector2(Size.X / 2, 0), new Vector2(Size.X / 2, Size.Y), new Color("506784"), 2);
        DrawLine(new Vector2(0, Size.Y / 2), new Vector2(Size.X, Size.Y / 2), new Color("506784"), 2);
    }

    private void HandlePress(ScreenPoint pointer)
    {
        var mapper = CreateMapper();
        var hit = FindHit(mapper, pointer);
        if (hit.ActorId is null)
        {
            CancelDrag();
            _session!.SelectActor(null);
            return;
        }

        try
        {
            _session!.CancelPreview();
            _session!.SelectActor(hit.ActorId);
            if (!_session.CanEditSelectedTransform)
            {
                ResetDrag();
                return;
            }

            var selected = _session.GetSelectedTransform()
                ?? throw new InvalidOperationException("선택한 배우의 변환을 찾을 수 없습니다.");
            _pressPoint = pointer;
            _dragStartPosition = selected.Position;
            _dragStartYaw = selected.YawDegrees;
            _dragMode = hit.Kind == TopViewHitKind.RotationHandle
                ? DragMode.Rotate
                : DragMode.MovePending;
            if (_dragMode == DragMode.Rotate)
            {
                _session.BeginPreview();
            }
        }
        catch (ArgumentException exception)
        {
            ReportInputError($"탑뷰 선택을 적용할 수 없습니다: {exception.Message}");
            ResetDrag();
        }
        catch (InvalidOperationException exception)
        {
            ReportInputError($"탑뷰 편집을 시작할 수 없습니다: {exception.Message}");
            ResetDrag();
        }
    }

    private void HandleMotion(ScreenPoint pointer)
    {
        if (!_session!.CanEditSelectedTransform)
        {
            CancelDrag();
            return;
        }

        var mapper = CreateMapper();
        if (_dragMode == DragMode.MovePending)
        {
            var deltaX = pointer.X - _pressPoint.X;
            var deltaY = pointer.Y - _pressPoint.Y;
            if ((deltaX * deltaX) + (deltaY * deltaY) < MoveThresholdPixels * MoveThresholdPixels)
            {
                return;
            }

            try
            {
                _session!.BeginPreview();
                _dragMode = DragMode.Move;
            }
            catch (InvalidOperationException exception)
            {
                ReportInputError($"탑뷰 이동을 시작할 수 없습니다: {exception.Message}");
                ResetDrag();
                return;
            }
        }

        try
        {
            if (_dragMode == DragMode.Move)
            {
                var pressedWorld = mapper.ScreenToWorld(_pressPoint, _dragStartPosition.Y);
                var pointerWorld = mapper.ScreenToWorld(pointer, _dragStartPosition.Y);
                _session!.UpdatePreview(
                    new Position3(
                        _dragStartPosition.X + pointerWorld.X - pressedWorld.X,
                        _dragStartPosition.Y,
                        _dragStartPosition.Z + pointerWorld.Z - pressedWorld.Z),
                    _dragStartYaw);
            }
            else if (_dragMode == DragMode.Rotate)
            {
                var actorCenter = mapper.WorldToScreen(_dragStartPosition);
                _session!.UpdatePreview(
                    _dragStartPosition,
                    mapper.PointerYawDegrees(pointer, actorCenter));
            }
        }
        catch (ArgumentException exception)
        {
            ReportInputError($"탑뷰 미리보기 값이 올바르지 않습니다: {exception.Message}");
            CancelDrag();
        }
        catch (InvalidOperationException exception)
        {
            ReportInputError($"탑뷰 미리보기를 갱신할 수 없습니다: {exception.Message}");
            CancelDrag();
        }
    }

    private void HandleRelease()
    {
        if (!_session!.CanEditSelectedTransform)
        {
            CancelDrag();
            return;
        }

        try
        {
            if (_dragMode is DragMode.Move or DragMode.Rotate)
            {
                if (!_session!.CommitPreview())
                {
                    ReportInputError("탑뷰 변경을 확정하지 못했거나 실제 변경이 없습니다.");
                }
            }
        }
        finally
        {
            ResetDrag();
        }
    }

    private (string? ActorId, TopViewHitKind Kind) FindHit(TopViewCoordinateMapper mapper, ScreenPoint pointer)
    {
        var snapshot = _latestSnapshot;
        if (snapshot is null)
        {
            return (null, TopViewHitKind.None);
        }

        if (_selectedActorId is not null &&
            snapshot.ActorTransforms.TryGetValue(_selectedActorId, out var selectedTransform))
        {
            var displayed = GetDisplayedTransform(_selectedActorId, selectedTransform);
            var center = mapper.WorldToScreen(displayed.Position);
            var handle = mapper.RotationHandlePosition(center, displayed.YawDegrees);
            if (mapper.IsRotationHandleHit(pointer, handle))
            {
                return (_selectedActorId, TopViewHitKind.RotationHandle);
            }
        }

        foreach (var (actorId, committed) in snapshot.ActorTransforms.Reverse())
        {
            var center = mapper.WorldToScreen(GetDisplayedTransform(actorId, committed).Position);
            if (mapper.IsActorBodyHit(pointer, center))
            {
                return (actorId, TopViewHitKind.ActorBody);
            }
        }

        return (null, TopViewHitKind.None);
    }

    private EvaluatedTransform GetDisplayedTransform(string actorId, EvaluatedTransform committed) =>
        _preview is not null && _preview.ActorId == actorId
            ? new EvaluatedTransform(_preview.Position, _preview.YawDegrees)
            : committed;

    private static IReadOnlyDictionary<string, Position3>? CreateDisplayedPositions(TransformPreview? preview) =>
        preview is null
            ? null
            : new Dictionary<string, Position3>(StringComparer.Ordinal)
            {
                [preview.ActorId] = preview.Position,
            };

    private TopViewCoordinateMapper CreateMapper() => new(
        Math.Max(Size.X, 1),
        Math.Max(Size.Y, 1),
        centerX: 0,
        centerZ: 0,
        PixelsPerUnit);

    private void CancelDrag()
    {
        if (_dragMode is DragMode.Move or DragMode.Rotate)
        {
            _session?.CancelPreview();
        }

        ResetDrag();
    }

    private void ResetDrag() => _dragMode = DragMode.None;

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        _selectedActorId = eventArgs.SelectedActorId;
        if (_trajectoryDisplay is not null)
        {
            _trajectoryDisplay = TrajectoryOverlayLayout.WithSelection(
                _trajectoryDisplay,
                _selectedActorId);
        }

        ResetDrag();
        QueueRedraw();
    }

    private void OnEditAvailabilityChanged(object? sender, EditAvailabilityChangedEventArgs eventArgs)
    {
        if (!eventArgs.CanEditSelectedTransform)
        {
            CancelDrag();
        }

        QueueRedraw();
    }

    private void OnFocusExited() => CancelDrag();

    private IReadOnlyDictionary<string, ActorDisplayInfo> CreateDisplayInfos(SceneSnapshot snapshot)
    {
        var displayInfos = new Dictionary<string, ActorDisplayInfo>(
            snapshot.ActorTransforms.Count,
            StringComparer.Ordinal);
        foreach (var actorId in snapshot.ActorTransforms.Keys)
        {
            try
            {
                displayInfos.Add(
                    actorId,
                    _session?.GetActorDisplayInfo(actorId)
                        ?? new ActorDisplayInfo(actorId, actorId, "알 수 없음"));
            }
            catch (ArgumentException)
            {
                displayInfos.Add(actorId, new ActorDisplayInfo(actorId, actorId, "알 수 없음"));
            }
        }

        return new ReadOnlyDictionary<string, ActorDisplayInfo>(displayInfos);
    }

    public static bool UsesHostileBodyShape(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return role.Contains("enemy", StringComparison.OrdinalIgnoreCase) ||
               role.Contains("invader", StringComparison.OrdinalIgnoreCase) ||
               role.Contains("target", StringComparison.OrdinalIgnoreCase) ||
               role.Contains('적');
    }

    private static ScreenPoint ToScreenPoint(Vector2 position) => new(position.X, position.Y);

    private static Vector2 ToVector2(ScreenPoint point) => new((float)point.X, (float)point.Y);

    private void DrawDiamond(Vector2 center, float radius, Color color)
    {
        var top = center + new Vector2(0, -radius);
        var right = center + new Vector2(radius, 0);
        var bottom = center + new Vector2(0, radius);
        var left = center + new Vector2(-radius, 0);
        DrawLine(top, right, color, 2, true);
        DrawLine(right, bottom, color, 2, true);
        DrawLine(bottom, left, color, 2, true);
        DrawLine(left, top, color, 2, true);
    }

    private void DrawTickEndpoint(
        Vector2 start,
        Vector2 end,
        TopViewTickEndpointShape endpointShape,
        Color color)
    {
        var direction = (end - start).Normalized();
        var perpendicular = new Vector2(-direction.Y, direction.X);
        switch (endpointShape)
        {
            case TopViewTickEndpointShape.FreeArrow:
                var arrowBase = end - (direction * 4);
                DrawLine(end, arrowBase + (perpendicular * 3), color, 2, true);
                DrawLine(end, arrowBase - (perpendicular * 3), color, 2, true);
                break;
            case TopViewTickEndpointShape.LockOnBar:
                DrawLine(end - (perpendicular * 4), end + (perpendicular * 4), color, 3, true);
                break;
            default:
                throw new InvalidOperationException($"Unsupported tick endpoint shape: {endpointShape}.");
        }
    }

    private static Color WithBrightness(Color color, double brightness)
    {
        var factor = (float)Math.Clamp(brightness, 0, 1);
        return new Color(color.R * factor, color.G * factor, color.B * factor, color.A);
    }

    private static void ReportInputError(string message) => GD.PushWarning(message);

    private enum DragMode
    {
        None,
        MovePending,
        Move,
        Rotate,
    }
}
