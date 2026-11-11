using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BE = BuilderEngine.Script;

namespace EasyEngine
{
    /// <summary>
    /// Optional FMOD audio ("better sound"). FMOD isn't shipped with EasyEngine (its own licence): when fmod.dll is found
    /// (next to EasyEngine.exe, FMOD_DLL, or the FMOD Engine install folder) games play their sound files through ONE shared
    /// FMOD Core system; otherwise — or with Settings → Sound → Classic — they keep the classic Windows players exactly as
    /// before. FMOD Studio events (banks built into a project's "fmod" folder) need FMOD whatever the setting is.
    /// The wrapper itself is BuilderEngine's (BE.FmodLibrary / FmodCore / FmodStudio / FmodEventHost, in
    /// lib/BuilderEngine/BuilderEngine.Script.dll); this class is EasyEngine's side: the setting, the shared core, the
    /// status lines, the pickers' event lists, the Sound behavior's parameter list and the export option.
    /// </summary>
    public static class GameAudio
    {
        // ---------------------------------------------------------------- the setting
        /// <summary>auto / fmod / classic, from settings.json (anything else = auto).</summary>
        public static BE.AudioBackend Backend => BE.FmodLibrary.ParseBackend(AiSettings.AudioBackend);

        /// <summary>Whether games should try FMOD for their sound files with this setting (and FMOD not turned off).</summary>
        public static bool WantsFmod => BE.FmodLibrary.WantsFmod(Backend);

        // ---------------------------------------------------------------- the shared FMOD Core
        private static BE.FmodCore? _core;
        private static bool _tried;
        private static string _why = "";

        /// <summary>The shared FMOD Core for a game or a preview starting now: null = classic sound (the setting says so, FMOD
        /// isn't installed or couldn't start — the reason is in <see cref="Why"/>). Made once, the first time it is wanted.</summary>
        public static BE.FmodCore? Core
        {
            get
            {
                if (!WantsFmod) { _why = BE.FmodLibrary.Disabled ? "FMOD is turned off (FMOD_DLL=none)." : "Classic sound chosen in Settings."; return null; }
                if (_core != null) return _core;
                if (_tried) return null;
                _tried = true;
                InstallLog();
                _core = BE.FmodCore.TryCreate(out _why);
                if (_core != null) Message?.Invoke("FMOD " + _core.VersionText + " plays this game's sounds.", false);
                else if (_why.Length > 0) Message?.Invoke("Classic sound: " + _why, false);
                return _core;
            }
        }

        /// <summary>Why <see cref="Core"/> is null (English, for the console).</summary>
        public static string Why => _why;

        /// <summary>Where FMOD's own messages go: the running game's F1 console (text, is it a warning).</summary>
        public static Action<string, bool>? Message;

        private static bool _logInstalled;
        private static void InstallLog()
        {
            if (_logInstalled) return;
            _logInstalled = true;
            BE.FmodLibrary.Log = m =>
            {
                bool warn = m.Contains("not found", StringComparison.OrdinalIgnoreCase) || m.Contains("couldn't", StringComparison.OrdinalIgnoreCase)
                            || m.Contains("error", StringComparison.OrdinalIgnoreCase) || m.Contains("missing", StringComparison.OrdinalIgnoreCase)
                            || m.Contains("isn't", StringComparison.OrdinalIgnoreCase);
                Message?.Invoke(m, warn);
            };
        }

        /// <summary>Called by a game that starts: FMOD's messages go to its console from now on.</summary>
        public static void Attach(Action<string, bool> sink) { InstallLog(); Message = sink; }
        public static void Detach(Action<string, bool> sink) { if (Message == sink) Message = null; }

        /// <summary>The event host of the game playing now (Settings → Reload banks reloads it).</summary>
        public static BE.FmodEventHost? LiveHost { get; set; }

        /// <summary>Settings → "Reload banks": the pickers read the banks again, and a game playing now reloads them.</summary>
        public static void ReloadBanks()
        {
            BE.FmodStudio.ClearCache();
            try { LiveHost?.Reload(); } catch { }
        }

        /// <summary>The app closes: FMOD lets go of the sound device.</summary>
        public static void Shutdown()
        {
            try { _core?.Dispose(); } catch { }
            _core = null;
        }

        // ---------------------------------------------------------------- status lines (Settings)
        private static string? _version; // "2.03.15" once read

        /// <summary>The FMOD version installed ("2.03.15"), or null when fmod.dll isn't found or doesn't start.</summary>
        public static string? InstalledVersion()
        {
            if (BE.FmodLibrary.Disabled) return null;
            if (_core != null) return _core.VersionText;
            if (_version != null) return _version;
            if (BE.FmodLibrary.FindCore() == null) return null;
            InstallLog();
            var probe = BE.FmodCore.TryCreate(out _, silent: true); // nothing is heard: only its version is read
            if (probe == null) return null;
            _version = probe.VersionText;
            probe.Dispose();
            return _version;
        }

        /// <summary>"FMOD 2.03.15 ✓" / "Classic sound (FMOD not found)" / "Classic sound (chosen above)" — for Settings.</summary>
        public static string StatusText()
        {
            var v = InstalledVersion();
            if (Backend == BE.AudioBackend.Classic)
                return v != null ? L.F("Classic sound (chosen above) · FMOD {0} is installed", v) : L.T("Classic sound (chosen above)");
            return v != null ? L.F("FMOD {0} ✓", v) : L.T("Classic sound (FMOD not found)");
        }

        /// <summary>A project's FMOD Studio banks, for Settings and the pickers: (banks, events, a line to show — translated).</summary>
        public static (int banks, int events, string text) ProjectBanks(string? projectDir)
        {
            if (string.IsNullOrEmpty(projectDir)) return (0, 0, L.T("Open a project to see its FMOD banks."));
            var folder = BE.FmodStudio.FindBankFolder(projectDir);
            if (folder == null)
                return (0, 0, BE.FmodLibrary.LoadStudio() ? L.T("No FMOD banks in this project yet.") : L.T("FMOD Studio isn't installed on this computer."));
            int banks = 0;
            try { banks = Directory.GetFiles(folder, "*.bank").Length; } catch { }
            var events = BE.FmodStudio.ListEvents(projectDir, out var msg);
            if (!BE.FmodLibrary.StudioLoaded) return (banks, 0, L.T("FMOD Studio isn't installed on this computer."));
            var text = L.F("{0} bank(s), {1} event(s)", banks, events.Count);
            if (msg.Length > 0 && !msg.EndsWith("event(s)")) text += " · " + msg; // what went wrong (FMOD's words)
            return (banks, events.Count, text);
        }

        /// <summary>The folder a project's FMOD Studio banks are built into.</summary>
        public static string BankFolder(string projectDir) => Path.Combine(projectDir, BE.FmodStudio.FolderName);

        // ---------------------------------------------------------------- pickers
        /// <summary>The events of a project's banks (path, as the person sees it), or none with a translated reason.</summary>
        public static List<(string value, string label)> EventOptions(string? projectDir, out string why)
        {
            why = "";
            var list = new List<(string, string)>();
            if (string.IsNullOrEmpty(projectDir)) { why = L.T("Save the project first."); return list; }
            var events = BE.FmodStudio.ListEvents(projectDir, out _);
            foreach (var e in events) list.Add((e.Path, e.Short));
            if (list.Count == 0) why = ProjectBanks(projectDir).text;
            return list;
        }

        /// <summary>One event's description (its parameters, 3D or not), or null.</summary>
        public static BE.FmodEventInfo? FindEvent(string? projectDir, string path)
        {
            if (string.IsNullOrEmpty(projectDir) || string.IsNullOrWhiteSpace(path)) return null;
            var p = BE.FmodStudio.NormalizePath(path);
            foreach (var e in BE.FmodStudio.ListEvents(projectDir, out _))
                if (string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>The parameters a "set an FMOD parameter" block can pick: every parameter of the project's events (and the
        /// global ones), each once.</summary>
        public static List<(string value, string label)> ParamOptions(string? projectDir)
        {
            var list = new List<(string, string)>();
            if (string.IsNullOrEmpty(projectDir)) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in BE.FmodStudio.ListEvents(projectDir, out _))
                foreach (var p in e.Parameters)
                    if (seen.Add(p.Name)) list.Add((p.Name, p.Global ? L.F("{0} (global)", p.Name) : p.Name));
            return list;
        }

        // ---------------------------------------------------------------- the Sound behavior's parameters
        /// <summary>Its "fmodParams": the event parameters it starts with, saved as name=value;name=value (the person only
        /// ever sees one slider per parameter). Tolerant: spaces, commas between pairs, "0,5" decimals.</summary>
        public static List<(string name, double value)> ParseParams(string? text)
        {
            var list = new List<(string, double)>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOfAny(new[] { '=', ':' });
                if (eq <= 0) continue;
                var name = part[..eq].Trim();
                if (name.Length > 0 && LooseJson.TryParse(part[(eq + 1)..].Trim(), out var v))
                {
                    list.RemoveAll(x => x.Item1.Equals(name, StringComparison.OrdinalIgnoreCase));
                    list.Add((name, v));
                }
            }
            return list;
        }

        public static string FormatParams(IEnumerable<(string name, double value)> ps) =>
            string.Join(";", ps.Where(p => p.name.Length > 0).Select(p => p.name + "=" + p.value.ToString("0.###", CultureInfo.InvariantCulture)));

        /// <summary>Two event paths name the same event (<c>Music/Forest</c> = <c>event:/Music/Forest</c>), without allocating.</summary>
        public static bool SameEvent(string? a, string? b)
        {
            var x = Bare(a); var y = Bare(b);
            return x.Length > 0 && x.Equals(y, StringComparison.OrdinalIgnoreCase);
        }
        private static ReadOnlySpan<char> Bare(string? p)
        {
            var s = (p ?? "").AsSpan().Trim();
            if (s.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) s = s[6..];
            return s.TrimStart('/');
        }

        // ---------------------------------------------------------------- export
        /// <summary>The FMOD files an exported game can carry (fmod.dll, and fmodstudio.dll when found): empty when FMOD isn't installed.</summary>
        public static List<string> ExportableDlls()
        {
            var list = new List<string>();
            if (BE.FmodLibrary.Disabled) return list;
            var core = BE.FmodLibrary.FindCore();
            if (core == null) return list;
            list.Add(core);
            var studio = BE.FmodLibrary.FindStudio(Path.GetDirectoryName(core));
            if (studio != null) list.Add(studio);
            return list;
        }

        /// <summary>Puts FMOD's DLLs next to an exported game's exe (only when the person opted in, accepting FMOD's licence).
        /// Returns the file names copied.</summary>
        public static List<string> CopyFmodInto(string gameDir, IEnumerable<string>? dlls = null)
        {
            var copied = new List<string>();
            foreach (var src in dlls ?? ExportableDlls())
            {
                var dst = Path.Combine(gameDir, Path.GetFileName(src));
                if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) { copied.Add(Path.GetFileName(src)); continue; }
                File.Copy(src, dst, overwrite: true);
                copied.Add(Path.GetFileName(src));
            }
            return copied;
        }

        /// <summary>Takes FMOD's DLLs out of an exported game (they came along with EasyEngine's own folder, but the person
        /// didn't choose to include FMOD). Returns how many were removed.</summary>
        public static int RemoveFmodFrom(string gameDir)
        {
            int n = 0;
            try
            {
                foreach (var f in Directory.GetFiles(gameDir, "fmod*.dll"))
                    try { File.Delete(f); n++; } catch { }
            }
            catch { }
            return n;
        }

        /// <summary>The export's FMOD step: with <paramref name="include"/> fmod.dll (+ fmodstudio.dll) go next to the game's exe;
        /// without it any FMOD DLL that came along with EasyEngine's own folder is taken out (the game then plays classic sound).</summary>
        public static void ExportFmod(string gameDir, bool include)
        {
            if (include) CopyFmodInto(gameDir);
            else RemoveFmodFrom(gameDir);
        }

        /// <summary>Whether a project uses FMOD Studio events (a Sound behavior set to an event, a rule or a cutscene step) or has banks.</summary>
        public static bool ProjectUsesFmod(Project p)
        {
            if (!string.IsNullOrEmpty(p.FolderPath) && BE.FmodStudio.FindBankFolder(p.FolderPath) != null) return true;
            foreach (var s in p.Scenes)
                foreach (var l in s.Layers)
                    foreach (var o in l.Objects)
                        foreach (var b in o.Behaviors)
                        {
                            if (b.Kind == BehaviorKind.SoundEmitter && string.Equals(b.Text("source", "file"), "fmod", StringComparison.OrdinalIgnoreCase)) return true;
                            foreach (var t in b.Texts.Values) if (t.Contains("fmodevent", StringComparison.OrdinalIgnoreCase)) return true;
                        }
            return false;
        }
    }
}
