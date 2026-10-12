using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// The sample projects that come with the engine (SampleProjects.cs) and the GitHub updater (Updater.cs).
public partial class App
{
    partial void FeatureTestsSamples(FullTestKit t)
    {
        string TempDir(string tag) => Path.Combine(Path.GetTempPath(), $"EE_Full_{tag}_" + Guid.NewGuid().ToString("N").Substring(0, 6));

        // ---------------- samples ----------------
        t.Test("samples: a new computer gets every sample built and opened; the same version adds nothing more", () =>
        {
            var folder = TempDir("Samples");
            var done = new List<string>(); var opened = new List<string>();
            int first = SampleProjects.Install(folder, done, true, opened.Add);
            int second = SampleProjects.Install(folder, done, false, opened.Add);
            bool ok = first == SampleProjects.All.Length && second == 0 && opened.Count == first && opened.All(File.Exists);
            TestScenarios.TryDeleteDir(folder);
            return ok;
        });
        t.Test("samples: a new version rebuilds them (a removed one comes back); an edited one is kept as 'your copy'", () =>
        {
            var folder = TempDir("Samples");
            SampleProjects.Install(folder, new List<string>(), true, _ => { });
            var boss = Path.Combine(folder, BossDemo.ProjectName);
            var top = Path.Combine(folder, TopDownGame.ProjectName);
            var edited = Directory.GetFiles(boss, "*" + ProjectIO.Extension).First();
            File.SetLastWriteTimeUtc(edited, DateTime.UtcNow.AddMinutes(5));         // the person changed the Boss Demo
            File.WriteAllText(Path.Combine(top, "save.json"), "{}");                  // and only played the Top-Down Demo
            File.SetLastWriteTimeUtc(Path.Combine(top, "save.json"), DateTime.UtcNow.AddMinutes(5));
            TestScenarios.TryDeleteDir(Path.Combine(folder, InfiniteWorld.ProjectName)); // and deleted Infinite World
            var opened = new List<string>();
            int n = SampleProjects.Install(folder, new List<string>(), true, opened.Add);  // the next version
            var copies = Directory.GetDirectories(folder, BossDemo.ProjectName + " (your copy *").ToList();
            bool ok = n == SampleProjects.All.Length && copies.Count == 1 && Directory.Exists(boss)
                      && Directory.Exists(Path.Combine(folder, InfiniteWorld.ProjectName))
                      && Directory.GetDirectories(folder, TopDownGame.ProjectName + " (your copy*").Length == 0
                      && opened.Any(f => f.StartsWith(copies[0]));
            TestScenarios.TryDeleteDir(folder);
            return ok;
        });

        // ---------------- updater ----------------
        t.Test("updater: reads GitHub's latest release (version from the tag, the engine zip's URL)", () =>
        {
            var json = "{\"tag_name\":\"v0.12.3\",\"body\":\"New stuff\",\"assets\":[{\"name\":\"other.zip\",\"browser_download_url\":\"x\"}," +
                       "{\"name\":\"" + Updater.AssetName + "\",\"browser_download_url\":\"https://example.com/e.zip\"}]}";
            var r = Updater.ParseLatest(json);
            var noZip = Updater.ParseLatest("{\"tag_name\":\"v0.12.3\",\"assets\":[]}");
            return r != null && r.Version == new Version(0, 12, 3) && r.ZipUrl == "https://example.com/e.zip" && r.Notes == "New stuff"
                   && noZip == null && Updater.ParseLatest("not json") == null;
        });
        t.Test("updater: versions compare by number (v0.10.10 > v0.10.9 > 0.9) and 'v' is optional", () =>
            Updater.ParseVersion("v0.10.10") > Updater.ParseVersion("0.10.9") && Updater.ParseVersion("0.10.9") > Updater.ParseVersion("v0.9")
            && Updater.ParseVersion("v1.2") == new Version(1, 2, 0) && Updater.ParseVersion("latest") == null);
        t.Test("updater: the swap removes only the old version's files, copies the new ones and keeps the person's files", () =>
        {
            var install = TempDir("Install"); var fresh = TempDir("New");
            Directory.CreateDirectory(Path.Combine(install, "runtimes", "old"));
            File.WriteAllText(Path.Combine(install, "EasyEngine.exe"), "old");
            File.WriteAllText(Path.Combine(install, "Old.dll"), "old");
            File.WriteAllText(Path.Combine(install, "runtimes", "old", "x.dll"), "old");
            File.WriteAllText(Path.Combine(install, "my notes.txt"), "mine");                 // not in the manifest
            File.WriteAllText(Path.Combine(install, Updater.ManifestName), "EasyEngine.exe\nOld.dll\nruntimes\\old\\x.dll\n" + Updater.ManifestName);
            Directory.CreateDirectory(fresh);
            File.WriteAllText(Path.Combine(fresh, "EasyEngine.exe"), "new");
            File.WriteAllText(Path.Combine(fresh, "New.dll"), "new");
            File.WriteAllText(Path.Combine(fresh, Updater.ManifestName), "EasyEngine.exe\nNew.dll\n" + Updater.ManifestName);
            var p = Updater.StartSwap(fresh, install, -1, start: false);
            bool exited = p.WaitForExit(30000);
            bool ok = exited && File.ReadAllText(Path.Combine(install, "EasyEngine.exe")) == "new" && File.Exists(Path.Combine(install, "New.dll"))
                      && !File.Exists(Path.Combine(install, "Old.dll")) && !Directory.Exists(Path.Combine(install, "runtimes"))
                      && File.ReadAllText(Path.Combine(install, "my notes.txt")) == "mine";
            TestScenarios.TryDeleteDir(install); TestScenarios.TryDeleteDir(fresh);
            return ok;
        });
    }
}
