using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyEngine
{
    /// <summary>
    /// "Clear temporary files": removes only what EasyEngine itself leaves in the Windows temp folder — logs
    /// (easyengine_*.log), test pictures (easyengine_snap_*.png), throwaway test projects (EE_SelfTest_…, EE_Full_…) and
    /// finished update / publish downloads. Never touches settings (%APPDATA%\EasyEngine), projects, samples, exported
    /// games or anything another program made. Files in use are skipped.
    /// </summary>
    public static class CacheCleaner
    {
        // folders the engine's tests and tools create in %TEMP% (always "<prefix>_<random>")
        private static readonly Regex OurDir = new(@"^(EE_[A-Za-z]+_[A-Za-z0-9_]+|EasyEngine_(update|publish))$", RegexOptions.CultureInvariant);
        private static readonly Regex OurFile = new(@"^easyengine_[A-Za-z0-9_\-]+\.(log|png|txt)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>What would be removed from <paramref name="tempDir"/> (default: the Windows temp folder).</summary>
        public static List<string> Targets(string? tempDir = null)
        {
            var temp = tempDir ?? Path.GetTempPath();
            var list = new List<string>();
            try
            {
                list.AddRange(Directory.GetFiles(temp).Where(f => OurFile.IsMatch(Path.GetFileName(f))));
                list.AddRange(Directory.GetDirectories(temp).Where(d => OurDir.IsMatch(Path.GetFileName(d)) && !IsBusyUpdate(d)));
            }
            catch { }
            return list;
        }

        /// <summary>Bytes in <paramref name="paths"/> (files and folders).</summary>
        public static long Size(IEnumerable<string> paths)
        {
            long total = 0;
            foreach (var p in paths)
                try
                {
                    if (File.Exists(p)) total += new FileInfo(p).Length;
                    else if (Directory.Exists(p)) total += new DirectoryInfo(p).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                }
                catch { }
            return total;
        }

        /// <summary>Removes the targets; returns (items removed, bytes freed). Anything in use stays.</summary>
        public static (int Count, long Bytes) Clean(string? tempDir = null)
        {
            int count = 0; long bytes = 0;
            foreach (var p in Targets(tempDir))
            {
                var size = Size(new[] { p });
                try
                {
                    if (File.Exists(p)) File.Delete(p); else Directory.Delete(p, true);
                    count++; bytes += size;
                }
                catch { } // in use (an open log, a running test): leave it
            }
            return (count, bytes);
        }

        /// <summary>An update that is being installed right now (its swap script still running) must stay.</summary>
        private static bool IsBusyUpdate(string dir)
        {
            if (!Path.GetFileName(dir).Equals("EasyEngine_update", StringComparison.OrdinalIgnoreCase)) return false;
            var log = Path.Combine(Path.GetTempPath(), "easyengine_update.log");
            try { return File.Exists(log) && (DateTime.Now - File.GetLastWriteTime(log)).TotalMinutes < 5; } catch { return false; }
        }

        /// <summary>"12.3 MB" style size.</summary>
        public static string Pretty(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
        };
    }
}
