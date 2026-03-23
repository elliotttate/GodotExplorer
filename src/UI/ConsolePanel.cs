using Godot;
using GodotExplorer.Core;
using System;
using System.Collections.Generic;

namespace GodotExplorer.UI;

/// <summary>
/// Debug console with log viewer and command input.
/// </summary>
public class ConsolePanel
{
    public VBoxContainer Root { get; }

    private RichTextLabel _logOutput;
    private LineEdit _commandInput;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private int _lineCount;
    private const int MaxLines = 1000;

    private readonly Dictionary<string, Action<string[]>> _commands = new();

    public ConsolePanel()
    {
        Root = new VBoxContainer();
        Root.Name = "ConsolePanel";
        Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Root.AddThemeConstantOverride("separation", ExplorerTheme.ItemSpacing);

        // Header
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        Root.AddChild(headerRow);

        var header = new Label();
        header.Text = "Console";
        ExplorerTheme.StyleLabel(header, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeHeader);
        headerRow.AddChild(header);

        var clearBtn = new Button();
        clearBtn.Text = "Clear";
        ExplorerTheme.StyleButton(clearBtn);
        clearBtn.Pressed += ClearLog;
        headerRow.AddChild(clearBtn);

        // Log output
        _logOutput = new RichTextLabel();
        _logOutput.BbcodeEnabled = true;
        _logOutput.ScrollFollowing = true;
        _logOutput.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _logOutput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _logOutput.SelectionEnabled = true;

        var logBg = ExplorerTheme.MakeFlatStyleBox(new Color(0.06f, 0.06f, 0.08f, 0.98f));
        logBg.SetContentMarginAll(4);
        _logOutput.AddThemeStyleboxOverride("normal", logBg);
        _logOutput.AddThemeColorOverride("default_color", ExplorerTheme.TextColor);
        _logOutput.AddThemeFontSizeOverride("normal_font_size", ExplorerTheme.FontSizeSmall);
        Root.AddChild(_logOutput);

        // Command input
        _commandInput = new LineEdit();
        _commandInput.PlaceholderText = "Type a command... (type 'help' for commands)";
        _commandInput.ClearButtonEnabled = true;
        ExplorerTheme.StyleLineEdit(_commandInput);
        _commandInput.TextSubmitted += OnCommandSubmitted;
        _commandInput.GuiInput += OnCommandInputKey;
        Root.AddChild(_commandInput);

        RegisterCommands();
        Log("[color=#5588cc]GodotExplorer Console[/color] — type 'help' for available commands.");
    }

    private void RegisterCommands()
    {
        _commands["help"] = _ => ShowHelp();
        _commands["tree"] = _ => PrintTree();
        _commands["inspect"] = args => InspectNode(args);
        _commands["get"] = args => GetProperty(args);
        _commands["set"] = args => SetProperty(args);
        _commands["find"] = args => FindNodes(args);
        _commands["freecam"] = _ => ToggleFreecam();
        _commands["hud"] = _ => ToggleHud();
        _commands["count"] = _ => CountNodes();
        _commands["groups"] = args => ListGroups(args);
        _commands["clear"] = _ => ClearLog();
    }

    public void Log(string message)
    {
        _logOutput.AppendText(message + "\n");
        _lineCount++;

        // Trim old lines if we exceed the limit
        if (_lineCount > MaxLines)
        {
            _logOutput.RemoveParagraph(0);
            _lineCount--;
        }
    }

    public void LogError(string message)
    {
        Log($"[color=#ff6666]{message}[/color]");
    }

    public void LogWarning(string message)
    {
        Log($"[color=#ffdd55]{message}[/color]");
    }

    public void LogSuccess(string message)
    {
        Log($"[color=#66ee77]{message}[/color]");
    }

    private void ClearLog()
    {
        _logOutput.Clear();
        _lineCount = 0;
    }

    private void OnCommandSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _commandInput.Clear();
        _commandHistory.Add(text);
        _historyIndex = _commandHistory.Count;

        Log($"[color=#aaaaaa]> {text}[/color]");

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string cmd = parts[0].ToLowerInvariant();
        string[] args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        if (_commands.TryGetValue(cmd, out var handler))
        {
            try { handler(args); }
            catch (Exception ex) { LogError($"Error: {ex.Message}"); }
        }
        else
        {
            LogError($"Unknown command: '{cmd}'. Type 'help' for available commands.");
        }
    }

    private void OnCommandInputKey(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed)
        {
            if (key.Keycode == Key.Up && _commandHistory.Count > 0)
            {
                _historyIndex = Math.Max(0, _historyIndex - 1);
                _commandInput.Text = _commandHistory[_historyIndex];
                _commandInput.CaretColumn = _commandInput.Text.Length;
            }
            else if (key.Keycode == Key.Down && _commandHistory.Count > 0)
            {
                _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);
                _commandInput.Text = _historyIndex < _commandHistory.Count
                    ? _commandHistory[_historyIndex] : "";
                _commandInput.CaretColumn = _commandInput.Text.Length;
            }
        }
    }

    // ==================== Commands ====================

    private void ShowHelp()
    {
        Log("[color=#5588cc]Available commands:[/color]");
        Log("  help                    — Show this help");
        Log("  tree                    — Print scene tree");
        Log("  inspect <path>          — Select a node in the inspector");
        Log("  get <path> <property>   — Get a property value");
        Log("  set <path> <prop> <val> — Set a property value");
        Log("  find <pattern>          — Find nodes by name");
        Log("  count                   — Count all nodes");
        Log("  groups [group]          — List groups or nodes in a group");
        Log("  freecam                 — Toggle free camera");
        Log("  hud                     — Toggle game HUD");
        Log("  clear                   — Clear console");
    }

    private void PrintTree()
    {
        var root = ExplorerCore.SceneTree?.Root;
        if (root == null) { LogError("No scene tree."); return; }
        PrintTreeRecursive(root, 0, 200);
    }

    private int PrintTreeRecursive(Node node, int depth, int remaining)
    {
        if (remaining <= 0)
        {
            Log("  ... (truncated)");
            return 0;
        }

        string indent = new string(' ', depth * 2);
        string typeName = node.GetClass();
        Log($"  {indent}{node.Name} [{typeName}]");
        remaining--;

        foreach (var child in node.GetChildren())
        {
            if (child.Name.ToString().StartsWith("GodotExplorer")) continue;
            remaining = PrintTreeRecursive(child, depth + 1, remaining);
            if (remaining <= 0) break;
        }

        return remaining;
    }

    private void InspectNode(string[] args)
    {
        if (args.Length == 0) { LogError("Usage: inspect <node_path>"); return; }
        string path = args[0];

        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(path);
        if (node == null) { LogError($"Node not found: {path}"); return; }

        ExplorerCore.SelectNode(node);
        LogSuccess($"Selected: {node.Name} [{node.GetClass()}]");
    }

    private void GetProperty(string[] args)
    {
        if (args.Length < 2) { LogError("Usage: get <node_path> <property>"); return; }

        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(args[0]);
        if (node == null) { LogError($"Node not found: {args[0]}"); return; }

        var value = PropertyHelper.ReadValue(node, args[1]);
        Log($"  {args[1]} = {value}");
    }

    private void SetProperty(string[] args)
    {
        if (args.Length < 3) { LogError("Usage: set <node_path> <property> <value>"); return; }

        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(args[0]);
        if (node == null) { LogError($"Node not found: {args[0]}"); return; }

        // Try to parse value as various types
        string valueStr = string.Join(' ', args[2..]);
        Variant value;

        if (bool.TryParse(valueStr, out bool bVal)) value = bVal;
        else if (int.TryParse(valueStr, out int iVal)) value = iVal;
        else if (float.TryParse(valueStr, out float fVal)) value = fVal;
        else value = valueStr;

        if (PropertyHelper.WriteValue(node, args[1], value))
            LogSuccess($"  Set {args[1]} = {value}");
        else
            LogError($"  Failed to set {args[1]}");
    }

    private void FindNodes(string[] args)
    {
        if (args.Length == 0) { LogError("Usage: find <pattern>"); return; }

        var results = ExplorerCore.SceneTree?.Root?.FindChildren($"*{args[0]}*", "", true, false);
        if (results == null || results.Count == 0)
        {
            Log("  No results found.");
            return;
        }

        int count = 0;
        foreach (var node in results)
        {
            if (node.Name.ToString().StartsWith("GodotExplorer")) continue;
            Log($"  {node.GetPath()} [{node.GetClass()}]");
            count++;
            if (count >= 50) { Log($"  ... and {results.Count - 50} more"); break; }
        }
        Log($"  {results.Count} total result(s).");
    }

    private void ToggleFreecam()
    {
        // Will be wired up when FreeCamPanel is integrated
        Log("Freecam toggle - use the Freecam panel button.");
    }

    private void ToggleHud()
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
        LogSuccess($"Toggled {toggled} CanvasLayer(s).");
    }

    private void CountNodes()
    {
        var root = ExplorerCore.SceneTree?.Root;
        if (root == null) return;

        int count = CountRecursive(root);
        Log($"  Total nodes: {count}");
    }

    private int CountRecursive(Node node)
    {
        int count = 1;
        foreach (var child in node.GetChildren())
            count += CountRecursive(child);
        return count;
    }

    private void ListGroups(string[] args)
    {
        if (args.Length > 0)
        {
            var nodes = ExplorerCore.SceneTree?.GetNodesInGroup(args[0]);
            if (nodes == null || nodes.Count == 0)
            {
                Log($"  No nodes in group '{args[0]}'.");
                return;
            }
            foreach (var node in nodes)
                Log($"  {node.GetPath()} [{node.GetClass()}]");
            Log($"  {nodes.Count} node(s) in group '{args[0]}'.");
        }
        else
        {
            Log("  Usage: groups <group_name>");
            Log("  Lists all nodes belonging to the specified group.");
        }
    }
}
