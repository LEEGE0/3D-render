using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;

namespace PvpGuide.Editor.Features.Timeline;

public sealed class TimelineController : IDisposable
{
    private readonly DocumentSession _session;
    private readonly PlaybackClock _playback;
    private readonly Button _playPauseButton;
    private readonly Button _stopButton;
    private readonly HSlider _timeSlider;
    private readonly Label _currentTimeLabel;
    private readonly Label _timelineStatus;
    private readonly TransformTrackSurface _transformTrackSurface;
    private readonly Button _addKeyframeButton;
    private readonly Button _deleteKeyframeButton;
    private bool _updatingSlider;
    private bool _disposed;

    public TimelineController(
        DocumentSession session,
        Button playPauseButton,
        Button stopButton,
        HSlider timeSlider,
        Label currentTimeLabel,
        Label timelineStatus,
        TransformTrackSurface transformTrackSurface,
        Button addKeyframeButton,
        Button deleteKeyframeButton)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _playback = session.Playback;
        _playPauseButton = playPauseButton ?? throw new ArgumentNullException(nameof(playPauseButton));
        _stopButton = stopButton ?? throw new ArgumentNullException(nameof(stopButton));
        _timeSlider = timeSlider ?? throw new ArgumentNullException(nameof(timeSlider));
        _currentTimeLabel = currentTimeLabel ?? throw new ArgumentNullException(nameof(currentTimeLabel));
        _timelineStatus = timelineStatus ?? throw new ArgumentNullException(nameof(timelineStatus));
        _transformTrackSurface = transformTrackSurface ?? throw new ArgumentNullException(nameof(transformTrackSurface));
        _addKeyframeButton = addKeyframeButton ?? throw new ArgumentNullException(nameof(addKeyframeButton));
        _deleteKeyframeButton = deleteKeyframeButton ?? throw new ArgumentNullException(nameof(deleteKeyframeButton));

        _timeSlider.MinValue = 0;
        _timeSlider.MaxValue = _playback.DurationSeconds;
        _timeSlider.Step = 1d / _playback.FramesPerSecond;

        _playPauseButton.Pressed += OnPlayPausePressed;
        _stopButton.Pressed += OnStopPressed;
        _timeSlider.ValueChanged += OnTimeSliderValueChanged;
        _addKeyframeButton.Pressed += OnAddKeyframePressed;
        _deleteKeyframeButton.Pressed += OnDeleteKeyframePressed;
        _playback.Changed += OnPlaybackChanged;
        _session.SelectionChanged += OnSelectionChanged;
        _session.TransformKeyframeSelectionChanged += OnTransformKeyframeSelectionChanged;
        _session.EditAvailabilityChanged += OnEditAvailabilityChanged;
        _session.SnapshotSource.Changed += OnDocumentChanged;

        RefreshPlaybackPresentation();
        RefreshEditAvailabilityPresentation();
    }

    public void TogglePlayback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playback.Toggle();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _playPauseButton.Pressed -= OnPlayPausePressed;
        _stopButton.Pressed -= OnStopPressed;
        _timeSlider.ValueChanged -= OnTimeSliderValueChanged;
        _addKeyframeButton.Pressed -= OnAddKeyframePressed;
        _deleteKeyframeButton.Pressed -= OnDeleteKeyframePressed;
        _playback.Changed -= OnPlaybackChanged;
        _session.SelectionChanged -= OnSelectionChanged;
        _session.TransformKeyframeSelectionChanged -= OnTransformKeyframeSelectionChanged;
        _session.EditAvailabilityChanged -= OnEditAvailabilityChanged;
        _session.SnapshotSource.Changed -= OnDocumentChanged;
        _disposed = true;
    }

    private void OnPlayPausePressed() => TogglePlayback();

    private void OnStopPressed() => _playback.Stop();

    private void OnAddKeyframePressed() => HandleKeyframeEdit(
        _session.AddTransformKeyframeAtCurrentTime(),
        "추가",
        () => _session.AddTransformKeyframeLockReason);

    private void OnDeleteKeyframePressed() => HandleKeyframeEdit(
        _session.RemoveSelectedTransformKeyframe(),
        "삭제",
        () => _session.DeleteTransformKeyframeLockReason);

    private void OnTimeSliderValueChanged(double value)
    {
        if (_updatingSlider)
        {
            return;
        }

        _playback.Pause();
        _playback.Seek(value);
    }

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs)
    {
        RefreshPlaybackPresentation();
        RefreshEditAvailabilityPresentation();
        _transformTrackSurface.QueueRedraw();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) => RefreshKeyframePresentation();

    private void OnTransformKeyframeSelectionChanged(object? sender, TransformKeyframeSelectionChangedEventArgs eventArgs) =>
        RefreshKeyframePresentation();

    private void OnEditAvailabilityChanged(object? sender, EditAvailabilityChangedEventArgs eventArgs) =>
        RefreshKeyframePresentation();

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs) => RefreshKeyframePresentation();

    private void RefreshPlaybackPresentation()
    {
        _updatingSlider = true;
        try
        {
            _timeSlider.Value = _playback.CurrentTimeSeconds;
        }
        finally
        {
            _updatingSlider = false;
        }

        _playPauseButton.Text = _playback.IsPlaying ? "일시정지" : "재생";
        _currentTimeLabel.Text = TimelineTimeFormatter.Format(
            _playback.CurrentTimeSeconds,
            _playback.DurationSeconds,
            _playback.FramesPerSecond);
    }

    private void RefreshEditAvailabilityPresentation()
    {
        _addKeyframeButton.Disabled = !_session.CanAddTransformKeyframe;
        _deleteKeyframeButton.Disabled = !_session.CanDeleteSelectedTransformKeyframe;
        _timelineStatus.Text = string.Join(
            " · ",
            FormatActionAvailability("추가", _session.CanAddTransformKeyframe, _session.AddTransformKeyframeLockReason),
            FormatActionAvailability("삭제", _session.CanDeleteSelectedTransformKeyframe, _session.DeleteTransformKeyframeLockReason),
            _session.CanEditSelectedTransform
                ? "변환 편집 가능"
                : $"변환 편집 불가: {_session.EditLockReason ?? "선택한 키프레임 시각에서만 편집할 수 있습니다"}");
    }

    private void HandleKeyframeEdit(SceneEditResult result, string actionName, Func<string?> getActionLockReason)
    {
        RefreshKeyframePresentation();
        if (result == SceneEditResult.Conflict)
        {
            _timelineStatus.Text = $"키프레임 {actionName} 실패: " +
                (getActionLockReason() ?? "선택한 키프레임 변경이 최신 문서 상태와 충돌했습니다.");
        }
    }

    private static string FormatActionAvailability(string actionName, bool available, string? lockReason) =>
        available
            ? $"{actionName} 가능"
            : $"{actionName} 불가: {lockReason ?? "현재 상태에서는 실행할 수 없습니다"}";

    private void RefreshKeyframePresentation()
    {
        RefreshEditAvailabilityPresentation();
        _transformTrackSurface.QueueRedraw();
    }
}
