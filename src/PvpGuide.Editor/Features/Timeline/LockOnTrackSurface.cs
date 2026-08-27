using Godot;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Editor.Features.Timeline;

public partial class LockOnTrackSurface : Control
{
    private const double HorizontalPadding = 12;
    private const double HitRadius = 10;
    private const double TimeMatchToleranceSeconds = 0.000000001;
    private const float MarkerHalfSize = 6;
    private readonly Color _trackColor = new("3c4c61");
    private readonly Color _enabledSegmentColor = new("7a5630");
    private readonly Color _disabledSegmentColor = new("424b56");
    private readonly Color _markerColor = new("d8a35f");
    private readonly Color _selectedMarkerColor = new("ffd166");
    private readonly Color _currentTimeOutlineColor = new("f5f7fa");
    private readonly Color _labelColor = new("f5f7fa");
    private DocumentSession? _session;

    public void Attach(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_session is not null)
        {
            throw new InvalidOperationException("Lock-on 트랙 표면은 한 세션에만 연결할 수 있습니다.");
        }

        _session = session;
        session.SnapshotSource.Changed += OnDocumentChanged;
        session.Playback.Changed += OnPlaybackChanged;
        session.SelectionChanged += OnSelectionChanged;
        session.ActionKeyframeSelectionChanged += OnActionSelectionChanged;
        session.LockOnKeyframeSelectionChanged += OnLockOnSelectionChanged;
        QueueRedraw();
    }

    public void Detach()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        session.SnapshotSource.Changed -= OnDocumentChanged;
        session.Playback.Changed -= OnPlaybackChanged;
        session.SelectionChanged -= OnSelectionChanged;
        session.ActionKeyframeSelectionChanged -= OnActionSelectionChanged;
        session.LockOnKeyframeSelectionChanged -= OnLockOnSelectionChanged;
        _session = null;
    }

    public override void _Draw()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var lane = CreateLane(session);
        var centerY = Size.Y / 2;
        DrawLine(
            new Vector2((float)Math.Min(HorizontalPadding, Math.Max(0, Size.X / 2)), centerY),
            new Vector2(Math.Max((float)Math.Min(HorizontalPadding, Math.Max(0, Size.X / 2)), Size.X - (float)HorizontalPadding), centerY),
            _trackColor,
            2);

        foreach (var segment in lane.Segments)
        {
            var startX = (float)segment.StartX;
            var endX = (float)segment.EndX;
            DrawRect(
                new Rect2(startX, centerY - 8, Math.Max(1, endX - startX), 16),
                segment.Emphasized ? _enabledSegmentColor : _disabledSegmentColor);
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(startX + 4, centerY + 5),
                segment.Label,
                modulate: _labelColor);
        }

        foreach (var marker in lane.Markers)
        {
            var center = new Vector2((float)marker.X, centerY);
            var selected = marker.Id == session.SelectedLockOnKeyframeId;
            DrawColoredPolygon(
            [
                center + new Vector2(0, -MarkerHalfSize),
                center + new Vector2(MarkerHalfSize, 0),
                center + new Vector2(0, MarkerHalfSize),
                center + new Vector2(-MarkerHalfSize, 0),
            ],
            selected ? _selectedMarkerColor : _markerColor);
            if (Math.Abs(marker.TimeSeconds - session.Playback.CurrentTimeSeconds) <= TimeMatchToleranceSeconds)
            {
                DrawArc(center, MarkerHalfSize + 3, 0, Mathf.Tau, 20, _currentTimeOutlineColor, 2);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
            } button || _session is null)
        {
            return;
        }

        var keyframeId = CreateLane(_session).HitTest(button.Position.X, HitRadius);
        if (keyframeId is not null)
        {
            _session.SelectLockOnKeyframe(keyframeId);
        }
        else
        {
            _session.ActivateSemanticTrack(TimelineTrackKind.LockOn);
        }

        AcceptEvent();
    }

    public override void _ExitTree()
    {
        Detach();
        base._ExitTree();
    }

    private StepTrackLane CreateLane(DocumentSession session)
    {
        var width = float.IsFinite(Size.X) && Size.X >= 0 ? Size.X : 0d;
        return StepTrackLayout.Create(
            session.Playback.DurationSeconds,
            width,
            HorizontalPadding,
            session.GetSelectedActorLockOnKeyframes()
                .Select(frame => new StepTrackItem(
                    frame.Id,
                    frame.TimeSeconds,
                    LockOnTrackLabelFormatter.Format(frame),
                    frame.Enabled))
                .ToArray());
    }

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs) => QueueRedraw();

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs) => QueueRedraw();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) => QueueRedraw();

    private void OnActionSelectionChanged(object? sender, ActionKeyframeSelectionChangedEventArgs eventArgs) => QueueRedraw();

    private void OnLockOnSelectionChanged(object? sender, LockOnKeyframeSelectionChangedEventArgs eventArgs) => QueueRedraw();
}

public static class LockOnTrackLabelFormatter
{
    public static string Format(LockOnKeyframe frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var mode = frame.TrackingMode switch
        {
            LockOnTrackingMode.Snap => "SNAP",
            LockOnTrackingMode.Continuous => "CONT",
            LockOnTrackingMode.KeyframeOnly => "KEY",
            _ => frame.TrackingMode.ToString().ToUpperInvariant(),
        };
        var state = frame.Enabled ? "ON" : "OFF";
        var target = frame.TargetActorId ?? "없음";
        return $"{state} · {target} · {mode}";
    }
}
