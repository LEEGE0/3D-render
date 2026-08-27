using System.Text.Json;
using System.Text.Json.Nodes;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using PvpGuide.Infrastructure.Serialization;
using Xunit;

namespace PvpGuide.Infrastructure.Tests;

public sealed class SceneRoundTripTests
{
    private static readonly string CacheTestRoot = Path.GetFullPath(@"D:\3D-render\cache\tests");

    [Fact]
    public void Serialize_round_trips_all_semantic_tracks_and_metadata_at_revision_zero()
    {
        var serializer = new SceneDocumentSerializer();
        var original = CreateDocument();

        var json = serializer.Serialize(original);
        var reopened = serializer.Deserialize(json);

        Assert.Contains("\n  \"schema\": \"pvp-guide-scene/2\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("revision", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currentTime", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, reopened.Revision);
        Assert.Equal(original.DocumentId, reopened.DocumentId);
        Assert.Equal(original.Name, reopened.Name);
        Assert.Equal(original.Note, reopened.Note);
        Assert.Equal(original.DurationSeconds, reopened.DurationSeconds);
        Assert.Equal(original.FramesPerSecond, reopened.FramesPerSecond);
        Assert.Equal(original.ImportMetadata!.SourceFormat, reopened.ImportMetadata!.SourceFormat);
        Assert.Equal(original.ImportMetadata.RawSourcePayload, reopened.ImportMetadata.RawSourcePayload);
        Assert.Equal(original.Actors.Count, reopened.Actors.Count);

        for (var index = 0; index < original.Actors.Count; index++)
        {
            var expected = original.Actors[index];
            var actual = reopened.Actors[index];
            Assert.Equal(expected.ActorId, actual.ActorId);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.Role, actual.Role);
            Assert.Equal(expected.TransformKeyframes.Select(FrameShape), actual.TransformKeyframes.Select(FrameShape));
            Assert.Equal(expected.ActionKeyframes.Select(frame => (frame.Id, frame.TimeSeconds, frame.ActionKey)), actual.ActionKeyframes.Select(frame => (frame.Id, frame.TimeSeconds, frame.ActionKey)));
            Assert.Equal(
                expected.LockOnKeyframes.Select(frame => (frame.Id, frame.TimeSeconds, frame.Enabled, frame.TargetActorId, frame.YawOffsetDegrees, frame.TrackingMode)),
                actual.LockOnKeyframes.Select(frame => (frame.Id, frame.TimeSeconds, frame.Enabled, frame.TargetActorId, frame.YawOffsetDegrees, frame.TrackingMode)));
        }
    }

    [Fact]
    public void Version_one_lock_on_migrates_to_version_two_defaults()
    {
        var document = new SceneDocumentSerializer().Deserialize(VersionOneSceneJson);
        var frame = document.Actors.Single().LockOnKeyframes.Single();

        Assert.Equal(0, frame.YawOffsetDegrees);
        Assert.Equal(LockOnTrackingMode.Continuous, frame.TrackingMode);
    }

    [Fact]
    public void Version_one_rejects_explicit_null_lock_on_semantics()
    {
        var root = JsonNode.Parse(VersionOneSceneJson)!.AsObject();
        var lockOn = root["actors"]![0]!["lockOnKeyframes"]![0]!.AsObject();
        lockOn["yawOffsetDegrees"] = null;
        lockOn["trackingMode"] = null;

        Assert.Throws<InvalidDataException>(() => new SceneDocumentSerializer().Deserialize(root.ToJsonString()));
    }

    [Fact]
    public void Serialize_writes_version_two_lock_on_semantics()
    {
        var json = new SceneDocumentSerializer().Serialize(CreateDocument());

        Assert.Contains("\"schema\": \"pvp-guide-scene/2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"trackingMode\": \"keyframe_only\"", json, StringComparison.Ordinal);
        Assert.Contains("\"yawOffsetDegrees\": -15", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_rejects_wrong_schema_and_unknown_members()
    {
        var serializer = new SceneDocumentSerializer();
        var json = serializer.Serialize(CreateDocument());

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json.Replace(SceneDocument.Schema, "pvp-guide-scene/3", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => serializer.Deserialize(json.Insert(json.IndexOf('{') + 1, "\n  \"unexpected\": true,")));
    }

    [Theory]
    [InlineData("invalid-mode")]
    [InlineData("null-mode")]
    [InlineData("missing-mode")]
    [InlineData("missing-offset")]
    [InlineData("nonfinite-offset")]
    public void Deserialize_rejects_invalid_version_two_lock_on_semantics(string mutation)
    {
        var root = JsonNode.Parse(ValidSceneJson)!.AsObject();
        var lockOn = root["actors"]![0]!["lockOnKeyframes"]![0]!.AsObject();
        string json;
        switch (mutation)
        {
            case "invalid-mode":
                lockOn["trackingMode"] = "future";
                break;
            case "null-mode":
                lockOn["trackingMode"] = null;
                break;
            case "missing-mode":
                lockOn.Remove("trackingMode");
                break;
            case "missing-offset":
                lockOn.Remove("yawOffsetDegrees");
                break;
            case "nonfinite-offset":
                json = ValidSceneJson.Replace("\"yawOffsetDegrees\": 0", "\"yawOffsetDegrees\": 1e999", StringComparison.Ordinal);
                Assert.Throws<InvalidDataException>(() => new SceneDocumentSerializer().Deserialize(json));
                return;
            default:
                throw new InvalidOperationException($"Unknown test mutation '{mutation}'.");
        }

        json = root.ToJsonString();
        Assert.Throws<InvalidDataException>(() => new SceneDocumentSerializer().Deserialize(json));
    }

    [Fact]
    public async Task LoadAsync_rejects_invalid_version_two_json_without_modifying_the_source_file()
    {
        var directory = CreateUniqueTestDirectory();
        try
        {
            var path = Path.Combine(directory, "scene.pvpscene.json");
            var invalidJson = ValidSceneJson.Replace("\"trackingMode\": \"continuous\"", "\"trackingMode\": null", StringComparison.Ordinal);
            var originalBytes = System.Text.Encoding.UTF8.GetBytes(invalidJson);
            await File.WriteAllBytesAsync(path, originalBytes, TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<InvalidDataException>(() => new SceneDocumentSerializer().LoadAsync(path, TestContext.Current.CancellationToken));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            DeleteUniqueTestDirectory(directory);
        }
    }

    [Theory]
    [InlineData("actors-null", "$.actors")]
    [InlineData("actor-null", "$.actors[0]")]
    [InlineData("transforms-null", "$.actors[0].transformKeyframes")]
    [InlineData("transform-null", "$.actors[0].transformKeyframes[0]")]
    [InlineData("position-null", "$.actors[0].transformKeyframes[0].position")]
    [InlineData("actions-null", "$.actors[0].actionKeyframes")]
    [InlineData("action-null", "$.actors[0].actionKeyframes[0]")]
    [InlineData("locks-null", "$.actors[0].lockOnKeyframes")]
    [InlineData("lock-null", "$.actors[0].lockOnKeyframes[0]")]
    [InlineData("metadata-source-null", "$.importMetadata.sourceFormat")]
    [InlineData("metadata-payload-null", "$.importMetadata.rawSourcePayload")]
    public void Deserialize_rejects_null_structural_members_as_invalid_data(string mutation, string expectedPath)
    {
        var root = JsonNode.Parse(ValidSceneJson)!.AsObject();
        var actor = root["actors"]![0]!.AsObject();
        switch (mutation)
        {
            case "actors-null":
                root["actors"] = null;
                break;
            case "actor-null":
                root["actors"]![0] = null;
                break;
            case "transforms-null":
                actor["transformKeyframes"] = null;
                break;
            case "transform-null":
                actor["transformKeyframes"]![0] = null;
                break;
            case "position-null":
                actor["transformKeyframes"]![0]!["position"] = null;
                break;
            case "actions-null":
                actor["actionKeyframes"] = null;
                break;
            case "action-null":
                actor["actionKeyframes"]![0] = null;
                break;
            case "locks-null":
                actor["lockOnKeyframes"] = null;
                break;
            case "lock-null":
                actor["lockOnKeyframes"]![0] = null;
                break;
            case "metadata-source-null":
                root["importMetadata"]!["sourceFormat"] = null;
                break;
            case "metadata-payload-null":
                root["importMetadata"]!["rawSourcePayload"] = null;
                break;
            default:
                throw new InvalidOperationException($"Unknown test mutation '{mutation}'.");
        }

        var exception = Assert.Throws<InvalidDataException>(() => new SceneDocumentSerializer().Deserialize(root.ToJsonString()));

        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_creation_validates_every_track_and_starts_without_mutation_events()
    {
        var notifications = 0;
        var document = CreateDocument();
        document.Changed += (_, _) => notifications++;

        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Throws<ArgumentException>(() => SceneDocument.Create(
            documentId: "bad-target",
            name: "Bad target",
            note: null,
            durationSeconds: 2,
            framesPerSecond: 30,
            actors:
            [
                new ActorTrack(
                    "solo",
                    "Solo",
                    "host",
                    [new TransformKeyframe("transform", 0, new Position3(0, 0, 0), 0)],
                    [],
                    [new LockOnKeyframe("lock", 0, true, "missing")]),
            ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneDocument.Create(
            documentId: "outside",
            name: "Outside",
            note: null,
            durationSeconds: 1,
            framesPerSecond: 30,
            actors:
            [
                new ActorTrack(
                    "solo",
                    "Solo",
                    "host",
                    [new TransformKeyframe("transform", 1.1, new Position3(0, 0, 0), 0)],
                    [],
                    []),
            ]));
    }

    [Fact]
    public void Action_and_lock_tracks_reject_invalid_values_and_copy_input_collections()
    {
        Assert.Throws<ArgumentException>(() => new ActionKeyframe("", 0, "idle"));
        Assert.Throws<ArgumentException>(() => new ActionKeyframe("action", 0, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionKeyframe("action", -0.1, "idle"));
        Assert.Throws<ArgumentException>(() => new LockOnKeyframe("lock", 0, true, null));

        var actions = new List<ActionKeyframe>
        {
            new("late", 1, "attack"),
            new("early", 0, "idle"),
        };
        var locks = new List<LockOnKeyframe>
        {
            new("lock", 0, false, "candidate"),
        };
        var track = new ActorTrack(
            "actor",
            "Actor",
            "host",
            [new TransformKeyframe("transform", 0, new Position3(0, 0, 0), 0)],
            actions,
            locks);
        actions.Clear();
        locks.Clear();

        Assert.Equal([0d, 1d], track.ActionKeyframes.Select(frame => frame.TimeSeconds));
        Assert.Equal("candidate", track.LockOnKeyframes.Single().TargetActorId);
        Assert.Throws<ArgumentException>(() => new ActorTrack(
            "actor",
            "Actor",
            "host",
            [new TransformKeyframe("transform", 0, new Position3(0, 0, 0), 0)],
            [new ActionKeyframe("first", 0.5, "idle"), new ActionKeyframe("duplicate", 0.5, "attack")],
            []));
    }

    [Fact]
    public async Task SaveAtomicAsync_replaces_a_scene_with_validated_utf8_and_removes_temporary_files()
    {
        var directory = CreateUniqueTestDirectory();
        try
        {
            var destination = Path.Combine(directory, "scene.pvpscene.json");
            await File.WriteAllBytesAsync(destination, [0x6f, 0x6c, 0x64], TestContext.Current.CancellationToken);
            var serializer = new SceneDocumentSerializer();

            await serializer.SaveAtomicAsync(CreateDocument(), destination, TestContext.Current.CancellationToken);
            var reopened = await serializer.LoadAsync(destination, TestContext.Current.CancellationToken);

            Assert.Equal("roundtrip-document", reopened.DocumentId);
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), path => !Path.GetFullPath(path).Equals(destination, StringComparison.OrdinalIgnoreCase));
            Assert.Equal((byte)'{', (await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken))[0]);
        }
        finally
        {
            DeleteUniqueTestDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_cancellation_before_move_preserves_existing_bytes_and_leaves_no_temp()
    {
        var directory = CreateUniqueTestDirectory();
        try
        {
            var destination = Path.Combine(directory, "scene.pvpscene.json");
            byte[] originalBytes = [0x01, 0x02, 0x03, 0x04];
            await File.WriteAllBytesAsync(destination, originalBytes, TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SceneDocumentSerializer().SaveAtomicAsync(CreateDocument(), destination, cancellation.Token));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
            Assert.Equal([destination], Directory.EnumerateFiles(directory));
        }
        finally
        {
            DeleteUniqueTestDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_cancellation_after_temp_validation_preserves_destination_and_deletes_temp()
    {
        var directory = CreateUniqueTestDirectory();
        try
        {
            var destination = Path.Combine(directory, "scene.pvpscene.json");
            byte[] originalBytes = [0x21, 0x43, 0x65, 0x87];
            await File.WriteAllBytesAsync(destination, originalBytes, TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var reachedBeforeMove = false;
            var serializer = new SceneDocumentSerializer(() =>
            {
                reachedBeforeMove = true;
                cancellation.Cancel();
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serializer.SaveAtomicAsync(CreateDocument(), destination, cancellation.Token));

            Assert.True(reachedBeforeMove);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
            Assert.Equal([destination], Directory.EnumerateFiles(directory));
        }
        finally
        {
            DeleteUniqueTestDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_move_failure_preserves_the_validated_temp_for_recovery()
    {
        var directory = CreateUniqueTestDirectory();
        try
        {
            var destination = Path.Combine(directory, "scene.pvpscene.json");
            byte[] originalBytes = [0x61, 0x62, 0x63];
            await File.WriteAllBytesAsync(destination, originalBytes, TestContext.Current.CancellationToken);
            await using var lockStream = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);

            var exception = await Record.ExceptionAsync(() => new SceneDocumentSerializer().SaveAtomicAsync(CreateDocument(), destination, TestContext.Current.CancellationToken));

            Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
            var recoveryFile = Assert.Single(Directory.EnumerateFiles(directory), path => !Path.GetFullPath(path).Equals(destination, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("roundtrip-document", (await new SceneDocumentSerializer().LoadAsync(recoveryFile, TestContext.Current.CancellationToken)).DocumentId);
        }
        finally
        {
            DeleteUniqueTestDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_rejects_wrong_extensions_and_missing_parents_before_writing()
    {
        var directory = CreateUniqueTestDirectory();
        try
        {
            var serializer = new SceneDocumentSerializer();
            await Assert.ThrowsAsync<ArgumentException>(() => serializer.SaveAtomicAsync(CreateDocument(), Path.Combine(directory, "scene.json"), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => serializer.SaveAtomicAsync(CreateDocument(), Path.Combine(directory, "missing", "scene.pvpscene.json"), TestContext.Current.CancellationToken));
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), _ => true);
        }
        finally
        {
            DeleteUniqueTestDirectory(directory);
        }
    }

    private static SceneDocument CreateDocument() => SceneDocument.Create(
        documentId: "roundtrip-document",
        name: "Roundtrip document",
        note: "All semantic data must survive.",
        durationSeconds: 2,
        framesPerSecond: 30,
        actors:
        [
            new ActorTrack(
                "host",
                "Host Alpha",
                "host",
                [
                    new TransformKeyframe("t0", 0, new Position3(1, 2, 3), 359),
                    new TransformKeyframe("t1", 2, new Position3(4, 5, 6), 1),
                ],
                [new ActionKeyframe("a0", 0, "idle"), new ActionKeyframe("a1", 1, "attack")],
                [
                    new LockOnKeyframe("l0", 0, false, "invader", -15, LockOnTrackingMode.KeyframeOnly),
                    new LockOnKeyframe("l1", 1, true, "invader", 20, LockOnTrackingMode.Snap),
                ]),
            new ActorTrack(
                "invader",
                "Invader Beta",
                "invader",
                [new TransformKeyframe("t0", 0, new Position3(-1, 0, 2), 180)],
                [new ActionKeyframe("a0", 0, "guard")],
                [new LockOnKeyframe("l0", 0, true, "host", 0, LockOnTrackingMode.Continuous)]),
        ],
        importMetadata: new ImportMetadata("synthetic-format", "{\"unknown\":42}"));

    private const string ValidSceneJson = """
        {
          "schema": "pvp-guide-scene/2",
          "documentId": "structure-test",
          "name": "Structure test",
          "note": null,
          "durationSeconds": 1,
          "framesPerSecond": 30,
          "actors": [
            {
              "actorId": "actor",
              "displayName": "Actor",
              "role": "host",
              "transformKeyframes": [
                {
                  "id": "transform",
                  "timeSeconds": 0,
                  "position": { "x": 0, "y": 0, "z": 0 },
                  "yawDegrees": 0
                }
              ],
              "actionKeyframes": [
                { "id": "action", "timeSeconds": 0, "actionKey": "idle" }
              ],
              "lockOnKeyframes": [
                {
                  "id": "lock",
                  "timeSeconds": 0,
                  "enabled": false,
                  "targetActorId": null,
                  "yawOffsetDegrees": 0,
                  "trackingMode": "continuous"
                }
              ]
            }
          ],
          "importMetadata": {
            "sourceFormat": "synthetic-format",
            "rawSourcePayload": "{}"
          }
        }
        """;

    private const string VersionOneSceneJson = """
        {
          "schema": "pvp-guide-scene/1",
          "documentId": "version-one",
          "name": "Version one",
          "note": null,
          "durationSeconds": 1,
          "framesPerSecond": 30,
          "actors": [
            {
              "actorId": "actor",
              "displayName": "Actor",
              "role": "host",
              "transformKeyframes": [
                {
                  "id": "transform",
                  "timeSeconds": 0,
                  "position": { "x": 0, "y": 0, "z": 0 },
                  "yawDegrees": 0
                }
              ],
              "actionKeyframes": [],
              "lockOnKeyframes": [
                { "id": "lock", "timeSeconds": 0, "enabled": false, "targetActorId": null }
              ]
            }
          ],
          "importMetadata": null
        }
        """;

    private static (string, double, Position3, double) FrameShape(TransformKeyframe frame) =>
        (frame.Id, frame.TimeSeconds, frame.Position, frame.YawDegrees);

    private static string CreateUniqueTestDirectory()
    {
        Directory.CreateDirectory(CacheTestRoot);
        var directory = Path.Combine(CacheTestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        EnsureUniqueTestDirectory(directory);
        return directory;
    }

    private static void DeleteUniqueTestDirectory(string directory)
    {
        EnsureUniqueTestDirectory(directory);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void EnsureUniqueTestDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(CacheTestRoot, fullPath);
        if (relative == "." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relative) || relative.Contains(Path.DirectorySeparatorChar))
        {
            throw new InvalidOperationException($"Unsafe test directory: {fullPath}");
        }

        if (!Guid.TryParseExact(relative, "N", out _))
        {
            throw new InvalidOperationException($"Test directory is not a unique GUID path: {fullPath}");
        }
    }
}
