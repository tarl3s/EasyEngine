using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyEngine
{
    /// <summary>
    /// Turns the AI's "creative" JSON into what the engine reads, before it is applied — so every model, from a 1.5B
    /// local one to a big cloud one, gets the same help. Behaviors: a value put in the wrong bag (a variable name in
    /// "nums", a number in "texts"), choices written as words ("axis":"y" → 1), a Custom written as a behavior,
    /// GoToScene's "trigger":"death". Rules: event/action synonyms (wait→after, onclick→click, fade→tween…), "target"
    /// instead of "name" on touch, "is"/"equals" comparisons. What can't be understood is reported, never guessed.
    /// </summary>
    public static class AiSpecFix
    {
        /// <summary>Every WHEN the runtime knows (PlayWindow.RunCustom / RunDeathRules).</summary>
        public static readonly HashSet<string> Events = new(StringComparer.OrdinalIgnoreCase)
        {
            "start", "always", "key", "keydown", "keypress", "timer", "after", "touch", "death", "ifvar", "iflocal", "hasitem", "iftext", "iflist",
            "isnight", "isday", "wheelup", "wheeldown", "expr", "click", "rightclick", "mousedown", "mouseover", "hover", "mousepress",
        };

        /// <summary>Every DO the runtime knows (PlayWindow.DoCustom).</summary>
        public static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase)
        {
            "velocity", "move", "accelerate", "accel", "jump", "rotate", "shoot", "spawn", "destroy", "hide", "show", "goscene", "setvar", "addvar",
            "setlocal", "addlocal", "settext", "addtext", "toggle", "setcolor", "tint", "listadd", "listremove", "listclear", "set", "additem",
            "removeitem", "boost", "setspeed", "say", "save", "load", "emit", "tween", "setvolume", "breaktile", "settime", "playsound", "log",
        };

        internal static readonly Dictionary<string, string> WhenSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["wait"] = "after", ["delay"] = "after", ["afterseconds"] = "after", ["aftertime"] = "after", ["once"] = "after", ["later"] = "after",
            ["every"] = "timer", ["interval"] = "timer", ["repeat"] = "timer", ["periodic"] = "timer", ["everyseconds"] = "timer",
            ["onclick"] = "click", ["clicked"] = "click", ["tap"] = "click", ["mouseclick"] = "click",
            ["collide"] = "touch", ["collision"] = "touch", ["collides"] = "touch", ["overlap"] = "touch", ["hit"] = "touch", ["touches"] = "touch",
            ["ontouch"] = "touch", ["contact"] = "touch", ["oncollision"] = "touch",
            ["die"] = "death", ["dies"] = "death", ["dead"] = "death", ["ondeath"] = "death", ["killed"] = "death", ["destroyed"] = "death",
            ["hold"] = "key", ["held"] = "key", ["keyheld"] = "key", ["whilekey"] = "key",
            ["press"] = "keypress", ["pressed"] = "keypress", ["onkey"] = "keypress", ["keypressed"] = "keypress", ["onkeypress"] = "keypress",
            ["begin"] = "start", ["onstart"] = "start", ["init"] = "start", ["spawn"] = "start", ["spawned"] = "start", ["create"] = "start",
            ["created"] = "start", ["levelstart"] = "start", ["gamestart"] = "start", ["appear"] = "start", ["load"] = "start",
            ["update"] = "always", ["frame"] = "always", ["forever"] = "always", ["tick"] = "always", ["loop"] = "always", ["everyframe"] = "always",
            ["condition"] = "expr", ["if"] = "expr", ["while"] = "expr", ["when"] = "expr",
            ["variable"] = "ifvar", ["var"] = "ifvar", ["ifvariable"] = "ifvar", ["whenvar"] = "ifvar",
            ["timeout"] = "after", ["leftclick"] = "click", ["ready"] = "start", ["keyhold"] = "key",
            ["touching"] = "touch", ["touched"] = "touch", ["hover"] = "mouseover", ["mouseenter"] = "mouseover", ["night"] = "isnight", ["day"] = "isday",
        };

        internal static readonly Dictionary<string, string> DoSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["message"] = "say", ["dialog"] = "say", ["dialogue"] = "say", ["talk"] = "say", ["speak"] = "say", ["showmessage"] = "say",
            ["showtext"] = "say", ["text"] = "say", ["print"] = "log",
            ["kill"] = "destroy", ["remove"] = "destroy", ["delete"] = "destroy", ["die"] = "destroy", ["despawn"] = "destroy",
            ["changescene"] = "goscene", ["loadscene"] = "goscene", ["scene"] = "goscene", ["nextlevel"] = "goscene", ["gotoscene"] = "goscene", ["gotolevel"] = "goscene",
            ["add"] = "addvar", ["increment"] = "addvar", ["increase"] = "addvar", ["addscore"] = "addvar", ["changevar"] = "addvar",
            ["assign"] = "setvar", ["setvariable"] = "setvar", ["variable"] = "setvar",
            ["spin"] = "rotate", ["turn"] = "rotate",
            ["sound"] = "playsound", ["play"] = "playsound",
            ["color"] = "tint", ["colour"] = "tint", ["changecolor"] = "tint", ["changecolour"] = "tint", ["paint"] = "tint",
            ["invisible"] = "hide", ["disappear"] = "hide", ["vanish"] = "hide",
            ["appear"] = "show", ["visible"] = "show", ["unhide"] = "show",
            ["particles"] = "emit", ["explode"] = "emit", ["explosion"] = "emit", ["burst"] = "emit",
            ["animate"] = "tween", ["glide"] = "tween", ["ease"] = "tween", ["fade"] = "tween", ["fadeout"] = "tween", ["fadein"] = "tween",
            ["grow"] = "tween", ["shrink"] = "tween",
            ["speed"] = "velocity", ["multiplyspeed"] = "boost", ["speedup"] = "boost",
            ["fire"] = "shoot", ["shot"] = "shoot", ["bullet"] = "shoot",
            ["create"] = "spawn", ["instantiate"] = "spawn", ["clone"] = "spawn",
            ["setproperty"] = "set", ["setprop"] = "set", ["property"] = "set",
            ["impulse"] = "velocity", ["setvelocity"] = "velocity", ["push"] = "velocity",
            ["giveitem"] = "additem", ["takeitem"] = "removeitem", ["give"] = "additem",
            ["particle"] = "emit", ["sparks"] = "emit", ["emitparticles"] = "emit", ["rotation"] = "rotate", ["destroyself"] = "destroy",
            ["addvariable"] = "addvar", ["console"] = "log", ["debug"] = "log", ["sfx"] = "playsound", ["moveat"] = "velocity",
            ["projectile"] = "shoot", ["hop"] = "jump", ["volume"] = "setvolume", ["accel"] = "accelerate",
        };

        private static readonly Dictionary<string, string> OpSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["is"] = "==", ["="] = "==", ["eq"] = "==", ["equals"] = "==", ["equal"] = "==", ["==="] = "==",
            ["isnot"] = "!=", ["not"] = "!=", ["ne"] = "!=", ["<>"] = "!=", ["!=="] = "!=", ["notequal"] = "!=",
            ["gt"] = ">", ["greater"] = ">", ["morethan"] = ">", ["lt"] = "<", ["less"] = "<", ["lessthan"] = "<",
            ["gte"] = ">=", ["ge"] = ">=", ["atleast"] = ">=", ["=>"] = ">=", ["lte"] = "<=", ["le"] = "<=", ["atmost"] = "<=", ["=<"] = "<=",
        };

        private static string Squeeze(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());
        private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToJsonString().Trim('"') ?? "";

        /// <summary>The AI's rules (raw JSON objects, as <see cref="CollectRules"/> gathers them), cleaned up.
        /// <paramref name="notes"/> gets one line per rule that had to be dropped. Never throws.</summary>
        public static string Rules(IEnumerable<string> rawRules, List<string> notes)
        {
            var outRules = new List<string>();
            foreach (var raw in rawRules)
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw, LooseJson.Options);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var props in Rule(doc.RootElement, notes))
                        outRules.Add("{" + string.Join(",", props.Select(p => JsonSerializer.Serialize(p.key) + ":" + (p.val?.ToJsonString() ?? "null"))) + "}");
                }
                catch (Exception) { notes.Add(L.T("Skipped a rule that couldn't be read.")); }
            }
            return "[" + string.Join(",", outRules) + "]";
        }

        // a value as a node — read leniently (a trailing comma inside it is fine); anything odd stays as its text
        private static JsonNode? Node(JsonElement e)
        {
            try { return JsonNode.Parse(e.GetRawText(), null, LooseJson.Options); }
            catch (Exception) { return JsonValue.Create(e.GetRawText()); }
        }

        // one rule in, zero (unusable) or more (a "teleport to x,y" becomes two) rules out — keeping key order (trigger params before "do")
        // (a rule may hold the same key twice — "var" for an ifvar event and for its setvar action — so it stays a list of pairs)
        private static IEnumerable<List<(string key, JsonNode? val)>> Rule(JsonElement r, List<string> notes)
        {
            // keys in lower case, in their order (params before "do" belong to the event)
            var props = r.EnumerateObject().Select(p => (key: p.Name.ToLowerInvariant(), val: Node(p.Value))).ToList();
            string Get(string k) => props.FirstOrDefault(p => p.key == k).val is { } v ? Str(v) : "";
            bool Has(string k) => props.Any(p => p.key == k);
            void Set(string k, JsonNode? v) { int i = props.FindIndex(p => p.key == k); if (i >= 0) props[i] = (k, v); else props.Add((k, v)); }
            void Rename(string from, string to) { int i = props.FindIndex(p => p.key == from); if (i >= 0 && !Has(to)) props[i] = (to, props[i].val); }
            foreach (var (from, to) in new[] { ("event", "when"), ("trigger", "when"), ("on", "when"), ("action", "do"), ("then", "do") }) if (!Has(to)) Rename(from, to);

            // the event
            var whenRaw = Get("when");
            var when = whenRaw.Length == 0 ? (Has("key") ? "keypress" : Has("every") ? "timer" : "start") : whenRaw.ToLowerInvariant();
            if (!Events.Contains(when)) when = WhenSynonyms.TryGetValue(Squeeze(when), out var w) ? w : when;
            if (when == "expr" && !Has("expr") && Has("var")) when = "ifvar"; // "if"/"condition" written with var/op/value
            if (!Events.Contains(when)) { notes.Add(L.F("Skipped a rule: \"{0}\" isn't an event the game knows.", whenRaw)); yield break; }
            Set("when", when);
            if (when == "after") foreach (var k in new[] { "time", "delay", "wait", "every" }) if (!Has("seconds") && Has(k)) Rename(k, "seconds");
            if (when == "timer") foreach (var k in new[] { "interval", "seconds", "time", "period" }) if (!Has("every") && Has(k)) Rename(k, "every");
            if (when == "touch") foreach (var k in new[] { "target", "object", "with", "other", "obj" }) if (!Has("name") && Has(k)) Rename(k, "name");
            if (when is "ifvar" or "iflocal" && OpSynonyms.TryGetValue(Get("op").Replace(" ", ""), out var op)) Set("op", op);

            // the action — some words mean different actions depending on what comes with them
            var doRaw = Get("do");
            var sq = Squeeze(doRaw).ToLowerInvariant();
            var act = Actions.Contains(doRaw) ? doRaw.ToLowerInvariant() : DoSynonyms.TryGetValue(sq, out var d) ? d : sq;
            if (act == "destroy" && sq is "remove" or "delete" or "take" && Has("item")) act = "removeitem";
            if (act == "velocity" && sq == "speed" && Has("factor")) act = "boost";
            if (act == "load" && (Has("scene") || Has("level"))) act = "goscene";
            if (act == "say" && sq is "text" or "showtext" && Has("var")) act = "settext";
            if (act == "setcolor" && Get("var").ToLowerInvariant() is "" or "color" or "colour" or "tint" or "fill") // "set my colour": paint it
            {
                act = "tint"; props.RemoveAll(p => p.key == "var");
                if (!Has("color")) Rename("to", "color");
            }
            if (!Actions.Contains(act))
            {
                // "teleport"/"moveto" to x,y = set x (and set y)
                if (sq is "teleport" or "moveto" or "goto" or "setposition" or "position" && (Has("x") || Has("y")))
                {
                    foreach (var axis in new[] { "x", "y" })
                    {
                        if (!Has(axis)) continue;
                        var one = props.Where(p => p.key is not ("do" or "x" or "y")).Select(p => (p.key, p.val?.DeepClone())).ToList();
                        one.Add(("do", "set")); one.Add(("prop", axis)); one.Add(("to", props.First(p => p.key == axis).val?.DeepClone()));
                        yield return one;
                    }
                    yield break;
                }
                notes.Add(L.F("Skipped a rule: \"{0}\" isn't an action the game knows.", doRaw));
                yield break;
            }
            Set("do", act);
            if (act == "tween")
            {
                if (!Has("prop")) Set("prop", sq is "grow" or "shrink" ? "scale" : "opacity");
                if (!Has("to") && !Has("expr")) Set("to", sq == "fadein" ? 1 : sq == "grow" ? 2 : sq == "shrink" ? 0.5 : 0);
                if (!Has("duration")) foreach (var k in new[] { "time", "over", "seconds" }) if (Has(k) && !(k == "seconds" && when == "after")) { Rename(k, "duration"); break; }
            }
            if (act == "tint" && !Has("color")) foreach (var k in new[] { "to", "colour", "value" }) if (Has(k)) { Rename(k, "color"); break; }
            if (act is "say" or "settext" && !Has(act == "say" ? "text" : "to")) foreach (var k in new[] { "text", "message", "msg", "line", "dialog", "value", "to" }) if (Has(k)) { Rename(k, act == "say" ? "text" : "to"); break; }
            if (act == "goscene" && !Has("scene")) foreach (var k in new[] { "to", "target", "name", "level" }) if (Has(k)) { Rename(k, "scene"); break; }
            if (act == "spawn" && !Has("object")) foreach (var k in new[] { "name", "prefab", "what", "target" }) if (Has(k) && !(when == "touch" && k == "name")) { Rename(k, "object"); break; }
            if (act is "addvar" or "addlocal" && !Has("amount")) foreach (var k in new[] { "value", "by", "delta" }) if (Has(k) && !(when is "ifvar" or "iflocal" && k == "value")) { Rename(k, "amount"); break; }
            if (act is "setvar" or "setlocal" && !Has("to") && !Has("expr")) foreach (var k in new[] { "value", "amount" }) if (Has(k) && !(when is "ifvar" or "iflocal" && k == "value")) { Rename(k, "to"); break; }
            if (act is "setvar" or "addvar" or "setlocal" or "addlocal" && !Has("var")) foreach (var k in new[] { "variable", "name" }) if (Has(k) && !(when == "touch" && k == "name")) { Rename(k, "var"); break; }

            // "expr: a - b" / expr "a - b" → the bare expression, once here instead of every frame in the game
            for (int i = 0; i < props.Count; i++)
                if (props[i].val is JsonValue sv && sv.TryGetValue<string>(out var text) && UnwrapExpr(text) is string inner) props[i] = (props[i].key, JsonValue.Create(inner));

            // "when" first, so a rule that wrote it last still reads its event params right
            int wi = props.FindIndex(p => p.key == "when");
            if (wi > 0) { var wp = props[wi]; props.RemoveAt(wi); props.Insert(0, wp); }
            yield return props;
        }

        private static readonly System.Text.RegularExpressions.Regex ExprWrap =
            new(@"^\s*expr\s*(?:[:=]\s*|\(\s*|(?=[""']))[""']?(.*?)[""']?\)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        /// <summary>"expr: a - b", "expr(a - b)", expr "a - b" → "a - b"; null when it isn't written that way ("exprCount + 1" stays).</summary>
        public static string? UnwrapExpr(string s) { var m = ExprWrap.Match(s); return m.Success ? m.Groups[1].Value : null; }

        /// <summary>The rules however the AI wrote them: an array, one rule object, or a JSON string holding either — as raw JSON.</summary>
        public static void CollectRules(JsonElement c, List<string> into)
        {
            if (c.ValueKind == JsonValueKind.String)
            {
                try { using var d = JsonDocument.Parse(c.GetString() ?? "", LooseJson.Options); CollectRules(d.RootElement.Clone(), into); } catch (Exception) { }
                return;
            }
            if (c.ValueKind == JsonValueKind.Object) { into.Add(c.GetRawText()); return; }
            if (c.ValueKind == JsonValueKind.Array)
                foreach (var r in c.EnumerateArray()) if (r.ValueKind == JsonValueKind.Object) into.Add(r.GetRawText());
        }

        /// <summary>{"kind":"Custom","texts":{"when":…,"do":…}} or {"kind":"Custom","rules":[…]} / texts.script — written as a behavior.</summary>
        public static void CustomAsBehavior(JsonElement item, List<string> into)
        {
            foreach (var k in new[] { "rules", "custom", "script" })
                if (item.TryGetProperty(k, out var r)) { CollectRules(r, into); return; }
            if (item.TryGetProperty("texts", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                if (t.TryGetProperty("script", out var s)) { CollectRules(s, into); return; }
                if (t.TryGetProperty("when", out _) || t.TryGetProperty("do", out _))
                {
                    var parts = t.EnumerateObject().Select(p => JsonSerializer.Serialize(p.Name) + ":" + p.Value.GetRawText()).ToList();
                    if (item.TryGetProperty("nums", out var n) && n.ValueKind == JsonValueKind.Object)
                        parts.AddRange(n.EnumerateObject().Select(p => JsonSerializer.Serialize(p.Name) + ":" + p.Value.GetRawText()));
                    into.Add("{" + string.Join(",", parts) + "}");
                }
            }
        }

        /// <summary>
        /// One behavior from the AI, cleaned up against the catalog: values in the bag their parameter belongs to, choices
        /// written as words turned into their value. Returns extra Custom rules it implies (GoToScene "on death").
        /// </summary>
        public static void Behavior(BehaviorInfo info, JsonObject item, List<string> extraRules)
        {
            var nums = item["nums"] as JsonObject ?? new JsonObject();
            var texts = item["texts"] as JsonObject ?? new JsonObject();
            // the object's physics written inside a behavior ({"kind":"Solid","nums":{"bounciness":0.8}}): handed to the caller
            foreach (var bag in new[] { item, item["nums"] as JsonObject })
                foreach (var k in new[] { "bounciness", "gravity", "gravityScale" })
                    if (bag != null && bag[k] is JsonNode pv && !info.Nums.Any(n => n.Key.Equals(k, StringComparison.OrdinalIgnoreCase)))
                    { item["_" + k.ToLowerInvariant()] = pv.DeepClone(); bag.Remove(k); }
            // params written right on the behavior: {"kind":"Patrol","speed":100}
            foreach (var kv in item.ToList())
            {
                if (kv.Key is "kind" or "nums" or "texts" || kv.Key.StartsWith('_')) continue;
                if (info.Nums.Any(n => n.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))) { nums[kv.Key] = kv.Value?.DeepClone(); item.Remove(kv.Key); }
                else if (info.Texts.Any(t => t.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))) { texts[kv.Key] = kv.Value?.DeepClone(); item.Remove(kv.Key); }
            }
            foreach (var kv in nums.ToList())
            {
                var np = info.Nums.FirstOrDefault(n => n.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                var tp = info.Texts.FirstOrDefault(t => t.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                if (np == null && tp != null) { nums.Remove(kv.Key); texts[tp.Key] = Str(kv.Value); continue; } // a name/variable in "nums"
                if (np != null && kv.Value is JsonValue jv && jv.TryGetValue<string>(out var s) && !LooseJson.TryParse(s, out _))
                {
                    if (ChoiceValue(np, s) is double cv) nums[kv.Key] = cv; else nums.Remove(kv.Key);
                }
                else if (np != null && kv.Value is JsonValue bv && bv.TryGetValue<bool>(out var b)) nums[kv.Key] = b ? 1 : 0;
            }
            foreach (var kv in texts.ToList())
            {
                var np = info.Nums.FirstOrDefault(n => n.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                var tp = info.Texts.FirstOrDefault(t => t.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                if (tp == null && np != null && (LooseJson.TryParse(Str(kv.Value), out _) || ChoiceValue(np, Str(kv.Value)) != null)) // a number in "texts"
                {
                    texts.Remove(kv.Key);
                    nums[np.Key] = LooseJson.TryParse(Str(kv.Value), out var n) ? n : ChoiceValue(np, Str(kv.Value));
                }
            }

            // Go To Scene "trigger" is WHO uses the door; "death"/"touch" there are the model's guesses at WHEN
            if (info.Kind == BehaviorKind.GoToScene && texts["trigger"] is JsonNode trig)
            {
                var t = Squeeze(Str(trig)).ToLowerInvariant();
                if (t is "death" or "die" or "dies" or "ondeath" or "killed")
                {
                    extraRules.Add("{\"when\":\"death\",\"do\":\"goscene\",\"scene\":" + JsonSerializer.Serialize(Str(texts["scene"])) + "}");
                    item["drop"] = true; // the caller removes this behavior: the rule does the job
                }
                else if (t is "touch" or "collision" or "collide" or "enter" or "player" or "contact" or "overlap" or "hit" or "") texts["trigger"] = "Player";
            }

            if (nums.Count > 0) item["nums"] = nums;
            if (texts.Count > 0) item["texts"] = texts;
        }

        // "y" / "vertical" / "Up and down" → the option's value; yes/no words → 1/0
        private static double? ChoiceValue(NumParam np, string word)
        {
            var w = word.Trim().ToLowerInvariant();
            if (w is "true" or "yes" or "on" or "sim") return 1;
            if (w is "false" or "no" or "off" or "não" or "nao") return 0;
            if (np.Key.Equals("axis", StringComparison.OrdinalIgnoreCase))
            {
                if (w is "x" or "horizontal" or "h" or "leftright" or "left-right" or "sideways") return 0;
                if (w is "y" or "vertical" or "v" or "updown" or "up-down" or "up and down") return 1;
            }
            foreach (var opt in np.Options ?? Array.Empty<string>())
            {
                int bar = opt.IndexOf('|');
                var label = bar >= 0 ? opt[(bar + 1)..].ToLowerInvariant() : opt.ToLowerInvariant();
                if (w.Length >= 2 && label.Contains(w) && double.TryParse(bar >= 0 ? opt[..bar] : opt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            }
            return null;
        }
    }
}
