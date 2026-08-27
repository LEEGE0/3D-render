using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Projection;
using PvpGuide.Domain;

namespace PvpGuide.Editor.Features.ViewportSync;

public sealed class WorldViewProjectionAdapter : ISceneProjectionConsumer, ITransformPreviewConsumer
{
    private readonly Node3D _actorsRoot;
    private readonly Dictionary<string, Node3D> _actorNodes = new(StringComparer.Ordinal);
    private SceneSnapshot? _latestSnapshot;
    private TransformPreview? _preview;

    public WorldViewProjectionAdapter(Node3D actorsRoot)
    {
        _actorsRoot = actorsRoot ?? throw new ArgumentNullException(nameof(actorsRoot));
    }

    public int ApplyCount { get; private set; }

    public int ActorCount => _actorNodes.Count;

    public void Apply(SceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _latestSnapshot = snapshot;
        ApplyCount++;

        var removedActorIds = _actorNodes.Keys
            .Where(actorId => !snapshot.ActorTransforms.ContainsKey(actorId))
            .ToArray();
        foreach (var actorId in removedActorIds)
        {
            var ownedNode = _actorNodes[actorId];
            _actorNodes.Remove(actorId);
            ownedNode.QueueFree();
        }

        foreach (var (actorId, transform) in snapshot.ActorTransforms)
        {
            ApplyTransform(GetOrCreateActorNode(actorId), transform.Position, transform.YawDegrees);
        }

        ApplyActivePreview();
    }

    public void ApplyPreview(TransformPreview? preview)
    {
        _preview = preview;
        RestoreCommittedTransforms();
        ApplyActivePreview();
    }

    private Node3D GetOrCreateActorNode(string actorId)
    {
        if (_actorNodes.TryGetValue(actorId, out var existing))
        {
            return existing;
        }

        var actorRoot = new Node3D
        {
            Name = CreateActorNodeName(
                actorId,
                _actorNodes.Values.Select(node => node.Name.ToString())),
        };
        _actorsRoot.AddChild(actorRoot);

        var visualRoot = new Node3D { Name = "VisualRoot" };
        actorRoot.AddChild(visualRoot);

        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("4f9cff"),
            Roughness = 0.8f,
        };
        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh
            {
                Radius = 0.35f,
                Height = 1.8f,
            },
            MaterialOverride = bodyMaterial,
            Position = new Vector3(0, 0.9f, 0),
        };
        visualRoot.AddChild(body);

        var facingMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("ffd166"),
            Roughness = 0.65f,
        };
        var facingMarker = new MeshInstance3D
        {
            Name = "FacingPositiveX",
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.8f, 0.1f, 0.12f),
            },
            MaterialOverride = facingMaterial,
            Position = new Vector3(0.65f, 1.15f, 0),
        };
        visualRoot.AddChild(facingMarker);

        actorRoot.AddChild(new Node3D { Name = "OverlayRoot" });
        _actorNodes.Add(actorId, actorRoot);
        return actorRoot;
    }

    private void RestoreCommittedTransforms()
    {
        if (_latestSnapshot is null)
        {
            return;
        }

        foreach (var (actorId, transform) in _latestSnapshot.ActorTransforms)
        {
            if (_actorNodes.TryGetValue(actorId, out var actorRoot))
            {
                ApplyTransform(actorRoot, transform.Position, transform.YawDegrees);
            }
        }
    }

    private void ApplyActivePreview()
    {
        if (_preview is not null && _actorNodes.TryGetValue(_preview.ActorId, out var actorRoot))
        {
            ApplyTransform(actorRoot, _preview.Position, _preview.YawDegrees);
        }
    }

    private static void ApplyTransform(Node3D actorRoot, Position3 position, double yawDegrees)
    {
        var worldPosition = WorldTransformMapper.ToWorldPosition(position);
        actorRoot.Position = new Vector3(
            (float)worldPosition.X,
            (float)worldPosition.Y,
            (float)worldPosition.Z);
        actorRoot.Rotation = new Vector3(0, (float)WorldTransformMapper.ToRotationYRadians(yawDegrees), 0);
    }

    private static string SanitizeNodeName(string actorId)
    {
        var sanitized = string.Concat(actorId.Select(character =>
            char.IsLetterOrDigit(character) || character == '_'
                ? character
                : '_'));
        return sanitized.Length == 0 ? "actor" : sanitized;
    }

    public static string CreateActorNodeName(string actorId, IEnumerable<string> existingNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(existingNames);
        var assigned = existingNames.ToHashSet(StringComparer.Ordinal);
        var baseName = $"Actor_{SanitizeNodeName(actorId)}";
        if (!assigned.Contains(baseName))
        {
            return baseName;
        }

        var stableSuffix = string.Join("_", actorId.Select(character => $"{(int)character:X4}"));
        var candidate = $"{baseName}__{stableSuffix}";
        var collisionIndex = 2;
        while (assigned.Contains(candidate))
        {
            candidate = $"{baseName}__{stableSuffix}_{collisionIndex}";
            collisionIndex++;
        }

        return candidate;
    }
}
