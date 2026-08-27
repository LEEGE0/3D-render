namespace PvpGuide.Application.Sessions;

public sealed class TimelineEditAvailabilityChangedEventArgs(
    TimelineTrackEditAvailability actionEditAvailability,
    TimelineTrackEditAvailability lockOnEditAvailability) : EventArgs
{
    public TimelineTrackEditAvailability ActionEditAvailability { get; } =
        actionEditAvailability ?? throw new ArgumentNullException(nameof(actionEditAvailability));

    public TimelineTrackEditAvailability LockOnEditAvailability { get; } =
        lockOnEditAvailability ?? throw new ArgumentNullException(nameof(lockOnEditAvailability));
}
