using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyEngine
{
    /// <summary>The options the inspector's pickers offer: items, variables, objects, prefabs, keys (all found in the project).</summary>
    public static class ParamSources
    {
        private static IEnumerable<GameObjectData> AllObjects(Project p) =>
            p.Scenes.SelectMany(s => s.Layers).SelectMany(l => l.Objects)
             .Concat(p.Prefabs.Where(pf => pf.Template != null).Select(pf => pf.Template!));

        /// <summary>Every item name the project uses (hotbar slots first), so an item is picked, not typed.</summary>
        public static List<string> Items(Project p)
        {
            var list = new List<string>();
            void Add(string? s) { s = s?.Trim(); if (!string.IsNullOrEmpty(s) && !list.Any(x => x.Equals(s, StringComparison.OrdinalIgnoreCase))) list.Add(s); }
            void AddRef(string? s) // "item:wood" / "holding:axe" → wood / axe
            {
                s = s?.Trim() ?? "";
                foreach (var pre in new[] { "item:", "holding:" })
                    if (s.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) Add(s.Substring(pre.Length));
            }
            var behs = AllObjects(p).SelectMany(o => o.Behaviors).ToList();
            foreach (var b in behs.Where(b => b.Kind == BehaviorKind.Hotbar))
                foreach (var it in PlayWindow.ParseHotbarItems(b.Text("items", ""))) Add(it.name);
            foreach (var b in behs)
                switch (b.Kind)
                {
                    case BehaviorKind.Shop: Add(b.Text("item", "")); AddRef(b.Text("currency", "")); break;
                    case BehaviorKind.Harvestable: Add(b.Text("tool", "")); Add(b.Text("drop", "")); break;
                    case BehaviorKind.Tilemap: Add(b.Text("drop", "")); AddRef(b.Text("requires", "")); break;
                    case BehaviorKind.ToolUse: Add(b.Text("tool", "")); break;
                    case BehaviorKind.Gate: Add(b.Text("item", "")); break;
                }
            foreach (var k in p.Variables.Keys.Concat(p.Scenes.SelectMany(s => s.Variables.Keys))) AddRef(k);
            if (list.Count == 0) { Add("pickaxe"); Add("axe"); Add("sword"); }
            return list;
        }

        /// <summary>Variables the engine itself writes (Day/Night): readable in conditions, but never something you pay with.</summary>
        public static readonly string[] EngineVariables = { "isDay", "isNight", "timeOfDay", "dayness", "cover", "underground" };

        /// <summary>Variables a picker can offer (not the engine-managed item:/holding:/stat:/check: ones).</summary>
        public static List<string> Variables(Project p)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? s) { s = s?.Trim(); if (!string.IsNullOrEmpty(s) && !s.Contains(':')) set.Add(s); }
            foreach (var k in p.Variables.Keys) Add(k);
            foreach (var s in p.Scenes) foreach (var k in s.Variables.Keys) Add(k);
            foreach (var o in AllObjects(p))
            {
                foreach (var k in o.Variables.Keys) Add(k);
                foreach (var b in o.Behaviors)
                {
                    Add(b.Text("var", "")); Add(b.Text("maxVar", ""));
                    if (b.Kind == BehaviorKind.Label) foreach (Match m in Regex.Matches(b.Text("text", ""), @"\{([^}]+)\}")) Add(m.Groups[1].Value);
                }
            }
            foreach (var builtin in EngineVariables) set.Add(builtin);
            return set.ToList();
        }

        public static List<string> Objects(Scene s) => s.Layers.SelectMany(l => l.Objects).Select(o => o.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        public static List<string> Prefabs(Project p) => p.Prefabs.Select(pf => pf.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

        /// <summary>The prefabs whose name isn't already one of <paramref name="objects"/> (case-insensitive).</summary>
        public static List<string> PrefabsNotIn(Project p, IEnumerable<string> objects)
        {
            var taken = new HashSet<string>(objects, StringComparer.OrdinalIgnoreCase);
            return Prefabs(p).Where(pf => !taken.Contains(pf)).ToList();
        }

        /// <summary>A "what to spawn / show" picker: the scene's objects, then the other prefabs labelled "(prefab)".</summary>
        public static List<(string value, string label)> ObjectOrPrefabOptions(Scene s, Project p)
        {
            var objects = Objects(s);
            return objects.Select(o => (o, o)).Concat(PrefabsNotIn(p, objects).Select(pf => (pf, pf + "  " + L.T("(prefab)")))).ToList();
        }

        /// <summary>The project's sound files (Assetssounds), as the relative paths the game plays.</summary>
        public static List<string> Sounds(Project p)
        {
            var dir = string.IsNullOrEmpty(p.FolderPath) ? null : System.IO.Path.Combine(p.FolderPath, "Assets", "sounds");
            if (dir == null || !System.IO.Directory.Exists(dir)) return new List<string>();
            return System.IO.Directory.GetFiles(dir)
                .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                .Select(f => System.IO.Path.Combine("Assets", "sounds", System.IO.Path.GetFileName(f))).ToList();
        }

        /// <summary>Keys a key picker offers: (value, what the person sees).</summary>
        public static readonly (string value, string label)[] Keys =
            Enumerable.Range('A', 26).Select(c => (((char)c).ToString(), ((char)c).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(i => ("D" + i, i.ToString())))
            .Concat(new[] { ("Space", "Space"), ("Enter", "Enter"), ("LeftShift", "Shift"), ("LeftCtrl", "Ctrl"), ("Tab", "Tab"),
                            ("Up", "↑ Up arrow"), ("Down", "↓ Down arrow"), ("Left", "← Left arrow"), ("Right", "→ Right arrow") })
            .ToArray();

        /// <summary><see cref="Keys"/> with the labels in the editor's language (<see cref="Keys"/> stays English: the game
        /// shows those labels too).</summary>
        public static List<(string value, string label)> KeyOptions() => Keys.Select(k => (k.value, KeyLabel(k.label))).ToList();

        // each key name written out as an L.T literal so --strings collects it for the translators
        private static string KeyLabel(string label) => label switch
        {
            "Space" => L.T("Space"), "Enter" => L.T("Enter"), "Shift" => L.T("Shift"), "Ctrl" => L.T("Ctrl"), "Tab" => L.T("Tab"),
            "↑ Up arrow" => L.T("↑ Up arrow"), "↓ Down arrow" => L.T("↓ Down arrow"),
            "← Left arrow" => L.T("← Left arrow"), "→ Right arrow" => L.T("→ Right arrow"),
            _ => label,
        };

        /// <summary>Parses "value|Label" options.</summary>
        public static (string value, string label) Option(string o)
        {
            int i = o.IndexOf('|');
            return i < 0 ? (o, o) : (o.Substring(0, i), o.Substring(i + 1));
        }

        /// <summary>"sword=2,axe=1" ⇄ dictionary (case-insensitive, keeps order).</summary>
        public static List<(string item, double dmg)> ParseDamage(string spec)
        {
            var list = new List<(string, double)>();
            foreach (var part in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && double.TryParse(kv[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    list.Add((kv[0].Trim(), d));
            }
            return list;
        }

        public static string SetDamage(string spec, string item, double dmg)
        {
            var list = ParseDamage(spec).Where(x => !x.item.Equals(item, StringComparison.OrdinalIgnoreCase)).ToList();
            list.Add((item, dmg));
            return string.Join(",", list.Select(x => $"{x.item}={x.dmg.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        }

        /// <summary>A file per item, e.g. Use Item's sounds: "sword=Assets\sounds\slash.wav|axe=Assets\sounds\chop.wav"
        /// ('|' between entries, since a path may hold a comma).</summary>
        public static List<(string item, string file)> ParseItemFiles(string spec)
        {
            var list = new List<(string, string)>();
            foreach (var part in (spec ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Trim().Length > 0 && kv[1].Trim().Length > 0) list.Add((kv[0].Trim(), kv[1].Trim()));
            }
            return list;
        }

        public static string ItemFile(string spec, string item) =>
            ParseItemFiles(spec).FirstOrDefault(x => x.item.Equals(item, StringComparison.OrdinalIgnoreCase)).file ?? "";

        /// <summary>Sets (or, with an empty file, removes) the item's file.</summary>
        public static string SetItemFile(string spec, string item, string file)
        {
            var list = ParseItemFiles(spec).Where(x => !x.item.Equals(item, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrWhiteSpace(file)) list.Add((item, file.Trim()));
            return string.Join("|", list.Select(x => $"{x.item}={x.file}"));
        }
    }
}
