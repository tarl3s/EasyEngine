using System;
using System.Collections.Generic;

namespace EasyEngine
{
    // List variables at play time: ordered lists of words (quests, collected names…), the game's/scene's in _lists and
    // each object's own, with the Custom-rule actions/conditions that use them:
    //   {"do":"listadd","var":"quests","value":"Find the key"}       (value may hold {placeholders}; + "local":true)
    //   {"do":"listremove","var":"quests","value":"Find the key"}    (the first item equal to it, capital letters don't matter)
    //   {"do":"listclear","var":"quests"}
    //   {"when":"iflist","var":"quests","op":"contains|notcontains|empty|notempty|countis|countatleast","value":"…","do":…}
    // "{quests}" in a text shows the items joined with ", "; "count:quests" (in an expression or "{count:quests}") is how
    // many items it has ("count:local.quests" for the object's own list).
    public partial class PlayWindow
    {
        private const string CountPrefix = "count:";
        private readonly Dictionary<string, List<string>> _lists = new(StringComparer.OrdinalIgnoreCase);

        private partial class Entity
        {
            public Dictionary<string, List<string>>? LocalLists; // created the first time this object gets a list of its own
        }

        // copies, so playing never changes the project's starting lists
        private void SeedLists(Dictionary<string, List<string>> from)
        {
            foreach (var kv in from) _lists[kv.Key] = new List<string>(kv.Value);
        }

        private static void SeedLocalLists(Entity en, GameObjectData obj)
        {
            if (obj.Lists.Count == 0) return;
            en.LocalLists ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in obj.Lists) en.LocalLists[kv.Key] = new List<string>(kv.Value);
        }

        // The list a rule talks about (created empty when <paramref name="create"/>), or null.
        private List<string>? ListOf(Entity? en, string name, bool local, bool create)
        {
            if (local)
            {
                if (en == null) return null;
                if (en.LocalLists != null && en.LocalLists.TryGetValue(name, out var ll)) return ll;
                if (!create) return null;
                return (en.LocalLists ??= new(StringComparer.OrdinalIgnoreCase))[name] = new List<string>();
            }
            if (_lists.TryGetValue(name, out var l)) return l;
            return create ? _lists[name] = new List<string>() : null;
        }

        // count:quests → how many items (count:local.quests = the object's own list); 0 when there's no such list
        private double ListCount(string name, Entity? self)
        {
            name = name.Trim();
            bool local = name.StartsWith("local.", StringComparison.OrdinalIgnoreCase);
            return ListOf(self, local ? name.Substring(6) : name, local, false)?.Count ?? 0;
        }

        private static int IndexOfItem(List<string> list, string item)
        {
            var want = item.Trim();
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Trim(), want, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // listadd / listremove / listclear
        private void DoListAction(Entity en, CustomRule r)
        {
            string name = r.Str("var").Trim();
            if (name.Length == 0) return;
            var list = ListOf(en, name, RuleLocal(r, false), true)!;
            if (r.Do == "listclear") { list.Clear(); return; }
            string item = Substitute(RuleText(r, "value", "item", "text", "to"), en);
            if (r.Do == "listadd") { if (item.Trim().Length > 0) list.Add(item); return; }
            int at = IndexOfItem(list, item); // listremove
            if (at >= 0) list.RemoveAt(at);
        }

        // iflist: contains / notcontains (an item equal to the value, capital letters don't matter), empty / notempty,
        // countis / countatleast (the value is a number)
        private bool ListCondition(Entity en, CustomRule r)
        {
            string name = r.WStr("var").Trim();
            var list = ListOf(en, name, RuleLocal(r, true), false);
            int count = list?.Count ?? 0;
            bool hasValue = r.WT.ContainsKey("value") || r.WN.ContainsKey("value");
            string op = r.WStr("op", hasValue ? "contains" : "notempty").Trim().ToLowerInvariant();
            switch (op)
            {
                case "empty": case "isempty": return count == 0;
                case "notempty": return count > 0;
                case "countis": case "countatleast": case "count": case "atleast":
                {
                    double want = r.WN.TryGetValue("value", out var wn) ? wn
                                : r.WT.TryGetValue("value", out var wt) ? VarScope.TextToNumber(wt.IndexOf('{') >= 0 ? Substitute(wt, en) : wt) : 0;
                    return op is "countis" or "count" ? Math.Abs(count - want) < 0.0001 : count >= want - 0.0001;
                }
            }
            string item = r.WT.TryGetValue("value", out var v) ? v : r.WN.TryGetValue("value", out var n) ? VarScope.NumberText(n) : "";
            if (item.IndexOf('{') >= 0) item = Substitute(item, en);
            bool has = list != null && IndexOfItem(list, item) >= 0;
            return op is "notcontains" or "doesnotcontain" or "lacks" ? !has : has; // "contains"
        }

        // ---- test hooks (--fulltest) ----
        public List<string> TList(string name) => _lists.TryGetValue(name, out var l) ? new List<string>(l) : new List<string>();
        public List<string> TLocalList(string obj, string name) => ListOf(TE(obj), name, true, false) is { } l ? new List<string>(l) : new List<string>();
        public void TSetList(string name, params string[] items) => _lists[name] = new List<string>(items);
        public double TEval(string expr, string? obj = null) => EvalExpr(expr, obj == null ? null : TE(obj));
        public string TFill(string text, string? obj = null) => Substitute(text, obj == null ? null : TE(obj));
        public string TLastParticleColor => _particles.Count == 0 ? "" : (_particles[^1].V.Background as System.Windows.Media.SolidColorBrush)?.Color.ToString() ?? "";
        public string TBackground(string obj) =>(TE(obj)?.Visual?.Background as System.Windows.Media.SolidColorBrush)?.Color.ToString() ?? "";
    }
}
