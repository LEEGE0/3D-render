using System.Collections.ObjectModel;

namespace PvpGuide.Editor.Features.Rendering;

public sealed class RenderJob
{
    private static readonly string RenderRoot = Path.GetFullPath(@"D:\3D-render");
    private readonly ReadOnlyCollection<string> _ffmpegArguments;

    public RenderJob(
        Guid id,
        string documentId,
        long documentRevision,
        string outputDirectory,
        int width,
        int height,
        int framesPerSecond,
        decimal startSeconds,
        decimal endSeconds,
        string ffmpegExecutablePath,
        IEnumerable<string> ffmpegArguments)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Render job IDs cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (documentRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        }

        if (startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Render start must be non-negative.");
        }

        if (endSeconds <= startSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "Render end must be greater than start.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException("Render output must be an absolute path.", nameof(outputDirectory));
        }

        var normalizedOutput = Path.GetFullPath(outputDirectory);
        var relativeOutput = Path.GetRelativePath(RenderRoot, normalizedOutput);
        if (relativeOutput == "."
            || Path.IsPathRooted(relativeOutput)
            || relativeOutput.Equals("..", StringComparison.Ordinal)
            || relativeOutput.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("Render output must be a strict child of D:\\3D-render.", nameof(outputDirectory));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegExecutablePath);
        if (!Path.IsPathFullyQualified(ffmpegExecutablePath)
            || !Path.GetExtension(ffmpegExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("FFmpeg must be an absolute .exe path.", nameof(ffmpegExecutablePath));
        }

        ArgumentNullException.ThrowIfNull(ffmpegArguments);
        var copiedArguments = ffmpegArguments.ToArray();
        if (copiedArguments.Length == 0
            || copiedArguments.Any(argument => string.IsNullOrWhiteSpace(argument) || argument.Contains('"'))
            || !copiedArguments.Contains("-n", StringComparer.Ordinal))
        {
            throw new ArgumentException("FFmpeg arguments must be unquoted argument-list entries and include '-n'.", nameof(ffmpegArguments));
        }

        decimal frameCount;
        try
        {
            frameCount = decimal.Ceiling((endSeconds - startSeconds) * framesPerSecond);
            FrameCount = decimal.ToInt64(frameCount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "The render frame count is too large.");
        }

        Id = id;
        DocumentId = documentId;
        DocumentRevision = documentRevision;
        OutputDirectory = normalizedOutput;
        Width = width;
        Height = height;
        FramesPerSecond = framesPerSecond;
        StartSeconds = startSeconds;
        EndSeconds = endSeconds;
        FfmpegExecutablePath = Path.GetFullPath(ffmpegExecutablePath);
        _ffmpegArguments = Array.AsReadOnly(copiedArguments);
    }

    public Guid Id { get; }
    public string DocumentId { get; }
    public long DocumentRevision { get; }
    public string OutputDirectory { get; }
    public int Width { get; }
    public int Height { get; }
    public int FramesPerSecond { get; }
    public decimal StartSeconds { get; }
    public decimal EndSeconds { get; }
    public long FrameCount { get; }
    public string FramePattern { get; } = "frame_%06d.png";
    public int StartNumber { get; } = 1;
    public string FfmpegExecutablePath { get; }
    public IReadOnlyList<string> FfmpegArguments => _ffmpegArguments;

    public decimal GetTimeSeconds(long frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        return StartSeconds + (frameIndex / (decimal)FramesPerSecond);
    }
}

public sealed class RenderQueue
{
    private readonly object _gate = new();
    private readonly Queue<RenderJob> _jobs = new();
    private readonly HashSet<Guid> _seenIds = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _jobs.Count;
            }
        }
    }

    public void Enqueue(RenderJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            if (!_seenIds.Add(job.Id))
            {
                throw new ArgumentException($"Render job ID '{job.Id}' was already used.", nameof(job));
            }

            _jobs.Enqueue(job);
        }
    }

    public IReadOnlyList<RenderJob> Snapshot()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_jobs.ToArray());
        }
    }

    public bool TryPeek(out RenderJob? job)
    {
        lock (_gate)
        {
            return _jobs.TryPeek(out job);
        }
    }

    public bool TryDequeue(out RenderJob? job)
    {
        lock (_gate)
        {
            return _jobs.TryDequeue(out job);
        }
    }
}
