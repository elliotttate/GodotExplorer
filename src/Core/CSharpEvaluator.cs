using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Godot;

namespace GodotExplorer.Core;

/// <summary>
/// Globals object exposed to C# scripts. Provides convenient access to
/// the game's scene tree, selected nodes, and utility methods.
/// </summary>
public class ScriptGlobals
{
    public SceneTree Tree => ExplorerCore.SceneTree;
    public Window Root => ExplorerCore.SceneTree.Root;
    public Node? Selected => ExplorerCore.SelectedNode;
    public Node? N(string path) => Root.GetNodeOrNull(path);
    public Godot.Collections.Array<Node> FindAll(string type) => Root.FindChildren("*", type, true, false);
    public Godot.Collections.Array<Node> Find(string pattern) => Root.FindChildren($"*{pattern}*", "", true, false);
    public void Print(object? value) => GD.Print(value?.ToString() ?? "(null)");
    public Variant Get(string prop) => Selected?.Get(prop) ?? default;
    public void Set(string prop, Variant value) => Selected?.Set(prop, value);
}

/// <summary>
/// C# REPL evaluator using Roslyn scripting. Loads Roslyn assemblies lazily
/// at runtime to avoid breaking the mod loader (which fails if it can't find
/// Roslyn DLLs during type scanning).
///
/// All Roslyn types are accessed via reflection to keep them out of our
/// assembly's type references.
/// </summary>
public class CSharpEvaluator
{
    private object? _state; // ScriptState<object> — stored as object to avoid compile-time ref
    private object? _options; // ScriptOptions
    private readonly ScriptGlobals _globals;
    private readonly List<string> _history = new();
    private bool _roslynLoaded;

    // Reflected types/methods from Roslyn (resolved once on first use)
    private Type? _csharpScriptType;
    private Type? _scriptOptionsType;
    private MethodInfo? _runAsyncMethod;
    private MethodInfo? _continueWithAsyncMethod;

    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;
    public IReadOnlyList<string> History => _history;

    public CSharpEvaluator()
    {
        _globals = new ScriptGlobals();
    }

    /// <summary>
    /// Load Roslyn assemblies and resolve types. Returns false if Roslyn isn't available.
    /// </summary>
    public bool EnsureRoslynLoaded()
    {
        if (_roslynLoaded) return true;

        try
        {
            // Find the Roslyn DLLs — check mod folder and game data folder
            string? modDir = FindModDirectory();
            string? gameDataDir = FindGameDataDirectory();

            var searchDirs = new List<string>();
            if (modDir != null) searchDirs.Add(modDir);
            if (gameDataDir != null) searchDirs.Add(gameDataDir);

            // Register an assembly resolver so Roslyn's transitive deps can be found
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                foreach (var dir in searchDirs)
                {
                    string path = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(path))
                        return ctx.LoadFromAssemblyPath(path);
                }
                return null;
            };

            // Load the scripting assembly
            Assembly? scriptingAsm = null;
            foreach (var dir in searchDirs)
            {
                string path = Path.Combine(dir, "Microsoft.CodeAnalysis.CSharp.Scripting.dll");
                if (File.Exists(path))
                {
                    scriptingAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                    GD.Print($"[GodotExplorer] Loaded Roslyn from: {dir}");
                    break;
                }
            }

            if (scriptingAsm == null)
            {
                GD.PrintErr("[GodotExplorer] Microsoft.CodeAnalysis.CSharp.Scripting.dll not found!");
                return false;
            }

            // Resolve types via reflection
            _csharpScriptType = scriptingAsm.GetType("Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript");

            var scriptingBaseAsm = AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName("Microsoft.CodeAnalysis.Scripting"));
            _scriptOptionsType = scriptingBaseAsm.GetType("Microsoft.CodeAnalysis.Scripting.ScriptOptions");

            if (_csharpScriptType == null || _scriptOptionsType == null)
            {
                GD.PrintErr("[GodotExplorer] Failed to resolve Roslyn types.");
                return false;
            }

            // Build ScriptOptions
            _options = BuildScriptOptions();

            _roslynLoaded = true;
            GD.Print("[GodotExplorer] Roslyn C# scripting loaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotExplorer] Failed to load Roslyn: {ex.Message}");
            return false;
        }
    }

    private object BuildScriptOptions()
    {
        // ScriptOptions.Default
        var defaultProp = _scriptOptionsType!.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
        var options = defaultProp!.GetValue(null)!;

        // .WithReferences(assemblies)
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .ToArray();
        var withRefs = _scriptOptionsType.GetMethods()
            .First(m => m.Name == "WithReferences" && m.GetParameters()[0].ParameterType == typeof(IEnumerable<Assembly>));
        options = withRefs.Invoke(options, new object[] { assemblies })!;

        // .WithImports(namespaces)
        string[] imports = { "System", "System.Linq", "System.Collections.Generic", "Godot" };
        var withImports = _scriptOptionsType.GetMethods()
            .First(m => m.Name == "WithImports" && m.GetParameters()[0].ParameterType == typeof(IEnumerable<string>));
        options = withImports.Invoke(options, new object[] { imports })!;

        return options;
    }

    public async Task<string> EvaluateAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        if (!EnsureRoslynLoaded()) return "Error: Roslyn scripting not available.";

        _history.Add(code);

        try
        {
            if (_state == null)
            {
                // CSharpScript.RunAsync<object>(code, options, globals, typeof(ScriptGlobals))
                var runMethod = _csharpScriptType!.GetMethods()
                    .Where(m => m.Name == "RunAsync" && m.IsGenericMethod)
                    .First()
                    .MakeGenericMethod(typeof(object));

                var task = (Task)runMethod.Invoke(null, new object?[] { code, _options, _globals, typeof(ScriptGlobals), default(System.Threading.CancellationToken) })!;
                await task;

                // Get the result from Task<ScriptState<object>>
                _state = task.GetType().GetProperty("Result")!.GetValue(task);
            }
            else
            {
                // state.ContinueWithAsync<object>(code, options)
                var continueMethod = _state.GetType().GetMethods()
                    .Where(m => m.Name == "ContinueWithAsync" && m.IsGenericMethod)
                    .First()
                    .MakeGenericMethod(typeof(object));

                var task = (Task)continueMethod.Invoke(_state, new object?[] { code, _options, default(System.Threading.CancellationToken) })!;
                await task;
                _state = task.GetType().GetProperty("Result")!.GetValue(task);
            }

            // Get ReturnValue from ScriptState
            var returnValue = _state!.GetType().GetProperty("ReturnValue")!.GetValue(_state);
            if (returnValue != null)
            {
                string result = FormatResult(returnValue);
                OutputReceived?.Invoke(result);
                return result;
            }

            return "(no return value)";
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Unwrap compilation or runtime errors
            var inner = ex.InnerException;
            string error;

            if (inner.GetType().Name == "CompilationErrorException")
            {
                var diags = inner.GetType().GetProperty("Diagnostics")!.GetValue(inner);
                error = string.Join("\n", ((System.Collections.IEnumerable)diags!).Cast<object>().Select(d => d.ToString()));
            }
            else
            {
                error = $"{inner.GetType().Name}: {inner.Message}";
            }

            ErrorReceived?.Invoke(error);
            return $"Error: {error}";
        }
        catch (Exception ex)
        {
            string error = $"{ex.GetType().Name}: {ex.Message}";
            ErrorReceived?.Invoke(error);
            return $"Error: {error}";
        }
    }

    public void Evaluate(string code, Action<string> onResult)
    {
        Task.Run(async () =>
        {
            try
            {
                string result = await EvaluateAsync(code);
                onResult(result);
            }
            catch (Exception ex)
            {
                onResult($"Error: {ex.Message}");
            }
        });
    }

    public void Reset()
    {
        _state = null;
        if (_roslynLoaded)
            _options = BuildScriptOptions();
        OutputReceived?.Invoke("C# evaluator state reset.");
    }

    private static string FormatResult(object value)
    {
        if (value is null) return "(null)";
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var items = new List<string>();
            int count = 0;
            foreach (var item in enumerable)
            {
                items.Add(item?.ToString() ?? "(null)");
                count++;
                if (count >= 20) { items.Add($"... ({count}+ items)"); break; }
            }
            if (items.Count == 0) return "(empty collection)";
            return string.Join("\n", items);
        }
        return value.ToString() ?? "(null)";
    }

    private static string? FindModDirectory()
    {
        // Our DLL is in the mod folder
        string? myPath = typeof(CSharpEvaluator).Assembly.Location;
        if (!string.IsNullOrEmpty(myPath))
            return Path.GetDirectoryName(myPath);
        return null;
    }

    private static string? FindGameDataDirectory()
    {
        string? exePath = Godot.OS.GetExecutablePath();
        if (string.IsNullOrEmpty(exePath)) return null;
        string? exeDir = Path.GetDirectoryName(exePath);
        if (exeDir == null) return null;

        // Game data is in data_sts2_windows_x86_64/ next to the exe
        string dataDir = Path.Combine(exeDir, "data_sts2_windows_x86_64");
        if (Directory.Exists(dataDir)) return dataDir;
        return exeDir;
    }
}
