using System;
using System.Collections.Generic;

namespace EasyEngine
{
    // Text variables and Yes/No toggles at play time: a text store next to the number variables (_vars), each object's
    // own texts, and the Custom-rule actions/conditions that use them:
    //   {"do":"settext","var":"greeting","to":"Hello {playerName}!"}   (+ "local":true for the object's own text)
    //   {"do":"addtext","var":"log","to":" and more"}                  (appends)
    //   {"do":"toggle","var":"hasKey"}                                  (Yes/No: 0 ↔ 1)
    //   {"when":"iftext","var":"name","op":"is|isnot|contains|empty|notempty","value":"Bob","do":…}
    // A Yes/No is a number 1/0 (it lives in _vars), so every behavior, condition and expression keeps reading it.
    public partial class PlayWindow
    {
        private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

        private partial class Entity
        {
            public Dictionary<string, string>? LocalTexts; // created the first time this object gets a text of its own
        }

        // The game's starting variables (numbers, Yes/No and texts).
        private void SeedGlobalVars()
        {
            foreach (var kv in _project.Variables) _vars[kv.Key] = kv.Value;
            foreach (var kv in _project.Texts) _texts[kv.Key] = kv.Value;
        }

        private void SeedSceneTexts(Scene s)
        {
            foreach (var kv in s.Texts) _texts[kv.Key] = kv.Value;
        }

        private static void SeedLocalTexts(Entity en, GameObjectData obj)
        {
            if (obj.Texts.Count == 0) return;
            en.LocalTexts ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in obj.Texts) en.LocalTexts[kv.Key] = kv.Value;
        }

        // A variable read as text: a text variable, else a number variable's digits (so "{coins}" and iftext work on numbers too).
        private string TextOf(string name)
            => _texts.TryGetValue(name, out var t) ? t : _vars.TryGetValue(name, out var v) ? VarScope.NumberText(v) : "";

        private static string LocalTextOf(Entity? en, string name)
        {
            if (en == null) return "";
            if (en.LocalTexts != null && en.LocalTexts.TryGetValue(name, out var t)) return t;
            return en.Locals.TryGetValue(name, out var v) ? VarScope.NumberText(v) : "";
        }

        private static void SetLocalText(Entity en, string name, string v) => (en.LocalTexts ??= new(StringComparer.OrdinalIgnoreCase))[name] = v;

        // A rule's "local" switch (true / 1 / "yes"): the condition's (before "do") or the action's (after it — or before it
        // when the condition doesn't use one), so "iftext … local" and "settext …" never borrow each other's.
        private static bool RuleLocal(CustomRule r, bool trigger)
        {
            if (trigger)
                return r.WN.TryGetValue("local", out var w) ? w >= 0.5 : r.WT.TryGetValue("local", out var wt) && VarScope.TextToYesNo(wt);
            double n = r.Num("local", double.NaN);
            if (!double.IsNaN(n)) return n >= 0.5;
            return VarScope.TextToYesNo(r.Str("local"));
        }

        // An action's text value: the first of the keys that is present (a number is written as its digits).
        private static string RuleText(CustomRule r, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (r.T.TryGetValue(k, out var s)) return s;
                if (r.N.TryGetValue(k, out var n)) return VarScope.NumberText(n);
            }
            foreach (var k in keys) // written before "do"
            {
                var s = r.Str(k, "\u0001");
                if (s != "\u0001") return s;
                var n = r.Num(k, double.NaN);
                if (!double.IsNaN(n)) return VarScope.NumberText(n);
            }
            return "";
        }

        // settext / addtext / toggle
        private void DoTextAction(Entity en, CustomRule r)
        {
            string name = r.Str("var").Trim();
            if (name.Length == 0) return;
            bool local = RuleLocal(r, false);
            if (r.Do == "toggle")
            {
                if (local) en.Locals[name] = (en.Locals.TryGetValue(name, out var lv) ? lv : 0) >= 0.5 ? 0 : 1;
                else _vars[name] = Var(name) >= 0.5 ? 0 : 1;
                return;
            }
            string v = Substitute(RuleText(r, "to", "text", "value"), en);
            if (r.Do == "addtext") v = (local ? LocalTextOf(en, name) : TextOf(name)) + v;
            if (local) SetLocalText(en, name, v); else _texts[name] = v;
        }

        // iftext: compares a text variable (case-insensitive)
        private bool TextCondition(Entity en, CustomRule r)
        {
            string name = r.WStr("var").Trim();
            string cur = RuleLocal(r, true) ? LocalTextOf(en, name) : TextOf(name);
            string want = r.WT.TryGetValue("value", out var wv) ? wv : r.WN.TryGetValue("value", out var wn) ? VarScope.NumberText(wn) : "";
            if (want.IndexOf('{') >= 0) want = Substitute(want, en);
            switch (r.WStr("op", "is").Trim().ToLowerInvariant())
            {
                case "isnot": case "not": case "!=": return !string.Equals(cur.Trim(), want.Trim(), StringComparison.OrdinalIgnoreCase);
                case "contains": return cur.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0;
                case "empty": case "isempty": return string.IsNullOrWhiteSpace(cur);
                case "notempty": return !string.IsNullOrWhiteSpace(cur);
                default: return string.Equals(cur.Trim(), want.Trim(), StringComparison.OrdinalIgnoreCase); // "is"
            }
        }

        // ---- test hooks (--fulltest) ----
        public string TText(string name) => TextOf(name);
        public void TSetText(string name, string v) => _texts[name] = v;
        public string TLocalText(string obj, string name) => LocalTextOf(TE(obj), name);
        public string TLabel(string obj)
        {
            var e = TE(obj);
            if (e?.LabelBlock == null) return "";
            UpdateLabel(e, 0.016);
            return e.LabelBlock.Text;
        }
        public void TSaveGame() => SaveGame();
        public bool TLoadGame() => LoadGame();
    }
}
