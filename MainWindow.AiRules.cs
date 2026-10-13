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
            // a block (rules sharing "block": n) the AI adds never joins one already at the end of the list
            int lastBlock = 0;
            foreach (var x in rules) try { using var d = JsonDocument.Parse(x, LooseJson.Options); lastBlock = Math.Max(lastBlock, BlockOf(d.RootElement)); } catch { }

            foreach (var r in added.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var raw = r.GetRawText();
                if (rules.Any(x => Squash(x) == Squash(raw))) continue;
                var key = RuleKey(r);
                int at = Enumerable.Range(0, oldKeys.Count).FirstOrDefault(i => !retuned[i] && oldKeys[i] == key, -1);
                if (at >= 0) { rules[at] = raw; retuned[at] = true; }
                else rules.Add(lastBlock > 0 && BlockOf(r) > 0 ? Reblocked(raw, BlockOf(r) + lastBlock) : raw);
            }
            return "[" + string.Join(",", rules) + "]";
        }

        private static int BlockOf(JsonElement r) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("block", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out var n) ? n : 0;
        private static string Reblocked(string raw, int block)
        {
            try { if (System.Text.Json.Nodes.JsonNode.Parse(raw, null, LooseJson.Options) is System.Text.Json.Nodes.JsonObject o) { o["block"] = block; return o.ToJsonString(); } }
            catch (Exception) { } // a key written twice: as it is
            return raw;
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

        // Applying a reply never takes the editor down: whatever odd JSON slipped past the clean-up just counts as "not applied".
        private bool SafeApply(string reply, string prompt, out string summary)
        {
            try { return ApplyProjectSpec(reply, prompt, out summary); }
            catch (Exception) { summary = ""; return false; } // the panel then shows the raw reply
        }

        // the behavior kinds the current spec added or changed (for StripCopiedHabits)
        private readonly HashSet<BehaviorKind> AiAddedKinds = new(), AiKeyWritten = new();
        private static readonly string[] KeyWords = { "press", "key", "hold", "button", "click", "tecla", "aperta", "apertar", "pressiona", "botão", "segura", "clique" };
        private static readonly string[] CameraWords = { "camera", "câmera", "câmara", "view", "screen follows", "tela segue" };

        // Habits small models copy from the examples into requests that never asked for them: a FollowCamera on anything that
        // should "follow" (the mouse, the player), a Spawner/Shooter key ("Y") on "every 2 seconds". Only on what this reply added.
        private bool StripCopiedHabits(GameObjectData target, List<BehaviorData> result, string userPrompt)
        {
            var low = (userPrompt ?? "").ToLowerInvariant();
            if (low.Length == 0) return false; // no request to compare with (tests, replays)
            bool changed = false;
            bool isPlayer = NameLooksPlayer(target.Name) || result.Any(b => b.Kind is BehaviorKind.Platformer or BehaviorKind.TopDown);
            if (AiAddedKinds.Contains(BehaviorKind.FollowCamera) && !isPlayer && !CameraWords.Any(low.Contains))
                changed |= result.RemoveAll(b => b.Kind == BehaviorKind.FollowCamera) > 0;
            if (!isPlayer && !KeyWords.Any(low.Contains)) // only a key the model itself wrote on a new Spawner/Shooter (never the player's gun)
                foreach (var b in result.Where(b => b.Kind is BehaviorKind.Spawner or BehaviorKind.Shooter && AiAddedKinds.Contains(b.Kind) && AiKeyWritten.Contains(b.Kind)))
                    if (b.Text("key", "").Length > 0) { b.Texts["key"] = ""; changed = true; }
            return changed;
        }

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
