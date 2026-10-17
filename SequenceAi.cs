using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyEngine
{
    /// <summary>The outcome of "✨ Ask the AI" in a Boss AI / Cutscene card.</summary>
    public record SeqAiResult(bool Ok, string Json, List<string> Notes, string Error, int Calls);

    /// <summary>"✨ Ask the AI" for bosses and cutscenes: a dedicated prompt (the JSON format and every step, generated
    /// from the step palette), then the reply is normalised and checked with the same parser the game uses; a reply
    /// that can't be used is retried once with the problem explained.</summary>
    public static class SequenceAi
    {
        public static string SystemPrompt(SeqMode mode, Project project, Scene scene, bool compact)
        {
            var sb = new StringBuilder();
            bool boss = mode == SeqMode.Boss;
            sb.AppendLine(boss
                ? "You design a BOSS FIGHT for a 2D game engine. Output ONLY a JSON object of this shape:"
                : "You write a CUTSCENE for a 2D game engine. Output ONLY a JSON object of this shape:");
            if (boss)
            {
                sb.AppendLine("{\"boss\":{\"attacks\":{\"<Attack name>\":[<step>, ...], ...},");
                sb.AppendLine(" \"phases\":[{\"name\":\"Phase 1\",\"when\":{\"type\":\"start\"},\"attacks\":[\"<attack names>\"],\"order\":\"random\",\"pause\":1,\"enter\":[<steps>]},");
                sb.AppendLine("           {\"name\":\"Phase 2\",\"when\":{\"type\":\"health\",\"value\":50},\"attacks\":[...],\"order\":\"inorder\",\"pause\":0.6}],");
                sb.AppendLine(" \"death\":[<steps>]}}");
                sb.AppendLine("RULES:");
                sb.AppendLine("- An attack is a list of steps run in order. Give each attack a short name. A good attack warns first (telegraph) then strikes.");
                sb.AppendLine("- Phases: the first has \"when\":{\"type\":\"start\"}; the next ones start when health is at or below \"value\" percent (decreasing: e.g. 60 then 25). \"attacks\" lists attack names used in that phase. \"order\": \"random\" or \"inorder\". \"pause\": seconds of rest between attacks. \"enter\": steps played once when the phase starts (optional).");
                sb.AppendLine("- \"death\": steps played when the boss is defeated (shake, particles, a last line, set a variable…).");
                sb.AppendLine("- Put every attack you make in at least one phase. Later phases should be harder (more attacks, shorter pauses).");
                sb.AppendLine("- In steps, \"self\" = the boss and \"player\" = the player.");
            }
            else
            {
                sb.AppendLine("{\"steps\":[<step>, <step>, ...]}");
                sb.AppendLine("RULES:");
                sb.AppendLine("- The steps run one after another. say, wait, title, move, camera, fadeout/fadein wait until they finish.");
                sb.AppendLine("- Objects are named by their scene name; \"self\" = the object with the cutscene, \"player\" = the player.");
                sb.AppendLine("- A typical cutscene: camera to something, a few say lines, camera back, maybe set a variable at the end.");
            }
            sb.AppendLine("- A step is {\"do\":\"<id>\", <params>}. Numbers are plain numbers (seconds, pixels). Colours are \"#RRGGBB\". Use ONLY these step ids:");
            foreach (var d in StepCatalog.For(mode))
            {
                var ps = string.Join(", ", d.Fields.Select(f => f.Type == "choice" && f.Options != null
                    ? $"{f.Key}={string.Join("|", f.Options.Select(o => ParamSources.Option(o).value))}"
                    : $"{f.Key}={(f.Default.Length > 0 ? f.Default : "\"\"")}"));
                sb.Append("  ").Append(d.Id).Append(ps.Length > 0 ? ": " + ps : "");
                if (!compact && d.Hint.Length > 0) sb.Append("  — ").Append(d.Hint);
                sb.AppendLine();
            }
            if (!compact)
                sb.AppendLine("Positions: camera/move/moveto/spawn take an object name, or \"\" and then x, y (pixels, top-left of the scene). shoot pattern: ring = all around, spread = a fan toward the player's side, aimed = a fan pointed at the player; spread = the fan's angle in degrees; volleys/every/turn repeat it (turn rotates each volley: spirals). Leave \"bullet\" empty for the boss's own bullets.");

            var objects = ParamSources.Objects(scene);
            var prefabs = ParamSources.PrefabsNotIn(project, objects);
            sb.AppendLine("OBJECTS in the scene: " + (objects.Count > 0 ? string.Join(", ", objects) : "(none)"));
            if (prefabs.Count > 0) sb.AppendLine("PREFABS (for spawn/summon/bullet): " + string.Join(", ", prefabs));
            var sounds = ParamSources.Sounds(project);
            if (sounds.Count > 0) sb.AppendLine("SOUNDS (for playsound): " + string.Join(", ", sounds));
            var vars = ParamSources.Variables(project).Where(v => !ParamSources.EngineVariables.Contains(v)).ToList();
            if (vars.Count > 0) sb.AppendLine("VARIABLES: " + string.Join(", ", vars));
            var texts = ParamSources.TextVariables(project);
            if (texts.Count > 0) sb.AppendLine("TEXT VARIABLES (for settext; {name} in a text shows a variable): " + string.Join(", ", texts));
            var colors = ParamSources.ColorVariables(project);
            if (colors.Count > 0) sb.AppendLine("COLOUR VARIABLES (for setcolor; \"{name}\" in a colour field uses one): " + string.Join(", ", colors));
            var lists = ParamSources.ListVariables(project);
            if (lists.Count > 0) sb.AppendLine("LIST VARIABLES (for listadd): " + string.Join(", ", lists));

            sb.AppendLine("Follow the REQUEST: the examples only show the format — invent names, attacks and numbers that fit what the user asked for.");
            sb.AppendLine("EXAMPLE:");
            sb.AppendLine(boss ? BossExample : CutsceneExample);
            if (!compact) sb.AppendLine("EXAMPLE 2:\n" + (boss ? BossExample2 : CutsceneExample2));
            return sb.ToString();
        }

        private const string BossExample =
            "{\"boss\":{\"attacks\":{\"Charge\":[{\"do\":\"telegraph\",\"time\":0.6,\"color\":\"#FF4040\"},{\"do\":\"dash\",\"speed\":620,\"time\":0.7}]," +
            "\"Ring\":[{\"do\":\"telegraph\",\"time\":0.4,\"color\":\"#FFB020\"},{\"do\":\"shoot\",\"pattern\":\"ring\",\"count\":12,\"speed\":300}]," +
            "\"Slam\":[{\"do\":\"jump\",\"height\":260,\"toward\":1,\"shockwave\":1}]}," +
            "\"phases\":[{\"name\":\"Phase 1\",\"when\":{\"type\":\"start\"},\"attacks\":[\"Charge\",\"Ring\"],\"order\":\"random\",\"pause\":1}," +
            "{\"name\":\"Phase 2\",\"when\":{\"type\":\"health\",\"value\":50},\"attacks\":[\"Charge\",\"Ring\",\"Slam\"],\"order\":\"random\",\"pause\":0.6,\"enter\":[{\"do\":\"invulnerable\",\"time\":1.5},{\"do\":\"shake\",\"strength\":10,\"time\":0.6}]}]," +
            "\"death\":[{\"do\":\"shake\",\"strength\":14,\"time\":1},{\"do\":\"emit\",\"at\":\"self\",\"count\":60,\"color\":\"#FF7040\"},{\"do\":\"say\",\"text\":\"Impossible…\",\"speaker\":\"Boss\"}]}}";

        private const string BossExample2 =
            "A flying bat boss: {\"boss\":{\"attacks\":{\"Swoop\":[{\"do\":\"telegraph\",\"time\":0.5},{\"do\":\"dash\",\"speed\":500,\"time\":0.8},{\"do\":\"moveto\",\"to\":\"\",\"x\":600,\"y\":120,\"time\":1}]," +
            "\"Spiral\":[{\"do\":\"shoot\",\"pattern\":\"ring\",\"count\":8,\"speed\":260,\"volleys\":4,\"every\":0.3,\"turn\":15}]," +
            "\"Bats\":[{\"do\":\"summon\",\"object\":\"Bat\",\"count\":2,\"radius\":100}]}," +
            "\"phases\":[{\"name\":\"Phase 1\",\"when\":{\"type\":\"start\"},\"attacks\":[\"Swoop\",\"Spiral\"],\"order\":\"inorder\",\"pause\":1.2}," +
            "{\"name\":\"Angry\",\"when\":{\"type\":\"health\",\"value\":35},\"attacks\":[\"Swoop\",\"Spiral\",\"Bats\"],\"order\":\"random\",\"pause\":0.7}],\"death\":[]}}";

        private const string CutsceneExample =
            "{\"steps\":[{\"do\":\"bars\",\"on\":1},{\"do\":\"camera\",\"object\":\"Boss\",\"time\":1.2},{\"do\":\"title\",\"text\":\"THE SLIME KING\",\"time\":2}," +
            "{\"do\":\"say\",\"text\":\"Who dares to wake me?\",\"speaker\":\"Slime King\"},{\"do\":\"shake\",\"strength\":8,\"time\":0.5}," +
            "{\"do\":\"cameraback\",\"time\":1},{\"do\":\"setvar\",\"var\":\"bossAwake\",\"to\":1}]}";

        private const string CutsceneExample2 =
            "{\"steps\":[{\"do\":\"fadeout\",\"color\":\"#000000\",\"time\":0.8},{\"do\":\"move\",\"object\":\"player\",\"to\":\"Door\",\"time\":0.1},{\"do\":\"fadein\",\"time\":0.8}," +
            "{\"do\":\"face\",\"object\":\"Guard\",\"toward\":\"player\"},{\"do\":\"say\",\"text\":\"Halt!|Show me your pass.\",\"speaker\":\"Guard\"},{\"do\":\"wait\",\"time\":0.5}]}";

        public static string UserPrompt(SeqMode mode, string request, string currentJson)
        {
            var sb = new StringBuilder();
            if (HasContent(mode, currentJson))
            {
                sb.AppendLine(mode == SeqMode.Boss ? "CURRENT BOSS:" : "CURRENT CUTSCENE:");
                sb.AppendLine(currentJson.Trim());
                sb.AppendLine("Change it according to the request below. Keep everything that the request doesn't mention.");
            }
            sb.AppendLine("REQUEST: " + request.Trim());
            sb.Append(mode == SeqMode.Boss ? "Answer with the complete {\"boss\":{...}} JSON." : "Answer with the complete {\"steps\":[...]} JSON.");
            return sb.ToString();
        }

        private static readonly Lazy<string> StarterBoss =
            new(() => SequenceJson.Serialize(SequenceJson.ParseBoss(BehaviorCatalog.Info(BehaviorKind.BossAI).CreateData().Text("script"), out _)));

        private static bool HasContent(SeqMode mode, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            if (mode == SeqMode.Cutscene) return SequenceJson.ParseCutscene(json, out var e).Steps.Count > 0 && e == null;
            var b = SequenceJson.ParseBoss(json, out var err);
            return err == null && (b.Attacks.Count > 0 || b.Death.Count > 0) && SequenceJson.Serialize(b) != StarterBoss.Value; // the starter boss counts as empty
        }

        /// <summary>Turns a model reply into a canonical script: finds the JSON, accepts {"boss":…} or the bare object,
        /// step-id/param synonyms, numbers as strings, missing fields → defaults, unknown steps dropped (noted).
        /// Returns null with an error when it still isn't a usable boss/cutscene.</summary>
        public static string? Normalize(SeqMode mode, string reply, List<string> notes, out string? error)
        {
            var json = LooseJson.Extract(reply ?? "", allowArray: mode == SeqMode.Cutscene); // a bare [...] counts as a step list
            if (json == null) { error = "the answer had no JSON object in it"; return null; }
            if (mode == SeqMode.Cutscene)
            {
                var c = SequenceJson.ParseCutscene(json, out error, notes);
                if (error != null) return null;
                error = SequenceJson.Problem(c);
                return error == null ? SequenceJson.Serialize(c) : null;
            }
            var b = SequenceJson.ParseBoss(json, out error, notes);
            if (error != null) return null;
            b.FillDefaultPhases(notes);
            error = SequenceJson.Problem(b);
            return error == null ? SequenceJson.Serialize(b) : null;
        }

        /// <summary>"when it dies … the variable doorOpen becomes 1": a death sequence that forgot the variable gets it.</summary>
        internal static string ForRequest(SeqMode mode, string json, string request)
        {
            if (mode != SeqMode.Boss) return json;
            var m = System.Text.RegularExpressions.Regex.Match(request ?? "", @"variab\w*\s+(\w+)\s+(?:becomes|is set to|goes to|turns|=|vira|fica|passa a ser|se vuelve)\s*(?:to\s+)?(-?\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success || !System.Text.RegularExpressions.Regex.IsMatch(request!, @"\b(dies|die|death|defeated|morre|morte|derrotad|muere)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return json;
            var b = SequenceJson.ParseBoss(json, out var err, new List<string>());
            if (err != null || b.Death.Any(s => s.Do == "setvar" && s.Str("var").Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))) return json;
            var step = new SeqStep { Do = "setvar" }; step.P["var"] = m.Groups[1].Value; step.P["to"] = m.Groups[2].Value;
            b.Death.Add(step);
            return SequenceJson.Serialize(b);
        }

        /// <summary>Asks the model (generate = (user prompt, system prompt) → reply), normalises the reply, and retries once
        /// with the problem explained when it can't be used.</summary>
        public static async Task<SeqAiResult> MakeAsync(Func<string, string, Task<string>> generate, SeqMode mode, string request, string currentJson,
                                                        Project project, Scene scene, bool compact)
        {
            var system = SystemPrompt(mode, project, scene, compact);
            var user = UserPrompt(mode, request, currentJson);
            var notes = new List<string>();
            var json = Normalize(mode, await generate(user, system), notes, out var error);
            if (json != null) return new SeqAiResult(true, ForRequest(mode, json, request), notes, "", 1);

            notes.Clear();
            var retry = user + $"\n\nYour previous answer could not be used: {error}. Answer again with ONLY the JSON object, using only the step ids listed.";
            json = Normalize(mode, await generate(retry, system), notes, out var error2);
            return json != null ? new SeqAiResult(true, ForRequest(mode, json, request), notes, "", 2) : new SeqAiResult(false, "", notes, error2 ?? error ?? "unknown problem", 2);
        }
    }
}
