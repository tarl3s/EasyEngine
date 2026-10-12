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
    //   {"do":"setcolor","var":"teamColor","to":"#FF8800"}               (a Color: "#RRGGBB" kept in the text store)
    //   {"do":"tint","color":"{teamColor}","target":"self"}              (paints a plain-colour object)
    // A Yes/No is a number 1/0 (it lives in _vars), so every behavior, condition and expression keeps reading it.
    // A Color is a text "#RRGGBB", so "{teamColor}" works in any colour field that goes through RuleColor / Substitute.
    // Lists live in PlayWindow.Lists.cs.
    public partial class PlayWindow
    {
        private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

        private partial class Entity
        {
            public Dictionary<string, string>? LocalTexts; // created the first time this object gets a text of its own
            public string? TintRaw;                        // the colour a tint action last painted it with (so "always" doesn't repaint)
        }

        // The game's starting variables (numbers, Yes/No, texts, colors and lists).
        private void SeedGlobalVars()
        {
            foreach (var kv in _project.Variables) _vars[kv.Key] = kv.Value;
            foreach (var kv in _project.Texts) _texts[kv.Key] = kv.Value;
            SeedLists(_project.Lists);
        }

        private void SeedSceneTexts(Scene s)
        {
            foreach (var kv in s.Texts) _texts[kv.Key] = kv.Value;
            SeedLists(s.Lists);
        }

        private static void SeedLocalTexts(Entity en, GameObjectData obj)
        {
            SeedLocalLists(en, obj);
            if (obj.Texts.Count == 0) return;
            en.LocalTexts ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in obj.Texts) en.LocalTexts[kv.Key] = kv.Value;
        }

        // A variable read as text: a text (or color) variable, a list's items joined with ", ", else a number variable's
        // digits (so "{coins}" and iftext work on numbers too).
        private string TextOf(string name)
            => _texts.TryGetValue(name, out var t) ? t : _lists.TryGetValue(name, out var l) ? VarScope.ListText(l)
             : _vars.TryGetValue(name, out var v) ? VarScope.NumberText(v) : "";

        private static string LocalTextOf(Entity? en, string name)
        {
            if (en == null) return "";
            if (en.LocalTexts != null && en.LocalTexts.TryGetValue(name, out var t)) return t;
            if (en.LocalLists != null && en.LocalLists.TryGetValue(name, out var l)) return VarScope.ListText(l);
            return en.Locals.TryGetValue(name, out var v) ? VarScope.NumberText(v) : "";
        }

        private static bool HasLocalText(Entity en, string name)
            => (en.LocalTexts != null && en.LocalTexts.ContainsKey(name)) || (en.LocalLists != null && en.LocalLists.ContainsKey(name)) || en.Locals.ContainsKey(name);

        // A colour field of a rule or step: "{teamColor}" (or any {placeholder}) is filled in first. A field that is just
        // one game variable ("{teamColor}") is read straight from the text store (no allocation for an "always" rule).
        private readonly Dictionary<string, string?> _soleVar = new();
        private string RuleColor(string value, Entity? self)
        {
            if (value.IndexOf('{') < 0) return value;
            if (!_soleVar.TryGetValue(value, out var name))
            {
                var t = value.Trim();
                name = t.Length > 2 && t[0] == '{' && t[^1] == '}' && t.IndexOf('{', 1) < 0 && t.IndexOf(':') < 0 && t.IndexOf('.') < 0 ? t[1..^1].Trim() : null;
                _soleVar[value] = name;
            }
            return name != null && _texts.TryGetValue(name, out var v) ? v : Substitute(value, self).Trim();
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
            if (r.Do == "setcolor") // a Color: only a colour "#RRGGBB" / "#AARRGGBB" is stored
            {
                if (VarScope.ColorHex(v) is not string hex)
                {
                    if (!r.WarnedUnknown) { r.WarnedUnknown = true; Warn("Script", en.Data.Name, $"setcolor {name}: \"{v}\" is not a colour like #FF8800 — ignored"); }
                    return;
                }
                v = hex;
            }
            if (r.Do == "addtext") v = (local ? LocalTextOf(en, name) : TextOf(name)) + v;
            if (local) SetLocalText(en, name, v); else _texts[name] = v;
        }

        // tint: paints an object (self or one named in "target") with a colour — a plain-colour object's fill; an object
        // drawn with a picture keeps its picture.
        private void DoTint(Entity en, CustomRule r)
        {
            var who = r.Str("target", "self");
            var tgt = who.Length == 0 || who.Equals("self", StringComparison.OrdinalIgnoreCase) ? en : FindAlive(who);
            if (tgt == null || tgt.Brush != null) return;
            var raw = RuleColor(RuleText(r, "color", "to", "value"), en);
            if (raw == tgt.TintRaw) return; // already painted with it (an "always" rule costs nothing)
            if (VarScope.ColorHex(raw) is not string hex) return;
            tgt.TintRaw = raw;
            tgt.Visual.Background = Brush(hex);
        }

        private Entity? FindAlive(string name)
        {
            foreach (var e in _entities) if (!e.Dead && e.Data.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
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
