using Godot;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;

namespace PvpGuide.Editor.Features.Timeline;

public partial class TransformTrackSurface : Control
{
    private const double HorizontalPadding = 12;
    private const double HitRadius = 10;
    private const float MarkerHalfSize = 6;
    private readonly Color _trackColor = new("4f6178");
    private readonly Color _markerColor = new("7ea7d8");
    private readonly Color _selectedMarkerColor = new("ffd166");
    private DocumentSession? _session;

    public void Attach(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_session is not null)
        {
            throw new InvalidOperationException("변환 트랙 표면은 한 세션에만 연결할 수 있습니다.");
        }

        _session = session;
        session.SelectionChanged += OnSelectionChanged;
        session.TransformKeyframeSelectionChanged += OnTransformKeyframeSelectionChanged;
        session.EditAvailabilityChanged += OnEditAvailabilityChanged;
        session.Playback.Changed += OnPlaybackChanged;
        session.SnapshotSource.Changed += OnDocumentChanged;
        QueueRedraw();
    }

    public void Detach()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        session.SelectionChanged -= OnSelectionChanged;
        session.TransformKeyframeSelectionChanged -= OnTransformKeyframeSelectionChanged;
        session.EditAvailabilityChanged -= OnEditAvailabilityChanged;
        session.Playback.Changed -= OnPlaybackChanged;
        session.SnapshotSource.Changed -= OnDocumentChanged;
        _session = null;
    }

    public override void _Draw()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var centerY = Size.Y / 2;
        DrawLine(
            new Vector2((float)HorizontalPadding, centerY),
            new Vector2(Math.Max((float)HorizontalPadding, Size.X - (float)HorizontalPadding), centerY),
            _trackColor,
            2);

        foreach (var marker in CreateMarkers(session))
        {
            var center = new Vector2((float)marker.X, centerY);
            var color = marker.Id == session.SelectedTransformKeyframeId
                ? _selectedMarkerColor
                : _markerColor;
            DrawColoredPolygon(
            [
                center + new Vector2(0, -MarkerHalfSize),
                center + new Vector2(MarkerHalfSize, 0),
                center + new Vector2(0, MarkerHalfSize),
                center + new Vector2(-MarkerHalfSize, 0),
            ],
            color);
            if (marker.Id == session.SelectedTransformKeyframeId)
            {
                DrawArc(center, MarkerHalfSize + 3, 0, Mathf.Tau, 20, _selectedMarkerColor, 2);
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

        var keyframeId = TransformTrackLayout.HitTest(CreateMarkers(_session), button.Position.X, HitRadius);
        if (keyframeId is not null)
        {
            _session.SelectTransformKeyframe(keyframeId);
        }

        AcceptEvent();
    }

    public override void _ExitTree()
    {
        Detach();
        base._ExitTree();
    }

    private IReadOnlyList<TransformTrackMarker> CreateMarkers(DocumentSession session)
    {
        var width = float.IsFinite(Size.X) && Size.X > 0 ? Size.X : 1d;
        var padding = Math.Min(HorizontalPadding, width / 2d);
        return TransformTrackLayout.CreateMarkers(
            session.Playback.DurationSeconds,
            width,
            padding,
            session.GetSelectedActorTransformKeyframes().Select(frame => (frame.Id, frame.TimeSeconds)));
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) => QueueRedraw();

    private void OnTransformKeyframeSelectionChanged(object? sender, TransformKeyframeSelectionChangedEventArgs eventArgs) =>
        QueueRedraw();

    private void OnEditAvailabilityChanged(object? sender, EditAvailabilityChangedEventArgs eventArgs) => QueueRedraw();

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs) => QueueRedraw();

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs) => QueueRedraw();
}
