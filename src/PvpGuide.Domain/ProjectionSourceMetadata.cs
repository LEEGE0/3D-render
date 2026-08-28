namespace PvpGuide.Domain;

public sealed record ProjectionSourceMetadata
{
    public ProjectionSourceMetadata(
        string documentId,
        double durationSeconds,
        int framesPerSecond,
        long revision,
        long motionRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds),
                "Document duration must be finite and non-negative.");
        }

        if (framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framesPerSecond),
                "Frames per second must be positive.");
        }

        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Revision cannot be negative.");
        }

        if (motionRevision < 0 || motionRevision > revision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(motionRevision),
                "Motion revision must be non-negative and cannot exceed revision.");
        }

        DocumentId = documentId;
        DurationSeconds = durationSeconds;
        FramesPerSecond = framesPerSecond;
        Revision = revision;
        MotionRevision = motionRevision;
    }

    public string DocumentId { get; }

    public double DurationSeconds { get; }

    public int FramesPerSecond { get; }

    public long Revision { get; }

    public long MotionRevision { get; }
}
