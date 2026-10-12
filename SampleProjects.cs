using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine
{
    /// <summary>
    /// The projects that come with the engine, ready to play and pick apart. They are built from code (no files to ship)
    /// the first time the launcher opens on a computer — and a sample added to <see cref="All"/> later is built the next
    /// time — into Documents\EasyEngine Samples, and listed under Recent projects. Each one is put there only once:
    /// a sample the person removed from the list doesn't come back. The owner decides which projects are here.
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

        /// <summary>Builds the samples this computer doesn't have yet. Returns how many were added.</summary>
        public static int EnsureInstalled()
        {
            if (ProjectIO.SuppressRecent) return 0; // test runs never touch the person's projects
            AiSettings.Load();
            int added = Install(Folder, AiSettings.SamplesInstalled, ProjectIO.GetRecent().Select(r => r.Name).ToList(),
                                file => ProjectIO.Load(file)); // Load puts it in Recent projects
            AiSettings.Save();
            return added;
        }

        /// <summary>The work of <see cref="EnsureInstalled"/>, with everything it touches passed in (so tests can run it):
        /// builds each sample not in <paramref name="done"/> into <paramref name="folder"/> and opens it.</summary>
        public static int Install(string folder, List<string> done, List<string> recentNames, Action<string> open)
        {
            int added = 0;
            foreach (var (name, build) in All)
            {
                if (done.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                done.Add(name);
                // already on this computer (e.g. built with --bossdemo): just remember it
                if (recentNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                try
                {
                    Directory.CreateDirectory(folder);
                    var existing = Path.Combine(folder, name);
                    var file = Directory.Exists(existing) ? Directory.GetFiles(existing, "*" + ProjectIO.Extension).FirstOrDefault() : null;
                    open(file ?? build(folder));
                    added++;
                }
                catch { done.Remove(name); } // try again next time
            }
            return added;
        }
    }
}
