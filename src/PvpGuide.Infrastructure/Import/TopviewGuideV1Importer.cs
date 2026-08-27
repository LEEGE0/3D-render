using System.Text.Json;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Infrastructure.Import;

public sealed class TopviewGuideV1ImportOptions
{
    public TopviewGuideV1ImportOptions(
        double OriginX,
        double OriginY,
        double Scale,
        double GroundHeight,
        int FramesPerSecond)
    {
        if (!double.IsFinite(OriginX))
        {
            throw new ArgumentOutOfRangeException(nameof(OriginX));
        }

        if (!double.IsFinite(OriginY))
        {
            throw new ArgumentOutOfRangeException(nameof(OriginY));
        }

        if (!double.IsFinite(Scale) || Scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Scale));
        }

        if (!double.IsFinite(GroundHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(GroundHeight));
        }

        if (FramesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FramesPerSecond));
        }

        this.OriginX = OriginX;
        this.OriginY = OriginY;
        this.Scale = Scale;
        this.GroundHeight = GroundHeight;
        this.FramesPerSecond = FramesPerSecond;
    }

    public double OriginX { get; }
    public double OriginY { get; }
    public double Scale { get; }
    public double GroundHeight { get; }
    public int FramesPerSecond { get; }
}

public sealed class TopviewGuideV1ImportResult
{
    public TopviewGuideV1ImportResult(SceneDocument document, int? currentIndex, IEnumerable<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(warnings);
        Document = document;
        CurrentIndex = currentIndex;
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }

    public SceneDocument Document { get; }
    public int? CurrentIndex { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed class TopviewGuideV1Importer
{
    public const string SupportedFormat = "gangqueen-topview-guide-v1";

    private static readonly IReadOnlySet<string> RootMembers = new HashSet<string>(["format", "coordinate_system", "backstab_rules", "scene", "evaluations"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> CoordinateMembers = new HashSet<string>(["x_axis", "y_axis", "yaw_zero", "yaw_direction"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> SceneMembers = new HashSet<string>(["id", "name", "note", "current_index", "keyframes"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> FrameMembers = new HashSet<string>(["id", "time", "actors"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ActorMembers = new HashSet<string>(["id", "display_name", "role", "x", "y", "yaw", "action", "lock_on", "lock_target"], StringComparer.Ordinal);

    public TopviewGuideV1ImportResult Import(string sourcePayload, TopviewGuideV1ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(sourcePayload);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            using var parsed = JsonDocument.Parse(sourcePayload);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Topview guide root must be an object.");
            }

            var format = RequiredString(root, "format");
            if (!string.Equals(format, SupportedFormat, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported topview guide format '{format}'.");
            }

            ValidateCoordinateSystem(RequiredObject(root, "coordinate_system", "$.coordinate_system"));
            var scene = RequiredObject(root, "scene", "$.scene");
            var actorBuilders = new Dictionary<string, ActorBuilder>(StringComparer.Ordinal);
            var frameTimes = new HashSet<double>();
            var frames = RequiredArray(scene, "keyframes", "$.scene.keyframes");
            if (frames.GetArrayLength() == 0)
            {
                throw new InvalidDataException("A topview guide must contain at least one keyframe.");
            }

            var durationSeconds = 0d;
            var frameIndex = 0;
            foreach (var frameValue in frames.EnumerateArray())
            {
                var framePath = $"$.scene.keyframes[{frameIndex}]";
                var frame = RequiredObject(frameValue, framePath);
                var frameId = RequiredString(frame, "id");
                var timeSeconds = RequiredFiniteNumber(frame, "time");
                if (timeSeconds < 0 || !frameTimes.Add(timeSeconds))
                {
                    throw new InvalidDataException("Topview keyframe times must be unique and non-negative.");
                }

                durationSeconds = Math.Max(durationSeconds, timeSeconds);
                var actorIdsInFrame = new HashSet<string>(StringComparer.Ordinal);
                var actorIndex = 0;
                foreach (var actorValue in RequiredArray(frame, "actors", $"{framePath}.actors").EnumerateArray())
                {
                    var actor = RequiredObject(actorValue, $"{framePath}.actors[{actorIndex}]");
                    var actorId = RequiredString(actor, "id");
                    if (!actorIdsInFrame.Add(actorId))
                    {
                        throw new InvalidDataException($"Actor '{actorId}' occurs more than once at time {timeSeconds}.");
                    }

                    var displayName = RequiredString(actor, "display_name");
                    var role = RequiredString(actor, "role");
                    if (!actorBuilders.TryGetValue(actorId, out var builder))
                    {
                        builder = new ActorBuilder(actorId, displayName, role);
                        actorBuilders.Add(actorId, builder);
                    }
                    else if (!string.Equals(builder.DisplayName, displayName, StringComparison.Ordinal)
                             || !string.Equals(builder.Role, role, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Actor '{actorId}' changes display name or role between frames.");
                    }

                    var guideX = RequiredFiniteNumber(actor, "x");
                    var guideY = RequiredFiniteNumber(actor, "y");
                    builder.Transforms.Add(new TransformKeyframe(
                        frameId,
                        timeSeconds,
                        new Position3(
                            (guideX - options.OriginX) * options.Scale,
                            options.GroundHeight,
                            (guideY - options.OriginY) * options.Scale),
                        RequiredFiniteNumber(actor, "yaw")));
                    builder.Actions.Add(new ActionKeyframe(frameId, timeSeconds, RequiredString(actor, "action")));
                    var lockEnabled = RequiredBoolean(actor, "lock_on");
                    var targetActorId = OptionalString(actor, "lock_target");
                    builder.LockOns.Add(new LockOnKeyframe(frameId, timeSeconds, lockEnabled, targetActorId));
                    actorIndex++;
                }

                frameIndex++;
            }

            var actors = actorBuilders.Values.Select(builder => new ActorTrack(
                builder.ActorId,
                builder.DisplayName,
                builder.Role,
                builder.Transforms,
                builder.Actions,
                builder.LockOns));
            var document = SceneDocument.Create(
                RequiredString(scene, "id"),
                RequiredString(scene, "name"),
                OptionalString(scene, "note"),
                durationSeconds,
                options.FramesPerSecond,
                actors,
                new ImportMetadata(format, sourcePayload));

            return new TopviewGuideV1ImportResult(
                document,
                OptionalInt32(scene, "current_index"),
                CollectWarnings(root));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Topview guide JSON is malformed.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Topview guide contains invalid domain data.", exception);
        }
    }

    private static void ValidateCoordinateSystem(JsonElement coordinateSystem)
    {
        if (RequiredString(coordinateSystem, "x_axis") != "right"
            || RequiredString(coordinateSystem, "y_axis") != "down"
            || RequiredString(coordinateSystem, "yaw_zero") != "right"
            || RequiredString(coordinateSystem, "yaw_direction") != "clockwise")
        {
            throw new InvalidDataException("Unsupported coordinate_system declaration.");
        }
    }

    private static IEnumerable<string> CollectWarnings(JsonElement root)
    {
        yield return "coordinate_system was converted to world X/Z coordinates; the declaration remains in raw source metadata.";
        if (root.TryGetProperty("backstab_rules", out _))
        {
            yield return "backstab_rules are preserved in raw source metadata but are not interpreted by this importer.";
        }

        if (root.TryGetProperty("evaluations", out _))
        {
            yield return "evaluations are preserved in raw source metadata but are not imported into SceneDocument.";
        }

        foreach (var warning in UnknownMemberWarnings(root, "$", RootMembers))
        {
            yield return warning;
        }

        var coordinateSystem = root.GetProperty("coordinate_system");
        foreach (var warning in UnknownMemberWarnings(coordinateSystem, "$.coordinate_system", CoordinateMembers))
        {
            yield return warning;
        }

        var scene = root.GetProperty("scene");
        foreach (var warning in UnknownMemberWarnings(scene, "$.scene", SceneMembers))
        {
            yield return warning;
        }

        var frameIndex = 0;
        foreach (var frame in scene.GetProperty("keyframes").EnumerateArray())
        {
            var framePath = $"$.scene.keyframes[{frameIndex}]";
            foreach (var warning in UnknownMemberWarnings(frame, framePath, FrameMembers))
            {
                yield return warning;
            }

            var actorIndex = 0;
            foreach (var actor in frame.GetProperty("actors").EnumerateArray())
            {
                foreach (var warning in UnknownMemberWarnings(actor, $"{framePath}.actors[{actorIndex}]", ActorMembers))
                {
                    yield return warning;
                }

                actorIndex++;
            }

            frameIndex++;
        }
    }

    private static IEnumerable<string> UnknownMemberWarnings(JsonElement value, string path, IReadOnlySet<string> knownMembers)
    {
        foreach (var property in value.EnumerateObject())
        {
            if (!knownMembers.Contains(property.Name))
            {
                yield return $"Unknown member '{path}.{property.Name}' is preserved in raw source metadata.";
            }
        }
    }

    private static JsonElement RequiredObject(JsonElement parent, string propertyName, string path)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"'{path}' must be an object.");
    }

    private static JsonElement RequiredObject(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"'{path}' must be an object.");

    private static JsonElement RequiredArray(JsonElement parent, string propertyName, string path)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException($"'{path}' must be an array.");
    }

    private static JsonElement RequiredProperty(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value)
            ? value
            : throw new InvalidDataException($"Required property '{propertyName}' is missing.");

    private static string RequiredString(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException($"'{propertyName}' must be a non-empty string.");
    }

    private static string? OptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException($"'{propertyName}' must be null or a non-empty string.");
    }

    private static double RequiredFiniteNumber(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            throw new InvalidDataException($"'{propertyName}' must be a finite number.");
        }

        return number;
    }

    private static bool RequiredBoolean(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"'{propertyName}' must be a boolean."),
        };
    }

    private static int? OptionalInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"'{propertyName}' must be an integer.");
    }

    private sealed class ActorBuilder(string actorId, string displayName, string role)
    {
        public string ActorId { get; } = actorId;
        public string DisplayName { get; } = displayName;
        public string Role { get; } = role;
        public List<TransformKeyframe> Transforms { get; } = [];
        public List<ActionKeyframe> Actions { get; } = [];
        public List<LockOnKeyframe> LockOns { get; } = [];
    }
}
