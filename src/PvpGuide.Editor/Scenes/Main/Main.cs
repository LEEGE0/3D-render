using Godot;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.ViewportSync;

namespace PvpGuide.Editor.Scenes.Main;

public partial class Main : Control
{
    private SceneProjectionController? _projectionController;
    private static readonly string[] RequiredPanels =
    [
        "TopViewPanel",
        "WorldViewPanel",
        "TimelinePanel",
        "InspectorPanel",
    ];

    public override void _Ready()
    {
        foreach (var panelName in RequiredPanels)
        {
            if (GetNodeOrNull<Control>(panelName) is null)
            {
                GD.PushError($"Required panel is missing: {panelName}");
                return;
            }
        }

        GD.Print("PROJECT_RUNTIME_READY");

        var topViewPanel = GetNode<Panel>("TopViewPanel");
        var worldViewPanel = GetNode<Panel>("WorldViewPanel");
        var topConsumer = new PanelProjectionConsumer(topViewPanel);
        var worldConsumer = new PanelProjectionConsumer(worldViewPanel);
        var document = new SceneDocument("main-runtime", 1, 30);
        _projectionController = new SceneProjectionController(document, topConsumer, worldConsumer);
        document.AddActor(new ActorTrack("runtime-actor", [
            new TransformKeyframe("runtime-origin", 0, new Position3(0, 0, 0), 0),
        ]));

        GD.Print($"PROJECTION_SYNC_READY revision={document.Revision} top={topConsumer.ApplyCount} world={worldConsumer.ApplyCount}");
    }

    public override void _ExitTree()
    {
        _projectionController?.Dispose();
        _projectionController = null;
    }

    private sealed class PanelProjectionConsumer(Panel panel) : ISceneProjectionConsumer
    {
        public int ApplyCount { get; private set; }

        public void Apply(SceneSnapshot snapshot)
        {
            ApplyCount++;
            panel.SetMeta("scene_document_revision", snapshot.Revision);
        }
    }
}
