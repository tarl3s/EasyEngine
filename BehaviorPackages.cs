using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using BE = BuilderEngine.Script; // its Project/Scene/World clash with ours

namespace EasyEngine
{
    /// <summary>The behavior packages one project can use: the valid ones (the project's own first) and the files that
    /// couldn't be read (reported once, never a crash).</summary>
    public sealed class PackageSet
    {
        public static readonly PackageSet Empty = new(new List<BE.BehaviorPackage>(), new List<string>(), null);

        private readonly Dictionary<string, BE.BehaviorPackage> _byName;
        public IReadOnlyList<BE.BehaviorPackage> All { get; }
        public IReadOnlyList<string> Problems { get; }
        /// <summary>The project's behaviors folder these were read with (null without a project folder).</summary>
        public string? ProjectFolder { get; }

        public PackageSet(List<BE.BehaviorPackage> all, List<string> problems, string? projectFolder)
        {
            All = all.OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Problems = problems;
            ProjectFolder = projectFolder;
            _byName = new(StringComparer.OrdinalIgnoreCase);
            foreach (var p in all) _byName.TryAdd(p.Name, p);
        }

        public BE.BehaviorPackage? Find(string? name) => name != null && _byName.TryGetValue(name.Trim(), out var p) ? p : null;
    }

    /// <summary>
    /// BEHAVIOR PACKAGES (.behavior files, BuilderEngine's docs/BEHAVIOR-FORMAT.md): a behavior both engines understand — a
    /// manifest (name, icon, description, settings with their pickers) and its logic in Builder Script. EasyEngine never
    /// reads the file itself: BuilderEngine's <c>BehaviorPackage</c> (lib/BuilderEngine) parses, validates and finds them.
    /// <list type="bullet">
    /// <item>Found in the project's <c>behaviors/</c> folder (first) and the person's installed folder
    /// (%APPDATA%\BuilderEngine\Behaviors, shared with BuilderEngine); a project copy wins over an installed one.</item>
    /// <item>Stored on an object as a behavior of kind <see cref="BehaviorKind.Package"/>: <c>Texts["package"]</c> = its
    /// name, numbers and on/off settings in <c>Nums</c> (on = 1), everything else in <c>Texts</c> (lists and paths as JSON
    /// text) — the bags every behavior has, so older EasyEngine versions keep the data untouched.</item>
    /// <item>Adding one to an object copies the file into the project's <c>behaviors/</c>, so the game works on another
    /// computer and in an exported .exe.</item>
    /// <item>Played through the BuilderEngine bridge (PlayWindow.BuilderBridge.cs): the object's BuilderEngine twin runs the
    /// package's mechanism with these settings.</item>
    /// </list>
    /// </summary>
    public static class BehaviorPackages
    {
        public const string NameKey = "package";
        /// <summary>The installed packages folder (BuilderEngine's, shared by both engines). Test modes point it at an empty
        /// temp folder, so the person's own packages never change a test.</summary>
        public static string InstalledFolder
        {
            get => BE.BehaviorPackage.InstalledFolder;
            set => BE.BehaviorPackage.InstalledFolder = value;
        }

        public static string? ProjectFolder(Project p) =>
            string.IsNullOrEmpty(p.FolderPath) ? null : BE.BehaviorPackage.ProjectFolder(p.FolderPath);

        // ------------------------------------------------------------------ finding
        private static readonly object Gate = new();
        private static readonly Dictionary<string, (string sig, PackageSet set)> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The packages this project can use — its own (behaviors/), the installed ones, the ones shipped with
        /// BuilderEngine, first wins (BE.BehaviorPackage.DiscoverAll; each has its Origin). Read again only when a file in the
        /// project's or the installed folder changed.</summary>
        public static PackageSet For(Project p)
        {
            string? projectDir = string.IsNullOrEmpty(p.FolderPath) ? null : p.FolderPath;
            var folders = new List<string>(2) { InstalledFolder };
            if (projectDir != null) folders.Insert(0, BE.BehaviorPackage.ProjectFolder(projectDir));
            string key = string.Join("|", folders), sig = Signature(folders);
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var c) && c.sig == sig) return c.set;
                var problems = new List<string>();
                List<BE.BehaviorPackage> found;
                try { found = BE.BehaviorPackage.DiscoverAll(projectDir, problems); }
                catch (Exception ex) { found = new(); problems.Add(ex.Message); } // a folder that can't be listed: no packages, never a crash
                var set = new PackageSet(found, problems, projectDir == null ? null : folders[0]);
                Cache[key] = (sig, set);
                return set;
            }
        }

        /// <summary>A package that stands for one of EasyEngine's own behaviors (the shipped Spin, Spawner…): EasyEngine keeps
        /// its built-in, so the AI isn't offered the package as a second one.</summary>
        public static bool ReplacesBuiltIn(BE.BehaviorPackage p) =>
            p.Replaces != null && Enum.TryParse<BehaviorKind>(p.Replaces, true, out var k) && !BehaviorCatalog.Info(k).Hidden
            || Enum.TryParse<BehaviorKind>(p.Name, true, out _);

        /// <summary>Where a package comes from, in words.</summary>
        public static string OriginText(BE.BehaviorPackage p) => p.Origin switch
        {
            BE.PackageOrigin.Project => L.T("in this project"),
            BE.PackageOrigin.Installed => L.T("installed"),
            BE.PackageOrigin.Shipped => L.T("comes with BuilderEngine"),
            _ => "",
        };

        // the folders' .behavior files with their size and time: a change in any of them reads them again
        private static string Signature(List<string> folders)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in folders)
            {
                sb.Append(f).Append(':');
                try
                {
                    if (Directory.Exists(f))
                        foreach (var file in new DirectoryInfo(f).EnumerateFiles("*" + BE.BehaviorPackage.Extension).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                            sb.Append(file.Name).Append('/').Append(file.Length).Append('/').Append(file.LastWriteTimeUtc.Ticks).Append(';');
                }
                catch (Exception) { sb.Append('?'); }
                sb.Append('|');
            }
            return sb.ToString();
        }

        /// <summary>The package this behavior uses, or null (not a package, or the package isn't installed).</summary>
        public static BE.BehaviorPackage? Of(Project p, BehaviorData b) => b.Kind == BehaviorKind.Package ? For(p).Find(b.Text(NameKey)) : null;

        /// <summary>The package's name of a behavior of kind Package ("" for other kinds).</summary>
        public static string NameOf(BehaviorData b) => b.Kind == BehaviorKind.Package ? b.Text(NameKey).Trim() : "";

        /// <summary>The same behavior for "another one of these?": same kind, and for packages the same package.</summary>
        public static bool Same(BehaviorData a, BehaviorData b) =>
            a.Kind == b.Kind && (a.Kind != BehaviorKind.Package || string.Equals(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase));

        /// <summary>What a behavior is called in the AI's scene list and the console: the package's name, else the kind.</summary>
        public static string KindName(BehaviorData b) => b.Kind == BehaviorKind.Package && NameOf(b).Length > 0 ? NameOf(b) : b.Kind.ToString();

        /// <summary>This object may get this package: a repeatable one always, the others once.</summary>
        public static bool CanAdd(GameObjectData obj, BE.BehaviorPackage pkg) =>
            pkg.Repeatable || !obj.Behaviors.Any(b => b.Kind == BehaviorKind.Package && string.Equals(NameOf(b), pkg.Name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Every object (scene objects and prefab templates) with a package behavior, and the names they use.</summary>
        public static HashSet<string> UsedNames(Project p)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in p.Scenes.SelectMany(s => s.Layers).SelectMany(l => l.Objects).Concat(p.Prefabs.Select(f => f.Template)))
                foreach (var b in o.Behaviors)
                    if (NameOf(b) is { Length: > 0 } n) names.Add(n);
            return names;
        }

        // ------------------------------------------------------------------ settings
        /// <summary>Numbers and on/off settings live in Nums; everything else in Texts.</summary>
        public static bool InNums(BE.ParamKind k) => k is BE.ParamKind.Number or BE.ParamKind.Bool;

        /// <summary>A new behavior using this package, every setting at its default.</summary>
        public static BehaviorData CreateData(BE.BehaviorPackage pkg)
        {
            var d = new BehaviorData { Kind = BehaviorKind.Package };
            d.Texts[NameKey] = pkg.Name;
            foreach (var p in pkg.AllParams())
            {
                if (InNums(p.Kind)) d.Nums[p.Name] = DefaultNumber(p);
                else d.Texts[p.Name] = DefaultText(p);
            }
            return d;
        }

        public static double DefaultNumber(BE.BehaviorPackage.PackageParam p) => p.Default switch
        {
            JsonValue v when v.TryGetValue<double>(out var d) => d,
            JsonValue v when v.TryGetValue<bool>(out var b) => b ? 1 : 0,
            JsonValue v when v.TryGetValue<string>(out var s) && LooseJson.TryParse(s, out var n) => n,
            _ => p.Kind == BE.ParamKind.Number && p.Min is double min && min > 0 ? min : 0,
        };

        public static string DefaultText(BE.BehaviorPackage.PackageParam p)
        {
            string s = p.Default switch
            {
                null => p.Kind == BE.ParamKind.Choice && p.Options.Count > 0 ? p.Options[0] : p.Kind is BE.ParamKind.List or BE.ParamKind.Path ? "[]" : "",
                JsonValue v when v.TryGetValue<string>(out var t) => t,
                JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
                JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
                _ => p.Default.ToJsonString(),
            };
            return p.Kind == BE.ParamKind.Key ? EeKey(s) : s;
        }

        /// <summary>A setting as the inspector and the AI keep it (Nums or Texts), its default when it's missing.</summary>
        public static double Number(BehaviorData b, BE.BehaviorPackage.PackageParam p) =>
            b.Nums.TryGetValue(p.Name, out var v) ? v
            : b.Texts.TryGetValue(p.Name, out var t) && (LooseJson.TryParse(t, out var n) || BoolText(t, out n)) ? n
            : DefaultNumber(p);

        public static string Text(BehaviorData b, BE.BehaviorPackage.PackageParam p) =>
            b.Texts.TryGetValue(p.Name, out var t) ? t
            : b.Nums.TryGetValue(p.Name, out var v) ? v.ToString(CultureInfo.InvariantCulture)
            : DefaultText(p);

        private static bool BoolText(string t, out double n)
        {
            n = t.Trim().ToLowerInvariant() is "true" or "yes" or "on" ? 1 : 0;
            return t.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "false" or "no" or "off";
        }

        /// <summary>A setting as BuilderEngine's runtime wants it (ParamTypes.Convert): a number, true/false, a text, a list, an
        /// object's name, a key's name as BuilderEngine spells keys.</summary>
        public static object? RuntimeValue(BehaviorData b, BE.BehaviorPackage.PackageParam p)
        {
            object? raw = p.Kind switch
            {
                BE.ParamKind.Number or BE.ParamKind.Bool => Number(b, p),
                BE.ParamKind.Key => BeKey(Text(b, p)),
                _ => Text(b, p),
            };
            return BE.ParamTypes.Convert(raw, p.Type);
        }

        /// <summary>Every setting of this behavior for the package's mechanism (Spawn overrides).</summary>
        public static Dictionary<string, object?> RuntimeSettings(BE.BehaviorPackage pkg, BehaviorData b)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var p in pkg.AllParams()) d[p.Name] = RuntimeValue(b, p);
            return d;
        }

        // ------------------------------------------------------------------ keys: EasyEngine's picker values ⇄ BuilderEngine's key names
        /// <summary>BuilderEngine's name of a key ("space", "a", "shift") as EasyEngine's key picker stores it ("Space", "A", "LeftShift").</summary>
        public static string EeKey(string beName)
        {
            var s = (beName ?? "").Trim();
            if (s.Length == 0) return "";
            foreach (var (value, _) in ParamSources.Keys)
                if (value.Equals(s, StringComparison.OrdinalIgnoreCase)) return value;
            return s.ToLowerInvariant() switch
            {
                "shift" => "LeftShift", "ctrl" or "control" => "LeftCtrl", "return" => "Enter", "esc" => "Escape",
                var d when d.Length == 1 && char.IsDigit(d[0]) => "D" + d,
                _ => s,
            };
        }

        /// <summary>EasyEngine's key value as BuilderEngine's runtime names it (the bridge sends held keys with the same names).</summary>
        public static string BeKey(string eeValue)
        {
            var s = (eeValue ?? "").Trim();
            if (s.Length == 0) return "";
            if (Enum.TryParse<System.Windows.Input.Key>(s, true, out var k)) return PlayWindow.BuilderKeyName(k);
            return s.ToLowerInvariant();
        }

        // ------------------------------------------------------------------ "show if"
        /// <summary>Whether a setting shows: its "show if" (a Builder Script condition over the other settings) is true for
        /// this behavior's settings — worked out by BuilderEngine (BehaviorPackage.Shows; one it can't work out shows).</summary>
        public static bool Shows(BE.BehaviorPackage pkg, BE.BehaviorPackage.PackageParam p, BehaviorData b) =>
            string.IsNullOrWhiteSpace(p.ShowIf) || pkg.Shows(p, RuntimeSettings(pkg, b));

        // ------------------------------------------------------------------ the catalog view of a package (AI, Learn, AiSpecFix)
        /// <summary>A package as a catalog entry: name, icon, description and its settings as Nums (numbers, on/off as
        /// "(0/1)") and Texts (with the picker each type gets) — what the AI's fix-ups and Learn mode read.</summary>
        public static BehaviorInfo InfoFor(BE.BehaviorPackage pkg)
        {
            var info = new BehaviorInfo { Kind = BehaviorKind.Package, Name = pkg.Name, Icon = pkg.Icon, Desc = pkg.Description, Repeatable = pkg.Repeatable };
            foreach (var p in pkg.AllParams())
            {
                string label = p.Label.Length > 0 ? p.Label : p.Name;
                if (p.Kind == BE.ParamKind.Bool) info.Nums.Add(new NumParam(p.Name, label + " (0/1)", DefaultNumber(p)));
                else if (p.Kind == BE.ParamKind.Number) info.Nums.Add(new NumParam(p.Name, label, DefaultNumber(p)));
                else info.Texts.Add(new TextParam(p.Name, label, DefaultText(p), Pick(p.Kind), p.Kind == BE.ParamKind.Choice ? p.Options.Select(o => o + "|" + o).ToArray() : Array.Empty<string>()));
            }
            return info;
        }

        /// <summary>The EasyEngine picker kind a package setting gets.</summary>
        public static PickKind Pick(BE.ParamKind k) => k switch
        {
            BE.ParamKind.Choice => PickKind.Choice,
            BE.ParamKind.Object => PickKind.Object,
            BE.ParamKind.Scene => PickKind.Scene,
            BE.ParamKind.Key => PickKind.Key,
            BE.ParamKind.Color => PickKind.Color,
            BE.ParamKind.Sound => PickKind.Sound,
            BE.ParamKind.Image => PickKind.Picture,
            BE.ParamKind.List or BE.ParamKind.Path => PickKind.Special,
            _ => PickKind.Text,
        };

        /// <summary>One package in a line for the AI: "Wobble — desc. nums amount=10; texts mode=\"x\"(x|y) (a behavior package)".</summary>
        public static string AiLine(BE.BehaviorPackage pkg)
        {
            var info = InfoFor(pkg);
            static string Opts(string[]? o) => o == null ? "" : "(" + string.Join("|", o.Select(x => x.Split('|')[0])) + ")";
            var desc = pkg.Description.Split(new[] { ". " }, 2, StringSplitOptions.None)[0];
            if (desc.Length > 110) desc = desc[..110] + "…";
            var nums = string.Join(" ", info.Nums.Select(n => $"{n.Key}={n.Default.ToString("0.##", CultureInfo.InvariantCulture)}{(n.Label.EndsWith("(0/1)") ? "(0|1)" : "")}"));
            var texts = string.Join(" ", info.Texts.Select(t => $"{t.Key}={(t.Default.Length > 0 ? JsonQuote(t.Default) : "\"\"")}{Opts(t.Options)}"));
            var guide = pkg.AiGuide.Length > 160 ? pkg.AiGuide[..160] + "…" : pkg.AiGuide;
            return $"  {pkg.Name} — {desc.TrimEnd('.')}." + (nums.Length > 0 ? " nums " + nums : "") + (texts.Length > 0 ? "; texts " + texts : "")
                   + (pkg.Repeatable ? " (an object may have several)" : "") + " (a behavior package)" + (guide.Length > 0 ? " " + guide : "");
        }

        private static string JsonQuote(string s) => s.StartsWith('[') ? s : System.Text.Json.JsonSerializer.Serialize(s);

        /// <summary>The packages a request is about: their name or one of their AI keywords is in it.</summary>
        public static List<BE.BehaviorPackage> Matching(IEnumerable<BE.BehaviorPackage> packages, string request, int max = 4)
        {
            var q = " " + System.Text.RegularExpressions.Regex.Replace(request.ToLowerInvariant(), "[^\\p{L}\\p{N}]+", " ") + " ";
            bool Has(string w) => w.Trim().Length >= 3 && q.Contains(" " + w.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            return packages.Where(p => Has(p.Name) || Has(System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<=[a-z])(?=[A-Z])|_", " ")) || p.AiKeywords.Any(Has))
                          .Take(max).ToList();
        }

        // ------------------------------------------------------------------ files: the project keeps a copy of what it uses
        /// <summary>Copies the package into the project's behaviors/ folder (so the game works anywhere) — an installed or a
        /// shipped one (BE CopyToProject); false when it is already the project's or the project has no folder yet.</summary>
        public static bool EnsureInProject(Project p, BE.BehaviorPackage pkg)
        {
            if (string.IsNullOrEmpty(p.FolderPath) || pkg.Origin == BE.PackageOrigin.Project) return false;
            if (File.Exists(BE.BehaviorPackage.FileIn(BE.BehaviorPackage.ProjectFolder(p.FolderPath), pkg.Name))) return false;
            pkg.CopyToProject(p.FolderPath);
            return true;
        }

        /// <summary>Exporting: every package the project uses that isn't in its behaviors/ yet (an installed or a shipped one)
        /// is written into the exported game's <paramref name="gameFolder"/>/behaviors. Returns how many.</summary>
        public static int CopyUsedInto(Project p, string gameFolder)
        {
            var set = For(p);
            int n = 0;
            var dir = BE.BehaviorPackage.ProjectFolder(gameFolder);
            foreach (var name in UsedNames(p))
            {
                if (set.Find(name) is not { } pkg || pkg.Origin == BE.PackageOrigin.Project) continue;
                if (File.Exists(BE.BehaviorPackage.FileIn(dir, pkg.Name))) continue;
                pkg.CopyToProject(gameFolder);
                n++;
            }
            return n;
        }
    }
}
