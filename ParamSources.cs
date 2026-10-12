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
    }
}
