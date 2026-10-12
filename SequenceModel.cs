using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace EasyEngine
{
    // Cutscenes and bosses are ordered lists of steps ({"do":"<id>", ...params}). This file is the single source of
    // truth for them: the step palette (used by the runtime, the Advanced Scratch editor and the AI prompt), the
    // parser (tolerant: synonyms, numbers as strings, missing fields → defaults) and the stable serializer.

    /// <summary>One field of a step. Types: num, val (number or expression), text, longtext, bool, choice,
    /// object (scene object / self / player), spawn (scene object or prefab), sound, color, var, key, scene.</summary>
    public record StepField(string Key, string Label, string Type, string Default = "", string[]? Options = null, string? OnlyIfEmpty = null);

    public record StepDef(string Id, string Label, string Category, StepField[] Fields, string Hint = "");

    public enum SeqMode { Cutscene, Boss }

    public static class StepCatalog
    {
        public static readonly (string name, string color)[] Categories =
        {
            ("Story", "#2EAA6B"), ("Motion", "#3B82F6"), ("Attack", "#E5484D"), ("Looks", "#9B59D0"),
            ("Camera", "#18A3A3"), ("Sound", "#E05AA8"), ("Control", "#F08C2E"),
        };

        public static string ColorOf(string category) => Categories.FirstOrDefault(c => c.name == category).color ?? "#888888";

        private static StepField Num(string k, string l, double d) => new(k, l, "num", d.ToString(CultureInfo.InvariantCulture));
        private static StepField Bool(string k, string l, bool d) => new(k, l, "bool", d ? "1" : "0");
        private static StepField F(string k, string l, string type, string d = "", params string[] options) => new(k, l, type, d, options.Length > 0 ? options : null);
        private static StepField At(string k, string l, string d) => new(k, l, "object", d, new[] { "|(a point: x, y)" });
        private static StepField X(string onlyIfEmpty) => new("x", "x", "num", "0", null, onlyIfEmpty);
        private static StepField Y(string onlyIfEmpty) => new("y", "y", "num", "0", null, onlyIfEmpty);

        /// <summary>Steps both cutscenes and bosses use.</summary>
        public static readonly StepDef[] Common =
        {
            new("say", "Say (waits for Enter)", "Story", new[] { F("text", "Text (| = next line)", "longtext", "Hello!"), F("speaker", "Speaker", "text") },
                "Shows a dialogue line and waits until the player presses Enter."),
            new("setvar", "Set a variable", "Story", new[] { F("var", "Variable", "var"), F("to", "To", "val", "1") }),
            new("addvar", "Add to a variable", "Story", new[] { F("var", "Variable", "var"), F("amount", "Amount", "val", "1") }),
            new("settime", "Set the time of day", "Story", new[] { F("time", "Time", "choice", "night", "day|Day", "noon|Noon", "dusk|Dusk", "night|Night", "midnight|Midnight") }),
            new("goscene", "Go to a scene", "Story", new[] { F("scene", "Scene", "scene") }),
            new("move", "Move an object", "Motion", new[] { F("object", "Object", "object", "player"), At("to", "To", ""), X("to"), Y("to"), Num("time", "Seconds", 1), Bool("wait", "Wait until it arrives", true) },
                "Slides the object in a straight line to another object or to a point."),
            new("face", "Turn to face", "Motion", new[] { F("object", "Object", "object", "self"), new StepField("toward", "Toward", "object", "player", new[] { "left|← Left", "right|→ Right" }) }),
            new("title", "Big title", "Looks", new[] { F("text", "Text", "text", "TITLE"), Num("time", "Seconds", 2), Bool("wait", "Wait until it's gone", true) }),
            new("fadeout", "Fade to a colour", "Looks", new[] { F("color", "Colour", "color", "#000000"), Num("time", "Seconds", 1) }),
            new("fadein", "Fade back in", "Looks", new[] { Num("time", "Seconds", 1) }),
            new("show", "Show an object", "Looks", new[] { F("object", "Object", "object", "self") }),
            new("hide", "Hide an object", "Looks", new[] { F("object", "Object", "object", "self") }),
            new("emit", "Burst of particles", "Looks", new[] { F("at", "At", "object", "self"), Num("count", "How many", 24), F("color", "Colour", "color", "#FFD24D") }),
            new("camera", "Move the camera", "Camera", new[] { At("object", "To", "self"), X("object"), Y("object"), Num("time", "Seconds", 1) },
                "Pans the camera to an object (or a point) and waits until it gets there."),
            new("cameraback", "Camera back to the player", "Camera", new[] { Num("time", "Seconds", 1) }),
            new("shake", "Shake the screen", "Camera", new[] { Num("strength", "Strength", 8), Num("time", "Seconds", 0.5) }),
            new("bars", "Cinema bars", "Camera", new[] { Bool("on", "On", true) }),
            new("playsound", "Play a sound", "Sound", new[] { F("sound", "Sound", "sound"), Num("volume", "Volume (0-1)", 0.8) }),
            new("wait", "Wait", "Control", new[] { Num("time", "Seconds", 1) }),
            new("lock", "Lock the player", "Control", Array.Empty<StepField>(), "The player's keys and clicks stop working (Enter still advances dialogue)."),
            new("unlock", "Unlock the player", "Control", Array.Empty<StepField>()),
            new("spawn", "Create an object", "Control", new[] { F("object", "Object", "spawn"), At("at", "At", "self"), X("at"), Y("at") }),
            new("destroy", "Destroy an object", "Control", new[] { F("object", "Object", "object", "self") }),
        };

        /// <summary>Steps only a boss has (self = the boss, player = the boss's target).</summary>
        public static readonly StepDef[] BossOnly =
        {
            new("telegraph", "Warn (flash)", "Attack", new[] { Num("time", "Seconds", 0.6), F("color", "Colour", "color", "#FF4040") },
                "The boss glows and shakes so the player sees the attack coming."),
            new("dash", "Dash at the target", "Motion", new[] { Num("speed", "Speed", 600), Num("time", "Seconds", 0.8) }),
            new("jump", "Jump", "Motion", new[] { Num("height", "Height", 220), Bool("toward", "Toward the target", true), Bool("shockwave", "Shockwave on landing", true), F("wave", "Shockwave object (optional)", "spawn") },
                "Leaps (landing near the target). The shockwave shakes the screen and sends a wave along the ground both ways."),
            new("walk", "Walk toward the target", "Motion", new[] { Num("speed", "Speed", 160), Num("time", "Seconds", 1.5) }),
            new("moveto", "Glide to", "Motion", new[] { At("to", "To", "player"), X("to"), Y("to"), Num("time", "Seconds", 1) }),
            new("shoot", "Shoot", "Attack", new[] { F("pattern", "Pattern", "choice", "ring", "ring|Ring (all around)", "spread|Fan (forward)", "aimed|Aimed at the target"),
                Num("count", "Bullets", 12), Num("speed", "Speed", 320), Num("spread", "Fan angle (°)", 60), F("bullet", "Bullet (optional)", "spawn"), Num("life", "Life (s)", 3),
                Num("volleys", "Volleys", 1), Num("every", "Every (s)", 0.4), Num("turn", "Turn per volley (°)", 0) },
                "Without a bullet object it fires small round bullets that hurt the target and vanish when they hit."),
            new("summon", "Summon minions", "Attack", new[] { F("object", "Object", "spawn"), Num("count", "How many", 2), Num("radius", "Distance (px)", 120) }),
            new("invulnerable", "Invulnerable", "Attack", new[] { Num("time", "Seconds", 1.5) }, "Takes no damage for a while (it flickers)."),
        };

        public static readonly StepDef[] Cutscene = Common;
        public static readonly StepDef[] Boss = BossOnly.Concat(Common).ToArray();

        public static StepDef[] For(SeqMode mode) => mode == SeqMode.Boss ? Boss : Cutscene;
        public static StepDef? Find(SeqMode mode, string id) => Array.Find(For(mode), d => d.Id == id);

        // Words people (and small AI models) use for a step id.
        private static readonly Dictionary<string, string> IdSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dialog"] = "say", ["dialogue"] = "say", ["talk"] = "say", ["speak"] = "say", ["message"] = "say", ["text"] = "say",
            ["pause"] = "wait", ["delay"] = "wait", ["sleep"] = "wait", ["fade"] = "fadeout", ["fadetoblack"] = "fadeout", ["unfade"] = "fadein",
            ["camerareturn"] = "cameraback", ["resetcamera"] = "cameraback", ["camerareset"] = "cameraback", ["pan"] = "camera", ["cameramove"] = "camera", ["movecamera"] = "camera",
            ["screenshake"] = "shake", ["camerashake"] = "shake", ["letterbox"] = "bars", ["cinemabars"] = "bars", ["showtitle"] = "title", ["banner"] = "title",
            ["fire"] = "shoot", ["bullets"] = "shoot", ["projectile"] = "shoot", ["projectiles"] = "shoot", ["ring"] = "shoot", ["bulletring"] = "shoot",
            ["charge"] = "dash", ["rush"] = "dash", ["leap"] = "jump", ["slam"] = "jump", ["jumpslam"] = "jump", ["groundpound"] = "jump",
            ["spawnminions"] = "summon", ["minions"] = "summon", ["callminions"] = "summon", ["invincible"] = "invulnerable", ["shield"] = "invulnerable", ["invulnerability"] = "invulnerable",
            ["flash"] = "telegraph", ["warn"] = "telegraph", ["warning"] = "telegraph", ["windup"] = "telegraph", ["glow"] = "telegraph",
            ["sound"] = "playsound", ["sfx"] = "playsound", ["play"] = "playsound", ["particles"] = "emit", ["explode"] = "emit", ["explosion"] = "emit", ["burst"] = "emit",
            ["set"] = "setvar", ["variable"] = "setvar", ["setvariable"] = "setvar", ["add"] = "addvar", ["walkto"] = "moveto", ["goto"] = "moveto", ["fly"] = "moveto", ["flyto"] = "moveto",
            ["turn"] = "face", ["look"] = "face", ["appear"] = "show", ["disappear"] = "hide", ["create"] = "spawn", ["remove"] = "destroy", ["kill"] = "destroy",
            ["lockplayer"] = "lock", ["unlockplayer"] = "unlock", ["freeze"] = "lock", ["unfreeze"] = "unlock", ["approach"] = "walk", ["chase"] = "walk", ["scene"] = "goscene", ["changescene"] = "goscene",
        };

        // Words used for a param name.
        private static readonly Dictionary<string, string[]> ParamSynonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["time"] = new[] { "duration", "seconds", "secs", "sec", "s", "t", "for" },
            ["text"] = new[] { "message", "line", "lines", "dialog", "dialogue", "title" },
            ["object"] = new[] { "target", "who", "name", "prefab", "minion", "minions", "what" },
            ["bullet"] = new[] { "prefab", "projectile", "object" },
            ["count"] = new[] { "number", "amount", "bullets", "n", "times" },
            ["volume"] = new[] { "vol" },
            ["sound"] = new[] { "file", "sfx", "asset" },
            ["strength"] = new[] { "intensity", "power", "amount" },
            ["to"] = new[] { "value", "target", "destination", "object" },
            ["amount"] = new[] { "by", "value", "add" },
            ["at"] = new[] { "where", "object", "target" },
            ["speaker"] = new[] { "who", "name", "character" },
            ["height"] = new[] { "force", "power" },
            ["toward"] = new[] { "towards", "attarget", "aim" },
            ["pattern"] = new[] { "type", "shape", "kind" },
            ["spread"] = new[] { "angle", "arc", "fan" },
            ["color"] = new[] { "colour", "tint" },
            ["on"] = new[] { "show", "enabled", "value" },
        };

        /// <summary>The step id for a word (lower-cased, punctuation dropped, synonyms resolved), or null if unknown in this mode.</summary>
        public static string? ResolveId(SeqMode mode, string raw)
        {
            var id = new string((raw ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (Find(mode, id) != null) return id;
            if (IdSynonyms.TryGetValue(id, out var syn) && Find(mode, syn) != null) return syn;
            return null;
        }

        /// <summary>The field a param name means for this step (its own name, else a synonym), or null.</summary>
        public static string? ResolveParam(StepDef def, string raw, ICollection<string> taken)
        {
            var key = raw.ToLowerInvariant();
            if (def.Fields.Any(f => f.Key == key)) return key;
            foreach (var f in def.Fields)
                if (!taken.Contains(f.Key) && ParamSynonyms.TryGetValue(f.Key, out var syns) && syns.Contains(key, StringComparer.OrdinalIgnoreCase))
                    return f.Key;
            return null;
        }
    }

    /// <summary>One step: its id and its params (as text; numbers are invariant-culture strings).</summary>
    public class SeqStep
    {
        public string Do = "";
        public Dictionary<string, string> P = new(StringComparer.OrdinalIgnoreCase);

        public string Str(string k, string d = "") => P.TryGetValue(k, out var v) ? v : d;
        public double Num(string k, double d = 0) =>
            P.TryGetValue(k, out var v) && LooseJson.TryParse(v, out var n) ? n : d;
        public bool On(string k, bool d = false) => P.TryGetValue(k, out var v) ? (LooseJson.TryParse(v, out var n) ? n >= 0.5 : d) : d;
        public SeqStep Clone() => new() { Do = Do, P = new Dictionary<string, string>(P, StringComparer.OrdinalIgnoreCase) };
    }

    public class CutsceneScript { public List<SeqStep> Steps = new(); }

    public class BossAttack { public string Name = "Attack"; public List<SeqStep> Steps = new(); }

    public class BossPhase
    {
        public string Name = "Phase";
        public string When = "start";      // "start" | "health"
        public double Value = 50;          // health % (at or below) for "health"
        public List<SeqStep> Enter = new();
        public List<string> Attacks = new();
        public string Order = "random";    // "random" | "inorder"
        public double Pause = 1;
    }

    public class BossScript
    {
        public List<BossAttack> Attacks = new();
        public List<BossPhase> Phases = new();
        public List<SeqStep> Death = new();
        public BossAttack? Attack(string name) => Attacks.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>A boss with attacks but no phases fights in one phase that uses every attack; a phase that lists
        /// no attacks uses all of them. (The game, the editor and the AI all read a script this way.)</summary>
        public void FillDefaultPhases(List<string>? notes = null)
        {
            if (Attacks.Count == 0) return;
            var all = Attacks.Select(a => a.Name).ToList();
            if (Phases.Count == 0)
            {
                Phases.Add(new BossPhase { Name = "Phase 1", Attacks = all });
                notes?.Add("no phases were given — added one that uses every attack");
            }
            foreach (var p in Phases.Where(p => p.Attacks.Count == 0))
            {
                p.Attacks = all.ToList();
                notes?.Add($"{p.Name} listed no attacks — it uses all of them");
            }
        }
    }

    /// <summary>Reads (tolerantly) and writes (stably) cutscene and boss scripts. The runtime, the editor, the inspector
    /// card and the AI all go through here, so they always agree on what a script means.</summary>
    public static class SequenceJson
    {
        private static readonly string[] IdKeys = { "do", "action", "type", "step", "attack", "id", "kind", "command" };

        // ------------------------------------------------------------------ parse
        public static CutsceneScript ParseCutscene(string json, out string? error, List<string>? notes = null)
        {
            var script = new CutsceneScript();
            notes ??= new List<string>();
            error = null;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json, LooseJson.Options);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (Prop(root, "cutscene", "scene") is JsonElement inner && inner.ValueKind is JsonValueKind.Object or JsonValueKind.Array) root = inner;
                }
                if (root.ValueKind == JsonValueKind.Object) root = Prop(root, "steps", "sequence", "actions", "script") ?? default;
                if (root.ValueKind == JsonValueKind.Array) script.Steps = ParseSteps(root, SeqMode.Cutscene, notes, "the cutscene");
                else if (root.ValueKind != JsonValueKind.Undefined) error = "Expected {\"steps\":[…]}";
            }
            catch (JsonException ex) { error = JsonError(ex); }
            return script;
        }

        public static BossScript ParseBoss(string json, out string? error, List<string>? notes = null)
        {
            var script = new BossScript();
            notes ??= new List<string>();
            error = null;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json, LooseJson.Options);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && Prop(root, "boss") is JsonElement b && b.ValueKind == JsonValueKind.Object) root = b;
                if (root.ValueKind != JsonValueKind.Object) { error = "The boss must be a JSON object {\"attacks\":…, \"phases\":…, \"death\":…}"; return script; }

                if (Prop(root, "attacks", "moves") is JsonElement atk)
                {
                    if (atk.ValueKind == JsonValueKind.Object)
                        foreach (var p in atk.EnumerateObject()) AddAttack(script, p.Name, p.Value, notes);
                    else if (atk.ValueKind == JsonValueKind.Array)
                        foreach (var a in atk.EnumerateArray())
                            if (a.ValueKind == JsonValueKind.Object)
                                AddAttack(script, Text(Prop(a, "name", "id", "title")) ?? $"Attack {script.Attacks.Count + 1}", Prop(a, "steps", "sequence", "actions") ?? default, notes);
                }

                if (Prop(root, "phases", "stages") is JsonElement ph && ph.ValueKind == JsonValueKind.Array)
                    foreach (var p in ph.EnumerateArray())
                        if (p.ValueKind == JsonValueKind.Object) script.Phases.Add(ParsePhase(p, script, notes));

                if (Prop(root, "death", "ondeath", "dies", "deathsequence", "defeat") is JsonElement d && d.ValueKind == JsonValueKind.Array)
                    script.Death = ParseSteps(d, SeqMode.Boss, notes, "the death sequence");
            }
            catch (JsonException ex) { error = JsonError(ex); }
            return script;
        }

        private static void AddAttack(BossScript script, string name, JsonElement steps, List<string> notes)
        {
            name = name.Trim();
            if (name.Length == 0) name = $"Attack {script.Attacks.Count + 1}";
            if (script.Attack(name) != null) { notes.Add($"attack '{name}' is listed twice — kept the first"); return; }
            script.Attacks.Add(new BossAttack { Name = name, Steps = steps.ValueKind == JsonValueKind.Array ? ParseSteps(steps, SeqMode.Boss, notes, $"attack '{name}'") : new List<SeqStep>() });
        }

        private static BossPhase ParsePhase(JsonElement p, BossScript script, List<string> notes)
        {
            var phase = new BossPhase { Name = Text(Prop(p, "name", "title", "id")) ?? $"Phase {script.Phases.Count + 1}" };
            // "when": {"type":"start"} | {"type":"health","value":60} | "start" | 60 — or a "health" number on the phase itself
            var when = Prop(p, "when", "starts", "trigger", "condition");
            double? health = Number(Prop(p, "health", "hp", "below", "at"), percent: true);
            if (when is JsonElement w)
            {
                if (w.ValueKind == JsonValueKind.Object)
                {
                    var type = (Text(Prop(w, "type", "kind", "when")) ?? "start").ToLowerInvariant();
                    double? v = Number(Prop(w, "value", "health", "hp", "percent", "below", "at"), percent: true);
                    if (type.Contains("health") || type.Contains("hp") || (v != null && !type.Contains("start"))) health = v ?? 50;
                }
                else if (w.ValueKind == JsonValueKind.Number) health = w.GetDouble();
                else if (w.ValueKind == JsonValueKind.String)
                {
                    var s = w.GetString() ?? "";
                    var digits = new string(s.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
                    if (s.ToLowerInvariant().Contains("health") || s.ToLowerInvariant().Contains("hp") || digits.Length > 0)
                        health = LooseJson.TryParse(digits, out var hv) ? hv : 50;
                }
            }
            if (health != null) { phase.When = "health"; phase.Value = Math.Clamp(health.Value, 0, 100); }

            if (Prop(p, "attacks", "uses", "moves") is JsonElement a && a.ValueKind == JsonValueKind.Array)
                foreach (var n in a.EnumerateArray())
                {
                    var name = n.ValueKind == JsonValueKind.Object ? Text(Prop(n, "name", "attack")) : Text(n);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var atk = script.Attack(name);
                    if (atk == null) { notes.Add($"{phase.Name} uses an attack that doesn't exist ('{name}') — removed"); continue; }
                    if (!phase.Attacks.Contains(atk.Name)) phase.Attacks.Add(atk.Name);
                }

            var order = (Text(Prop(p, "order", "pick", "mode")) ?? "random").ToLowerInvariant().Replace(" ", "");
            phase.Order = order is "inorder" or "order" or "sequence" or "sequential" or "cycle" or "loop" or "inturn" ? "inorder" : "random";
            phase.Pause = Math.Max(0, Number(Prop(p, "pause", "rest", "delay", "cooldown", "wait")) ?? 1);
            if (Prop(p, "enter", "onenter", "intro", "start", "onstart") is JsonElement en && en.ValueKind == JsonValueKind.Array)
                phase.Enter = ParseSteps(en, SeqMode.Boss, notes, $"'{phase.Name}' start");
            return phase;
        }

        /// <summary>A list of steps; unknown step ids are dropped (with a note), missing fields get their defaults.</summary>
        public static List<SeqStep> ParseSteps(JsonElement arr, SeqMode mode, List<string> notes, string where)
        {
            var list = new List<SeqStep>();
            foreach (var el in arr.EnumerateArray())
            {
                string? rawId = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
                string? idKey = null;
                if (el.ValueKind == JsonValueKind.Object)
                    foreach (var k in IdKeys)
                        if (Prop(el, k) is JsonElement v && v.ValueKind == JsonValueKind.String) { rawId = v.GetString(); idKey = k; break; }
                if (rawId == null) { notes.Add($"a step in {where} has no \"do\" — dropped"); continue; }
                var id = StepCatalog.ResolveId(mode, rawId);
                if (id == null) { notes.Add($"unknown step '{rawId}' in {where} — dropped"); continue; }
                var def = StepCatalog.Find(mode, id)!;
                var step = new SeqStep { Do = id };
                if (el.ValueKind == JsonValueKind.Object)
                    foreach (var p in el.EnumerateObject())
                    {
                        if (idKey != null && p.Name.Equals(idKey, StringComparison.OrdinalIgnoreCase)) continue;
                        var key = StepCatalog.ResolveParam(def, p.Name, step.P.Keys) ?? p.Name;
                        if (step.P.ContainsKey(key)) continue;
                        step.P[key] = ValueText(p.Value);
                    }
                foreach (var f in def.Fields)
                {
                    if (!step.P.TryGetValue(f.Key, out var v)) { step.P[f.Key] = f.Default; continue; }
                    if (f.Type == "bool") step.P[f.Key] = v.ToLowerInvariant() is "true" or "yes" or "on" ? "1" : v.ToLowerInvariant() is "false" or "no" or "off" ? "0" : v;
                    else if (f.Type == "choice" && f.Options != null)
                    {
                        var opt = f.Options.Select(ParamSources.Option).FirstOrDefault(o => o.value.Equals(v, StringComparison.OrdinalIgnoreCase));
                        step.P[f.Key] = opt.value ?? f.Default;
                    }
                }
                list.Add(step);
            }
            return list;
        }

        private static JsonElement? Prop(JsonElement o, params string[] names)
        {
            if (o.ValueKind != JsonValueKind.Object) return null;
            foreach (var n in names)
                foreach (var p in o.EnumerateObject())
                    if (p.Name.Replace("_", "").Equals(n, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }

        private static string? Text(JsonElement? e) => e is JsonElement v ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null : null;

        private static double? Number(JsonElement? e, bool percent = false) => e is JsonElement v && LooseJson.TryNumber(v, out var d, percent) ? d : null;

        private static string ValueText(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.Null => "",
            _ => v.GetRawText()
        };

        private static string JsonError(JsonException ex) =>
            $"Not valid JSON (line {(ex.LineNumber ?? 0) + 1}): " + (ex.Message.Split('.').FirstOrDefault() ?? ex.Message);

        // ------------------------------------------------------------------ serialize (stable: same model → same text)
        public static string Serialize(CutsceneScript s)
        {
            var sb = new StringBuilder("{\"steps\":");
            Steps(sb, s.Steps, SeqMode.Cutscene, "");
            return sb.Append('}').ToString();
        }

        public static string Serialize(BossScript s)
        {
            var sb = new StringBuilder("{\n  \"attacks\": {");
            for (int i = 0; i < s.Attacks.Count; i++)
            {
                sb.Append(i == 0 ? "\n" : ",\n").Append("    ").Append(Q(s.Attacks[i].Name)).Append(": ");
                Steps(sb, s.Attacks[i].Steps, SeqMode.Boss, "    ");
            }
            sb.Append(s.Attacks.Count > 0 ? "\n  },\n" : "},\n");
            sb.Append("  \"phases\": [");
            for (int i = 0; i < s.Phases.Count; i++)
            {
                var p = s.Phases[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"name\":").Append(Q(p.Name)).Append(",\"when\":");
                sb.Append(p.When == "health" ? $"{{\"type\":\"health\",\"value\":{N(p.Value)}}}" : "{\"type\":\"start\"}");
                sb.Append(",\"attacks\":[").Append(string.Join(",", p.Attacks.Select(Q))).Append(']');
                sb.Append(",\"order\":").Append(Q(p.Order)).Append(",\"pause\":").Append(N(p.Pause));
                if (p.Enter.Count > 0) { sb.Append(",\"enter\":"); Steps(sb, p.Enter, SeqMode.Boss, "    "); }
                sb.Append('}');
            }
            sb.Append(s.Phases.Count > 0 ? "\n  ],\n" : "],\n");
            sb.Append("  \"death\": ");
            Steps(sb, s.Death, SeqMode.Boss, "  ");
            return sb.Append("\n}").ToString();
        }

        private static void Steps(StringBuilder sb, List<SeqStep> steps, SeqMode mode, string indent)
        {
            if (steps.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[');
            for (int i = 0; i < steps.Count; i++)
                sb.Append(i == 0 ? "\n" : ",\n").Append(indent).Append("  ").Append(StepJson(steps[i], mode));
            sb.Append('\n').Append(indent).Append(']');
        }

        /// <summary>{"do":"id", fields in palette order, then any extra params sorted by name}. Empty texts are left out unless the field's default isn't empty ("" can mean "a point" there).</summary>
        public static string StepJson(SeqStep s, SeqMode mode)
        {
            var def = StepCatalog.Find(mode, s.Do);
            var parts = new List<string> { "\"do\":" + Q(s.Do) };
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in def?.Fields ?? Array.Empty<StepField>())
            {
                done.Add(f.Key);
                var v = s.Str(f.Key, f.Default);
                if (v.Length == 0 && f.Default.Length == 0 && f.Type is not ("num" or "bool")) continue;
                double d = 0;
                bool numeric = f.Type is "num" or "bool" ? LooseJson.TryParse(v, out d) : f.Type == "val" && IsNum(v, out d);
                parts.Add(Q(f.Key) + ":" + (numeric ? N(d) : Q(v)));
            }
            foreach (var kv in s.P.Where(kv => !done.Contains(kv.Key) && kv.Value.Length > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                parts.Add(Q(kv.Key.ToLowerInvariant()) + ":" + (IsNum(kv.Value, out var d) ? N(d) : Q(kv.Value)));
            return "{" + string.Join(",", parts) + "}";
        }

        private static bool IsNum(string v, out double d) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && !double.IsNaN(d) && !double.IsInfinity(d);
        private static string N(double d) => Math.Round(d, 4).ToString(CultureInfo.InvariantCulture);
        private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        public static string Q(string s) => JsonSerializer.Serialize(s, Relaxed);

        // ------------------------------------------------------------------ summaries + checks
        public static string Summary(SeqMode mode, string json, out bool ok)
        {
            string? err;
            if (mode == SeqMode.Cutscene)
            {
                var c = ParseCutscene(json, out err);
                ok = err == null;
                return err ?? "✓ " + Steps(c.Steps.Count);
            }
            var b = ParseBoss(json, out err);
            ok = err == null;
            if (err != null) return err;
            var s = "✓ " + (b.Phases.Count == 1 ? L.T("1 phase") : L.F("{0} phases", b.Phases.Count)) + " · " + (b.Attacks.Count == 1 ? L.T("1 attack") : L.F("{0} attacks", b.Attacks.Count));
            return b.Death.Count > 0 ? s + " · " + L.F("death: {0}", Steps(b.Death.Count)) : s;
        }

        private static string Steps(int n) => n == 1 ? L.T("1 step") : L.F("{0} steps", n);

        /// <summary>What an AI result must have to be usable (null = fine).</summary>
        public static string? Problem(BossScript b)
        {
            if (b.Attacks.Count == 0) return "the boss has no attacks";
            var empty = b.Attacks.FirstOrDefault(a => a.Steps.Count == 0);
            if (empty != null) return $"attack '{empty.Name}' has no steps";
            if (b.Phases.Count == 0) return "the boss has no phases";
            if (b.Phases.All(p => p.Attacks.Count == 0)) return "no phase uses any attack";
            return null;
        }

        public static string? Problem(CutsceneScript c) => c.Steps.Count == 0 ? "the cutscene has no steps" : null;
    }
}
