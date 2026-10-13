using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasyEngine
{
    // ✨ Ask the AI inside the block editor: the person describes what the object should do and the AI answers rules for
    // the very blocks this window shows — its prompt is written from the palette (ids, fields, choices, defaults), so it
    // can't drift from what the blocks and the runtime support. The usable rules are ADDED as blocks to tweak. The reply
    // is read tolerantly (fences, a bare array, {"rules"} / {"custom"}, synonyms like wait → after or fade → tween
    // opacity); rules that still aren't blocks are left out and named.
    public partial class BlockEditorWindow
    {
        private readonly Func<string, string, Task<string>>? _generate;
        private TextBox _aiAsk = null!;
        private Button _aiGo = null!;
        private TextBlock _aiStatus = null!;

        /// <summary>The usable rules of an AI reply (rule JSON, an array), how many, and the rules left out.</summary>
        internal record AiRules(string Json, int Count, List<string> Dropped);

        private FrameworkElement BuildAiBox()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = L.T("✨ Ask the AI"), Foreground = Theme.Brush("Text", "#EEE"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _aiAsk = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            var placeholder = new TextBlock
            {
                Text = L.T("Describe what it should do… (e.g. every 2 seconds it jumps; when it touches Coin, add 1 to score)"),
                Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            };
            _aiAsk.TextChanged += (s, e) => placeholder.Visibility = _aiAsk.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _aiAsk.KeyDown += async (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; await AskAiAsync(); } };
            row.Children.Add(_aiAsk); row.Children.Add(placeholder);
            _aiGo = new Button { Content = L.T("Add blocks"), Height = 30, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(8, 0, 0, 0), Style = Theme.Style("AccentButton") };
            _aiGo.Click += async (s, e) => await AskAiAsync();
            Grid.SetColumn(_aiGo, 1); row.Children.Add(_aiGo);
            panel.Children.Add(row);

            // set by the code, already translated (it can hold the ids the AI made up)
            _aiStatus = L.Keep(new TextBlock { Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0) });
            panel.Children.Add(_aiStatus);

            return new Border
            {
                Background = Theme.Brush("Bg1", "#1E1E26"), BorderBrush = Theme.Brush("Border", "#333"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10), Child = panel
            };
        }

        /// <summary>Test hook: asks the AI (the generator given to the window) and returns what the status line says.</summary>
        internal async Task<string> TestAskAi(string request) { _aiAsk.Text = request; await AskAiAsync(); return _aiStatus.Text; }

        private async Task AskAiAsync()
        {
            var request = _aiAsk.Text.Trim();
            if (_generate == null || !_aiGo.IsEnabled) return;
            if (request.Length == 0) { _aiStatus.Text = L.T("Write what you want first."); return; }
            _aiGo.IsEnabled = _aiAsk.IsEnabled = false;
            _aiStatus.Text = L.T("Asking the AI…");
            try
            {
                var system = AiSystemPrompt(request);
                var got = ReadAiReply(await _generate("Make blocks for: " + request + "\nAnswer with the JSON only.", system));
                if (got.Count == 0) // one firmer try
                    got = ReadAiReply(await _generate("Make blocks for: " + request + "\nYour last answer had no usable rule. Answer ONLY with JSON like "
                        + "{\"rules\":[{\"when\":\"timer\",\"every\":2,\"do\":\"jump\",\"force\":600}]} using ONLY the event and action ids listed.", system));
                int added = AddRules(AiSpecFix.ForRequest(got.Json, request, _obj?.Name ?? ""));
                var left = got.Dropped.Count > 0 ? "\n" + L.F("Left out (not a block): {0}", string.Join("; ", got.Dropped.Take(4))) : "";
                _aiStatus.Text = (added > 0 ? L.F("✓ Added {0} block(s) at the end — check them and change anything you like.", added)
                                            : L.T("⚠ The AI's answer had no usable blocks. Try describing it differently.")) + left;
                if (added > 0) _aiAsk.Text = "";
            }
            catch (Exception ex) { _aiStatus.Text = "⚠ " + ex.Message; } // AiException: no key, no model, network…
            finally { _aiGo.IsEnabled = _aiAsk.IsEnabled = true; }
        }

        /// <summary>Adds the rules of <paramref name="json"/> (a rule array) as blocks at the end; returns how many.</summary>
        internal int AddRules(string json)
        {
            int before = _rules.Count;
            ParseJson(json);
            if (_rules.Count == before) return 0;
            RenderRules();
            _scroll.ScrollToEnd();
            return _rules.Count - before;
        }

        // ------------------------------------------------------------ the prompt (from the palette itself)

        // Made for each request like the 🔨 Build prompt (MainWindow.AiPrompt.cs): every block by id, the params of the ones
        // that matter here, and the same how-to hints — so the blocks' AI knows what the panel's AI knows, in few tokens.
        private static readonly HashSet<string> CommonBlocks = new() { "start", "always", "timer", "after", "keypress", "touch", "click", "velocity", "set", "addvar", "tween", "say", "destroy" };

        internal string AiSystemPrompt(string request = "")
        {
            var (events, actions) = L.InEnglish(() => (MakeEvents(), MakeActions()));
            var hints = MainWindow.HintsFor(request).Where(h => h.RuleIds.Count > 0).ToList();
            var asked = request.ToLowerInvariant();
            var used = hints.SelectMany(h => h.RuleIds)
                            .Concat(events.Concat(actions).Select(d => d.Id).Where(id => System.Text.RegularExpressions.Regex.IsMatch(asked, $@"\b{id}\b"))).ToHashSet(); // a block named in the request
            if (used.Count == 0) used = CommonBlocks;
            var sb = new StringBuilder();
            sb.AppendLine("You write behavior rules for ONE object of a 2D game made with EasyEngine. Each rule is a block: WHEN an event happens -> DO an action.");
            sb.AppendLine("Answer ONLY with JSON: {\"rules\":[{\"when\":\"<event id>\", <event fields>, \"do\":\"<action id>\", <action fields>}]}. One action per rule (two actions = two rules).");
            sb.AppendLine(MainWindow.FormulaHelp + " Seconds. opacity 0..1. \"self\" is this object. Optional on any rule: \"cond\":\"<formula>\" (runs only while true).");
            sb.AppendLine("EVENTS: " + string.Join(", ", events.Select(d => d.Id)));
            sb.AppendLine("ACTIONS: " + string.Join(", ", actions.Select(d => d.Id)));
            sb.AppendLine("PARAMS:");
            foreach (var d in events.Concat(actions).Where(d => used.Contains(d.Id))) sb.AppendLine(BlockDoc(d));
            if (hints.Count > 0) { sb.AppendLine("HOW TO:"); foreach (var h in hints) sb.AppendLine("- " + h.Text); }
            sb.AppendLine("Keys: Space, A-Z, D0-D9, Up, Down, Left, Right, Enter, LeftShift.");
            sb.AppendLine();
            if (_obj != null) sb.AppendLine($"These rules belong to the object \"{_obj.Name}\".");
            if (_obj != null && _obj.Behaviors.Count > 0) sb.AppendLine("This object has: " + string.Join(", ", _obj.Behaviors.Select(b => b.Kind.ToString()).Distinct()) + ".");
            sb.AppendLine("These blocks only work on an object with the behavior they are for: "
                + string.Join("; ", events.Concat(actions).Where(d => d.Needs != null && d.Needs.Length <= 3).Select(d => d.Id + " (" + string.Join("/", d.Needs!) + ")")) + "; behavior (a behavior it has).");
            sb.AppendLine("Objects in the scene: " + Listed(ParamSources.Objects(_scene)));
            sb.AppendLine("Number variables: " + Listed(NumberVarNames()));
            var locals = LocalVarNames();
            if (locals.Count > 0) sb.AppendLine("This object's own variables: " + Listed(locals));
            sb.AppendLine("Items: " + Listed(ParamSources.Items(_project)));
            sb.AppendLine("Scenes: " + Listed(_project.Scenes.Select(s => s.Name)));
            sb.AppendLine("A new variable or item name is fine when the description needs one (short, camelCase).");
            sb.AppendLine();
            sb.AppendLine("EXAMPLES");
            // the closest whole examples of the Build panel, as rules only — else a few fixed ones
            var near = MainWindow.ExamplesFor(request, 3).Select(e => (e.ask, json: AsRules(e.json))).Where(e => e.json != null).ToList();
            foreach (var (ask, json) in near.Count > 0 ? near! : AiExamples.Take(3)) sb.AppendLine($"\"{ask}\" -> {json}");
            if (!L.IsEnglish) sb.AppendLine(L.AiReplyRule + " That includes the words of a say or log action; ids, field names and variable names stay in English.");
            return sb.ToString();
        }

        // {"edit":[{"name":"Box","custom":[…]}]} → {"rules":[…]} (null when the example has no rules)
        private static string? AsRules(string buildJson)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(buildJson);
                var rules = new List<string>();
                foreach (var key in new[] { "edit", "create" })
                    if (doc.RootElement.TryGetProperty(key, out var arr))
                        foreach (var o in arr.EnumerateArray())
                            if (o.TryGetProperty("custom", out var c)) rules.AddRange(c.EnumerateArray().Select(r => r.GetRawText()));
                return rules.Count > 0 ? "{\"rules\":[" + string.Join(",", rules) + "]}" : null;
            }
            catch (System.Text.Json.JsonException) { return null; }
        }

        private static readonly (string ask, string json)[] AiExamples =
        {
            ("fade out after 5 seconds", "{\"rules\":[{\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":1}]}"),
            ("jump every 2 seconds", "{\"rules\":[{\"when\":\"timer\",\"every\":2,\"do\":\"jump\",\"force\":600}]}"),
            ("when clicked, add 1 to score", "{\"rules\":[{\"when\":\"click\",\"do\":\"addvar\",\"var\":\"score\",\"amount\":1}]}"),
            ("when it touches Coin, score goes up by 10", "{\"rules\":[{\"when\":\"touch\",\"name\":\"Coin\",\"do\":\"addvar\",\"var\":\"score\",\"amount\":10}]}"),
            ("spin while R is held", "{\"rules\":[{\"when\":\"key\",\"key\":\"R\",\"do\":\"rotate\",\"speed\":180}]}"),
            ("when it dies, a burst of red sparks", "{\"rules\":[{\"when\":\"death\",\"do\":\"emit\",\"count\":24,\"color\":\"#FF4040\"}]}"),
        };

        private static string Listed(IEnumerable<string> names)
        {
            var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
            return list.Count == 0 ? "(none yet)" : string.Join(", ", list);
        }

        // "tween (Glide a property): prop=x (x|y|opacity…), to=0 (number or expression), duration=0.5 …"
        private static string BlockDoc(BlockDef d)
        {
            var fields = d.Fields.Select(f =>
            {
                string kind = f.Type switch
                {
                    "num" => "",
                    "val" => "number or expression like score + 1",
                    "choice" => string.Join("|", (f.Options ?? Array.Empty<string>()).Select(o => o.Length == 0 ? "\"\"" : o)),
                    "obj" => "object name or self",
                    "aim" => "object name, or leave out",
                    "key" => "key",
                    "sound" => "sound file like Assets\\sounds\\hit.wav",
                    "behkind" => "one of its behaviors, e.g. Patrol, Chase, Shooter, Platformer",
                    "numvar" => "number variable",
                    "localvar" => "this object's own variable",
                    "textvar" => "text variable", "flagvar" => "Yes/No variable", "colorvar" => "colour variable", "listvar" => "list variable",
                    "color" => "#RRGGBB or {colourVariable}",
                    "bool" => "true or leave out",
                    _ => f.Type, // text, item, scene
                };
                var def = f.Absent ?? f.Default; // what leaving it out means
                return f.Key + (def.Length > 0 ? "=" + def : "") + (kind.Length > 0 ? $" ({kind})" : "");
            });
            var line = $"- {d.Id} ({d.Label})" + (d.Fields.Length > 0 ? ": " + string.Join(", ", fields) : "");
            return d.Hint.Length > 0 && d.Hint.Length < 90 ? line + ". " + d.Hint : line;
        }

        // ------------------------------------------------------------ reading the reply

        // what models write instead of the block ids: the same table the Build mode uses (AiSpecFix)
        private static Dictionary<string, string> WhenSynonyms => AiSpecFix.WhenSynonyms;
        private static Dictionary<string, string> DoSynonyms => AiSpecFix.DoSynonyms;

        // a field a block doesn't have → the first of these it does have (only when that one isn't given too)
        private static readonly Dictionary<string, string[]> KeySynonyms = new()
        {
            ["seconds"] = new[] { "every", "duration" }, ["every"] = new[] { "seconds" }, ["interval"] = new[] { "every", "seconds" },
            ["delay"] = new[] { "seconds", "every" }, ["wait"] = new[] { "seconds", "every" }, ["time"] = new[] { "seconds", "every", "duration" },
            ["duration"] = new[] { "seconds", "every" }, ["object"] = new[] { "name" }, ["target"] = new[] { "name", "to" }, ["with"] = new[] { "name" },
            ["other"] = new[] { "name" }, ["variable"] = new[] { "var" }, ["name"] = new[] { "var", "object", "item" }, ["value"] = new[] { "amount", "to" },
            ["by"] = new[] { "amount" }, ["amount"] = new[] { "to", "count" }, ["count"] = new[] { "amount" }, ["quantity"] = new[] { "amount", "count" },
            ["property"] = new[] { "prop" }, ["degrees"] = new[] { "speed" }, ["angle"] = new[] { "speed" }, ["message"] = new[] { "text" },
            ["strength"] = new[] { "force", "jump" }, ["power"] = new[] { "force", "jump" }, ["height"] = new[] { "force", "jump" }, ["force"] = new[] { "jump" },
            ["enabled"] = new[] { "on" }, ["state"] = new[] { "on" }, ["behavior"] = new[] { "kind" }, ["behaviour"] = new[] { "kind" }, ["health"] = new[] { "amount" }, ["hp"] = new[] { "amount" }, ["prefab"] = new[] { "object" },
            ["particles"] = new[] { "count" }, ["number"] = new[] { "count", "amount" }, ["button"] = new[] { "key" }, ["file"] = new[] { "sound" },
            ["times"] = new[] { "factor" }, ["multiplier"] = new[] { "factor" }, ["easing"] = new[] { "ease" }, ["level"] = new[] { "scene", "to" },
            ["x"] = new[] { "vx", "dx", "ax" }, ["y"] = new[] { "vy", "dy", "ay" }, ["colour"] = new[] { "color" },
        };

        // choice values written another way (tween's property names)
        private static readonly Dictionary<string, string> ChoiceSynonyms = new()
        {
            ["alpha"] = "opacity", ["transparency"] = "opacity", ["fade"] = "opacity", ["rotation"] = "rot", ["angle"] = "rot",
            ["width"] = "w", ["height"] = "h", ["size"] = "scale", ["everything"] = "master", ["all"] = "master", ["sound"] = "sfx", ["effects"] = "sfx",
        };

        private static string Canon(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        private static string? MapId(string raw, BlockDef[] defs, Dictionary<string, string> synonyms)
        {
            var c = Canon(raw);
            foreach (var cand in new[] { c, c.StartsWith("on") ? c[2..] : "", c.StartsWith("when") ? c[4..] : "", c.StartsWith("while") ? c[5..] : "" })
            {
                if (cand.Length == 0) continue;
                if (Array.Exists(defs, d => d.Id == cand)) return cand;
                if (synonyms.TryGetValue(cand, out var id)) return id;
            }
            return null;
        }

        /// <summary>The usable rules of an AI reply (see the class comment). Never throws.</summary>
        internal static AiRules ReadAiReply(string reply)
        {
            var events = MakeEvents(); var actions = MakeActions();
            var rules = new List<string>(); var dropped = new List<string>();
            try
            {
                var json = LooseJson.Extract(reply ?? "", allowArray: true);
                if (json != null)
                {
                    using var doc = JsonDocument.Parse(json, LooseJson.Options);
                    int block = 0;
                    foreach (var el in RuleElements(doc.RootElement, 0))
                    {
                        var one = ReadRule(el, events, actions, dropped).ToList();
                        if (one.Count > 1) { block++; one = one.Select(r => r[..^1] + ",\"block\":" + block + "}").ToList(); } // a list of actions: one block that does them all
                        rules.AddRange(one);
                    }
                }
            }
            catch (Exception) { } // nothing usable: the caller says so
            return new AiRules("[" + string.Join(",", rules) + "]", rules.Count, dropped);
        }

        // the rule objects of a reply: a bare array, {"rules"|"custom"|"script"|"blocks": [...]} (or that array as a JSON
        // string, as the Custom behavior stores it), or a single rule
        private static IEnumerable<JsonElement> RuleElements(JsonElement e, int depth)
        {
            if (depth > 3) return Array.Empty<JsonElement>();
            switch (e.ValueKind)
            {
                case JsonValueKind.Array: return e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => x.Clone()).ToList();
                case JsonValueKind.String:
                    try { using var inner = JsonDocument.Parse(e.GetString() ?? "", LooseJson.Options); return RuleElements(inner.RootElement, depth + 1).ToList(); }
                    catch (JsonException) { return Array.Empty<JsonElement>(); }
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                        if (p.Name.ToLowerInvariant() is "rules" or "custom" or "script" or "blocks") return RuleElements(p.Value, depth + 1);
                    return e.EnumerateObject().Any(p => p.Name.ToLowerInvariant() is "when" or "do" or "event" or "action") ? new[] { e.Clone() } : Array.Empty<JsonElement>();
                default: return Array.Empty<JsonElement>();
            }
        }

        // one AI rule → rule JSON for the blocks (one per action when "do" is a list); unusable ones go to dropped
        private static IEnumerable<string> ReadRule(JsonElement el, BlockDef[] events, BlockDef[] actions, List<string> dropped)
        {
            string whenRaw = "", cond = "";
            var whenPs = new List<(string k, JsonElement v)>();
            var dos = new List<(string raw, List<(string k, JsonElement v)> ps)>();
            var loose = new List<(string k, JsonElement v)>();
            var guards = new List<(string k, JsonElement v)>();
            foreach (var p in el.EnumerateObject())
            {
                var k = p.Name.ToLowerInvariant();
                if (k is "when" or "event" or "trigger" || k == "on" && !AiSpecFix.IsOnOff(Text(p.Value))) whenRaw = IdOf(p.Value, whenPs); // "on":"off" is a switch (controls, ability…)
                else if (k is "do" or "action" or "then")
                {
                    foreach (var a in p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().ToList() : new List<JsonElement> { p.Value })
                    { var ps = new List<(string, JsonElement)>(); dos.Add((IdOf(a, ps), ps)); }
                }
                else if (k is "cond" or "condition" or "onlyif") cond = Text(p.Value);
                else if (k is "if" or "ifop" or "ifval") guards.Add((k, p.Value)); // the old guard: the blocks turn it into "Only run if"
                else loose.Add((k, p.Value));
            }

            var when = MapId(whenRaw, events, WhenSynonyms);
            if (dos.Count == 0) dos.Add(("", new()));
            foreach (var (doRaw, doPs) in dos)
            {
                var act = MapId(doRaw, actions, DoSynonyms);
                if (when == null || act == null) { dropped.Add($"{(whenRaw.Length > 0 ? whenRaw : "?")} → {(doRaw.Length > 0 ? doRaw : "?")}"); continue; }
                var ev = Array.Find(events, d => d.Id == when)!;
                var ac = Array.Find(actions, d => d.Id == act)!;

                var wv = new Dictionary<string, JsonElement>(); var dv = new Dictionary<string, JsonElement>();
                foreach (var (k, v) in whenPs) Put(ev, wv, k, v);
                foreach (var (k, v) in doPs) Put(ac, dv, k, v);
                foreach (var (k, v) in loose) // a field next to when/do: whichever block has it — by name first, then by synonym
                    _ = Put(ev, wv, k, v, exactOnly: true) || Put(ac, dv, k, v, exactOnly: true) || Put(ev, wv, k, v) || Put(ac, dv, k, v);
                if (act == "tween") FixTween(Canon(doRaw), dv);

                var parts = new List<string> { $"\"when\":{Q(when)}" };
                parts.AddRange(Fields(ev, wv));
                parts.Add($"\"do\":{Q(act)}");
                parts.AddRange(Fields(ac, dv));
                foreach (var (k, v) in guards) parts.Add($"{Q(k)}:{v.GetRawText()}");
                if (cond.Trim().Length > 0) parts.Add($"\"cond\":{Q(cond.Trim())}");
                yield return "{" + string.Join(",", parts) + "}";
            }
        }

        // "jump" or {"type":"jump","force":600}: the id, and the object's other fields go to ps
        private static string IdOf(JsonElement v, List<(string k, JsonElement v)> ps)
        {
            if (v.ValueKind != JsonValueKind.Object) return Text(v);
            string id = "";
            foreach (var p in v.EnumerateObject())
            {
                var k = p.Name.ToLowerInvariant();
                if (id.Length == 0 && k is "id" or "type" or "do" or "when" or "action" or "event" or "kind") id = Text(p.Value);
                else ps.Add((k, p.Value.Clone()));
            }
            return id;
        }

        // puts a field into the first still-empty field of the block with that name (or, unless exactOnly, a synonym);
        // false when there is none (the field may then belong to the other block)
        private static bool Put(BlockDef def, Dictionary<string, JsonElement> bag, string key, JsonElement v, bool exactOnly = false)
        {
            IEnumerable<string> names = new[] { key };
            if (KeyAliases.TryGetValue(def.Id, out var game)) names = names.Concat(game.Where(g => g.alias == key).Select(g => g.key)); // the game's own other names
            if (!exactOnly && KeySynonyms.TryGetValue(key, out var alts)) names = names.Concat(alts);
            foreach (var a in names)
                if (!bag.ContainsKey(a) && Array.Exists(def.Fields, f => f.Key == a)) { bag[a] = v; return true; }
            return false;
        }

        // a fade is a glide of opacity: to 0 (out) or 1 (in), over 1 s unless said otherwise
        private static void FixTween(string raw, Dictionary<string, JsonElement> dv)
        {
            static JsonElement J(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
            bool fade = raw is "fade" or "fadeout" or "fadein";
            if (fade) dv["prop"] = J("\"opacity\"");
            if (fade && !dv.ContainsKey("to")) dv["to"] = J(raw == "fadein" ? "1" : "0");
            if (fade && !dv.ContainsKey("duration")) dv["duration"] = J("1");
        }

        // the block's fields in its order, as the blocks save them (numbers as numbers, a Yes/No as true, a choice by value)
        private static IEnumerable<string> Fields(BlockDef def, Dictionary<string, JsonElement> bag)
        {
            foreach (var f in def.Fields)
            {
                if (!bag.TryGetValue(f.Key, out var v)) continue;
                if (f.Type == "bool") { if (LooseJson.IsTrue(v)) yield return $"\"{f.Key}\":true"; continue; }
                if (f.Type is "num" or "val" && LooseJson.TryNumber(v, out var n)) { yield return $"\"{f.Key}\":{n.ToString(CultureInfo.InvariantCulture)}"; continue; }
                var s = Text(v).Trim();
                if (f.Type == "num") s = AiSpecFix.UnwrapExpr(s) ?? s; // "expr: …"
                if (s.Length == 0 || f.Type == "num" && !LooksLikeFormula(s)) continue; // a number field holding a stray word: its default
                if (f.Type == "num") { yield return $"\"{f.Key}\":{Q(s)}"; continue; } // a formula: the game works it out
                if (f.Type == "choice" && f.Options != null)
                {
                    var c = s.ToLowerInvariant();
                    if (ChoiceSynonyms.TryGetValue(Canon(c), out var syn) && f.Options.Contains(syn)) c = syn;
                    if (f.Options.SequenceEqual(OnOff) && AiSpecFix.IsOnOff(c)) c = c is "on" or "true" or "yes" or "1" ? "on" : "off"; // a Yes/No for a switch
                    s = f.Options.FirstOrDefault(o => o.Equals(c, StringComparison.OrdinalIgnoreCase) || (Canon(o).Length > 0 && Canon(o) == Canon(c))) ?? s; // "double jump" → doubleJump
                }
                if (f.Type == "behkind" && f.Options != null) s = f.Options.FirstOrDefault(o => Canon(o) == Canon(s)) ?? s; // "patrol" → Patrol
                yield return $"\"{f.Key}\":{Q(s)}";
            }
        }

        private static string Text(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true", JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => v.GetRawText(),
        };
    }
}
