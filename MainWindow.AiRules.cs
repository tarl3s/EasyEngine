using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EasyEngine
{
    // How the AI's Custom rules join the ones an object already has.
    public partial class MainWindow
    {
        // The AI's "custom" ADDS to an object's rules instead of wiping them ("also make it spin" keeps "jumps every 2 s").
        // A new rule that only retunes an old one (same event, same action, same thing — e.g. every 3 s instead of 2)
        // replaces it; one already there is not doubled. "replaceCustom":true still swaps the whole list.
        internal static string MergeRules(string oldJson, JsonElement added)
        {
            var rules = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(oldJson) ? "[]" : oldJson, LooseJson.Options);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var r in doc.RootElement.EnumerateArray()) if (r.ValueKind == JsonValueKind.Object) rules.Add(r.GetRawText());
            }
            catch { } // unreadable old script: the new rules take its place
            // only an OLD rule can be retuned, and once: two new ones alike ("fade out at 2 s", "fade in at 4 s") both stay
            var oldKeys = rules.Select(RuleKeyOf).ToList();
            var retuned = new bool[oldKeys.Count];

            foreach (var r in added.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var raw = r.GetRawText();
                if (rules.Any(x => Squash(x) == Squash(raw))) continue;
                var key = RuleKey(r);
                int at = Enumerable.Range(0, oldKeys.Count).FirstOrDefault(i => !retuned[i] && oldKeys[i] == key, -1);
                if (at >= 0) { rules[at] = raw; retuned[at] = true; } else rules.Add(raw);
            }
            return "[" + string.Join(",", rules) + "]";
        }

        // top-level "delete":["Name", …] (also "remove"/"destroy" when they name objects) — removes those objects
        private void ApplyDeletes(JsonElement root, Action ensureUndo, List<string> changes)
        {
            foreach (var key in new[] { "delete", "remove", "destroy" })
            {
                if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                if (key == "remove" && (root.TryGetProperty("behaviors", out _) || root.TryGetProperty("custom", out _))) continue; // there it lists behaviors to take off
                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ValueKind == JsonValueKind.Object && el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    foreach (var layer in _scene.Layers)
                    {
                        var o = layer.Objects.FirstOrDefault(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (o == null) continue;
                        if (o.Locked) { if (!_aiSkippedLocked.Contains(o.Name)) _aiSkippedLocked.Add(o.Name); break; } // locked: never deleted by the AI
                        ensureUndo();
                        layer.Objects.Remove(o);
                        _selection.Remove(o);
                        if (ReferenceEquals(_selected, o)) _selected = null;
                        changes.Add(L.F("Deleted \"{0}\"", o.Name));
                        break;
                    }
                }
            }
        }

        // things that stay where they are put: the model asking for "Dynamic" on them is ignored
        private static readonly HashSet<BehaviorKind> StaysPut = new()
        {
            BehaviorKind.Label, BehaviorKind.UIButton, BehaviorKind.Bar, BehaviorKind.Minimap, BehaviorKind.Parallax, BehaviorKind.DayNight,
            BehaviorKind.Generator, BehaviorKind.Area, BehaviorKind.GoToScene, BehaviorKind.Checkpoint, BehaviorKind.Spawner, BehaviorKind.Solid,
            BehaviorKind.Gate, BehaviorKind.Switch, BehaviorKind.Spring, BehaviorKind.Collectible, BehaviorKind.AbilityPickup, BehaviorKind.Interact,
        };

        // "physics":"Dynamic" or {"mode":"Dynamic","bounciness":0.8,"gravity":0.5}; "bounciness"/"gravity" may also sit on the object
        private static (PhysicsMode? mode, double? bounce, double? gravity) SpecPhysics(JsonElement spec)
        {
            PhysicsMode? mode = null; double? bounce = null, grav = null;
            void Read(JsonElement o)
            {
                foreach (var k in new[] { "bounciness", "bounce", "restitution" })
                    if (o.TryGetProperty(k, out var b) && LooseJson.TryNumber(b, out var bv)) bounce = bv;
                foreach (var k in new[] { "gravity", "gravityScale" })
                    if (o.TryGetProperty(k, out var g) && LooseJson.TryNumber(g, out var gv)) grav = gv;
                foreach (var k in new[] { "mode", "physics" })
                    if (o.TryGetProperty(k, out var m) && m.ValueKind == JsonValueKind.String && Enum.TryParse<PhysicsMode>(m.GetString(), true, out var pm)) mode = pm;
            }
            Read(spec);
            if (spec.TryGetProperty("physics", out var ph) && ph.ValueKind == JsonValueKind.Object) Read(ph);
            return (mode, bounce, grav);
        }

        // Short, exact shapes for the requests people make most (the --aitest cases): small models copy them well,
        // big ones stop guessing. Shared by the cloud (compact) and local prompts.
        internal const string Recipes =
            "RECIPES (copy these shapes):\n" +
            "- coin worth 10 points: {\"create\":[{\"name\":\"Coin\",\"width\":24,\"height\":24,\"color\":\"#FFD700\",\"behaviors\":[{\"kind\":\"Collectible\",\"texts\":{\"var\":\"score\"},\"nums\":{\"points\":10}}]}]} — show it with a Label \"Score: {score}\" on the UI layer.\n" +
            "- score / timer on screen: {\"create\":[{\"name\":\"ScoreText\",\"layer\":\"UI\",\"x\":20,\"y\":16,\"width\":220,\"height\":32,\"behaviors\":[{\"kind\":\"Label\",\"texts\":{\"text\":\"Score: {score}\",\"align\":\"left\"}}]}]} (a Label's {name} shows a variable live — no rule needed).\n" +
            "- 60 s countdown: {\"variables\":{\"scene\":{\"timer\":60}},\"create\":[{\"name\":\"TimerText\",\"layer\":\"UI\",\"x\":20,\"y\":16,\"behaviors\":[{\"kind\":\"Label\",\"texts\":{\"text\":\"Time: {timer}\"}}],\"custom\":[{\"when\":\"timer\",\"every\":1,\"cond\":\"timer > 0\",\"do\":\"addvar\",\"var\":\"timer\",\"amount\":-1}]}]}\n" +
            "- fade out after 5 s: {\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":3}. Blink: {\"when\":\"start\",\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":0.5,\"loop\":1,\"yoyo\":1}. Invisible now: \"opacity\":0 on the object (or do:hide / do:show in a rule).\n" +
            "- keeps moving right: {\"when\":\"always\",\"do\":\"velocity\",\"vx\":150,\"vy\":0}. Follow the mouse: {\"when\":\"always\",\"do\":\"set\",\"prop\":\"x\",\"expr\":\"mousex - self.w/2\"} (+ the same for y). Move in a circle: {\"when\":\"always\",\"do\":\"set\",\"prop\":\"x\",\"expr\":\"400 + cos(time*2)*120\"} and prop y with sin. Run away from the player: {\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"sign(self.cx - Player.cx) * 160\"}.\n" +
            "- enemy that shoots at the player every 2 s: Shooter texts{key:\"\",aim:\"Player\"} nums{directions:1,cooldown:2,speed:300} (no key = fires by itself).\n" +
            "- platform moving up and down: Solid + Patrol nums{axis:1,distance:150,speed:80}. Bouncy ball: \"physics\":{\"mode\":\"Dynamic\",\"bounciness\":0.8}.\n" +
            "- locked door + key: door = Gate texts{var:\"hasKey\"}; key = AbilityPickup texts{var:\"hasKey\"}.\n" +
            "- door to another scene: GoToScene texts{scene:\"Level2\"} (texts.trigger is WHO walks in, normally Player).\n" +
            "- when the Boss dies go to Level2: on the Boss {\"when\":\"death\",\"do\":\"goscene\",\"scene\":\"Level2\"}. When the player dies → the same rule on the player.\n" +
            "- when the score reaches 10: {\"when\":\"ifvar\",\"var\":\"score\",\"op\":\">=\",\"value\":10,\"do\":\"goscene\",\"scene\":\"Level2\"}.\n" +
            "- message when touching: {\"when\":\"touch\",\"name\":\"Player\",\"do\":\"say\",\"text\":\"Welcome!\"} (touch's \"name\" is the OTHER object).\n" +
            "- delete objects: {\"delete\":[\"Enemy\"]}.\n" +
            "Numbers in rules may be expressions (\"rand(1,3)\", \"Player.x - self.x\"). move dx/dy are pixels PER SECOND. Bosses: BossAI (moves=fly for a flying one) + Health respawn=0 + Damager.\n";

        // Applying a reply never takes the editor down: whatever odd JSON slipped past the clean-up just counts as "not applied".
        private bool SafeApply(string reply, string prompt, out string summary)
        {
            try { return ApplyProjectSpec(reply, prompt, out summary); }
            catch (Exception) { summary = ""; return false; } // the panel then shows the raw reply
        }

        // Small models often answer with an example from the prompt, word for word ("make a player that walks…") whatever
        // was asked. Such a reply is not applied (the build retries) — unless the request really is that example's.
        internal static bool CopiedAnExample(string system, string reply, string userPrompt)
        {
            var json = LooseJson.Extract(reply);
            if (json == null) return false;
            var answer = Squash(json);
            string lastRequest = "";
            foreach (var line in system.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, "^User: \"(.+?)\"");
                if (m.Success) lastRequest = m.Groups[1].Value;
                int at = line.IndexOf('{');
                if (at < 0 || lastRequest.Length == 0 || Squash(line[at..].Trim()) != answer) continue;
                var asked = Words(userPrompt);
                return Words(lastRequest).Count(asked.Contains) < 4; // not the same request → a copy
            }
            return false;
        }
        private static HashSet<string> Words(string s) => System.Text.RegularExpressions.Regex.Matches(s.ToLowerInvariant(), "[a-zà-ú]{3,}").Select(m => m.Value).ToHashSet();

        // what the AI asked for that couldn't be used (a made-up event or action) — shown under the change summary
        private readonly List<string> _aiNotes = new();
        private string SummaryOf(List<string> changes) => string.Join("\n", changes.Concat(_aiNotes.Distinct().Select(n => "⚠ " + n)));

        private static string Squash(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

        private static string RuleKeyOf(string raw)
        {
            try { using var d = JsonDocument.Parse(raw, LooseJson.Options); return RuleKey(d.RootElement); } catch { return raw; }
        }

        // event + action + what they are about (key, object, variable, property…), not the amounts or timings
        private static readonly string[] RuleIdentity = { "key", "name", "var", "item", "prop", "object", "target", "scene" };
        private static string RuleKey(JsonElement r)
        {
            string S(string k) => r.TryGetProperty(k, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText()).ToLowerInvariant() : "";
            return S("when") + "|" + S("do") + "|" + string.Join("|", RuleIdentity.Select(S));
        }
    }
}
