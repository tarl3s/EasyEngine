using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using BE = BuilderEngine.Script; // never "using BuilderEngine.Script;": it has its own Project, Scene, World, TileKind…

namespace EasyEngine
{
    /// <summary>
    /// The EasyEngine side of the EasyEngine ⇄ BuilderEngine link. The contract is BuilderEngine's
    /// docs/EASYENGINE-LINK.md; the one converter is BuilderEngine's <c>EasyLink</c> (lib\BuilderEngine\BuilderEngine.Script.dll,
    /// updated by tools/Update-BuilderEngineLib.ps1) — nothing here maps fields itself.
    /// <list type="bullet">
    /// <item>BuilderEngine's data rides in a <c>"Builder"</c> property of the project, scenes and objects, kept by the model's
    /// <c>Extra</c> ([JsonExtensionData]) so an EasyEngine save never loses it.</item>
    /// <item>An object with <c>Builder.mechanisms</c> is "Built on BuilderEngine": the inspector shows only its common fields,
    /// the AI and the behavior pickers leave it alone (MainWindow.BuilderLink.cs) and its mechanisms run when the game plays
    /// (PlayWindow.BuilderBridge.cs).</item>
    /// <item>A BuilderEngine project folder (<c>project.json</c>) opens as an EasyEngine project and saves back into the folder.</item>
    /// </list>
    /// </summary>
    public static class BuilderLink
    {
        public const string Key = BE.EasyLink.BuilderKey; // "Builder"
        public const string ProjectJson = "project.json";  // BuilderEngine's project file

        /// <summary>One mechanism on an object: its name and its params as BuilderEngine saved them.</summary>
        public readonly record struct Mechanism(string Name, JsonElement Params);

        private static bool Block(Dictionary<string, JsonElement>? extra, out JsonElement b)
        {
            b = default;
            return extra != null && extra.TryGetValue(Key, out b) && b.ValueKind == JsonValueKind.Object;
        }

        /// <summary>Built on BuilderEngine: the object has at least one BuilderEngine mechanism. (No allocation.)</summary>
        public static bool IsBuilt(GameObjectData? o) =>
            o != null && Block(o.Extra, out var b) && b.TryGetProperty("mechanisms", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0;

        /// <summary>The object's mechanisms, in order (empty when it has none).</summary>
        public static List<Mechanism> Mechanisms(GameObjectData o)
        {
            var list = new List<Mechanism>();
            if (!Block(o.Extra, out var b) || !b.TryGetProperty("mechanisms", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var m in arr.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                string name = m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
                if (name.Length == 0) continue;
                list.Add(new Mechanism(name, m.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object ? p : default));
            }
            return list;
        }

        public static List<string> MechanismNames(GameObjectData o) => Mechanisms(o).Select(m => m.Name).ToList();

        /// <summary>The object's BuilderEngine tags (<c>Builder.tags</c>).</summary>
        public static List<string> Tags(GameObjectData o)
        {
            var list = new List<string>();
            if (Block(o.Extra, out var b) && b.TryGetProperty("tags", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var t in arr.EnumerateArray())
                    if (t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } s) list.Add(s);
            return list;
        }

        /// <summary>The scene has objects built on BuilderEngine (the game then runs a BuilderEngine world beside it).</summary>
        public static bool HasBuilt(Scene s)
        {
            foreach (var l in s.Layers) foreach (var o in l.Objects) if (IsBuilt(o)) return true;
            return false;
        }

        /// <summary>The project is shared with BuilderEngine: a BuilderEngine folder, or a .eeproj BuilderEngine has saved into.</summary>
        public static bool IsLinked(Project p) => p.IsBuilderEngine || Block(p.Extra, out _);

        /// <summary>The project's own mechanisms (<c>Builder.mechanisms</c> of the project: name → Builder Script source).</summary>
        public static Dictionary<string, string> ProjectMechanisms(Project p)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Block(p.Extra, out var b) && b.TryGetProperty("mechanisms", out var m) && m.ValueKind == JsonValueKind.Object)
                foreach (var kv in m.EnumerateObject())
                    if (kv.Value.ValueKind == JsonValueKind.String) d[kv.Name] = kv.Value.GetString()!;
            return d;
        }

        // ---------------- paths ----------------

        public static bool IsProjectJson(string? path) =>
            !string.IsNullOrEmpty(path) && Path.GetFileName(path).Equals(ProjectJson, StringComparison.OrdinalIgnoreCase);

        /// <summary>What EasyEngine opens for a path (a command-line argument, a pick in the Open dialog): a .eeproj, a
        /// BuilderEngine project.json, or a folder holding one of them (project.json first, as BuilderEngine does); null otherwise.</summary>
        public static string? OpenablePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Trim('"');
            try
            {
                if (File.Exists(path))
                {
                    var full = Path.GetFullPath(path);
                    return full.EndsWith(ProjectIO.Extension, StringComparison.OrdinalIgnoreCase) || IsProjectJson(full) ? full : null;
                }
                if (Directory.Exists(path))
                {
                    var json = Path.Combine(Path.GetFullPath(path), ProjectJson);
                    return File.Exists(json) ? json : BE.EasyLink.FindEeproj(Path.GetFullPath(path));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
            return null;
        }

        // ---------------- load / save a BuilderEngine folder ----------------

        /// <summary>A BuilderEngine project folder as .eeproj text (EasyLink.ToEeprojFromFolder).</summary>
        public static string ReadFolder(string projectJsonPath) => BE.EasyLink.ToEeprojFromFolder(Path.GetDirectoryName(Path.GetFullPath(projectJsonPath))!);

        /// <summary>Saves .eeproj text into a BuilderEngine folder: project.json (atomically) + its mechanisms (EasyLink.SaveFromEeproj).</summary>
        public static void SaveFolder(string eeprojJson, string projectJsonPath) => BE.EasyLink.SaveFromEeproj(eeprojJson, Path.GetDirectoryName(Path.GetFullPath(projectJsonPath))!);

        /// <summary>Renames a BuilderEngine project (inside its project.json only: the folder keeps its name).</summary>
        public static void RenameFolderProject(string projectJsonPath, string newName)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(projectJsonPath))!;
            var p = BE.Project.Load(dir);
            p.Name = newName;
            p.Save(dir);
        }

        // ---------------- Open in BuilderEngine ----------------

        /// <summary>BuilderEngine on this computer: the BUILDERENGINE_EXE variable, its Start menu shortcut, or
        /// %LOCALAPPDATA%\Programs\BuilderEngine\BuilderEngine.exe; null when it isn't found.</summary>
        public static string? FindBuilderExe()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("BUILDERENGINE_EXE") is { Length: > 0 } env && File.Exists(env)) return env;
                foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
                    foreach (var link in new[] { Path.Combine(root, "BuilderEngine.lnk"), Path.Combine(root, "BuilderEngine", "BuilderEngine.lnk") })
                        if (File.Exists(link) && StartMenu.TargetOf(link) is { Length: > 0 } target && File.Exists(target)) return target;
                var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BuilderEngine", "BuilderEngine.exe");
                if (File.Exists(local)) return local;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
            return null;
        }

        /// <summary>Starts BuilderEngine on this project (<c>--project &lt;folder&gt;</c>). False when BuilderEngine isn't found.</summary>
        public static bool OpenInBuilder(Project p)
        {
            if (FindBuilderExe() is not string exe || string.IsNullOrEmpty(p.FilePath)) return false;
            var target = p.IsBuilderEngine ? Path.GetDirectoryName(p.FilePath)! : p.FilePath;
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
                psi.ArgumentList.Add("--project");
                psi.ArgumentList.Add(target);
                Process.Start(psi);
                return true;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
        }
    }
}
