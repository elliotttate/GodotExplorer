using Godot;
using GodotExplorer.UI;
using GodotExplorer.Patches;

namespace GodotExplorer.Core;

/// <summary>
/// Singleton coordinator. Manages lifecycle, UI, selected node state, and subsystems.
/// </summary>
public static class ExplorerCore
{
    public const string Version = "1.0.0";

    private static SceneTree? _sceneTree;
    private static bool _initialized;

    public static SceneTree SceneTree => _sceneTree!;
    public static ExplorerUI? UI { get; private set; }
    public static bool IsVisible { get; private set; }

    // Currently selected node (stored as instance ID for safety)
    private static ulong _selectedNodeId;

    public static Node? SelectedNode
    {
        get
        {
            if (_selectedNodeId == 0) return null;
            var obj = GodotObject.InstanceFromId(_selectedNodeId);
            if (obj is Node node && GodotObject.IsInstanceValid(node))
                return node;
            _selectedNodeId = 0;
            return null;
        }
    }

    // Events
    public static event System.Action<Node?>? NodeSelected;
    public static event System.Action<bool>? VisibilityChanged;

    public static void Initialize(SceneTree sceneTree)
    {
        if (_initialized) return;
        _initialized = true;
        _sceneTree = sceneTree;

        GD.Print($"[GodotExplorer] Initializing v{Version}...");

        // Install per-frame input polling via SceneTree signal.
        // This avoids relying on Godot virtual method overrides which don't work
        // in dynamically loaded mod DLLs (no source generators).
        InputPatch.Install(sceneTree);

        // Create the UI
        UI = new ExplorerUI();
        sceneTree.Root.CallDeferred("add_child", UI.RootLayer);

        // Start hidden
        SetVisible(false);

        GD.Print("[GodotExplorer] Initialized. Press F12 to toggle.");
    }

    public static void ToggleExplorer()
    {
        SetVisible(!IsVisible);
    }

    public static void SetVisible(bool visible)
    {
        IsVisible = visible;
        if (UI != null)
        {
            UI.RootLayer.Visible = visible;
        }
        VisibilityChanged?.Invoke(visible);
    }

    public static void SelectNode(Node? node)
    {
        _selectedNodeId = node != null && GodotObject.IsInstanceValid(node)
            ? node.GetInstanceId()
            : 0;
        NodeSelected?.Invoke(node);
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        if (UI != null && GodotObject.IsInstanceValid(UI.RootLayer))
        {
            UI.RootLayer.QueueFree();
        }

        _initialized = false;
        GD.Print("[GodotExplorer] Shut down.");
    }
}
