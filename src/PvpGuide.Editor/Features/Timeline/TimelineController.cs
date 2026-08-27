using Godot;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;

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
    private bool _updatingSlider;
    private bool _disposed;

    public TimelineController(
        DocumentSession session,
        Button playPauseButton,
        Button stopButton,
        HSlider timeSlider,
        Label currentTimeLabel,
        Label timelineStatus)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _playback = session.Playback;
        _playPauseButton = playPauseButton ?? throw new ArgumentNullException(nameof(playPauseButton));
        _stopButton = stopButton ?? throw new ArgumentNullException(nameof(stopButton));
        _timeSlider = timeSlider ?? throw new ArgumentNullException(nameof(timeSlider));
        _currentTimeLabel = currentTimeLabel ?? throw new ArgumentNullException(nameof(currentTimeLabel));
        _timelineStatus = timelineStatus ?? throw new ArgumentNullException(nameof(timelineStatus));

        _timeSlider.MinValue = 0;
        _timeSlider.MaxValue = _playback.DurationSeconds;
        _timeSlider.Step = 1d / _playback.FramesPerSecond;

        _playPauseButton.Pressed += OnPlayPausePressed;
        _stopButton.Pressed += OnStopPressed;
        _timeSlider.ValueChanged += OnTimeSliderValueChanged;
        _playback.Changed += OnPlaybackChanged;
        _session.EditAvailabilityChanged += OnEditAvailabilityChanged;

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
        _playback.Changed -= OnPlaybackChanged;
        _session.EditAvailabilityChanged -= OnEditAvailabilityChanged;
        _disposed = true;
    }

    private void OnPlayPausePressed() => TogglePlayback();

    private void OnStopPressed() => _playback.Stop();

    private void OnTimeSliderValueChanged(double value)
    {
        if (_updatingSlider)
        {
            return;
        }

        _playback.Pause();
        _playback.Seek(value);
    }

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs) =>
        RefreshPlaybackPresentation();

    private void OnEditAvailabilityChanged(object? sender, EditAvailabilityChangedEventArgs eventArgs) =>
        RefreshEditAvailabilityPresentation();

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
        _timelineStatus.Text = _session.CanEditSelectedTransform
            ? "현재 시각에서 변환 편집 가능"
            : _session.EditLockReason ?? "현재 시각에서는 변환을 편집할 수 없습니다";
    }
}
