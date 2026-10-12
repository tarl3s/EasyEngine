using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;

namespace EasyEngine;

// Variables of more kinds than numbers: Yes/No (a 1/0 number named in Flags) and Text (its own store), in every scope.
// Old projects and saves load; settext / addtext / toggle / iftext run (also on the object's own variables); {name}
// shows a text; save/load keeps texts; the AI's "variables" make the right kinds; the Variables window converts kinds;
// the new blocks round-trip exactly; the cutscene "Set a text" step runs.
public partial class App
{
    partial void FeatureTestsVarTypes(FullTestKit t)
    {
        // ---------------- model + old files
        t.Test("var types: an old project without Flags/Texts loads with empty ones (game, scene and object)", () =>
        {
            var json = J("{'Name':'Old','Variables':{'coins':2},'Scenes':[{'Name':'S','Variables':{'x':1},'Layers':[{'Name':'Base','Objects':[{'Name':'A','Variables':{'hp':3}}]}]}]}");
            var p = JsonSerializer.Deserialize<Project>(json)!;
            var o = p.Scenes[0].Layers[0].Objects[0];
            return p.Flags.Count == 0 && p.Texts.Count == 0 && p.Scenes[0].Flags.Count == 0 && p.Scenes[0].Texts.Count == 0
                   && o.Flags.Count == 0 && o.Texts.Count == 0 && p.Variables["coins"] == 2 && o.Variables["hp"] == 3;
        });
        t.Test("var types: Yes/No and Text survive a save round-trip; Yes/No names ignore capital letters", () =>
        {
            var p = new Project();
            VarScope.Of(p).SetYesNo("hasKey", true); VarScope.Of(p).SetText("hero", "Ana");
            var o = new GameObjectData(); VarScope.Of(o).SetText("mood", "calm");
            p.Scenes.Add(new Scene { Layers = { new Layer { Objects = { o } } } });
            var back = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(p))!;
            var bs = VarScope.Of(back);
            return bs.KindOf("hasKey") == VarKind.YesNo && back.Variables["hasKey"] == 1 && back.Flags.Contains("HASKEY")
                   && bs.KindOf("hero") == VarKind.Text && back.Texts["hero"] == "Ana" && !back.Variables.ContainsKey("hero")
                   && back.Scenes[0].Layers[0].Objects[0].Texts["mood"] == "calm";
        });
        t.Test("var types: changing a kind converts the value and moves it between stores", () =>
        {
            var s = VarScope.Of(new Project());
            s.SetNumber("a", 3); s.ChangeKind("a", VarKind.YesNo); bool a1 = s.KindOf("a") == VarKind.YesNo && s.Nums["a"] == 1;
            s.SetNumber("z", 0); s.ChangeKind("z", VarKind.YesNo); bool z0 = s.Nums["z"] == 0;
            s.ChangeKind("a", VarKind.Text); bool a2 = s.Texts["a"] == "Yes" && !s.Nums.ContainsKey("a") && !s.Flags.Contains("a");
            s.ChangeKind("a", VarKind.YesNo); bool a3 = s.Nums["a"] == 1 && !s.Texts.ContainsKey("a");
            s.SetText("t", "12,5"); s.ChangeKind("t", VarKind.Number); bool t1 = s.Nums["t"] == 12.5 && !s.Texts.ContainsKey("t");
            s.SetText("w", "hello"); s.ChangeKind("w", VarKind.Number); bool w1 = s.Nums["w"] == 0;
            s.SetNumber("n", 3.5); s.ChangeKind("n", VarKind.Text); bool n1 = s.Texts["n"] == "3.5";
            s.SetText("y", "yes"); s.ChangeKind("y", VarKind.YesNo); bool y1 = s.Nums["y"] == 1;
            bool removed = s.Remove("y") && !s.Has("y") && !s.Flags.Contains("y");
            return a1 && z0 && a2 && a3 && t1 && w1 && n1 && y1 && removed
                   && VariablesWindow.KindFrom(VariablesWindow.KindId(VarKind.Text)) == VarKind.Text
                   && VariablesWindow.KindFrom(VariablesWindow.KindId(VarKind.YesNo)) == VarKind.YesNo;
        });
        t.Test("var types: the Variables window shows a type picker per row, a checkbox for Yes/No and a text box for Text", () =>
        {
            var project = ProjectIO.CreateNew("VT", Path.Combine(Path.GetTempPath(), "EE_Full_VT_" + Guid.NewGuid().ToString("N")[..6]));
            var scene = project.GetStartScene();
            var obj = new GameObjectData { Name = "Npc" };
            VarScope.Of(project).SetYesNo("doorOpen", true);
            VarScope.Of(scene).SetText("greeting", "Hi there");
            VarScope.Of(obj).SetNumber("hp", 3);
            var w = new VariablesWindow(project, scene, obj);
            var all = Descendants(w).ToList();
            bool check = all.OfType<CheckBox>().Any(c => c.IsChecked == true);
            bool text = all.OfType<TextBox>().Any(b => b.Text == "Hi there");
            bool num = all.OfType<TextBox>().Any(b => b.Text == "3");
            int pickers = all.OfType<ComboBox>().Count(c => c.Items.OfType<ComboBoxItem>().Any(i => (string?)i.Tag == "yesno"));
            w.Close();
            return check && text && num && pickers == 6; // 3 rows + 3 "add" rows
        });

        // ---------------- runtime: settext / addtext / toggle / iftext
        t.Test("var types: settext fills {placeholders} (texts and numbers); addtext appends", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'setvar','var':'coins','to':5},{'when':'start','do':'settext','var':'name','to':'Bob'}," +
                            "{'when':'always','do':'settext','var':'greet','to':'Hi {name}, {coins} coins'}," +
                            "{'when':'start','do':'settext','var':'log','to':'a'},{'when':'start','do':'addtext','var':'log','to':'b'}]"), PhysicsMode.None, null);
            p.TSteps(3);
            return p.TText("greet") == "Hi Bob, 5 coins" && p.TText("log") == "ab";
        });
        t.Test("var types: toggle flips a Yes/No (global and the object's own); setvar takes true/false", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'toggle','var':'a'},{'when':'start','do':'toggle','var':'b'},{'when':'start','do':'toggle','var':'open','local':true}," +
                            "{'when':'start','do':'setvar','var':'yes','to':true},{'when':'start','do':'setvar','var':'no','to':false}]"), PhysicsMode.None, null);
            p.TSetVar("b", 1); p.TSetVar("no", 1);
            p.TSteps(2);
            return p.TVar("a") == 1 && p.TVar("b") == 0 && p.TLocal("A", "open") == 1 && p.TVar("open") == 0 && p.TVar("yes") == 1 && p.TVar("no") == 0;
        });
        t.Test("var types: iftext is / isnot / contains / empty / notempty (capital letters don't matter)", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'settext','var':'name','to':'Hello World'}," +
                            "{'when':'iftext','var':'name','op':'is','value':'hello world','do':'setvar','var':'is','to':1}," +
                            "{'when':'iftext','var':'name','op':'is','value':'nope','do':'setvar','var':'bad','to':1}," +
                            "{'when':'iftext','var':'name','op':'isnot','value':'x','do':'setvar','var':'isnot','to':1}," +
                            "{'when':'iftext','var':'name','op':'contains','value':'WORLD','do':'setvar','var':'has','to':1}," +
                            "{'when':'iftext','var':'missing','op':'empty','do':'setvar','var':'emp','to':1}," +
                            "{'when':'iftext','var':'name','op':'notempty','do':'setvar','var':'full','to':1}]"), PhysicsMode.None, null);
            p.TSteps(3);
            return p.TVar("is") == 1 && p.TVar("bad") == 0 && p.TVar("isnot") == 1 && p.TVar("has") == 1 && p.TVar("emp") == 1 && p.TVar("full") == 1;
        });
        t.Test("var types: the object's own texts (local) stay apart from the game's; the condition's local isn't the action's", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'settext','var':'mood','to':'angry','local':true}," +
                            "{'when':'iftext','var':'mood','op':'is','value':'angry','local':true,'do':'setvar','var':'seen','to':1}," +
                            "{'when':'iftext','var':'mood','op':'is','value':'angry','local':true,'do':'settext','var':'g','to':'x'}]"), PhysicsMode.None, null);
            p.TSteps(3);
            return p.TText("mood") == "" && p.TLocalText("A", "mood") == "angry" && p.TVar("seen") == 1 && p.TText("g") == "x" && p.TLocalText("A", "g") == "";
        });
        t.Test("var types: a Label shows text variables (game, scene and its own {local.x}); texts win over numbers", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, (pr, sc) =>
            {
                VarScope.Of(pr).SetText("playerName", "Ana");
                VarScope.Of(sc).SetText("place", "the cave");
                var lbl = new GameObjectData { Id = 5, Name = "Sign", X = 10, Y = 10, Width = 200, Height = 40 };
                var b = BehaviorCatalog.Info(BehaviorKind.Label).CreateData();
                b.Texts["text"] = "Hello {playerName} in {place}, {local.title}! {coins}";
                lbl.Behaviors.Add(b);
                VarScope.Of(lbl).SetText("title", "Sir");
                sc.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(lbl);
                pr.Variables["coins"] = 7;
            });
            p.TStep();
            return p.TLabel("Sign") == "Hello Ana in the cave, Sir! 7";
        });
        t.Test("var types: save/load keeps texts; an old save without texts still loads", () =>
        {
            Project? proj = null;
            var p = t.Sim("[]", PhysicsMode.None, (pr, sc) => proj = pr);
            p.TSetText("hero", "Zed"); p.TSetVar("gold", 4);
            p.TSaveGame();
            p.TSetText("hero", "nobody"); p.TSetVar("gold", 0);
            bool loaded = p.TLoadGame() && p.TText("hero") == "Zed" && p.TVar("gold") == 4;
            var save = Path.Combine(proj!.FolderPath!, "save.json");
            File.WriteAllText(save, J("{'Scene':'','Vars':{'gold':9}}"));
            bool old = p.TLoadGame() && p.TVar("gold") == 9 && p.TText("hero") == "Zed";
            File.Delete(save);
            return loaded && old;
        });
        t.Test("var types: the cutscene step 'Set a text' writes words (with {placeholders}) and round-trips", () =>
        {
            var steps = "{'steps':[{'do':'setvar','var':'n','to':3},{'do':'settext','var':'msg','to':'You have {n} keys'}]}";
            var p = t.Sim("[]", PhysicsMode.None, CutArena(steps));
            p.TSteps(3);
            var json = J(steps);
            var rt = SequenceJson.StepJson(new SeqStep { Do = "settext", P = { ["var"] = "msg", ["to"] = "Hi" } }, SeqMode.Cutscene);
            return p.TText("msg") == "You have 3 keys" && StepCatalog.Find(SeqMode.Boss, "settext") != null && rt == "{\"do\":\"settext\",\"var\":\"msg\",\"to\":\"Hi\"}";
        });

        // ---------------- block editor
        t.Test("var types: the new blocks round-trip exactly (Set a text, Add to a text, Switch a Yes/No, If text, Its own)", () =>
        {
            string[] rules =
            {
                "[{\"when\":\"start\",\"do\":\"settext\",\"var\":\"greet\",\"to\":\"Hello {name}!\"}]",
                "[{\"when\":\"keypress\",\"key\":\"E\",\"cooldown\":0.3,\"do\":\"addtext\",\"var\":\"log\",\"to\":\" more\",\"local\":true}]",
                "[{\"when\":\"click\",\"do\":\"toggle\",\"var\":\"door\"}]",
                "[{\"when\":\"iftext\",\"var\":\"name\",\"op\":\"contains\",\"value\":\"Bo\",\"local\":true,\"do\":\"toggle\",\"var\":\"ok\",\"local\":true}]",
                "[{\"when\":\"iftext\",\"var\":\"name\",\"op\":\"empty\",\"do\":\"setvar\",\"var\":\"blank\",\"to\":1}]",
            };
            return rules.All(r => t.RT(r) == r);
        });
        t.Test("var types: a block round-trip keeps what the text rules do", () =>
        {
            var script = J("[{'when':'start','do':'settext','var':'name','to':'Bob'},{'when':'iftext','var':'name','op':'is','value':'bob','do':'setvar','var':'match','to':true}]");
            var a = t.Sim(script, PhysicsMode.None, null); a.TSteps(2);
            var b = t.Sim(t.RT(script), PhysicsMode.None, null); b.TSteps(2);
            return a.TVar("match") == 1 && b.TVar("match") == 1 && a.TText("name") == b.TText("name");
        });
        t.Test("var types: setvar to true/false in a block becomes 1/0", () =>
            t.RT(J("[{'when':'start','do':'setvar','var':'f','to':true}]")) == J("[{'when':'start','do':'setvar','var':'f','to':1}]"));
        t.Test("var types: pickers — text variables are offered for text blocks, not where a number is needed; Yes/No for toggle", () =>
        {
            var pr = new Project();
            VarScope.Of(pr).SetText("hero", "Ana"); VarScope.Of(pr).SetYesNo("lamp", false); VarScope.Of(pr).SetNumber("coins", 0);
            var o = new GameObjectData();
            o.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = J("[{'when':'start','do':'settext','var':'title','to':'x'},{'when':'start','do':'toggle','var':'bell'}]") } });
            pr.Scenes.Add(new Scene { Layers = { new Layer { Objects = { o } } } });
            var nums = ParamSources.Variables(pr); var texts = ParamSources.TextVariables(pr); var flags = ParamSources.YesNoVariables(pr);
            return texts.Contains("hero") && texts.Contains("title") && !nums.Contains("hero") && nums.Contains("coins") && nums.Contains("lamp")
                   && flags.Contains("lamp") && flags.Contains("bell") && !flags.Contains("coins");
        });

        // ---------------- AI
        t.Test("var types: the AI's variables make numbers, Yes/No (true/false) and Texts (words) — game, scene and object", () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_FullVT_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("VT", temp);
            var ed = new MainWindow(project);
            ed.TestApplySpec(J("{'variables':{'global':{'lives':'3','hasKey':true,'playerName':'Hero','dark':'false'},'scene':{'place':'Cave','depth':2}}," +
                               "'create':[{'name':'Npc','variables':{'mood':'happy','met':false,'hp':5}}]}"));
            var g = VarScope.Of(project); var s = VarScope.Of(project.GetStartScene());
            var npc = ed.TestFindObject("Npc");
            var n = npc != null ? VarScope.Of(npc) : default;
            ed.Close();
            return g.KindOf("lives") == VarKind.Number && g.Number("lives") == 3
                   && g.KindOf("hasKey") == VarKind.YesNo && g.YesNo("hasKey") && g.KindOf("dark") == VarKind.YesNo && !g.YesNo("dark")
                   && g.KindOf("playerName") == VarKind.Text && g.Text("playerName") == "Hero"
                   && s.KindOf("place") == VarKind.Text && s.Number("depth") == 2
                   && npc != null && n.KindOf("mood") == VarKind.Text && n.KindOf("met") == VarKind.YesNo && n.Number("hp") == 5;
        });
    }
}
