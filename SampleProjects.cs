using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine
{
    /// <summary>
    /// The projects that come with the engine, ready to play and pick apart. They are built from code (no files to ship)
    /// into Documents\EasyEngine Samples and listed under Recent projects: the first time the engine opens, and again with
    /// every new version (so they improve with it — one the person removed comes back). A sample the person edited is
    /// never lost: it is kept as "&lt;name&gt; (your copy …)" before the new one is built. Between versions, a sample
    /// added to <see cref="All"/> is built on the next launch. The owner decides which projects are here.
    /// Not done when running from the source code: the owner's own demo copies stay as they are.
    /// </summary>
    public static class SampleProjects
    {
        public static readonly (string name, Func<string, string> build)[] All =
        {
            (TopDownGame.ProjectName, TopDownGame.Build),
            (BossDemo.ProjectName, BossDemo.Build),
            (InfiniteWorld.ProjectName, InfiniteWorld.Build),
        };

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EasyEngine Samples");

        private const string Marker = ".easyengine-sample"; // written when a sample is built; anything newer = the person edited it

        /// <summary>Builds the samples this computer doesn't have for this version. Returns how many were built/added.</summary>
        public static int EnsureInstalled()
        {
            if (ProjectIO.SuppressRecent || Updater.IsDevBuild) return 0; // tests, and the owner's source build, keep their own copies
            AiSettings.Load();
            var version = Updater.Current.ToString(3);
            bool newVersion = AiSettings.SamplesVersion != version;
            if (newVersion) AiSettings.SamplesInstalled.Clear(); // every sample again, at this version
            int added = Install(Folder, AiSettings.SamplesInstalled, rebuild: newVersion, file => ProjectIO.Load(file)); // Load lists it in Recent
            AiSettings.SamplesVersion = version;
            AiSettings.Save();
            return added;
        }

        /// <summary>The work of <see cref="EnsureInstalled"/>, with what it touches passed in (so tests can run it): each
        /// sample not in <paramref name="done"/> is built into <paramref name="folder"/> (with <paramref name="rebuild"/>,
        /// replacing an older copy; else an existing copy is kept) and opened.</summary>
        public static int Install(string folder, List<string> done, bool rebuild, Action<string> open)
        {
            int added = 0;
            foreach (var (name, build) in All)
            {
                if (done.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                done.Add(name);
                try
                {
                    Directory.CreateDirectory(folder);
                    var dir = Path.Combine(folder, name);
                    string? file = null;
                    if (Directory.Exists(dir))
                    {
                        if (!rebuild) file = Directory.GetFiles(dir, "*" + ProjectIO.Extension).FirstOrDefault();
                        else if (Edited(dir))
                        {
                            var copy = Path.Combine(folder, $"{name} (your copy {DateTime.Now:yyyy-MM-dd HHmm})");
                            Directory.Move(dir, copy);
                            if (Directory.GetFiles(copy, "*" + ProjectIO.Extension).FirstOrDefault() is string mine) open(mine);
                        }
                        else Directory.Delete(dir, true);
                    }
                    if (file == null)
                    {
                        file = build(folder);
                        File.WriteAllText(Path.Combine(Path.GetDirectoryName(file)!, Marker), Updater.Current.ToString(3));
                    }
                    open(file);
                    added++;
                }
                catch { done.Remove(name); } // try again next time
            }
            return added;
        }

        // Did the person change the sample after it was built? (Playing it only writes save.json — that doesn't count.)
        private static bool Edited(string dir)
        {
            var marker = new FileInfo(Path.Combine(dir, Marker));
            if (!marker.Exists) return true; // not built by us: keep it to be safe
            var built = marker.LastWriteTimeUtc.AddSeconds(2);
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                .Any(f => f.Name != Marker && !f.Name.Equals("save.json", StringComparison.OrdinalIgnoreCase) && f.LastWriteTimeUtc > built);
        }
    }
}
