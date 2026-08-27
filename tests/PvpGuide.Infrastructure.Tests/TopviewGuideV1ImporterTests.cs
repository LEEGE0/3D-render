using System.Reflection;
using System.Text.Json.Nodes;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;
using PvpGuide.Infrastructure.Import;
using Xunit;

namespace PvpGuide.Infrastructure.Tests;

public sealed class TopviewGuideV1ImporterTests
{
    private static readonly TopviewGuideV1ImportOptions Options = new(
        OriginX: 100,
        OriginY: 200,
        Scale: 0.1,
        GroundHeight: 0,
        FramesPerSecond: 30);

    [Fact]
    public void Import_converts_the_synthetic_first_frame_and_preserves_actor_meaning()
    {
        var result = new TopviewGuideV1Importer().Import(ReadFixture(), Options);

        Assert.Equal("synthetic-four-role-drill", result.Document.DocumentId);
        Assert.Equal("Synthetic Four Role Drill", result.Document.Name);
        Assert.Equal("Copyright-free importer fixture with invented coordinates.", result.Document.Note);
        Assert.Equal(1.4, result.Document.DurationSeconds);
        Assert.Equal(30, result.Document.FramesPerSecond);
        Assert.Equal(0, result.Document.Revision);
        Assert.Equal(1, result.CurrentIndex);
        Assert.Equal(["host", "invader", "phantom1", "phantom2"], result.Document.Actors.Select(actor => actor.Role));

        var expectedPositions = new Dictionary<string, Position3>(StringComparer.Ordinal)
        {
            ["host"] = new(0, 0, 0),
            ["invader"] = new(1, 0, 0),
            ["phantom1"] = new(-2, 0, 2),
            ["phantom2"] = new(3, 0, 2),
        };
        var expectedYaw = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["host"] = 359,
            ["invader"] = 1,
            ["phantom1"] = 180,
            ["phantom2"] = 270,
        };

        foreach (var actor in result.Document.Actors)
        {
            Assert.Equal([0.25, 0.9, 1.4], actor.TransformKeyframes.Select(frame => frame.TimeSeconds));
            Assert.Equal(["10", "20", "30"], actor.TransformKeyframes.Select(frame => frame.Id));
            Assert.Equal(expectedPositions[actor.ActorId], actor.TransformKeyframes[0].Position);
            Assert.Equal(expectedYaw[actor.ActorId], actor.TransformKeyframes[0].YawDegrees);
            Assert.Equal(3, actor.ActionKeyframes.Count);
            Assert.Equal(3, actor.LockOnKeyframes.Count);
        }

        var phantom1 = result.Document.Actors.Single(actor => actor.ActorId == "phantom1");
        Assert.Equal("Phantom Gamma", phantom1.DisplayName);
        Assert.False(phantom1.LockOnKeyframes[0].Enabled);
        Assert.Equal("invader", phantom1.LockOnKeyframes[0].TargetActorId);
        Assert.All(
            result.Document.Actors.SelectMany(actor => actor.LockOnKeyframes),
            frame =>
            {
                Assert.Equal(0, frame.YawOffsetDegrees);
                Assert.Equal(LockOnTrackingMode.Continuous, frame.TrackingMode);
            });

        var phantom2 = result.Document.Actors.Single(actor => actor.ActorId == "phantom2");
        Assert.Equal("attack", phantom2.ActionKeyframes.Single(frame => frame.TimeSeconds == 0.9).ActionKey);
        Assert.Equal("idle", phantom2.ActionKeyframes.Single(frame => frame.TimeSeconds == 1.4).ActionKey);
    }

    [Fact]
    public void Import_preserves_raw_extensions_and_reports_unsupported_sections_without_polluting_document_state()
    {
        var source = ReadFixture();

        var result = new TopviewGuideV1Importer().Import(source, Options);

        Assert.NotNull(result.Document.ImportMetadata);
        Assert.Equal("gangqueen-topview-guide-v1", result.Document.ImportMetadata.SourceFormat);
        Assert.Equal(source, result.Document.ImportMetadata.RawSourcePayload);
        Assert.Contains(result.Warnings, warning => warning.Contains("coordinate_system", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("backstab_rules", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("evaluations", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("synthetic_extension", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_reports_unknown_members_at_each_typed_object_with_exact_json_paths()
    {
        var root = JsonNode.Parse(ReadFixture())!.AsObject();
        root["coordinate_system"]!["future_axis"] = "diagonal";
        root["scene"]!["future_scene"] = 17;
        root["scene"]!["keyframes"]![0]!["future_frame"] = true;
        root["scene"]!["keyframes"]![0]!["actors"]![0]!["future_actor"] = "preserved";
        var source = root.ToJsonString();

        var result = new TopviewGuideV1Importer().Import(source, Options);

        Assert.Equal(source, result.Document.ImportMetadata!.RawSourcePayload);
        Assert.Contains(result.Warnings, warning => warning.Contains("$.coordinate_system.future_axis", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("$.scene.future_scene", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("$.scene.keyframes[0].future_frame", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("$.scene.keyframes[0].actors[0].future_actor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("frames-null", "$.scene.keyframes")]
    [InlineData("frame-null", "$.scene.keyframes[0]")]
    [InlineData("frame-scalar", "$.scene.keyframes[0]")]
    [InlineData("actors-null", "$.scene.keyframes[0].actors")]
    [InlineData("actor-null", "$.scene.keyframes[0].actors[0]")]
    [InlineData("actor-scalar", "$.scene.keyframes[0].actors[0]")]
    public void Import_rejects_null_or_non_object_frame_and_actor_elements(string mutation, string expectedPath)
    {
        var root = JsonNode.Parse(ReadFixture())!.AsObject();
        var frames = root["scene"]!["keyframes"]!.AsArray();
        switch (mutation)
        {
            case "frames-null":
                root["scene"]!["keyframes"] = null;
                break;
            case "frame-null":
                frames[0] = null;
                break;
            case "frame-scalar":
                frames[0] = 42;
                break;
            case "actors-null":
                frames[0]!["actors"] = null;
                break;
            case "actor-null":
                frames[0]!["actors"]![0] = null;
                break;
            case "actor-scalar":
                frames[0]!["actors"]![0] = "not-an-object";
                break;
            default:
                throw new InvalidOperationException($"Unknown test mutation '{mutation}'.");
        }

        var exception = Assert.Throws<InvalidDataException>(() => new TopviewGuideV1Importer().Import(root.ToJsonString(), Options));

        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_rejects_a_mismatched_format()
    {
        var root = JsonNode.Parse(ReadFixture())!.AsObject();
        root["format"] = "gangqueen-topview-guide-v2";

        Assert.Throws<InvalidDataException>(() => new TopviewGuideV1Importer().Import(root.ToJsonString(), Options));
    }

    [Fact]
    public void Import_rejects_an_invalid_coordinate_declaration()
    {
        var root = JsonNode.Parse(ReadFixture())!.AsObject();
        root["coordinate_system"]!["y_axis"] = "up";

        Assert.Throws<InvalidDataException>(() => new TopviewGuideV1Importer().Import(root.ToJsonString(), Options));
    }

    [Fact]
    public void Import_rejects_duplicate_actor_ids_within_a_frame()
    {
        var root = JsonNode.Parse(ReadFixture())!.AsObject();
        var actors = root["scene"]!["keyframes"]![0]!["actors"]!.AsArray();
        actors.Add(actors[0]!.DeepClone());

        Assert.Throws<InvalidDataException>(() => new TopviewGuideV1Importer().Import(root.ToJsonString(), Options));
    }

    [Fact]
    public void Import_rejects_duplicate_keyframe_times()
    {
        var root = JsonNode.Parse(ReadFixture())!.AsObject();
        root["scene"]!["keyframes"]![1]!["time"] = 0.25;

        Assert.Throws<InvalidDataException>(() => new TopviewGuideV1Importer().Import(root.ToJsonString(), Options));
    }

    private static string ReadFixture()
    {
        const string suffix = "Fixtures.synthetic-topview-v1.scene.json";
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded fixture '{resourceName}' is unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
