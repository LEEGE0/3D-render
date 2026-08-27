using Godot;

namespace PvpGuide.Editor.Scenes.Main;

public partial class Main : Control
{
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
    }
}
