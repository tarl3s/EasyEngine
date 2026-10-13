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
            var keys = rules.Select(RuleKeyOf).ToList();

            foreach (var r in added.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var raw = r.GetRawText();
                if (rules.Any(x => Squash(x) == Squash(raw))) continue;
                var key = RuleKey(r);
                int at = keys.IndexOf(key);
                if (at >= 0) rules[at] = raw; else { rules.Add(raw); keys.Add(key); }
            }
            return "[" + string.Join(",", rules) + "]";
        }

        // top-level "delete":["Name", …] (also "remove"/"destroy" when they name objects) — removes those objects
        private void ApplyDeletes(JsonElement root, Action ensureUndo, List<string> changes)
        {
            foreach (var key in new[] { "delete", "remove", "destroy" })
            {
                if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ValueKind == JsonValueKind.Object && el.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    foreach (var layer in _scene.Layers)
                    {
                        var o = layer.Objects.FirstOrDefault(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (o == null) continue;
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
