using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace EasyEngine
{
    /// <summary>The kinds a game variable can be. A Yes/No is a number 1/0 underneath (so behaviors, conditions, Gate,
    /// Switch and expressions read it as before); a Text lives in its own store; a Color is a Text holding "#RRGGBB"
    /// (so {name} and iftext read it); a List is an ordered list of words in its own store.</summary>
    public enum VarKind { Number, YesNo, Text, Color, List }

    /// <summary>One scope's variables (the game's, a scene's or an object's): numbers (+ which of them are Yes/No), texts
    /// (+ which of them are Colors) and lists. A name lives in one store at a time; changing its kind converts the value
    /// and moves it.</summary>
    public readonly struct VarScope
    {
        public readonly Dictionary<string, double> Nums;
        public readonly HashSet<string> Flags;
        public readonly Dictionary<string, string> Texts;
        public readonly HashSet<string> Colors;
        public readonly Dictionary<string, List<string>> Lists;

        public VarScope(Dictionary<string, double> nums, HashSet<string> flags, Dictionary<string, string> texts,
                        HashSet<string> colors, Dictionary<string, List<string>> lists)
        {
            Nums = nums; Flags = flags; Texts = texts; Colors = colors; Lists = lists;
        }

        public static VarScope Of(Project p) => new(p.Variables, p.Flags, p.Texts, p.Colors, p.Lists);
        public static VarScope Of(Scene s) => new(s.Variables, s.Flags, s.Texts, s.Colors, s.Lists);
        public static VarScope Of(GameObjectData o) => new(o.Variables, o.Flags, o.Texts, o.Colors, o.Lists);

        /// <summary>A set of names (the Yes/No or the Color ones, case-insensitive), also when it comes from a project file.</summary>
        public static HashSet<string> NewFlags(IEnumerable<string>? names)
            => names == null ? new(StringComparer.OrdinalIgnoreCase)
                             : new(names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);

        /// <summary>The list store, also when it comes from a project file (a missing list becomes an empty one).</summary>
        public static Dictionary<string, List<string>> NewLists(IDictionary<string, List<string>>? lists)
        {
            var d = new Dictionary<string, List<string>>();
            if (lists != null)
                foreach (var kv in lists)
                    if (!string.IsNullOrWhiteSpace(kv.Key)) d[kv.Key] = kv.Value == null ? new() : kv.Value.Select(s => s ?? "").ToList();
            return d;
        }

        public bool Has(string name) => Nums.ContainsKey(name) || Texts.ContainsKey(name) || Lists.ContainsKey(name);
        public int Count => Names().Count;
        public VarKind KindOf(string name) => Lists.ContainsKey(name) ? VarKind.List
            : Texts.ContainsKey(name) ? (Colors.Contains(name) ? VarKind.Color : VarKind.Text)
            : Flags.Contains(name) ? VarKind.YesNo : VarKind.Number;

        /// <summary>Every variable name of this scope, sorted.</summary>
        public List<string> Names() => Nums.Keys.Concat(Texts.Keys).Concat(Lists.Keys).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        private void Clear(string name) { Nums.Remove(name); Flags.Remove(name); Texts.Remove(name); Colors.Remove(name); Lists.Remove(name); }
        public void SetNumber(string name, double v) { Clear(name); Nums[name] = v; }
        public void SetYesNo(string name, bool v) { Clear(name); Flags.Add(name); Nums[name] = v ? 1 : 0; }
        public void SetText(string name, string v) { Clear(name); Texts[name] = v ?? ""; }
        /// <summary>A Color variable; anything that isn't a colour "#RRGGBB" / "#AARRGGBB" becomes white.</summary>
        public void SetColor(string name, string hex) { var c = ColorHex(hex) ?? White; Clear(name); Colors.Add(name); Texts[name] = c; }
        public void SetList(string name, IEnumerable<string>? items) { var l = items?.Select(s => s ?? "").ToList() ?? new(); Clear(name); Lists[name] = l; }
        public bool Remove(string name) { bool had = Has(name); Clear(name); return had; }

        /// <summary>The value read as a number (a text gives the number written in it, or 0; a list its item count).</summary>
        public double Number(string name) => Nums.TryGetValue(name, out var v) ? v
            : Lists.TryGetValue(name, out var l) ? l.Count
            : Texts.TryGetValue(name, out var t) ? TextToNumber(t) : 0;
        /// <summary>The value read as Yes/No (a number: not 0; a text: yes/true/on or a number that isn't 0; a list: not empty).</summary>
        public bool YesNo(string name) => Nums.TryGetValue(name, out var v) ? v != 0
            : Lists.TryGetValue(name, out var l) ? l.Count > 0
            : Texts.TryGetValue(name, out var t) && TextToYesNo(t);
        /// <summary>The value read as text (a Yes/No gives "Yes"/"No", a number its digits, a color its hex, a list its items
        /// joined with ", ").</summary>
        public string Text(string name) => Texts.TryGetValue(name, out var t) ? t
            : Lists.TryGetValue(name, out var l) ? ListText(l)
            : Nums.TryGetValue(name, out var v) ? (Flags.Contains(name) ? YesNoText(v != 0) : NumberText(v)) : "";
        /// <summary>The value read as a colour: a text (or a list's first item) that is a colour, else white.</summary>
        public string ColorValue(string name) => (Texts.TryGetValue(name, out var t) ? ColorHex(t)
            : Lists.TryGetValue(name, out var l) && l.Count > 0 ? ColorHex(l[0]) : null) ?? White;
        /// <summary>The value read as a list: a list's items; a text split at its commas; a color as its one item; a number
        /// or Yes/No as an empty list.</summary>
        public List<string> ListValue(string name) => Lists.TryGetValue(name, out var l) ? new List<string>(l)
            : Texts.TryGetValue(name, out var t) ? (Colors.Contains(name) ? new List<string> { t } : SplitList(t)) : new List<string>();

        /// <summary>Changes a variable's kind, converting its value: number → Yes/No = "not 0"; text → number = the number
        /// in it (or 0); Yes/No → text = "Yes"/"No"; number → text = its digits; text → color = the colour written in it
        /// (else white); color → text = its hex; list → text = the items joined with ", "; text → list = split at the
        /// commas; list → number = how many items; number / Yes/No → list = empty; number / Yes/No → color = white.</summary>
        public void ChangeKind(string name, VarKind to)
        {
            switch (to)
            {
                case VarKind.Number: SetNumber(name, Number(name)); break;
                case VarKind.YesNo: SetYesNo(name, YesNo(name)); break;
                case VarKind.Color: SetColor(name, ColorValue(name)); break;
                case VarKind.List: SetList(name, ListValue(name)); break;
                default: SetText(name, Text(name)); break;
            }
        }

        public const string White = "#FFFFFF";

        /// <summary>"#RRGGBB" / "#AARRGGBB" (upper-case) when <paramref name="s"/> is a colour written that way, else null.</summary>
        public static string? ColorHex(string? s)
        {
            var t = (s ?? "").Trim();
            if (t.Length is not (7 or 9) || t[0] != '#') return null;
            for (int i = 1; i < t.Length; i++) if (!Uri.IsHexDigit(t[i])) return null;
            return t.ToUpperInvariant();
        }

        public static string ListText(IEnumerable<string> items) => string.Join(", ", items);
        public static List<string> SplitList(string? s) => (s ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        public static double TextToNumber(string s) => LooseJson.TryParse(s ?? "", out var v) ? v : 0;
        public static bool TextToYesNo(string s)
        {
            var t = (s ?? "").Trim().ToLowerInvariant();
            if (t is "yes" or "true" or "on" or "y") return true;
            return LooseJson.TryParse(t, out var v) && v != 0;
        }
        public static string NumberText(double v)
            => v == Math.Floor(v) && Math.Abs(v) < 1e15 ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);
        public static string YesNoText(bool v) => v ? "Yes" : "No";

        /// <summary>A value from JSON (the AI's "variables"): a number (or a number in quotes) is a Number, true/false is a
        /// Yes/No, a colour "#RRGGBB" is a Color, an array ["a","b"] is a List, other words are a Text. A Yes/No keeps its
        /// kind for a number (0/1); a Text keeps its kind for any string; a List takes a string as comma-separated items.
        /// <paramref name="shown"/> describes the change ("name=5", "name=yes", "name=\"Hi\"", "name=#FF8800", "name=[a, b]").</summary>
        public bool SetFromJson(string name, JsonElement e, out string shown)
        {
            shown = "";
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return SetBool(name, e.ValueKind == JsonValueKind.True, out shown);
            if (e.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var it in e.EnumerateArray())
                    switch (it.ValueKind)
                    {
                        case JsonValueKind.String: items.Add(it.GetString() ?? ""); break;
                        case JsonValueKind.Number: items.Add(LooseJson.TryNumber(it, out var n) ? NumberText(n) : it.GetRawText()); break;
                        case JsonValueKind.True: case JsonValueKind.False: items.Add(YesNoText(it.ValueKind == JsonValueKind.True)); break;
                    }
                return SetListShown(name, items, out shown);
            }
            if (e.ValueKind == JsonValueKind.String)
            {
                var s = e.GetString() ?? "";
                var low = s.Trim().ToLowerInvariant();
                var kind = KindOf(name);
                if (kind == VarKind.Text) { SetText(name, s); shown = $"{name}=\"{s}\""; return true; }
                if (kind == VarKind.List) return SetListShown(name, SplitList(s), out shown);
                if (ColorHex(s) is string hex) { SetColor(name, hex); shown = $"{name}={hex}"; return true; }
                if (low is "true" or "false") return SetBool(name, low == "true", out shown);
                if (!LooseJson.TryParse(s, out _)) { SetText(name, s); shown = $"{name}=\"{s}\""; return true; }
            }
            if (!LooseJson.TryNumber(e, out var nv)) return false;
            if (KindOf(name) == VarKind.YesNo) return SetBool(name, nv != 0, out shown);
            SetNumber(name, nv);
            shown = $"{name}={NumberText(nv)}";
            return true;
        }

        private bool SetBool(string name, bool v, out string shown)
        {
            SetYesNo(name, v);
            shown = $"{name}={(v ? "yes" : "no")}";
            return true;
        }

        private bool SetListShown(string name, List<string> items, out string shown)
        {
            SetList(name, items);
            shown = $"{name}=[{ListText(items)}]";
            return true;
        }

        /// <summary>A short listing for the AI's scene summary: numbers as name=5, Yes/No as name=yes, texts as name="Hi",
        /// colors as name=#FF8800, lists as name=["a","b"].</summary>
        public string Describe()
        {
            var self = this;
            static string Short(string s) => s.Length > 30 ? s[..30] + "…" : s;
            return string.Join(",", Names().Select(n => self.KindOf(n) switch
            {
                VarKind.Text => $"{n}=\"{Short(self.Texts[n])}\"",
                VarKind.Color => $"{n}={self.Texts[n]}",
                VarKind.List => $"{n}=[{string.Join(",", self.Lists[n].Take(8).Select(i => "\"" + Short(i) + "\""))}{(self.Lists[n].Count > 8 ? ",…" : "")}]",
                VarKind.YesNo => $"{n}={(self.YesNo(n) ? "yes" : "no")}",
                _ => $"{n}={self.Number(n).ToString("0.##", CultureInfo.InvariantCulture)}",
            }));
        }
    }
}
