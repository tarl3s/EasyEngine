using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace EasyEngine;

/// <summary>An EasyPixel export waiting to be imported: its folder, name, speed, frame count and when it was written.</summary>
public sealed record HandoffInfo(string Folder, string Name, int Fps, int FrameCount, DateTime When);

/// <summary>A readable export: the speed and the existing frame files (full paths, in order).</summary>
public sealed record Handoff(string Folder, string Name, int Fps, IReadOnlyList<string> Frames);

/// <summary>
/// The bridge to EasyPixel (the pixel-art app). EasyPixel writes %LOCALAPPDATA%\EasyPixel\Exports\&lt;name&gt;-&lt;yyyyMMdd-HHmmss&gt;\
/// with frame_000.png… and easypixel-export.json ({"format":1,"name","fps","frames":[…],"width","height"});
/// the reader below is a copy of EasyPixel's (ExportHandoff.ListRecent) — no reference to it. Each app also leaves
/// its exe path in HKCU\Software\&lt;App&gt;\InstallPath so the other can find it.
/// </summary>
public static class EasyPixelBridge
{
    public const string ManifestName = "easypixel-export.json";
    public const string DownloadUrl = "https://github.com/tarl3s/EasyPixel";

    /// <summary>Tests point this at a throwaway folder; null = the real exports folder.</summary>
    public static string? ExportsRootOverride;

    public static string ExportsRoot => ExportsRootOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyPixel", "Exports");

    /// <summary>The newest exports first (at most <paramref name="max"/>); folders without a readable manifest are skipped.</summary>
    public static IReadOnlyList<HandoffInfo> ListRecent(int max = 20)
    {
        var found = new List<HandoffInfo>();
        try
        {
            var root = ExportsRoot;
            if (!Directory.Exists(root)) return found;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifest = Path.Combine(dir, ManifestName);
                if (!File.Exists(manifest)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifest), LooseJson.Options);
                    var r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array) continue;
                    found.Add(new HandoffInfo(dir, NameOf(r, dir), FpsOf(r), frames.GetArrayLength(), File.GetLastWriteTime(manifest)));
                }
                catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return found.OrderByDescending(i => i.When).Take(Math.Max(0, max)).ToList();
    }

    private static string NameOf(JsonElement r, string dir) =>
        r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString()! : Path.GetFileName(dir);

    private static int FpsOf(JsonElement r) =>
        r.TryGetProperty("fps", out var fp) && LooseJson.TryNumber(fp, out var v) && v >= 1 ? (int)Math.Min(60, Math.Round(v)) : 10;

    /// <summary>Reads one handoff folder. False + a message for the person when it is broken or has no usable frames.</summary>
    public static bool TryRead(string folder, out Handoff? handoff, out string error)
    {
        handoff = null; error = "";
        try
        {
            var manifest = Path.Combine(folder, ManifestName);
            if (!File.Exists(manifest)) { error = L.T("That folder is not an EasyPixel export (no easypixel-export.json)."); return false; }
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest), LooseJson.Options);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("frames", out var fr) || fr.ValueKind != JsonValueKind.Array)
            { error = L.T("This export can't be read. Export it again from EasyPixel."); return false; }
            var files = new List<string>();
            foreach (var f in fr.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.String) continue;
                var name = Path.GetFileName(f.GetString() ?? ""); // never follow a path out of the folder
                var full = Path.Combine(folder, name);
                if (name.Length > 0 && File.Exists(full)) files.Add(full);
            }
            if (files.Count == 0) { error = L.T("This export has no pictures. Export it again from EasyPixel."); return false; }
            handoff = new Handoff(folder, NameOf(r, folder), FpsOf(r), files);
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = L.T("This export can't be read. Export it again from EasyPixel.");
            return false;
        }
    }

    /// <summary>Copies the frames into the project's assets (unique names) and returns their project-relative paths.</summary>
    public static List<string> ImportFrames(Project project, Handoff h)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "EE_EasyPixel_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var safe = new string(h.Name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (safe.Length == 0) safe = "sprite";
            var rel = new List<string>();
            for (int i = 0; i < h.Frames.Count; i++)
            {
                var named = Path.Combine(tmp, safe + "_" + i.ToString("000", CultureInfo.InvariantCulture) + ".png");
                File.Copy(h.Frames[i], named, true);
                rel.Add(ProjectIO.ImportAsset(project, named, "sprites"));
            }
            return rel;
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>Adds the export's frames to <paramref name="current"/>, or makes a new animation named
    /// <paramref name="newName"/> with the export's speed when there is none. Returns the animation.</summary>
    public static SpriteAnimation Apply(Project project, GameObjectData obj, SpriteAnimation? current, Handoff h, string newName)
    {
        var frames = ImportFrames(project, h);
        if (current != null) { current.Frames.AddRange(frames); return current; }
        var anim = new SpriteAnimation { Name = newName, Fps = h.Fps };
        anim.Frames.AddRange(frames);
        obj.Animations.Add(anim);
        if (obj.DefaultAnimation == null) obj.DefaultAnimation = anim.Name;
        return anim;
    }

    // ---------------- discovery (the only place that touches the registry) ----------------

    /// <summary>EasyPixel's exe, or null when it is not installed (never throws).</summary>
    public static string? FindEasyPixel()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\EasyPixel");
            var path = k?.GetValue("InstallPath") as string;
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    /// <summary>Leaves this exe's path where EasyPixel looks for it. Best effort.</summary>
    public static void RegisterEasyEngine()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\EasyEngine");
            k?.SetValue("InstallPath", exe);
        }
        catch { }
    }
}
