using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyEngine;

// "The blocks must do everything the AI does, and vice versa": the block palette (BlockEditorWindow.Palette.cs), the game
// (PlayWindow RunCustom/DoCustom and the feature files) and the AI (AiSpecFix, the block AI prompt, RuleDocs) must cover the
// very same rule language. The game's side is not a hand-written list: each event and action is PLAYED with the game
// recording every key it reads (PlayWindow.RuleReads), so a new param or a new block that isn't wired on both sides fails
// here, naming what's out of sync. Then a corpus of AI-style rules (the 📚 recipes, the rules shown in the build prompt,
// formulas in number fields, the game's other key names, every block left bare) must play the same after a trip through
// the blocks.
public partial class App
{
    partial void FeatureTestsParity(FullTestKit t)
    {
        static string J(string s) => s.Replace('\'', '"');
        var events = BlockEditorWindow.MakeEvents();
        var actions = BlockEditorWindow.MakeActions();
        // the game's other names for a block (read by the game, opened as that block)
        var idAliases = new Dictionary<string, string> { ["keydown"] = "key", ["hover"] = "mouseover", ["accel"] = "accelerate" };

        // a check whose name lists what's out of sync when it fails
        void Check(string name, Func<List<string>> gaps)
        {
            List<string> found;
            try { found = gaps(); }
            catch (Exception ex) { found = new List<string> { ex.GetType().Name + ": " + ex.Message }; }
            t.Test(name + (found.Count == 0 ? "" : "  ⟶ OUT OF SYNC: " + string.Join("; ", found)), () => found.Count == 0);
        }

        // ---------------- ids
        Check("parity: every event the game knows is a block, and every event block is one the game knows", () =>
        {
            var ids = events.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return AiSpecFix.Events.Where(e => !ids.Contains(e) && !(idAliases.TryGetValue(e, out var a) && ids.Contains(a))).Select(e => $"game event '{e}' has no block")
                .Concat(ids.Where(i => !AiSpecFix.Events.Contains(i)).Select(i => $"event block '{i}' isn't in AiSpecFix.Events")).ToList();
        });
        Check("parity: every action the game knows is a block, and every action block is one the game knows", () =>
        {
            var ids = actions.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var gaps = AiSpecFix.Actions.Where(e => !ids.Contains(e) && !(idAliases.TryGetValue(e, out var a) && ids.Contains(a))).Select(e => $"game action '{e}' has no block")
                .Concat(ids.Where(i => !AiSpecFix.Actions.Contains(i)).Select(i => $"action block '{i}' isn't in AiSpecFix.Actions")).ToList();
            foreach (var d in actions) // and the game really runs it
            {
                var p = t.Sim(J($"[{{'when':'start','do':'{d.Id}'}}]"), PhysicsMode.None, null); p.TSteps(1);
                if (p.TLogHas("Unknown action")) gaps.Add($"the game doesn't run action block '{d.Id}'");
            }
            return gaps;
        });

        // ---------------- params: what the game reads ⇄ the block's fields
        // a value for each kind of field that makes the game read the whole rule (a variable named, a key held…)
        static string Sample(BlockEditorWindow.Field f) => f.Type switch
        {
            "bool" => "true",
            "num" or "val" => f.Default.Length > 0 ? f.Default : "1",
            "choice" => f.Default.Length > 0 ? f.Default : f.Options!.First(o => o.Length > 0),
            "color" => "#FF0000",
            "key" => "Space",
            "obj" or "aim" => f.Default.Length > 0 ? f.Default : "B",
            "scene" => "S2",
            "sound" => "Assets\\sounds\\none.wav",
            "item" => "wood",
            _ => f.Default.Length > 0 ? f.Default : "v", // text and the variable pickers
        };
        static string Fields(BlockEditorWindow.BlockDef d) =>
            string.Concat(d.Fields.Select(f => "," + JsonSerializer.Serialize(f.Key) + ":" + (f.Type is "bool" ? "true" : JsonSerializer.Serialize(Sample(f)))));
        HashSet<string> Reads(string script, string side, Action<PlayWindow>? before = null, int steps = 2, Action<Project, Scene>? extra = null)
        {
            var p = t.Sim(script, PhysicsMode.None, extra ?? AddTouchTarget);
            before?.Invoke(p);
            var got = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PlayWindow.RuleReads = got;
            try { p.TSteps(steps); }
            finally { PlayWindow.RuleReads = null; }
            return got.Where(k => k.StartsWith(side)).Select(k => k[side.Length..]).Where(k => k is not ("cond" or "if" or "ifop" or "ifval")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        List<string> Compare(BlockEditorWindow.BlockDef d, HashSet<string> read, string what)
        {
            var known = d.Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (BlockEditorWindow.KeyAliases.TryGetValue(d.Id, out var al)) foreach (var (alias, _) in al) known.Add(alias);
            if (d.Fields.Any(f => f.Type == "val")) known.Add("expr"); // a "val" field saves an expression as "expr"
            return read.Where(k => !known.Contains(k)).Select(k => $"the game reads {what} '{d.Id}' key '{k}' but its block has no field for it (or KeyAliases entry)")
                .Concat(d.Fields.Where(f => !read.Contains(f.Key)).Select(f => $"{what} block '{d.Id}' has field '{f.Key}' the game never reads")).ToList();
        }
        Check("parity: every key the game reads for an action is a field of its block, and every field is read", () =>
        {
            var gaps = new List<string>();
            foreach (var d in actions)
            {
                bool jump = d.Id == "jump"; // only on the ground
                var script = "[{\"when\":\"always\",\"do\":" + JsonSerializer.Serialize(d.Id) + Fields(d) + "}]";
                gaps.AddRange(Compare(d, Reads(script, "do:", steps: jump ? 40 : 2, extra: jump ? MakePlatformerOnFloor : null), "action"));
            }
            return gaps;
        });
        Check("parity: every key the game reads for an event is a field of its block, and every field is read", () =>
        {
            var gaps = new List<string>();
            foreach (var d in events)
            {
                var script = "[{\"when\":" + JsonSerializer.Serialize(d.Id) + Fields(d) + ",\"do\":\"destroy\"}]";
                gaps.AddRange(Compare(d, Reads(script, "when:", p => p.TKey("Space", true)), "event"));
            }
            return gaps;
        });

        // ---------------- the AI's side: RuleDocs (for the build prompt) and the block AI prompt cover every block
        Check("parity: RuleDocs() has one short English line per block, naming every field", () =>
        {
            var docs = BlockEditorWindow.RuleDocs();
            var gaps = new List<string>();
            foreach (var (d, ev) in events.Select(d => (d, true)).Concat(actions.Select(d => (d, false))))
            {
                var mine = docs.Where(x => x.id == d.Id && x.isEvent == ev).ToList();
                if (mine.Count != 1) { gaps.Add($"RuleDocs has {mine.Count} lines for '{d.Id}'"); continue; }
                var doc = mine[0].doc;
                if (!doc.StartsWith(d.Id)) gaps.Add($"RuleDocs line for '{d.Id}' doesn't start with its id");
                gaps.AddRange(d.Fields.Where(f => !doc.Contains(f.Key)).Select(f => $"RuleDocs line for '{d.Id}' misses field '{f.Key}'"));
                if (doc.Length > 160) gaps.Add($"RuleDocs line for '{d.Id}' is {doc.Length} chars");
                if (doc.Any(c => c > 0x2014 && c != '…')) gaps.Add($"RuleDocs line for '{d.Id}' isn't plain English: {doc}");
            }
            if (docs.Count != events.Length + actions.Length) gaps.Add($"RuleDocs has {docs.Count} lines for {events.Length + actions.Length} blocks");
            return gaps;
        });
        var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Parity_" + Guid.NewGuid().ToString("N")[..6]);
        var project = ProjectIO.CreateNew("Parity", dir);
        Check("parity: the block AI prompt lists every event and action block with its fields", () =>
        {
            // every block is always named; a request that names one gets its line with every field
            var win = new BlockEditorWindow(project, project.GetStartScene(), "[]", null, (u, s) => Task.FromResult("[]"));
            var ids = win.AiSystemPrompt().Split('\n').Where(l => l.StartsWith("EVENTS:") || l.StartsWith("ACTIONS:")).SelectMany(l => l[(l.IndexOf(':') + 1)..].Split(',').Select(x => x.Trim())).ToHashSet();
            var gaps = new List<string>();
            foreach (var d in events.Concat(actions))
            {
                if (!ids.Contains(d.Id)) { gaps.Add($"block AI prompt misses '{d.Id}'"); continue; }
                var line = win.AiSystemPrompt("use " + d.Id).Split('\n').FirstOrDefault(l => l.StartsWith("- " + d.Id + " ("));
                if (line == null) { gaps.Add($"block AI prompt has no params for '{d.Id}' when asked for it"); continue; }
                gaps.AddRange(d.Fields.Where(f => !line.Contains(f.Key)).Select(f => $"block AI prompt line '{d.Id}' misses field '{f.Key}'"));
            }
            win.Close();
            return gaps;
        });

        // ---------------- round trip: AI-style rules play the same after the blocks
        var corpus = new List<(string from, string rule)>();
        void AddRules(string from, string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, LooseJson.Options);
                foreach (var r in RuleObjects(doc.RootElement)) corpus.Add((from, r));
            }
            catch (JsonException) { }
        }
        foreach (var (label, json) in BlockEditorWindow.Recipes()) AddRules("recipe " + label, json);
        var ed = NewParityEditor(dir);
        foreach (var text in new[] { ed.TestBuildPrompt(), EasyEngine.MainWindow.HintsText, EasyEngine.MainWindow.ExamplesText }) // every rule the build prompt can show
            foreach (var obj in JsonObjectsIn(text)) AddRules("build prompt", obj);
        // what models write: formulas in number fields (quoted or "expr: …"), the game's other key names, choices, aims
        foreach (var r in new[]
        {
            "{'when':'always','do':'velocity','vx':'-sin(time*2)*200','vy':'cos(time*2) * 200'}",
            "{'when':'timer','every':'0.1 + 0.05','do':'addvar','var':'n','amount':'1 + 1'}",
            "{'when':'after','every':0.2,'do':'emit','count':'4 * 3','speed':'100 + 20','color':'#00FF00'}",
            "{'when':'ifvar','var':'n','op':'<','value':'2 + 1','do':'addvar','var':'n','value':1}",
            "{'when':'always','do':'move','dx':'expr: 30 * 2','dy':0}",
            "{'when':'keypress','key':'Space','cooldown':'0.1 * 2','do':'jump','force':'500 + 100'}",
            "{'when':'start','do':'shoot','directions':1,'speed':300,'aim':'B'}",
            "{'when':'start','do':'shoot','speed':'200 * 2'}",
            "{'when':'start','do':'spawn','name':'Enemy','at':'near'}",
            "{'when':'start','do':'set','prop':'opacity','to':0.4}",
            "{'when':'start','do':'set','target':'B','prop':'scale','expr':'1 + 0.5'}",
            "{'when':'start','do':'setvar','var':'a','value':'3 * 3'}",
            "{'when':'start','value':5,'do':'setvar','var':'b'}",
            "{'when':'start','do':'addlocal','var':'hp','by':4}",
            "{'when':'start','do':'settext','var':'greet','text':'Hi {a}'}",
            "{'when':'start','do':'tint','target':'B','to':'#123456'}",
            "{'when':'start','do':'listadd','var':'bag','item':'apple'}",
            "{'when':'start','do':'tween','prop':'y','value':'self.y - 100','duration':'0.5 * 2','ease':'easeinout','loop':1,'yoyo':true}",
            "{'when':'start','do':'playsound','sound':'Assets\\\\sounds\\\\hit.wav','volume':'0.5','channel':'music'}",
            "{'when':'start','do':'log','message':'hello {a}','level':'warn'}",
            "{'when':'start','do':'rotate','speed':'rand(10, 20)'}",
            "{'when':'start','do':'boost','factor':'1 + 0.5'}",
            "{'when':'start','do':'additem','item':'wood','amount':'2 * 2'}",
        }) AddRules("AI-style", "[" + J(r) + "]");
        // every block left bare (the game's defaults ⇄ what the block shows for a field left out)
        foreach (var d in actions) AddRules("bare action", "[{\"when\":\"start\",\"do\":" + JsonSerializer.Serialize(d.Id) + "}]");
        foreach (var d in events) AddRules("bare event", "[{\"when\":" + JsonSerializer.Serialize(d.Id) + ",\"do\":\"addvar\",\"var\":\"fired\",\"amount\":1}]");

        Check($"parity: {corpus.Count} AI-style rules (recipes, build prompt, formulas, other key names, bare blocks) play the same after the blocks", () =>
        {
            var gaps = new List<string>();
            foreach (var (from, rule) in corpus.Distinct())
            {
                var once = t.RT("[" + rule + "]");
                if (t.RT(once) != once) { gaps.Add($"{from} {rule}: changes again on a second trip ({once})"); continue; }
                string Play(string script)
                {
                    var p = t.Sim(script, PhysicsMode.None, ParityScene);
                    p.TMouseAt(232, 232); p.TKey("Space", true);
                    p.TSteps(30);
                    return p.TFingerprint();
                }
                string a = Play("[" + rule + "]"), b = Play(once);
                if (a != b) gaps.Add($"{from} {rule} → {once}: plays differently");
            }
            return gaps;
        });

        TestScenarios.TryDeleteDir(dir); TestScenarios.TryDeleteDir(dir + "_ed");
    }

    private static EasyEngine.MainWindow NewParityEditor(string dir)
    {
        var project = ProjectIO.CreateNew("ParityEd", dir + "_ed");
        var layer = project.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
        layer.Objects.Add(new GameObjectData { Id = 90, Name = "Player", X = 100, Y = 100, Width = 48, Height = 64 });
        return new EasyEngine.MainWindow(project);
    }

    // a target "B" next to "A", the Enemy prefab, a second scene "S2" and a "Player" to aim at
    private static void ParityScene(Project p, Scene s)
    {
        AddTouchTarget(p, s); AddEnemyPrefab(p, s); AddScene2(p, s);
        s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 3, Name = "Player", Type = "Object", X = 600, Y = 200, Width = 40, Height = 40, Color = "#3BC97A" });
    }

    // the rule objects inside some JSON: objects with a "when" or a string "do", anywhere (in "custom" lists too)
    private static IEnumerable<string> RuleObjects(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) foreach (var r in RuleObjects(x)) yield return r; yield break; }
        if (e.ValueKind != JsonValueKind.Object) yield break;
        if (e.TryGetProperty("do", out var d) && d.ValueKind == JsonValueKind.String && e.TryGetProperty("when", out _)) { yield return e.GetRawText(); yield break; }
        foreach (var p in e.EnumerateObject()) foreach (var r in RuleObjects(p.Value)) yield return r;
    }

    // the top-level {...} written in a text (a prompt)
    private static IEnumerable<string> JsonObjectsIn(string text)
    {
        int depth = 0, start = -1; bool inStr = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inStr) { if (ch == '\\') i++; else if (ch == '"' || ch == '\n') inStr = false; continue; } // a JSON string (prose quotes don't count)
            if (ch == '"' && depth > 0) inStr = true;
            else if (ch == '{') { if (depth++ == 0) start = i; }
            else if (ch == '}' && depth > 0 && --depth == 0) yield return text[start..(i + 1)];
            else if (ch == '\n') depth = 0; // an object never spans lines in the prompt
        }
    }
}
