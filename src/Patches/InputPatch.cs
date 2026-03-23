using Godot;
using GodotExplorer.Core;

namespace GodotExplorer.Patches;

/// <summary>
/// Input handling for the explorer. Since mod DLLs don't have Godot's source
/// generators (no Godot.NET.Sdk), virtual methods like _Input/_Process won't
/// be called. Instead, we poll input state each frame via SceneTree.ProcessFrame.
/// </summary>
public static class InputPatch
{
    private static bool _f12WasPressed;
    private static bool _f11WasPressed;
    private static bool _installed;

    /// <summary>
    /// Install the per-frame input poller via SceneTree signal.
    /// </summary>
    public static void Install(SceneTree sceneTree)
    {
        if (_installed) return;
        _installed = true;
        sceneTree.Connect("process_frame", Callable.From(PollInput));
        GD.Print("[GodotExplorer] Input poller installed.");
    }

    private static void PollInput()
    {
        // F12 toggle (edge-detect: only trigger on press, not hold)
        bool f12Pressed = Input.IsKeyPressed(Key.F12);
        if (f12Pressed && !_f12WasPressed)
        {
            ExplorerCore.ToggleExplorer();
        }
        _f12WasPressed = f12Pressed;

        // F11 HUD toggle
        bool f11Pressed = Input.IsKeyPressed(Key.F11);
        if (f11Pressed && !_f11WasPressed && ExplorerCore.IsVisible)
        {
            ToggleGameHud();
        }
        _f11WasPressed = f11Pressed;

        // Freecam processing (per-frame movement)
        if (ExplorerCore.IsVisible)
        {
            var freeCamPanel = ExplorerCore.UI?.FreeCamPanel;
            if (freeCamPanel?.Controller?.IsActive == true)
            {
                var controller = freeCamPanel.Controller;
                // Build movement from held keys
                var moveDir = Vector2.Zero;
                if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) moveDir.Y -= 1;
                if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) moveDir.Y += 1;
                if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) moveDir.X -= 1;
                if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) moveDir.X += 1;
                controller.MoveSpeed = Input.IsKeyPressed(Key.Shift) ? 800f : 400f;
                controller.SetMoveDirection(moveDir);

                double delta = ExplorerCore.SceneTree.Root.GetProcessDeltaTime();
                controller.Process(delta);
            }
        }
    }

    private static void ToggleGameHud()
    {
        var root = ExplorerCore.SceneTree?.Root;
        if (root == null) return;

        int toggled = 0;
        foreach (var child in root.GetChildren())
        {
            if (child is CanvasLayer cl && cl.Name.ToString() != "GodotExplorer")
            {
                cl.Visible = !cl.Visible;
                toggled++;
            }
        }
        GD.Print($"[GodotExplorer] Toggled {toggled} CanvasLayer(s).");
    }
}
