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
            "isnight", "isday", "wheelup", "wheeldown", "expr", "click", "rightclick", "mousedown", "mouseover", "hover", "mousepress", "landed", "hurt", "interact",
            "keynear", "keyidle", "becomes", "allgone",
        };

        /// <summary>Every DO the runtime knows (PlayWindow.DoCustom).</summary>
        public static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase)
        {
            "velocity", "move", "accelerate", "accel", "jump", "rotate", "shoot", "spawn", "destroy", "hide", "show", "setactive", "goscene", "setvar", "addvar",
            "setlocal", "addlocal", "settext", "addtext", "toggle", "setcolor", "tint", "listadd", "listremove", "listclear", "set", "additem",
            "removeitem", "boost", "setspeed", "say", "save", "load", "emit", "tween", "setvolume", "breaktile", "settime", "playsound", "log",
            "controls", "ability", "setjump", "heal", "behavior", "playanim",
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
            ["land"] = "landed", ["lands"] = "landed", ["landing"] = "landed", ["touchground"] = "landed", ["hitground"] = "landed", ["onground"] = "landed",
            ["hurts"] = "hurt", ["damaged"] = "hurt", ["takedamage"] = "hurt", ["takesdamage"] = "hurt", ["injured"] = "hurt", ["gethurt"] = "hurt", ["getshurt"] = "hurt",
            ["interacted"] = "interact", ["interaction"] = "interact", ["used"] = "interact", ["activated"] = "interact",
            ["pressnear"] = "keynear", ["keypressnear"] = "keynear", ["nearkey"] = "keynear", ["presskeynear"] = "keynear", ["interactnear"] = "keynear", ["usenear"] = "keynear",
            ["keynearby"] = "keynear", ["pressnearby"] = "keynear", ["nearpress"] = "keynear", ["nearandpress"] = "keynear", ["keypressednear"] = "keynear",
            ["idle"] = "keyidle", ["notpressed"] = "keyidle", ["keynotpressed"] = "keyidle", ["nopress"] = "keyidle", ["idlekey"] = "keyidle", ["inactive"] = "keyidle", ["nokey"] = "keyidle",
            ["reaches"] = "becomes", ["varbecomes"] = "becomes", ["varreaches"] = "becomes", ["whenvarbecomes"] = "becomes", ["changesto"] = "becomes", ["reach"] = "becomes", ["become"] = "becomes",
            ["allcollected"] = "allgone", ["collectedall"] = "allgone", ["noneleft"] = "allgone", ["nonleft"] = "allgone", ["alldestroyed"] = "allgone", ["nomore"] = "allgone",
            ["allcleared"] = "allgone", ["cleared"] = "allgone", ["allpicked"] = "allgone", ["allkilled"] = "allgone", ["alldead"] = "allgone", ["allcollect"] = "allgone", ["collectall"] = "allgone",
        };

        internal static readonly Dictionary<string, string> DoSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["animation"] = "playanim", ["playanimation"] = "playanim", ["setanimation"] = "playanim", ["changeanimation"] = "playanim", ["anim"] = "playanim",
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
            ["cure"] = "heal", ["heals"] = "heal", ["addhealth"] = "heal", ["gainhealth"] = "heal", ["restorehealth"] = "heal", ["regen"] = "heal", ["healself"] = "heal",
            ["jumpheight"] = "setjump", ["setjumpheight"] = "setjump", ["jumppower"] = "setjump", ["setjumpforce"] = "setjump", ["changejump"] = "setjump",
            ["control"] = "controls", ["setcontrols"] = "controls", ["togglecontrols"] = "controls", ["disablecontrols"] = "controls", ["enablecontrols"] = "controls",
            ["freeze"] = "controls", ["lockcontrols"] = "controls", ["unlockcontrols"] = "controls",
            ["giveability"] = "ability", ["setability"] = "ability", ["unlockability"] = "ability", ["enableability"] = "ability", ["disableability"] = "ability",
            ["setbehavior"] = "behavior", ["togglebehavior"] = "behavior", ["enablebehavior"] = "behavior", ["disablebehavior"] = "behavior", ["behaviour"] = "behavior",
            ["activate"] = "setactive", ["deactivate"] = "setactive", ["turnon"] = "setactive", ["turnoff"] = "setactive", ["setenabled"] = "setactive",
            ["enableobject"] = "setactive", ["disableobject"] = "setactive", ["enablelayer"] = "setactive", ["disablelayer"] = "setactive", ["switchon"] = "setactive", ["switchoff"] = "setactive",
        };

        private static readonly Dictionary<string, string> OpSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["is"] = "==", ["="] = "==", ["eq"] = "==", ["equals"] = "==", ["equal"] = "==", ["==="] = "==",
            ["isnot"] = "!=", ["not"] = "!=", ["ne"] = "!=", ["<>"] = "!=", ["!=="] = "!=", ["notequal"] = "!=",
            ["gt"] = ">", ["greater"] = ">", ["morethan"] = ">", ["lt"] = "<", ["less"] = "<", ["lessthan"] = "<",
            ["gte"] = ">=", ["ge"] = ">=", ["atleast"] = ">=", ["=>"] = ">=", ["lte"] = "<=", ["le"] = "<=", ["atmost"] = "<=", ["=<"] = "<=",
        };

        /// <summary>The reply's overall shape fixed: "create"/"edit" written as ONE object become a list of one; a bare rule
        /// ({"when":…,"do":…}) or a bare list of rules becomes {"custom":[…]} (for the selected object or the player).</summary>
        public static string Shape(string json)
        {
            try
            {
                var root = JsonNode.Parse(json, null, LooseJson.Options);
                static bool IsRule(JsonNode? n) => n is JsonObject o && (o.ContainsKey("when") || o.ContainsKey("do"));
                if (root is JsonArray arr && arr.Count > 0 && arr.All(IsRule))
                    return new JsonObject { ["custom"] = arr }.ToJsonString();
                bool changed = false;
                if (root is JsonArray { Count: 1 } wrapped && wrapped[0] is JsonObject inner) { wrapped.Clear(); root = inner; changed = true; } // [{"create":…}]
                if (root is not JsonObject obj) return json;
                if (!obj.ContainsKey("create") && !obj.ContainsKey("edit") && IsRule(obj) && !obj.ContainsKey("custom"))
                    return new JsonObject { ["custom"] = new JsonArray(obj) }.ToJsonString();
                if (obj["rules"] is JsonArray rules && !obj.ContainsKey("custom")) { obj.Remove("rules"); obj["custom"] = rules; changed = true; } // the block editor's shape
                foreach (var key in new[] { "create", "edit" })
                    if (obj[key] is JsonObject one) { obj.Remove(key); obj[key] = new JsonArray(one); changed = true; }
                // an object written inside "level" ("boss":{"name":…,"behaviors":[…]}) is one more object to create
                if (obj["level"] is JsonObject lvl)
                    foreach (var (k, v) in lvl.ToList())
                        if (v is JsonObject piece && (piece.ContainsKey("behaviors") || piece.ContainsKey("name")))
                        {
                            lvl.Remove(k);
                            if (!piece.ContainsKey("name")) piece["name"] = char.ToUpperInvariant(k[0]) + k[1..];
                            if (obj["create"] is not JsonArray made) obj["create"] = made = new JsonArray();
                            made.Add(piece); changed = true;
                        }
                return changed ? obj.ToJsonString() : json;
            }
            catch (Exception) { return json; } // a key written twice etc.: read as it is
        }

        internal static bool IsOnOff(string s) => s.Trim().ToLowerInvariant() is "on" or "off" or "true" or "false" or "yes" or "no" or "1" or "0";
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
            foreach (var (from, to) in new[] { ("event", "when"), ("trigger", "when"), ("on", "when"), ("action", "do"), ("then", "do") }) if (!Has(to) && !(from == "on" && IsOnOff(Get("on")))) Rename(from, to); // "on":"off" is a switch, not an event

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
            if (when is "keynear" or "allgone") foreach (var k in new[] { "target", "object", "with", "near", "who", "obj", "what", "type" }) if (!Has("name") && Has(k)) Rename(k, "name");
            if (when == "keynear") foreach (var k in new[] { "distance", "range", "within", "dist" }) if (!Has("radius") && Has(k)) Rename(k, "radius");
            if (when == "keyidle") foreach (var k in new[] { "time", "delay", "after", "wait", "every" }) if (!Has("seconds") && Has(k)) Rename(k, "seconds");
            if (when is "ifvar" or "iflocal" or "becomes" && OpSynonyms.TryGetValue(Get("op").Replace(" ", ""), out var op)) Set("op", op);

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
            if (act is "controls" or "ability" or "behavior" && !Has("on")) // "disable controls", "give ability"…: which way it switches
            {
                if (sq.StartsWith("enable") || sq.StartsWith("unlock") || sq.StartsWith("give")) Set("on", "on");
                else if (sq.StartsWith("disable") || sq.StartsWith("lock") || sq == "freeze") Set("on", "off");
            }
            if (act == "setactive") // "deactivate", "turn off the Bridge", "active":false…
            {
                if (!Has("on")) foreach (var k in new[] { "active", "enabled", "state", "to" }) if (Has(k)) { Rename(k, "on"); break; }
                if (!Has("on")) Set("on", sq.StartsWith("deactivate") || sq.StartsWith("disable") || sq.EndsWith("off") ? "off" : "on");
                if (!Has("target") && !Has("layer")) foreach (var k in new[] { "object", "obj", "what" }) if (Has(k)) { Rename(k, "target"); break; }
            }
            if (act == "tween")
            {
                if (!Has("prop")) Set("prop", sq is "grow" or "shrink" ? "scale" : "opacity");
                if (!Has("to") && !Has("expr")) Set("to", sq == "fadein" ? 1 : sq == "grow" ? 2 : sq == "shrink" ? 0.5 : 0);
                if (!Has("duration")) foreach (var k in new[] { "time", "over", "seconds" }) if (Has(k) && !(k == "seconds" && when == "after")) { Rename(k, "duration"); break; }
            }
            if (act == "tint" && !Has("color")) foreach (var k in new[] { "to", "colour", "value" }) if (Has(k)) { Rename(k, "color"); break; }
            if (act is "say" or "settext" && !Has(act == "say" ? "text" : "to")) foreach (var k in new[] { "text", "message", "msg", "line", "dialog", "value", "to" }) if (Has(k)) { Rename(k, act == "say" ? "text" : "to"); break; }
            if (act == "playanim" && !Has("anim")) foreach (var k in new[] { "animation", "name", "to", "value" }) if (Has(k) && !(when == "touch" && k == "name")) { Rename(k, "anim"); break; }
            if (act == "goscene" && !Has("scene")) foreach (var k in new[] { "to", "target", "name", "level" }) if (Has(k)) { Rename(k, "scene"); break; }
            if (act == "spawn" && !Has("object")) foreach (var k in new[] { "name", "prefab", "what", "target" }) if (Has(k) && !(when is "touch" or "keynear" or "allgone" && k == "name")) { Rename(k, "object"); break; }
            if (act is "addvar" or "addlocal" && !Has("amount")) foreach (var k in new[] { "value", "by", "delta" }) if (Has(k) && !(when is "ifvar" or "iflocal" or "becomes" && k == "value")) { Rename(k, "amount"); break; }
            if (act is "setvar" or "setlocal" && !Has("to") && !Has("expr")) foreach (var k in new[] { "value", "amount" }) if (Has(k) && !(when is "ifvar" or "iflocal" or "becomes" && k == "value")) { Rename(k, "to"); break; }
            if (act is "setvar" or "addvar" or "setlocal" or "addlocal" && !Has("var")) foreach (var k in new[] { "variable", "name" }) if (Has(k) && !(when is "touch" or "keynear" or "allgone" && k == "name")) { Rename(k, "var"); break; }

            // a second action written into the same rule ({"do":"addvar",…,"additem":"Sword"}): its own rule, same event and cond
            var seconds = new List<List<(string key, JsonNode? val)>>();
            foreach (var (key, param) in SecondActions)
            {
                if (key == act || props.FindIndex(p => p.key == key) is var xi && xi < 0) continue;
                var v = Str(props[xi].val);
                props.RemoveAt(xi);
                int di = props.FindIndex(p => p.key == "do");
                if (v is "" or "false" or "0") continue;
                var two = props.Take(di).Where(p => p.key is not "do").Select(p => (p.key, p.val?.DeepClone())).ToList();
                if (!two.Any(p => p.key == "cond") && props.FirstOrDefault(p => p.key == "cond").val is JsonNode c) two.Add(("cond", c.DeepClone()));
                two.Add(("do", key));
                if (param != null && !(key == "emit" && !LooseJson.TryParse(v, out _)) && v != "true") two.Add((param, JsonValue.Create(v)));
                if (key is "additem" or "removeitem") two.Add(("amount", JsonValue.Create(1)));
                seconds.Add(two);
            }

            // "expr: a - b" / expr "a - b" → the bare expression, once here instead of every frame in the game
            for (int i = 0; i < props.Count; i++)
                if (props[i].val is JsonValue sv && sv.TryGetValue<string>(out var text) && UnwrapExpr(text) is string inner) props[i] = (props[i].key, JsonValue.Create(inner));

            // "when" first, so a rule that wrote it last still reads its event params right
            foreach (var one in seconds.Prepend(props))
            {
                int wi = one.FindIndex(p => p.key == "when");
                if (wi > 0) { var wp = one[wi]; one.RemoveAt(wi); one.Insert(0, wp); }
                else if (wi < 0) one.Insert(0, ("when", JsonValue.Create(when)));
                yield return one;
            }
        }

        /// <summary>
        /// Clean-ups that need the request and the object the rules belong to (both AI paths: the Build panel and the block
        /// editor), for what small models write but don't mean: a touch of the object itself (the other one is the player),
        /// "addvar score * 2" for doubling (= setvar), "keypress" for "while I hold", a loop/yoyo copied from the blink hint
        /// onto a one-time change. Takes and returns a rule array as JSON; never throws.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex Vanish = new(@"disappear|vanish|is removed|is destroyed|goes away|is gone|is collected|desaparec|\bsome\b|sumir|somem|se destr|destru[ií]d|eliminad", System.Text.RegularExpressions.RegexOptions.Compiled);
        // which of the two names the request says disappears: the one named last before "disappears" ("other" / "self"), or null
        private static string? WhoVanishes(string p, string touched, string own)
        {
            var m = Vanish.Match(p);
            if (!m.Success) return null;
            var before = p[..m.Index];
            own = System.Text.RegularExpressions.Regex.Replace(own, @"[\s_-]*\d+$", ""); // Star2 is "a Star" in the request
            int t = before.LastIndexOf(touched.ToLowerInvariant(), StringComparison.Ordinal), o = before.LastIndexOf(own.ToLowerInvariant(), StringComparison.Ordinal);
            if (t < 0 && o < 0) return null;
            return t > o ? "other" : "self";
        }
        public static string ForRequest(string rulesJson, string request, string ownName)
        {
            try
            {
                if (JsonNode.Parse(rulesJson, null, LooseJson.Options) is not JsonArray rules) return rulesJson;
                var p = (request ?? "").ToLowerInvariant();
                bool hold = System.Text.RegularExpressions.Regex.IsMatch(p, @"\b(hold|holds|holding|held|keep pressing|segur|mantén|mantener|pressionad)");
                bool repeat = System.Text.RegularExpressions.Regex.IsMatch(p, @"blink|pisca|parpad|pulse|pulsa|loop|repeat|again|forever|back and forth|vai e volta|sempre|siempre|keeps|non-stop");
                bool stopSpin = System.Text.RegularExpressions.Regex.IsMatch(p, @"\b(stop|stops|no longer|para de|parar de|pare de|parando de|deja de|dejar de|n[aã]o .{0,20}mais)\b.*\b(spin|rotat|gir|turn)");
                foreach (var r in rules.OfType<JsonObject>())
                {
                    var when = Str(r["when"]); var act = Str(r["do"]);
                    if (when is "touch" or "keynear" && ownName.Length > 0 && Str(r["name"]).Equals(ownName, StringComparison.OrdinalIgnoreCase)) r["name"] = "Player";
                    // "the player touches a Star and that Star disappears" written on the Player: the Star goes (target other);
                    // "… and the Heart disappears" written on the Heart: the Heart itself goes, whatever the model wrote
                    if (when == "touch" && act == "destroy" && Str(r["name"]) is { Length: > 0 } touched && ownName.Length > 0 && !touched.Equals(ownName, StringComparison.OrdinalIgnoreCase)
                        && WhoVanishes(p, touched, ownName) is string goes)
                    { if (goes == "other") r["target"] = "other"; else if (Str(r["target"]) is "other" or "") r.Remove("target"); }
                    if (act is "addvar" or "addlocal" && r["amount"] == null && Str(r["expr"] ?? r["to"]) is { Length: > 0 } f
                        && System.Text.RegularExpressions.Regex.IsMatch(f, $@"\b{System.Text.RegularExpressions.Regex.Escape(Str(r["var"]))}\b"))
                    { r["do"] = act == "addvar" ? "setvar" : "setlocal"; if (r["to"] == null) { r["to"] = f; r.Remove("expr"); } }
                    else if (act is "addvar" or "addlocal" && Str(r["amount"]) is { Length: > 0 } fa && Str(r["var"]) is { Length: > 0 } av
                        && System.Text.RegularExpressions.Regex.IsMatch(fa, $@"\b{System.Text.RegularExpressions.Regex.Escape(av)}\b\s*[*/]"))
                    { r["do"] = act == "addvar" ? "setvar" : "setlocal"; r["to"] = fa; r.Remove("amount"); } // "add score * 2" = double it
                    // "item:hasKey" for the variable hasKey the request names (items are things, not hasX flags)
                    if (Str(r["cond"]) is { Length: > 0 } cnd && cnd.Contains("item:"))
                        r["cond"] = System.Text.RegularExpressions.Regex.Replace(cnd, @"item:([a-z]\w*)", m => (request ?? "").Contains(m.Groups[1].Value) ? m.Groups[1].Value : m.Value);
                    if (hold && when == "keypress") { r["when"] = "key"; r.Remove("cooldown"); }
                    // "when score reaches 10, say Well done": a "while" event would say it every frame — it means the moment
                    if (when == "ifvar" && act is "say" or "playsound" or "emit" or "spawn" or "shoot" or "additem" or "removeitem" or "addvar" or "addlocal"
                        or "log" or "toggle" or "listadd" or "addtext" or "tween") r["when"] = "becomes";
                    if (!repeat && act == "tween") { r.Remove("loop"); r.Remove("yoyo"); }
                    if (stopSpin && act == "rotate") r["speed"] = 0; // "stop the Box from spinning" answered with the spin again
                }
                // "… I pay 10 coins and get a Sword": the item without the payment — the rule that takes the coins is added
                if (System.Text.RegularExpressions.Regex.Match(p, @"\b(?:pay|pays|paying|spend|spends|spending|costs?|pago|pague|pagar|paga|pagando|gasto|gasta|gastar|custa|cuesta|pagas)\s+(\d+)\s+(\w+)") is { Success: true } pay)
                {
                    double cost = double.Parse(pay.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var word = pay.Groups[2].Value;
                    foreach (var r in rules.OfType<JsonObject>().ToList())
                    {
                        if (Str(r["do"]) is not ("additem" or "setvar" or "addvar") || Str(r["when"]).Length == 0) continue;
                        // the variable paid with: the one the rule's condition checks against the cost, else the word itself
                        var cm = System.Text.RegularExpressions.Regex.Match(Str(r["cond"]), @"(\w+)\s*>=?\s*" + pay.Groups[1].Value + @"\b");
                        var v = cm.Success ? cm.Groups[1].Value : word;
                        if (Str(r["var"]).Equals(v, StringComparison.OrdinalIgnoreCase)) continue; // this rule is the payment
                        if (rules.OfType<JsonObject>().Any(o => Str(o["do"]) is "addvar" or "setvar" && Str(o["var"]).Equals(v, StringComparison.OrdinalIgnoreCase))) break;
                        var take = new JsonObject();
                        foreach (var k in new[] { "when", "key", "name", "radius", "cond", "block" }) if (r[k] is JsonNode n) take[k] = n.DeepClone();
                        take["do"] = "addvar"; take["var"] = v; take["amount"] = -cost;
                        rules.Insert(rules.IndexOf(r) + 1, take);
                        break;
                    }
                }
                return rules.ToJsonString();
            }
            catch (Exception) { return rulesJson; }
        }

        // actions a model sometimes adds as a key to a rule that already does something else, and the param their value is
        private static readonly (string action, string? param)[] SecondActions =
            { ("additem", "item"), ("removeitem", "item"), ("goscene", "scene"), ("say", "text"), ("tint", "color"), ("destroy", null), ("emit", "count"), ("spawn", "object") };

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
        // "while E is held" written as words or as a call: key(E), keydown:E, "E held", "key E pressed", "holding E", key:E → key:E >= 1
        private static readonly System.Text.RegularExpressions.Regex KeyCondRx = new(
            @"^(?:while\s+)?(?:(?:the\s+)?key(?:\s*[:(=]\s*|\s+)|key(?:down|held)(?:\s*[:(=]\s*|\s+)|hold(?:ing)?\s+(?:the\s+)?(?:key\s+)?)[""']?(\w+)[""']?\)?(?:\s+key)?(?:\s+(?:is\s+)?(?:held|pressed|down))?(?:\s*(?:>=\s*1|==\s*1|>\s*0))?$" +
            @"|^(?:while\s+)?(?:the\s+)?[""']?(\w+)[""']?(?:\s+key)?\s+(?:is\s+)?(?:held|pressed|down)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>A "key is held" condition in the shape the inspector picks (key:E &gt;= 1), or null when it isn't one.</summary>
        internal static string? KeyCond(string f)
        {
            var m = KeyCondRx.Match(f.Trim());
            if (!m.Success) return null;
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (PlayWindow.KeyByName(name) is not System.Windows.Input.Key k) return null;
            return $"key:{k} >= 1";
        }

        public static void Behavior(BehaviorInfo info, JsonObject item, List<string> extraRules)
        {
            var nums = item["nums"] as JsonObject ?? new JsonObject();
            var texts = item["texts"] as JsonObject ?? new JsonObject();
            // "only while" written on the behavior or in its nums: a formula, kept in texts.cond
            foreach (var bag in new[] { item, nums })
                foreach (var k in new[] { "cond", "condition", "onlyWhile", "while" })
                    if (KeyOf(bag, k) is string real && bag[real] is JsonNode cv) { bag.Remove(real); texts["cond"] = Str(cv); }
            if (texts["cond"] is JsonNode c0)
                texts["cond"] = Str(c0).Trim() switch { var f when f.Equals("isnight", StringComparison.OrdinalIgnoreCase) || f.Equals("night", StringComparison.OrdinalIgnoreCase) => "isNight >= 1",
                                                        var f when f.Equals("isday", StringComparison.OrdinalIgnoreCase) || f.Equals("day", StringComparison.OrdinalIgnoreCase) => "isNight < 1", var f => KeyCond(f) ?? f };
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
            // keys spelled as the catalog spells them ("Speed" → speed): the game reads them exactly
            var catalogKeys = info.Nums.Select(n => n.Key).Concat(info.Texts.Select(t => t.Key)).ToList();
            foreach (var bag in new[] { nums, texts })
                foreach (var kv in bag.ToList())
                    if (catalogKeys.FirstOrDefault(k => k.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)) is string canon && canon != kv.Key && !bag.ContainsKey(canon))
                    { var v = kv.Value?.DeepClone(); bag.Remove(kv.Key); bag[canon] = v; }
            // a button / zone / door whose scene was written under another name ("to":"Level2", "level":"Level2")
            if (info.Kind is BehaviorKind.UIButton or BehaviorKind.Area or BehaviorKind.GoToScene && texts["scene"] == null && Str(texts["action"]) is "" or "goscene") // ("to" is a value for setvar)
                foreach (var bag in new[] { texts, nums, item })
                    foreach (var k in new[] { "to", "goto", "level", "destination", "target", "onclick", "action", "click" })
                        if (texts["scene"] == null && KeyOf(bag, k) is string real && bag[real] is JsonValue sv && sv.TryGetValue<string>(out var sc) && GoSceneTarget(k, sc) is string sc2
                            && !(k == "target" && info.Kind == BehaviorKind.Area)) // an Area's "target" is who enters
                        { texts["scene"] = sc2; bag.Remove(real); if (k == "action") texts["action"] = "goscene"; }
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

            // a Light's shape / direction / effect in words ("flashlight" → beam, "cursor" → mouse, "candle" → flicker),
            // its shape under another name ("type":"flashlight") and its cone under another name ("angle":60)
            if (info.Kind == BehaviorKind.LightSource)
            {
                foreach (var k in new[] { "type", "style", "form", "mode", "kind" })
                    if (texts["shape"] == null && KeyOf(texts, k) is string real && LightShapes.Shape(Str(texts[real])) != LightShapes.Circle)
                    { texts["shape"] = Str(texts[real]); texts.Remove(real); }
                foreach (var k in new[] { "angle", "coneAngle", "cone", "fov", "beamWidth", "coneWidth" })
                    if (nums["spread"] == null && KeyOf(nums, k) is string real && nums[real] is JsonNode sv) { nums["spread"] = sv.DeepClone(); nums.Remove(real); }
                if (texts["shape"] is JsonNode sh) texts["shape"] = LightShapes.Shape(Str(sh));
                if (texts["direction"] is JsonNode dir) texts["direction"] = LightShapes.Direction(Str(dir));
                if (texts["effect"] is JsonNode fx) texts["effect"] = LightShapes.Effect(Str(fx));
            }

            if (info.Kind == BehaviorKind.SoundEmitter) SpatialSoundParams(item, nums, texts);

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

        // A Sound heard only nearby / placed left or right, in the model's words: "stereo":true / "3d":true / "spatial":1,
        // "maxDistance":500 / "hearingRange":500 / "range":500 (a distance, not on/off), "radius" alone, "heardBy":"Hero".
        private static void SpatialSoundParams(JsonObject item, JsonObject nums, JsonObject texts)
        {
            static double? OnOff(JsonNode? v)
            {
                var s = Str(v).Trim().ToLowerInvariant();
                if (s is "true" or "yes" or "on" or "sim" or "stereo") return 1;
                if (s is "false" or "no" or "off" or "não" or "nao" or "mono") return 0;
                return LooseJson.TryParse(s, out var n) ? (n >= 0.5 ? 1 : 0) : null;
            }
            static bool Take(JsonObject[] bags, string[] keys, out JsonNode? value)
            {
                foreach (var bag in bags)
                    foreach (var k in keys)
                        if (KeyOf(bag, k) is string real) { value = bag[real]; bag.Remove(real); return true; }
                value = null; return false;
            }
            var bags = new[] { nums, texts, item };
            // the whole thing at once: placed in space = heard nearby AND left/right
            if (Take(bags, new[] { "spatial", "3d", "positional", "surround", "spatialSound", "sound3d" }, out var all) && OnOff(all) is double a)
            { nums["range"] = a; nums["pan"] = a; }
            if (Take(bags, new[] { "stereo", "panning", "balance", "leftRight", "headphones", "pan3d", "earSide" }, out var pv) && OnOff(pv) is double p) nums["pan"] = p;
            if (Take(bags, new[] { "maxDistance", "distance", "hearingRadius", "hearRadius", "hearingRange", "hearRange", "maxRange", "audibleRange", "falloff", "rolloff", "reach" },
                     out var dv) && LooseJson.TryParse(Str(dv), out var d) && d > 1)
            { nums["radius"] = d; nums["range"] = 1; }
            // "range":500 is a distance (on/off is 0/1)
            if (nums["range"] is JsonNode rv)
            {
                if (LooseJson.TryParse(Str(rv), out var r) && r > 1) { nums["radius"] ??= r; nums["range"] = 1; }
                else if (OnOff(rv) is double ro) nums["range"] = ro;
            }
            if (nums["radius"] != null && nums["range"] == null) nums["range"] = 1; // a radius alone means "only heard nearby"
            if (texts["listener"] == null && Take(bags, new[] { "heardBy", "hearer", "ear", "listenerObject", "listenTo" }, out var lv) && Str(lv).Trim().Length > 0)
                texts["listener"] = Str(lv).Trim();
        }

        // the key as the model wrote it ("onClick" for "onclick")
        private static string? KeyOf(JsonObject o, string k) => o.Select(kv => kv.Key).FirstOrDefault(x => x.Equals(k, StringComparison.OrdinalIgnoreCase));

        // the scene in "Level2" (for to/goto/level…) or "goscene Level2" / "go to Level2" (any of them); null if neither
        private static string? GoSceneTarget(string key, string value)
        {
            var v = value.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(v, @"^(?:goscene|go\s*to|goto|load)[\s:]+(.+)$|^scene\s*:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
            if (key is "onclick" or "action" or "click") return null; // a plain word there is an action name ("goscene", "say")
            return v.Length > 0 && !LooseJson.TryParse(v, out _) ? v : null;
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
