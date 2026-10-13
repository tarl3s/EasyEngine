using System;
using System.IO;
using System.Linq;

namespace EasyEngine;

// The project's hidden backup of its pictures and sounds (AssetBackup.cs, ⚙ Settings → Backup).
public partial class App
{
    partial void FeatureTestsBackup(FullTestKit t)
    {
        t.Test("backup: imported files get a hidden copy; a used file deleted by mistake comes back, an unused one stays gone; off = nothing", () =>
        {
            bool was = AiSettings.AssetBackup;
            var temp = Path.Combine(Path.GetTempPath(), "EE_Full_Backup_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                AiSettings.AssetBackup = true;
                var p = ProjectIO.CreateNew("Backup", temp);
                var src = Path.Combine(temp, "slime.png"); File.WriteAllBytes(src, new byte[] { 1, 2, 3 });
                var src2 = Path.Combine(temp, "old.wav"); File.WriteAllBytes(src2, new byte[] { 4, 5 });
                var used = ProjectIO.ImportAsset(p, src, "sprites");
                var unused = ProjectIO.ImportAsset(p, src2, "sounds");
                var root = Path.Combine(p.FolderPath!, AssetBackup.FolderName);
                bool copied = File.Exists(Path.Combine(root, used)) && new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.Hidden);

                File.WriteAllText(p.FilePath!, "{\"sprite\":\"" + used.Replace("\\", "\\\\") + "\"}"); // the project uses only the slime
                File.Delete(Path.Combine(p.FolderPath!, used)); File.Delete(Path.Combine(p.FolderPath!, unused)); File.Delete(src);
                var back = AssetBackup.Sync(p);
                bool restored = back.SequenceEqual(new[] { used }) && File.ReadAllBytes(Path.Combine(p.FolderPath!, used)).Length == 3
                                && !File.Exists(Path.Combine(p.FolderPath!, unused)) && !File.Exists(Path.Combine(root, unused));

                AiSettings.AssetBackup = false;
                File.Delete(Path.Combine(p.FolderPath!, used));
                bool off = AssetBackup.Sync(p).Count == 0 && !File.Exists(Path.Combine(p.FolderPath!, used));
                return copied && restored && off;
            }
            finally { AiSettings.AssetBackup = was; try { Directory.Delete(temp, true); } catch { } }
        });
    }
}
