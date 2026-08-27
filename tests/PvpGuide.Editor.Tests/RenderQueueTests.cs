using PvpGuide.Editor.Features.Rendering;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class RenderQueueTests
{
    [Fact]
    public void RenderJob_uses_half_open_exact_frame_times_without_accumulation()
    {
        var job = CreateJob(startSeconds: 0.25m, endSeconds: 1.4m, framesPerSecond: 30);

        Assert.Equal(35, job.FrameCount);
        Assert.Equal(0.25m, job.GetTimeSeconds(0));
        Assert.Equal(0.25m + (34m / 30m), job.GetTimeSeconds(34));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.GetTimeSeconds(35));
        Assert.Equal("frame_%06d.png", job.FramePattern);
        Assert.Equal(1, job.StartNumber);
    }

    [Theory]
    [InlineData(@"D:\3D-render")]
    [InlineData(@"relative\frames")]
    [InlineData(@"D:\3D-render\exports\..\..")]
    [InlineData(@"D:\3D-render-other\frames")]
    public void RenderJob_rejects_output_that_is_not_a_strict_child_of_the_render_root(string outputDirectory)
    {
        Assert.Throws<ArgumentException>(() => CreateJob(outputDirectory: outputDirectory));
    }

    [Fact]
    public void RenderJob_requires_an_absolute_exe_and_unquoted_no_overwrite_argument_array()
    {
        Assert.Throws<ArgumentException>(() => CreateJob(ffmpegExecutablePath: @"tools\ffmpeg.exe"));
        Assert.Throws<ArgumentException>(() => CreateJob(ffmpegExecutablePath: @"D:\3D-render\tools\ffmpeg"));
        Assert.Throws<ArgumentException>(() => CreateJob(ffmpegArguments: ["-framerate", "30"]));
        Assert.Throws<ArgumentException>(() => CreateJob(ffmpegArguments: ["-n", "\"quoted path\""]));
    }

    [Fact]
    public void RenderJob_defensively_copies_ffmpeg_arguments_and_validates_numeric_inputs()
    {
        var arguments = new[] { "-n", "-framerate", "30", "frame_%06d.png" };
        var job = CreateJob(ffmpegArguments: arguments);
        arguments[0] = "-y";

        Assert.Equal("-n", job.FfmpegArguments[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)job.FfmpegArguments).Add("-y"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateJob(width: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateJob(height: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateJob(framesPerSecond: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateJob(startSeconds: -0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateJob(startSeconds: 1, endSeconds: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateJob(documentRevision: -1));
    }

    [Fact]
    public void RenderQueue_is_fifo_and_rejects_an_id_even_after_it_was_dequeued()
    {
        var queue = new RenderQueue();
        var first = CreateJob(id: Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = CreateJob(id: Guid.Parse("22222222-2222-2222-2222-222222222222"));

        queue.Enqueue(first);
        queue.Enqueue(second);

        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryPeek(out var peeked));
        Assert.Same(first, peeked);
        Assert.Equal([first, second], queue.Snapshot());
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Same(first, dequeued);
        Assert.Throws<ArgumentException>(() => queue.Enqueue(first));
        Assert.True(queue.TryDequeue(out dequeued));
        Assert.Same(second, dequeued);
        Assert.False(queue.TryDequeue(out _));
        Assert.False(queue.TryPeek(out _));
    }

    [Fact]
    public void RenderQueue_snapshot_is_a_defensive_read_only_copy()
    {
        var queue = new RenderQueue();
        queue.Enqueue(CreateJob());

        var snapshot = queue.Snapshot();
        queue.Enqueue(CreateJob(id: Guid.Parse("33333333-3333-3333-3333-333333333333")));

        Assert.Single(snapshot);
        Assert.Throws<NotSupportedException>(() => ((IList<RenderJob>)snapshot).Clear());
        Assert.Equal(2, queue.Count);
    }

    private static RenderJob CreateJob(
        Guid? id = null,
        long documentRevision = 0,
        string outputDirectory = @"D:\3D-render\exports\tests\job",
        int width = 1920,
        int height = 1080,
        int framesPerSecond = 30,
        decimal startSeconds = 0.25m,
        decimal endSeconds = 1.4m,
        string ffmpegExecutablePath = @"D:\3D-render\tools\ffmpeg\ffmpeg.exe",
        IEnumerable<string>? ffmpegArguments = null) => new(
            id ?? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "document-1",
            documentRevision,
            outputDirectory,
            width,
            height,
            framesPerSecond,
            startSeconds,
            endSeconds,
            ffmpegExecutablePath,
            ffmpegArguments ?? ["-n", "-framerate", "30", "frame_%06d.png"]);
}
