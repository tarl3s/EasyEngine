using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace EasyEngine
{
    /// <summary>The kinds a game variable can be. A Yes/No is a number 1/0 underneath (so behaviors, conditions, Gate,
    /// Switch and expressions read it as before); a Text lives in its own store.</summary>
    public enum VarKind { Number, YesNo, Text }

    /// <summary>One scope's variables (the game's, a scene's or an object's): numbers (+ which of them are Yes/No) and
    /// texts. A name lives in one store at a time; changing its kind converts the value and moves it.</summary>
    public readonly struct VarScope
    {
        public readonly Dictionary<string, double> Nums;
        public readonly HashSet<string> Flags;
        public readonly Dictionary<string, string> Texts;

        public VarScope(Dictionary<string, double> nums, HashSet<string> flags, Dictionary<string, string> texts)
        {
            Nums = nums; Flags = flags; Texts = texts;
        }

        public static VarScope Of(Project p) => new(p.Variables, p.Flags, p.Texts);
        public static VarScope Of(Scene s) => new(s.Variables, s.Flags, s.Texts);
        public static VarScope Of(GameObjectData o) => new(o.Variables, o.Flags, o.Texts);

        /// <summary>The Yes/No name set (case-insensitive), also when it comes from a project file.</summary>
        public static HashSet<string> NewFlags(IEnumerable<string>? names)
            => names == null ? new(StringComparer.OrdinalIgnoreCase)
                             : new(names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);

        public bool Has(string name) => Nums.ContainsKey(name) || Texts.ContainsKey(name);
        public int Count { get { var n = Nums; return n.Count + Texts.Count(kv => !n.ContainsKey(kv.Key)); } }
        public VarKind KindOf(string name) => Texts.ContainsKey(name) ? VarKind.Text : Flags.Contains(name) ? VarKind.YesNo : VarKind.Number;

        /// <summary>Every variable name of this scope, sorted.</summary>
        public List<string> Names() => Nums.Keys.Concat(Texts.Keys).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        public void SetNumber(string name, double v) { Texts.Remove(name); Flags.Remove(name); Nums[name] = v; }
        public void SetYesNo(string name, bool v) { Texts.Remove(name); Flags.Add(name); Nums[name] = v ? 1 : 0; }
        public void SetText(string name, string v) { Nums.Remove(name); Flags.Remove(name); Texts[name] = v ?? ""; }
        public bool Remove(string name) { bool a = Nums.Remove(name), b = Texts.Remove(name); Flags.Remove(name); return a || b; }

        /// <summary>The value read as a number (a text gives the number written in it, or 0).</summary>
        public double Number(string name) => Nums.TryGetValue(name, out var v) ? v : Texts.TryGetValue(name, out var t) ? TextToNumber(t) : 0;
        /// <summary>The value read as Yes/No (a number: not 0; a text: yes/true/on or a number that isn't 0).</summary>
        public bool YesNo(string name) => Nums.TryGetValue(name, out var v) ? v != 0 : Texts.TryGetValue(name, out var t) && TextToYesNo(t);
        /// <summary>The value read as text (a Yes/No gives "Yes"/"No", a number its digits).</summary>
        public string Text(string name) => Texts.TryGetValue(name, out var t) ? t
            : Nums.TryGetValue(name, out var v) ? (Flags.Contains(name) ? YesNoText(v != 0) : NumberText(v)) : "";

        /// <summary>Changes a variable's kind, converting its value: number → Yes/No = "not 0"; text → number = the number
        /// in it (or 0); Yes/No → text = "Yes"/"No"; number → text = its digits.</summary>
        public void ChangeKind(string name, VarKind to)
        {
            switch (to)
            {
                case VarKind.Number: SetNumber(name, Number(name)); break;
                case VarKind.YesNo: SetYesNo(name, YesNo(name)); break;
                default: SetText(name, Text(name)); break;
            }
        }

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
        /// Yes/No, other words are a Text. A Yes/No keeps its kind for a number (0/1); a Text keeps its kind for any string.
        /// <paramref name="shown"/> describes the change ("name=5", "name=yes", "name=\"Hi\"").</summary>
        public bool SetFromJson(string name, JsonElement e, out string shown)
        {
            shown = "";
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return SetBool(name, e.ValueKind == JsonValueKind.True, out shown);
            if (e.ValueKind == JsonValueKind.String)
            {
                var s = e.GetString() ?? "";
                var low = s.Trim().ToLowerInvariant();
                if (KindOf(name) == VarKind.Text) { SetText(name, s); shown = $"{name}=\"{s}\""; return true; }
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

        /// <summary>A short listing for the AI's scene summary: numbers as name=5, Yes/No as name=yes, texts as name="Hi".</summary>
        public string Describe()
        {
            var self = this;
            return string.Join(",", Names().Select(n => self.KindOf(n) switch
            {
                VarKind.Text => $"{n}=\"{(self.Texts[n].Length > 30 ? self.Texts[n][..30] + "…" : self.Texts[n])}\"",
                VarKind.YesNo => $"{n}={(self.YesNo(n) ? "yes" : "no")}",
                _ => $"{n}={self.Number(n).ToString("0.##", CultureInfo.InvariantCulture)}",
            }));
        }
    }
}
