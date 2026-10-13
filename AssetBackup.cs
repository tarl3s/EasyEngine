using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine
{
    /// <summary>
    /// A hidden copy of every picture and sound of a project (⚙ Settings → Backup, on by default), in a ".backup" folder
    /// inside the project — so it travels with it and an exported game leaves it out. Every file the engine writes into
    /// Assets is copied there; when the editor opens a project, a file the project still uses that went missing from
    /// Assets comes back, and files with no copy yet get one (older projects too). A copy of a file that is gone AND no
    /// longer used is dropped: what the person deleted on purpose stays deleted.
    /// </summary>
    public static class AssetBackup
    {
        public const string FolderName = ".backup";

        private static string? Root(Project p) => string.IsNullOrEmpty(p.FolderPath) ? null : Path.Combine(p.FolderPath, FolderName);

        /// <summary>Copies one project asset (a project-relative path like "Assets\sprites\slime.png") into the backup.</summary>
        public static void Keep(Project p, string relative)
        {
            if (!AiSettings.AssetBackup || Root(p) is not string root || string.IsNullOrEmpty(relative)) return;
            try
            {
                var src = Path.Combine(p.FolderPath!, relative);
                if (!File.Exists(src)) return;
                EnsureRoot(root);
                var dst = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            catch (Exception) { } // a backup must never stop the work it protects
        }

        /// <summary>When the editor opens a project: brings back missing files the project uses, backs up the rest.
        /// Returns the project-relative paths it restored.</summary>
        public static List<string> Sync(Project p)
        {
            var restored = new List<string>();
            if (!AiSettings.AssetBackup || Root(p) is not string root) return restored;
            try
            {
                var assets = Path.Combine(p.FolderPath!, "Assets");
                string used = p.FilePath != null && File.Exists(p.FilePath) ? File.ReadAllText(p.FilePath) : "";
                bool Uses(string rel) => used.Contains(Path.GetFileName(rel), StringComparison.OrdinalIgnoreCase); // by name: paths are escaped differently in tile palettes

                if (Directory.Exists(root))
                    foreach (var copy in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList())
                    {
                        var rel = Path.GetRelativePath(root, copy);
                        var real = Path.Combine(p.FolderPath!, rel);
                        if (File.Exists(real)) continue;
                        try
                        {
                            if (Uses(rel)) { Directory.CreateDirectory(Path.GetDirectoryName(real)!); File.Copy(copy, real); restored.Add(rel); }
                            else File.Delete(copy); // deleted on purpose and no longer used
                        }
                        catch (Exception) { }
                    }

                if (Directory.Exists(assets))
                    foreach (var file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(p.FolderPath!, file);
                        var dst = Path.Combine(root, rel);
                        try
                        {
                            if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(file).Length) continue;
                            EnsureRoot(root);
                            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                            File.Copy(file, dst, overwrite: true);
                        }
                        catch (Exception) { }
                    }
            }
            catch (Exception) { }
            return restored;
        }

        // the folder, hidden in Windows Explorer so nobody tidies it away by accident
        private static void EnsureRoot(string root)
        {
            if (Directory.Exists(root)) return;
            var d = Directory.CreateDirectory(root);
            try { d.Attributes |= FileAttributes.Hidden; } catch (Exception) { }
        }
    }
}
