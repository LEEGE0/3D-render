using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Infrastructure.Serialization;

public sealed class SceneDocumentSerializer
{
    private const string SceneExtension = ".pvpscene.json";
    private const string LegacySchemaV1 = "pvp-guide-scene/1";
    private const string CurrentSchemaV2 = "pvp-guide-scene/2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly Action? _beforeMove;

    public SceneDocumentSerializer()
    {
    }

    internal SceneDocumentSerializer(Action beforeMove)
    {
        ArgumentNullException.ThrowIfNull(beforeMove);
        _beforeMove = beforeMove;
    }

    public string Serialize(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(ToDto(document), JsonOptions);
    }

    public SceneDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var dto = JsonSerializer.Deserialize<SceneDocumentDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Scene JSON cannot be null.");
        ValidateStructure(dto);
        var isVersionOne = string.Equals(dto.Schema, LegacySchemaV1, StringComparison.Ordinal);
        if (!isVersionOne && !string.Equals(dto.Schema, CurrentSchemaV2, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported scene schema '{dto.Schema}'.");
        }

        ValidateLockOnSemantics(dto, requireVersionTwoMembers: !isVersionOne);

        try
        {
            var actors = dto.Actors!.Select(actor => new ActorTrack(
                actor!.ActorId!,
                actor.DisplayName!,
                actor.Role!,
                actor.TransformKeyframes!.Select(frame => new TransformKeyframe(
                    frame!.Id!,
                    frame.TimeSeconds,
                    new Position3(frame.Position!.X, frame.Position.Y, frame.Position.Z),
                    frame.YawDegrees)),
                actor.ActionKeyframes!.Select(frame => new ActionKeyframe(frame!.Id!, frame.TimeSeconds, frame.ActionKey!)),
                actor.LockOnKeyframes!.Select(frame => ToLockOnKeyframe(frame!, isVersionOne))));
            var metadata = dto.ImportMetadata is null
                ? null
                : new ImportMetadata(dto.ImportMetadata.SourceFormat!, dto.ImportMetadata.RawSourcePayload!);
            return SceneDocument.Create(
                dto.DocumentId!,
                dto.Name!,
                dto.Note,
                dto.DurationSeconds,
                dto.FramesPerSecond,
                actors,
                metadata);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Scene JSON contains invalid domain data.", exception);
        }
    }

    public async Task<SceneDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = await File.ReadAllTextAsync(Path.GetFullPath(path), Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
        return Deserialize(json);
    }

    public async Task SaveAtomicAsync(
        SceneDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var destination = Path.GetFullPath(destinationPath);
        if (!destination.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Scene destinations must end with '{SceneExtension}'.", nameof(destinationPath));
        }

        var parent = Path.GetDirectoryName(destination);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException($"Scene destination parent does not exist: {parent}");
        }

        var tempPath = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        var tempCreated = false;
        var moveAttempted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = Utf8WithoutBom.GetBytes(Serialize(document));
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                tempCreated = true;
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _ = Deserialize(await File.ReadAllTextAsync(tempPath, Utf8WithoutBom, cancellationToken).ConfigureAwait(false));
            _beforeMove?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            moveAttempted = true;
            File.Move(tempPath, destination, overwrite: true);
        }
        catch
        {
            if (tempCreated && !moveAttempted)
            {
                TryDelete(tempPath);
            }

            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ValidateStructure(SceneDocumentDto dto)
    {
        RequireNonEmpty(dto.Schema, "$.schema");
        RequireNonEmpty(dto.DocumentId, "$.documentId");
        RequireNonEmpty(dto.Name, "$.name");
        var actors = Require(dto.Actors, "$.actors");
        for (var actorIndex = 0; actorIndex < actors.Length; actorIndex++)
        {
            var actorPath = $"$.actors[{actorIndex}]";
            var actor = Require(actors[actorIndex], actorPath);
            RequireNonEmpty(actor.ActorId, $"{actorPath}.actorId");
            RequireNonEmpty(actor.DisplayName, $"{actorPath}.displayName");
            RequireNonEmpty(actor.Role, $"{actorPath}.role");

            var transforms = Require(actor.TransformKeyframes, $"{actorPath}.transformKeyframes");
            for (var frameIndex = 0; frameIndex < transforms.Length; frameIndex++)
            {
                var framePath = $"{actorPath}.transformKeyframes[{frameIndex}]";
                var frame = Require(transforms[frameIndex], framePath);
                RequireNonEmpty(frame.Id, $"{framePath}.id");
                _ = Require(frame.Position, $"{framePath}.position");
            }

            var actions = Require(actor.ActionKeyframes, $"{actorPath}.actionKeyframes");
            for (var frameIndex = 0; frameIndex < actions.Length; frameIndex++)
            {
                var framePath = $"{actorPath}.actionKeyframes[{frameIndex}]";
                var frame = Require(actions[frameIndex], framePath);
                RequireNonEmpty(frame.Id, $"{framePath}.id");
                RequireNonEmpty(frame.ActionKey, $"{framePath}.actionKey");
            }

            var lockOns = Require(actor.LockOnKeyframes, $"{actorPath}.lockOnKeyframes");
            for (var frameIndex = 0; frameIndex < lockOns.Length; frameIndex++)
            {
                var framePath = $"{actorPath}.lockOnKeyframes[{frameIndex}]";
                var frame = Require(lockOns[frameIndex], framePath);
                RequireNonEmpty(frame.Id, $"{framePath}.id");
            }
        }

        if (dto.ImportMetadata is not null)
        {
            RequireNonEmpty(dto.ImportMetadata.SourceFormat, "$.importMetadata.sourceFormat");
            _ = Require(dto.ImportMetadata.RawSourcePayload, "$.importMetadata.rawSourcePayload");
        }
    }

    private static void ValidateLockOnSemantics(SceneDocumentDto dto, bool requireVersionTwoMembers)
    {
        var actors = dto.Actors!;
        for (var actorIndex = 0; actorIndex < actors.Length; actorIndex++)
        {
            var lockOns = actors[actorIndex]!.LockOnKeyframes!;
            for (var frameIndex = 0; frameIndex < lockOns.Length; frameIndex++)
            {
                var frame = lockOns[frameIndex]!;
                var framePath = $"$.actors[{actorIndex}].lockOnKeyframes[{frameIndex}]";
                if (requireVersionTwoMembers || frame.HasYawOffsetDegrees)
                {
                    _ = RequireFinite(frame.YawOffsetDegrees, $"{framePath}.yawOffsetDegrees");
                }

                if (requireVersionTwoMembers || frame.HasTrackingMode)
                {
                    RequireNonEmpty(frame.TrackingMode, $"{framePath}.trackingMode");
                }

                if (frame.TrackingMode is not null)
                {
                    _ = ParseTrackingMode(frame.TrackingMode);
                }
            }
        }
    }

    private static T Require<T>(T? value, string path)
        where T : class => value ?? throw new InvalidDataException($"Scene JSON member '{path}' cannot be null.");

    private static void RequireNonEmpty(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Scene JSON member '{path}' must be a non-empty string.");
        }
    }

    private static double RequireFinite(double? value, string path)
    {
        if (value is null || !double.IsFinite(value.Value))
        {
            throw new InvalidDataException($"Scene JSON member '{path}' must be a finite number.");
        }

        return value.Value;
    }

    private static LockOnKeyframe ToLockOnKeyframe(LockOnKeyframeDto frame, bool isVersionOne) => new(
        frame.Id!,
        frame.TimeSeconds,
        frame.Enabled,
        frame.TargetActorId,
        frame.YawOffsetDegrees ?? 0,
        frame.TrackingMode is null && isVersionOne
            ? LockOnTrackingMode.Continuous
            : ParseTrackingMode(frame.TrackingMode!));

    private static LockOnTrackingMode ParseTrackingMode(string value) => value switch
    {
        "snap" => LockOnTrackingMode.Snap,
        "continuous" => LockOnTrackingMode.Continuous,
        "keyframe_only" => LockOnTrackingMode.KeyframeOnly,
        _ => throw new InvalidDataException($"Unsupported lock-on tracking mode '{value}'."),
    };

    private static string FormatTrackingMode(LockOnTrackingMode trackingMode) => trackingMode switch
    {
        LockOnTrackingMode.Snap => "snap",
        LockOnTrackingMode.Continuous => "continuous",
        LockOnTrackingMode.KeyframeOnly => "keyframe_only",
        _ => throw new InvalidDataException($"Unsupported lock-on tracking mode '{trackingMode}'."),
    };

    private static SceneDocumentDto ToDto(SceneDocument document) => new()
    {
        Schema = SceneDocument.Schema,
        DocumentId = document.DocumentId,
        Name = document.Name,
        Note = document.Note,
        DurationSeconds = document.DurationSeconds,
        FramesPerSecond = document.FramesPerSecond,
        Actors = document.Actors.Select(actor => new ActorTrackDto
        {
            ActorId = actor.ActorId,
            DisplayName = actor.DisplayName,
            Role = actor.Role,
            TransformKeyframes = actor.TransformKeyframes.Select(frame => new TransformKeyframeDto
            {
                Id = frame.Id,
                TimeSeconds = frame.TimeSeconds,
                Position = new PositionDto { X = frame.Position.X, Y = frame.Position.Y, Z = frame.Position.Z },
                YawDegrees = frame.YawDegrees,
            }).ToArray(),
            ActionKeyframes = actor.ActionKeyframes.Select(frame => new ActionKeyframeDto
            {
                Id = frame.Id,
                TimeSeconds = frame.TimeSeconds,
                ActionKey = frame.ActionKey,
            }).ToArray(),
            LockOnKeyframes = actor.LockOnKeyframes.Select(frame => new LockOnKeyframeDto
            {
                Id = frame.Id,
                TimeSeconds = frame.TimeSeconds,
                Enabled = frame.Enabled,
                TargetActorId = frame.TargetActorId,
                YawOffsetDegrees = frame.YawOffsetDegrees,
                TrackingMode = FormatTrackingMode(frame.TrackingMode),
            }).ToArray(),
        }).ToArray(),
        ImportMetadata = document.ImportMetadata is null
            ? null
            : new ImportMetadataDto
            {
                SourceFormat = document.ImportMetadata.SourceFormat,
                RawSourcePayload = document.ImportMetadata.RawSourcePayload,
            },
    };

    private sealed class SceneDocumentDto
    {
        public required string? Schema { get; init; }
        public required string? DocumentId { get; init; }
        public required string? Name { get; init; }
        public string? Note { get; init; }
        public required double DurationSeconds { get; init; }
        public required int FramesPerSecond { get; init; }
        public required ActorTrackDto?[]? Actors { get; init; }
        public ImportMetadataDto? ImportMetadata { get; init; }
    }

    private sealed class ActorTrackDto
    {
        public required string? ActorId { get; init; }
        public required string? DisplayName { get; init; }
        public required string? Role { get; init; }
        public required TransformKeyframeDto?[]? TransformKeyframes { get; init; }
        public required ActionKeyframeDto?[]? ActionKeyframes { get; init; }
        public required LockOnKeyframeDto?[]? LockOnKeyframes { get; init; }
    }

    private sealed class TransformKeyframeDto
    {
        public required string? Id { get; init; }
        public required double TimeSeconds { get; init; }
        public required PositionDto? Position { get; init; }
        public required double YawDegrees { get; init; }
    }

    private sealed class PositionDto
    {
        public required double X { get; init; }
        public required double Y { get; init; }
        public required double Z { get; init; }
    }

    private sealed class ActionKeyframeDto
    {
        public required string? Id { get; init; }
        public required double TimeSeconds { get; init; }
        public required string? ActionKey { get; init; }
    }

    private sealed class LockOnKeyframeDto
    {
        private double? _yawOffsetDegrees;
        private string? _trackingMode;

        public required string? Id { get; init; }
        public required double TimeSeconds { get; init; }
        public required bool Enabled { get; init; }
        public string? TargetActorId { get; init; }
        public double? YawOffsetDegrees
        {
            get => _yawOffsetDegrees;
            init
            {
                HasYawOffsetDegrees = true;
                _yawOffsetDegrees = value;
            }
        }

        public string? TrackingMode
        {
            get => _trackingMode;
            init
            {
                HasTrackingMode = true;
                _trackingMode = value;
            }
        }

        internal bool HasYawOffsetDegrees { get; private init; }
        internal bool HasTrackingMode { get; private init; }
    }

    private sealed class ImportMetadataDto
    {
        public required string? SourceFormat { get; init; }
        public required string? RawSourcePayload { get; init; }
    }
}
