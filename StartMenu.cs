using System;
using System.IO;

namespace EasyEngine
{
    /// <summary>A Start menu shortcut, so the engine can be found by typing its name in Windows search (a downloaded zip
    /// has none). Made once per computer when the launcher opens; a shortcut the person deleted isn't brought back, and one
    /// whose target was moved away is pointed at this copy. Never for the owner's source build or the tests.</summary>
    public static class StartMenu
    {
        public static string Folder => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        public const string Name = "EasyEngine.lnk";

        public static void EnsureShortcut()
        {
            if (ProjectIO.SuppressRecent || Updater.IsDevBuild) return;
            AiSettings.Load();
            var link = Path.Combine(Folder, Name);
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            bool exists = File.Exists(link);
            if (AiSettings.StartMenuShortcut && !exists) return;                 // they removed it: leave it gone
            if (exists && File.Exists(TargetOf(link) ?? "")) { Remember(); return; } // already there and working
            if (Make(link, exe)) Remember();
        }

        private static void Remember() { if (!AiSettings.StartMenuShortcut) { AiSettings.StartMenuShortcut = true; AiSettings.Save(); } }

        /// <summary>Writes a shortcut to <paramref name="exe"/> (its folder as the working folder, its icon). False when it can't.</summary>
        internal static bool Make(string link, string exe)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(link)!);
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return false;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic sc = shell.CreateShortcut(link);
                sc.TargetPath = exe;
                sc.WorkingDirectory = Path.GetDirectoryName(exe);
                sc.IconLocation = exe + ",0";
                sc.Description = "EasyEngine — the easiest 2D game engine";
                sc.Save();
                return File.Exists(link);
            }
            catch { return false; }
        }

        internal static string? TargetOf(string link)
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                dynamic shell = Activator.CreateInstance(type)!;
                return (string)shell.CreateShortcut(link).TargetPath;
            }
            catch { return null; }
        }
    }
}
