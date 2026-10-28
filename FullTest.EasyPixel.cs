using System;
using System.IO;
using System.Linq;

namespace EasyEngine;

// "Import from EasyPixel" (EasyPixelBridge.cs): the handoff folders EasyPixel writes, read tolerantly and turned into frames.
public partial class App
{
    private static string MakeHandoff(string root, string folderName, string name, int fps, int frames, DateTime when, string? rawManifest = null)
    {
        var dir = Path.Combine(root, folderName);
        Directory.CreateDirectory(dir);
        var names = Enumerable.Range(0, frames).Select(i => "frame_" + i.ToString("000") + ".png").ToList();
        foreach (var n in names) File.WriteAllBytes(Path.Combine(dir, n), new byte[] { 137, 80, 78, 71, (byte)n.Length });
        var manifest = Path.Combine(dir, EasyPixelBridge.ManifestName);
        File.WriteAllText(manifest, rawManifest ??
            "{\"format\":1,\"name\":\"" + name + "\",\"fps\":" + fps + ",\"frames\":[" + string.Join(",", names.Select(n => "\"" + n + "\"")) + "],\"width\":32,\"height\":32}");
        File.SetLastWriteTime(manifest, when);
        return dir;
    }

    partial void FeatureTestsEasyPixel(FullTestKit t)
    {
        t.Test("EasyPixelImport_CreatesAnimationWithFps: a handoff becomes a new animation with its speed; newest handoff is listed first", () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_Full_EasyPixel_" + Guid.NewGuid().ToString("N")[..6]);
            var was = EasyPixelBridge.ExportsRootOverride;
            try
            {
                var root = Path.Combine(temp, "Exports");
                MakeHandoff(root, "old-20260101-000000", "old", 5, 2, new DateTime(2026, 1, 1));
                var newest = MakeHandoff(root, "walk-20260102-000000", "walk", 12, 3, new DateTime(2026, 1, 2));
                EasyPixelBridge.ExportsRootOverride = root;
                var list = EasyPixelBridge.ListRecent();
                bool order = list.Count == 2 && list[0].Name == "walk" && list[0].Fps == 12 && list[0].FrameCount == 3 && list[1].Name == "old";

                var p = ProjectIO.CreateNew("EP", temp);
                var obj = new GameObjectData { Name = "Hero" };
                bool ok = EasyPixelBridge.TryRead(newest, out var h, out _);
                var anim = EasyPixelBridge.Apply(p, obj, null, h!, "walk");
                bool made = ok && anim.Name == "walk" && Math.Abs(anim.Fps - 12) < 0.01 && anim.Frames.Count == 3
                            && obj.Animations.Count == 1 && obj.DefaultAnimation == "walk"
                            && anim.Frames.All(f => File.Exists(ProjectIO.AbsoluteAsset(p, f)))
                            && anim.Frames.Distinct().Count() == 3;
                return order && made;
            }
            finally { EasyPixelBridge.ExportsRootOverride = was; try { Directory.Delete(temp, true); } catch { } }
        });

        t.Test("EasyPixelImport_AddsToExisting: frames go onto the selected animation (speed kept); importing twice gets unique files", () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_Full_EasyPixel_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                var dir = MakeHandoff(Path.Combine(temp, "Exports"), "run-20260102-000000", "run", 8, 2, new DateTime(2026, 1, 2));
                var p = ProjectIO.CreateNew("EP", temp);
                var obj = new GameObjectData { Name = "Hero" };
                var cur = new SpriteAnimation { Name = "idle", Fps = 4 };
                cur.Frames.Add("Assets/sprites/existing.png");
                obj.Animations.Add(cur);
                EasyPixelBridge.TryRead(dir, out var h, out _);
                var same = EasyPixelBridge.Apply(p, obj, cur, h!, "run");
                bool added = ReferenceEquals(same, cur) && cur.Frames.Count == 3 && cur.Frames[0] == "Assets/sprites/existing.png"
                             && Math.Abs(cur.Fps - 4) < 0.01 && obj.Animations.Count == 1;
                // a second import of a DIFFERENT drawing with the same frame names must not overwrite the first one's files
                File.WriteAllBytes(Path.Combine(dir, "frame_000.png"), new byte[] { 9, 9, 9 });
                EasyPixelBridge.TryRead(dir, out var h2, out _);
                var again = EasyPixelBridge.Apply(p, obj, cur, h2!, "run");
                bool unique = cur.Frames.Skip(1).Distinct().Count() == 3 && cur.Frames.Count == 5
                              && File.ReadAllBytes(ProjectIO.AbsoluteAsset(p, cur.Frames[1])).Length == 5
                              && File.ReadAllBytes(ProjectIO.AbsoluteAsset(p, cur.Frames[3])).Length == 3;
                return added && unique;
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        });

        t.Test("EasyPixelImport_BrokenJson_Message: broken, empty and missing manifests give a message (never throw) and are not listed", () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_Full_EasyPixel_" + Guid.NewGuid().ToString("N")[..6]);
            var was = EasyPixelBridge.ExportsRootOverride;
            try
            {
                var root = Path.Combine(temp, "Exports");
                var broken = MakeHandoff(root, "bad-20260103-000000", "bad", 10, 1, DateTime.Now, "{ this is not json");
                var noFrames = MakeHandoff(root, "empty-20260103-000001", "empty", 10, 0, DateTime.Now, "{\"name\":\"empty\",\"frames\":[]}");
                var missing = MakeHandoff(root, "gone-20260103-000002", "gone", 10, 0, DateTime.Now, "{\"name\":\"gone\",\"frames\":[\"frame_000.png\"]}");
                var noManifest = Path.Combine(root, "plain"); Directory.CreateDirectory(noManifest);
                var tolerant = MakeHandoff(root, "ok-20260103-000003", "ok", 10, 1, DateTime.Now,
                    "{ // comment\n \"name\":\"ok\", \"fps\":\"6\", \"frames\":[\"frame_000.png\",], }");
                EasyPixelBridge.ExportsRootOverride = root;
                var list = EasyPixelBridge.ListRecent();
                bool listed = list.Any(i => i.Name == "ok" && i.Fps == 6) && !list.Any(i => i.Name == "bad") && list.All(i => i.Name != "plain");
                bool b1 = !EasyPixelBridge.TryRead(broken, out var h1, out var m1) && h1 == null && m1.Length > 0;
                bool b2 = !EasyPixelBridge.TryRead(noFrames, out _, out var m2) && m2.Length > 0;
                bool b3 = !EasyPixelBridge.TryRead(missing, out _, out var m3) && m3.Length > 0;
                bool b4 = !EasyPixelBridge.TryRead(noManifest, out _, out var m4) && m4.Length > 0;
                bool b5 = EasyPixelBridge.TryRead(tolerant, out var h5, out _) && h5!.Fps == 6 && h5.Frames.Count == 1;
                EasyPixelBridge.ExportsRootOverride = Path.Combine(temp, "nothing");
                bool none = EasyPixelBridge.ListRecent().Count == 0;
                return listed && b1 && b2 && b3 && b4 && b5 && none;
            }
            finally { EasyPixelBridge.ExportsRootOverride = was; try { Directory.Delete(temp, true); } catch { } }
        });
    }
}
