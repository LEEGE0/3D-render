using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Projection;
using PvpGuide.Domain;
using PvpGuide.Editor.Features.Timeline;

namespace PvpGuide.Editor.Features.ViewportSync;

public sealed record WorldOverlayLabelStyle(BaseMaterial3D.BillboardModeEnum Billboard);

public sealed class WorldViewProjectionAdapter : ISceneProjectionConsumer, ITransformPreviewConsumer
{
    private const string TrajectoryShaderPath =
        "res://Features/ViewportSync/TrajectoryTimeFade.gdshader";

    private readonly Node3D _actorsRoot;
    private readonly Dictionary<string, ActorProjectionNodes> _actorNodes = new(StringComparer.Ordinal);
    private readonly Node3D _trajectoryOverlayRoot;
    private readonly Shader _trajectoryShader;
    private readonly Dictionary<string, ActorTrajectoryProjectionNodes> _actorTrajectoryNodes =
        new(StringComparer.Ordinal);
    private SceneSnapshot? _latestSnapshot;
    private WorldTrajectoryRenderState? _trajectoryRenderState;
    private TransformPreview? _preview;

    public static WorldOverlayLabelStyle OverlayLabelStyle { get; } =
        new(BaseMaterial3D.BillboardModeEnum.Enabled);

    public WorldViewProjectionAdapter(Node3D actorsRoot)
    {
        _actorsRoot = actorsRoot ?? throw new ArgumentNullException(nameof(actorsRoot));
        _trajectoryShader = GD.Load<Shader>(TrajectoryShaderPath)
            ?? throw new InvalidOperationException($"Could not load trajectory shader '{TrajectoryShaderPath}'.");
        _trajectoryOverlayRoot = new Node3D { Name = "TrajectoryOverlayRoot" };
        _actorsRoot.AddChild(_trajectoryOverlayRoot);
    }

    public int ApplyCount { get; private set; }

    public int ActorCount => _actorNodes.Count;

    public int TrajectoryActorCount => _actorTrajectoryNodes.Count;

    public void Apply(SceneProjectionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var snapshot = frame.Snapshot;
        var overlays = CreateSemanticOverlays(snapshot, _preview);
        var trajectoryRenderState = WorldTrajectoryRenderState.Create(frame, _trajectoryRenderState);
        _latestSnapshot = snapshot;
        ApplyCount++;

        var removedActorIds = _actorNodes.Keys
            .Where(actorId => !snapshot.ActorTransforms.ContainsKey(actorId))
            .ToArray();
        foreach (var actorId in removedActorIds)
        {
            var ownedNode = _actorNodes[actorId];
            _actorNodes.Remove(actorId);
            ownedNode.ActorRoot.QueueFree();
        }

        var removedTrajectoryActorIds = _actorTrajectoryNodes.Keys
            .Where(actorId => !trajectoryRenderState.ActorGeometries.ContainsKey(actorId))
            .ToArray();
        foreach (var actorId in removedTrajectoryActorIds)
        {
            var ownedNode = _actorTrajectoryNodes[actorId];
            _actorTrajectoryNodes.Remove(actorId);
            ownedNode.Container.QueueFree();
        }

        foreach (var (actorId, transform) in snapshot.ActorTransforms)
        {
            var nodes = GetOrCreateActorNodes(actorId);
            var yawDegrees = snapshot.ActorFacings.TryGetValue(actorId, out var facing)
                ? facing.YawDegrees
                : transform.YawDegrees;
            ApplyTransform(nodes.ActorRoot, transform.Position, yawDegrees);
        }

        ApplyActivePreview();
        ApplyOverlays(overlays);
        ApplyTrajectoryRenderState(trajectoryRenderState);
        _trajectoryRenderState = trajectoryRenderState;
    }

    public void ApplyPreview(TransformPreview? preview)
    {
        var overlays = _latestSnapshot is null
            ? null
            : CreateSemanticOverlays(_latestSnapshot, preview);

        _preview = preview;
        RestoreCommittedTransforms();
        ApplyActivePreview();
        if (overlays is not null)
        {
            ApplyOverlays(overlays);
        }
    }

    private ActorProjectionNodes GetOrCreateActorNodes(string actorId)
    {
        if (_actorNodes.TryGetValue(actorId, out var existing))
        {
            return existing;
        }

        var actorRoot = new Node3D
        {
            Name = CreateActorNodeName(
                actorId,
                _actorNodes.Values.Select(nodes => nodes.ActorRoot.Name.ToString())),
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

        var overlayRoot = new Node3D { Name = "OverlayRoot" };
        actorRoot.AddChild(overlayRoot);

        var actionLabel = CreateOverlayLabel(
            "ActionLabel",
            new Vector3(0, 2.45f, 0),
            fontSize: 32,
            pixelSize: 0.004f,
            new Color("f4f7ff"));
        overlayRoot.AddChild(actionLabel);

        var lockBadge = CreateOverlayLabel(
            "LockBadge",
            new Vector3(0, 2.15f, 0),
            fontSize: 28,
            pixelSize: 0.004f,
            new Color("ff6b6b"));
        overlayRoot.AddChild(lockBadge);

        var lockLineMesh = new ImmediateMesh();
        var lockLine = new MeshInstance3D
        {
            Name = "LockLine",
            Mesh = lockLineMesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("ff6b6b"),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Visible = false,
        };
        overlayRoot.AddChild(lockLine);

        var created = new ActorProjectionNodes(actorRoot, actionLabel, lockBadge, lockLine, lockLineMesh);
        _actorNodes.Add(actorId, created);
        return created;
    }

    private void RestoreCommittedTransforms()
    {
        if (_latestSnapshot is null)
        {
            return;
        }

        foreach (var (actorId, transform) in _latestSnapshot.ActorTransforms)
        {
            if (_actorNodes.TryGetValue(actorId, out var nodes))
            {
                var yawDegrees = _latestSnapshot.ActorFacings.TryGetValue(actorId, out var facing)
                    ? facing.YawDegrees
                    : transform.YawDegrees;
                ApplyTransform(nodes.ActorRoot, transform.Position, yawDegrees);
            }
        }
    }

    private void ApplyActivePreview()
    {
        if (_preview is not null && _actorNodes.TryGetValue(_preview.ActorId, out var nodes))
        {
            ApplyTransform(nodes.ActorRoot, _preview.Position, _preview.YawDegrees);
        }
    }

    private void ApplyTrajectoryRenderState(WorldTrajectoryRenderState state)
    {
        foreach (var (actorId, geometry) in state.ActorGeometries)
        {
            var nodes = GetOrCreateTrajectoryNodes(actorId);
            if (!ReferenceEquals(nodes.Geometry, geometry))
            {
                RewriteTrajectoryMesh(
                    nodes.SharedPath.Mesh,
                    geometry.SharedPath,
                    Mesh.PrimitiveType.LineStrip);
                RewriteTrajectoryMesh(
                    nodes.FreeFacingTicks.Mesh,
                    geometry.FreeFacingTicks,
                    Mesh.PrimitiveType.Lines);
                RewriteTrajectoryMesh(
                    nodes.LockOnFacingTicks.Mesh,
                    geometry.LockOnFacingTicks,
                    Mesh.PrimitiveType.Lines);
                nodes.Geometry = geometry;
            }

            ApplyCurrentTime(nodes.SharedPath.Material, state.CurrentTimeNormalized);
            ApplyCurrentTime(nodes.FreeFacingTicks.Material, state.CurrentTimeNormalized);
            ApplyCurrentTime(nodes.LockOnFacingTicks.Material, state.CurrentTimeNormalized);
        }
    }

    private ActorTrajectoryProjectionNodes GetOrCreateTrajectoryNodes(string actorId)
    {
        if (_actorTrajectoryNodes.TryGetValue(actorId, out var existing))
        {
            return existing;
        }

        var container = new Node3D
        {
            Name = CreateTrajectoryNodeName(
                actorId,
                _actorTrajectoryNodes.Values.Select(nodes => nodes.Container.Name.ToString())),
        };
        _trajectoryOverlayRoot.AddChild(container);

        var sharedPath = CreateTrajectoryMesh("SharedTrajectory", new Color("6ea8fe"));
        var freeFacingTicks = CreateTrajectoryMesh("FreeFacingTicks", new Color("55aaff"));
        var lockOnFacingTicks = CreateTrajectoryMesh("LockOnFacingTicks", new Color("ffd166"));
        container.AddChild(sharedPath.Instance);
        container.AddChild(freeFacingTicks.Instance);
        container.AddChild(lockOnFacingTicks.Instance);

        var created = new ActorTrajectoryProjectionNodes(
            container,
            sharedPath,
            freeFacingTicks,
            lockOnFacingTicks);
        _actorTrajectoryNodes.Add(actorId, created);
        return created;
    }

    private TrajectoryMeshProjectionNodes CreateTrajectoryMesh(string name, Color color)
    {
        var mesh = new ImmediateMesh();
        var material = new ShaderMaterial { Shader = _trajectoryShader };
        material.SetShaderParameter("base_color", color);
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
        };
        return new TrajectoryMeshProjectionNodes(instance, mesh, material);
    }

    private static void RewriteTrajectoryMesh(
        ImmediateMesh mesh,
        WorldTrajectoryMeshGeometry geometry,
        Mesh.PrimitiveType primitiveType)
    {
        mesh.ClearSurfaces();
        if (geometry.Vertices.Count < 2)
        {
            return;
        }

        mesh.SurfaceBegin(primitiveType);
        for (var index = 0; index < geometry.Vertices.Count; index++)
        {
            mesh.SurfaceSetUV(new Vector2((float)geometry.NormalizedTimes[index], 0));
            mesh.SurfaceAddVertex(ToVector3(geometry.Vertices[index]));
        }

        mesh.SurfaceEnd();
    }

    private static void ApplyCurrentTime(ShaderMaterial material, double currentTimeNormalized) =>
        material.SetShaderParameter("current_time_normalized", (float)currentTimeNormalized);

    private void ApplyOverlays(IReadOnlyDictionary<string, SemanticActorOverlay> overlays)
    {
        foreach (var (actorId, overlay) in overlays)
        {
            if (_actorNodes.TryGetValue(actorId, out var nodes))
            {
                ApplyOverlay(nodes, overlay);
            }
        }
    }

    private static IReadOnlyDictionary<string, SemanticActorOverlay> CreateSemanticOverlays(
        SceneSnapshot snapshot,
        TransformPreview? preview) =>
        SemanticOverlayLayout.CreateScene(
            snapshot,
            preview is null
                ? null
                : new Dictionary<string, Position3>(StringComparer.Ordinal)
                {
                    [preview.ActorId] = preview.Position,
                });

    private static void ApplyOverlay(ActorProjectionNodes nodes, SemanticActorOverlay overlay)
    {
        nodes.ActionLabel.Text = overlay.ActionLabel ?? string.Empty;
        nodes.ActionLabel.Visible = overlay.ActionLabel is not null;
        nodes.LockBadge.Text = overlay.LockBadge ?? string.Empty;
        nodes.LockBadge.Visible = overlay.LockBadge is not null;

        nodes.LockLineMesh.ClearSurfaces();
        if (overlay.LockLine is null)
        {
            nodes.LockLine.Visible = false;
            return;
        }

        var vertices = ToWorldLineVertices(overlay.LockLine);
        var actorTransformInverse = nodes.ActorRoot.Transform.AffineInverse();
        nodes.LockLineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        nodes.LockLineMesh.SurfaceAddVertex(actorTransformInverse * ToVector3(vertices.Start));
        nodes.LockLineMesh.SurfaceAddVertex(actorTransformInverse * ToVector3(vertices.End));
        nodes.LockLineMesh.SurfaceEnd();
        nodes.LockLine.Visible = true;
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

    public static (WorldPosition Start, WorldPosition End) ToWorldLineVertices(SemanticOverlayLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return (
            WorldTransformMapper.ToWorldPosition(line.Start),
            WorldTransformMapper.ToWorldPosition(line.End));
    }

    private static Label3D CreateOverlayLabel(
        string name,
        Vector3 position,
        int fontSize,
        float pixelSize,
        Color modulateColor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Label3D
        {
            Name = name,
            Position = position,
            FontSize = fontSize,
            PixelSize = pixelSize,
            Modulate = modulateColor,
            Billboard = OverlayLabelStyle.Billboard,
            Visible = false,
        };
    }

    private static Vector3 ToVector3(WorldPosition position) =>
        new((float)position.X, (float)position.Y, (float)position.Z);

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

    private static string CreateTrajectoryNodeName(string actorId, IEnumerable<string> existingNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(existingNames);
        var assigned = existingNames.ToHashSet(StringComparer.Ordinal);
        var baseName = $"Trajectory_{SanitizeNodeName(actorId)}";
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

    private sealed record ActorProjectionNodes(
        Node3D ActorRoot,
        Label3D ActionLabel,
        Label3D LockBadge,
        MeshInstance3D LockLine,
        ImmediateMesh LockLineMesh);

    private sealed class ActorTrajectoryProjectionNodes(
        Node3D container,
        TrajectoryMeshProjectionNodes sharedPath,
        TrajectoryMeshProjectionNodes freeFacingTicks,
        TrajectoryMeshProjectionNodes lockOnFacingTicks)
    {
        public Node3D Container { get; } = container;

        public TrajectoryMeshProjectionNodes SharedPath { get; } = sharedPath;

        public TrajectoryMeshProjectionNodes FreeFacingTicks { get; } = freeFacingTicks;

        public TrajectoryMeshProjectionNodes LockOnFacingTicks { get; } = lockOnFacingTicks;

        public WorldTrajectoryGeometry? Geometry { get; set; }
    }

    private sealed record TrajectoryMeshProjectionNodes(
        MeshInstance3D Instance,
        ImmediateMesh Mesh,
        ShaderMaterial Material);
}
