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

        FeatureTestsColorAndList(t);
    }

    // Colour variables ("#RRGGBB" in Texts, named in Colors) and List variables (Lists), in every scope.
    private static void FeatureTestsColorAndList(FullTestKit t)
    {
        // ---------------- model + old files + conversions
        t.Test("colour/list: an old project without Colors/Lists loads with empty ones; a list saved as null becomes empty", () =>
        {
            var p = JsonSerializer.Deserialize<Project>(J("{'Name':'Old','Texts':{'a':'x'},'Scenes':[{'Name':'S','Lists':{'q':null},'Layers':[{'Name':'Base','Objects':[{'Name':'A'}]}]}]}"))!;
            var o = p.Scenes[0].Layers[0].Objects[0];
            return p.Colors.Count == 0 && p.Lists.Count == 0 && o.Colors.Count == 0 && o.Lists.Count == 0
                   && p.Scenes[0].Lists["q"].Count == 0 && VarScope.Of(p).KindOf("a") == VarKind.Text;
        });
        t.Test("colour/list: Colour and List survive a save round-trip (game, scene and object)", () =>
        {
            var p = new Project();
            VarScope.Of(p).SetColor("team", "#ff8800"); VarScope.Of(p).SetList("quests", new[] { "Find the key", "Open the door" });
            var sc = new Scene(); VarScope.Of(sc).SetList("names", new[] { "Bo" });
            var o = new GameObjectData(); VarScope.Of(o).SetColor("tint", "#11223344");
            sc.Layers.Add(new Layer { Objects = { o } }); p.Scenes.Add(sc);
            var back = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(p))!;
            var g = VarScope.Of(back); var bo = VarScope.Of(back.Scenes[0].Layers[0].Objects[0]);
            return g.KindOf("team") == VarKind.Color && g.Text("team") == "#FF8800" && back.Colors.Contains("TEAM")
                   && g.KindOf("quests") == VarKind.List && string.Join("|", back.Lists["quests"]) == "Find the key|Open the door"
                   && VarScope.Of(back.Scenes[0]).KindOf("names") == VarKind.List && bo.KindOf("tint") == VarKind.Color && bo.Text("tint") == "#11223344";
        });
        t.Test("colour/list: changing a kind converts (text↔colour, colour↔text, list↔text, number/Yes-No ↔ list/colour)", () =>
        {
            var s = VarScope.Of(new Project());
            s.SetText("a", " #00ff7f "); s.ChangeKind("a", VarKind.Color); bool a = s.KindOf("a") == VarKind.Color && s.Texts["a"] == "#00FF7F";
            s.SetText("b", "red"); s.ChangeKind("b", VarKind.Color); bool b = s.Texts["b"] == "#FFFFFF";
            s.ChangeKind("a", VarKind.Text); bool c = s.KindOf("a") == VarKind.Text && s.Texts["a"] == "#00FF7F" && !s.Colors.Contains("a");
            s.SetList("l", new[] { "x", "y z" }); s.ChangeKind("l", VarKind.Text); bool d = s.Texts["l"] == "x, y z" && !s.Lists.ContainsKey("l");
            s.SetText("t", "sword, shield ,, bow"); s.ChangeKind("t", VarKind.List); bool e = string.Join("|", s.Lists["t"]) == "sword|shield|bow" && !s.Texts.ContainsKey("t");
            s.ChangeKind("t", VarKind.Number); bool f = s.Nums["t"] == 3; // how many items
            s.SetNumber("n", 7); s.ChangeKind("n", VarKind.List); bool g = s.Lists["n"].Count == 0;
            s.SetYesNo("y", true); s.ChangeKind("y", VarKind.Color); bool h = s.Texts["y"] == "#FFFFFF";
            s.SetNumber("m", 5); s.ChangeKind("m", VarKind.Color); bool i = s.Texts["m"] == "#FFFFFF" && !s.Nums.ContainsKey("m");
            s.ChangeKind("m", VarKind.Number); bool j = s.Nums["m"] == 0;
            s.SetColor("k", "#ABCDEF"); s.ChangeKind("k", VarKind.List); bool k = string.Join("|", s.Lists["k"]) == "#ABCDEF";
            s.ChangeKind("k", VarKind.Color); bool l = s.Texts["k"] == "#ABCDEF";
            s.SetList("e", null); s.ChangeKind("e", VarKind.YesNo); bool m = s.Nums["e"] == 0 && s.KindOf("e") == VarKind.YesNo;
            bool removed = s.Remove("k") && !s.Has("k") && !s.Colors.Contains("k");
            return a && b && c && d && e && f && g && h && i && j && k && l && m && removed
                   && VariablesWindow.KindFrom(VariablesWindow.KindId(VarKind.Color)) == VarKind.Color
                   && VariablesWindow.KindFrom(VariablesWindow.KindId(VarKind.List)) == VarKind.List
                   && VarScope.ColorHex("#12345") == null && VarScope.ColorHex("FF8800") == null && VarScope.ColorHex("#ff8800") == "#FF8800";
        });
        t.Test("colour/list: the Variables window shows a colour button and a list editor (one box + ✕ per item, ＋ Add item)", () =>
        {
            var project = ProjectIO.CreateNew("VT", Path.Combine(Path.GetTempPath(), "EE_Full_VT_" + Guid.NewGuid().ToString("N")[..6]));
            var scene = project.GetStartScene();
            VarScope.Of(project).SetColor("team", "#FF8800");
            VarScope.Of(scene).SetList("quests", new[] { "Find the key", "Open the door" });
            var w = new VariablesWindow(project, scene, null);
            var all = Descendants(w).ToList();
            bool color = all.OfType<Button>().Any(b => Descendants(b).OfType<TextBlock>().Any(x => x.Text == "#FF8800"));
            bool items = all.OfType<TextBox>().Any(b => b.Text == "Find the key") && all.OfType<TextBox>().Any(b => b.Text == "Open the door");
            var add = all.OfType<Button>().FirstOrDefault(b => b.Content as string == "＋ Add item");
            add?.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            bool added = scene.Lists["quests"].Count == 3 && w.Changed;
            var box = Descendants(w).OfType<TextBox>().First(b => b.Text == "Find the key");
            var x = Descendants((System.Windows.DependencyObject)box.Parent).OfType<Button>().First();
            x.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            bool removed = string.Join("|", scene.Lists["quests"]) == "Open the door|";
            bool kinds = all.OfType<ComboBox>().Any(c => c.Items.OfType<ComboBoxItem>().Any(i => (string?)i.Tag == "list"));
            w.Close();
            return color && items && add != null && added && removed && kinds;
        });

        // ---------------- runtime: colours
        t.Test("colour/list: setcolor stores a colour (game and its own); a word that isn't a colour is ignored", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'setcolor','var':'team','to':'#ff8800'},{'when':'start','do':'setcolor','var':'mine','to':'#00FF00','local':true}," +
                            "{'when':'start','do':'setcolor','var':'bad','to':'banana'},{'when':'start','do':'setcolor','var':'copy','to':'{team}'}]"), PhysicsMode.None, null);
            p.TSteps(2);
            return p.TText("team") == "#FF8800" && p.TLocalText("A", "mine") == "#00FF00" && p.TText("mine") == "" && p.TText("bad") == "" && p.TText("copy") == "#FF8800";
        });
        t.Test("colour/list: {colourVar} works in colour fields — tint (self and a named object) and particles", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'setcolor','var':'team','to':'#336699'}," +
                            "{'when':'always','do':'tint','target':'Lamp','color':'{team}'},{'when':'start','do':'tint','color':'#112233'}," +
                            "{'when':'keypress','key':'E','do':'emit','count':3,'color':'{team}'}]"), PhysicsMode.None, (pr, sc) =>
                sc.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 7, Name = "Lamp", X = 300, Y = 10, Width = 20, Height = 20, Color = "#444444" }));
            p.TSteps(3);
            bool tinted = p.TBackground("Lamp") == "#FF336699" && p.TBackground("A") == "#FF112233";
            p.TKey("E", true); p.TStep(); p.TKey("E", false);
            return tinted && p.TLastParticleColor == "#FF336699";
        });
        t.Test("colour/list: a Label shows a colour's hex and a list's items; {count:x} and {local.list} too", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, (pr, sc) =>
            {
                VarScope.Of(pr).SetColor("team", "#FF8800");
                VarScope.Of(sc).SetList("quests", new[] { "Find the key", "Open the door" });
                var lbl = new GameObjectData { Id = 5, Name = "Sign", X = 10, Y = 10, Width = 300, Height = 40 };
                var b = BehaviorCatalog.Info(BehaviorKind.Label).CreateData();
                b.Texts["text"] = "{team} | {quests} ({count:quests}) | {local.bag} {count:local.bag}";
                lbl.Behaviors.Add(b);
                VarScope.Of(lbl).SetList("bag", new[] { "apple" });
                sc.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(lbl);
            });
            p.TStep();
            return p.TLabel("Sign") == "#FF8800 | Find the key, Open the door (2) | apple 1";
        });

        // ---------------- runtime: lists
        t.Test("colour/list: listadd (with {placeholders}) / listremove (first match, capital letters don't matter) / listclear", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'settext','var':'who','to':'Bo'},{'when':'start','do':'listadd','var':'q','value':'Find {who}'}," +
                            "{'when':'start','do':'listadd','var':'q','value':'b'},{'when':'start','do':'listadd','var':'q','value':'B'},{'when':'start','do':'listadd','var':'q','value':'  '}," +
                            "{'when':'start','do':'listremove','var':'q','value':'b'},{'when':'start','do':'listremove','var':'q','value':'nope'}," +
                            "{'when':'start','do':'listadd','var':'gone','value':'x'},{'when':'start','do':'listclear','var':'gone'}]"), PhysicsMode.None, null);
            p.TSteps(2);
            return string.Join("|", p.TList("q")) == "Find Bo|B" && p.TList("gone").Count == 0;
        });
        t.Test("colour/list: the object's own lists (local) stay apart from the game's", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'listadd','var':'bag','value':'apple','local':true},{'when':'start','do':'listadd','var':'bag','value':'pear','local':true}," +
                            "{'when':'start','do':'listremove','var':'bag','value':'APPLE','local':true},{'when':'start','do':'listadd','var':'bag','value':'global'}," +
                            "{'when':'iflist','var':'bag','op':'contains','value':'pear','local':true,'do':'setvar','var':'ownHasPear','to':1}," +
                            "{'when':'iflist','var':'bag','op':'contains','value':'pear','do':'setvar','var':'gameHasPear','to':1}," +
                            "{'when':'keypress','key':'E','do':'listclear','var':'bag','local':true}]"), PhysicsMode.None, null);
            p.TSteps(3);
            bool before = string.Join("|", p.TLocalList("A", "bag")) == "pear" && string.Join("|", p.TList("bag")) == "global" && p.TVar("ownHasPear") == 1 && p.TVar("gameHasPear") == 0;
            p.TKey("E", true); p.TStep(); p.TKey("E", false);
            return before && p.TLocalList("A", "bag").Count == 0 && p.TList("bag").Count == 1;
        });
        t.Test("colour/list: iflist contains / notcontains / empty / notempty / countis / countatleast", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'listadd','var':'q','value':'Key'},{'when':'start','do':'listadd','var':'q','value':'Door'}," +
                            "{'when':'iflist','var':'q','op':'contains','value':'key','do':'setvar','var':'has','to':1}," +
                            "{'when':'iflist','var':'q','op':'notcontains','value':'Boss','do':'setvar','var':'lacks','to':1}," +
                            "{'when':'iflist','var':'q','op':'notcontains','value':'Door','do':'setvar','var':'bad1','to':1}," +
                            "{'when':'iflist','var':'none','op':'empty','do':'setvar','var':'emp','to':1}," +
                            "{'when':'iflist','var':'q','op':'notempty','do':'setvar','var':'full','to':1}," +
                            "{'when':'iflist','var':'q','op':'countis','value':2,'do':'setvar','var':'two','to':1}," +
                            "{'when':'iflist','var':'q','op':'countis','value':'3','do':'setvar','var':'bad2','to':1}," +
                            "{'when':'iflist','var':'q','op':'countatleast','value':'2','do':'setvar','var':'atleast','to':1}," +
                            "{'when':'iflist','var':'q','value':'door','do':'setvar','var':'defaultContains','to':1}]"), PhysicsMode.None, null);
            p.TSteps(3);
            return p.TVar("has") == 1 && p.TVar("lacks") == 1 && p.TVar("bad1") == 0 && p.TVar("emp") == 1 && p.TVar("full") == 1
                   && p.TVar("two") == 1 && p.TVar("bad2") == 0 && p.TVar("atleast") == 1 && p.TVar("defaultContains") == 1;
        });
        t.Test("colour/list: count:name reads a list's size in expressions (and count:local.name the object's own)", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'listadd','var':'q','value':'a'},{'when':'start','do':'listadd','var':'q','value':'b'}," +
                            "{'when':'start','do':'listadd','var':'mine','value':'z','local':true}," +
                            "{'when':'always','do':'setvar','var':'score','expr':'count:q * 10 + count:local.mine'}," +
                            "{'when':'expr','expr':'count:q >= 2','do':'setvar','var':'ok','to':1}]"), PhysicsMode.None, null);
            p.TSteps(3);
            return p.TVar("score") == 21 && p.TVar("ok") == 1 && p.TEval("count:missing") == 0 && p.TEval("count:local.mine", "A") == 1 && p.TFill("{count:q}") == "2";
        });
        t.Test("colour/list: save/load keeps lists (and colours); an old save without lists still loads", () =>
        {
            Project? proj = null;
            var p = t.Sim("[]", PhysicsMode.None, (pr, sc) => proj = pr);
            p.TSetList("quests", "Find the key", "Open the door"); p.TSetText("team", "#FF8800");
            p.TSaveGame();
            p.TSetList("quests"); p.TSetText("team", "#000000");
            bool loaded = p.TLoadGame() && string.Join("|", p.TList("quests")) == "Find the key|Open the door" && p.TText("team") == "#FF8800";
            var save = Path.Combine(proj!.FolderPath!, "save.json");
            File.WriteAllText(save, J("{'Scene':'','Vars':{'gold':9},'Texts':{'hero':'Zed'}}"));
            bool old = p.TLoadGame() && p.TVar("gold") == 9 && p.TList("quests").Count == 2;
            File.Delete(save);
            return loaded && old;
        });
        t.Test("colour/list: the cutscene steps 'Set a colour' and 'Add to a list' run and round-trip", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, CutArena("{'steps':[{'do':'setcolor','var':'team','to':'#00aa00'},{'do':'settext','var':'who','to':'Bo'},{'do':'listadd','var':'q','value':'Meet {who}'}]}"));
            p.TSteps(3);
            var a = SequenceJson.StepJson(new SeqStep { Do = "setcolor", P = { ["var"] = "team", ["to"] = "#FF0000" } }, SeqMode.Cutscene);
            var b = SequenceJson.StepJson(new SeqStep { Do = "listadd", P = { ["var"] = "q", ["value"] = "Hi" } }, SeqMode.Boss);
            return p.TText("team") == "#00AA00" && string.Join("|", p.TList("q")) == "Meet Bo"
                   && a == "{\"do\":\"setcolor\",\"var\":\"team\",\"to\":\"#FF0000\"}" && b == "{\"do\":\"listadd\",\"var\":\"q\",\"value\":\"Hi\"}";
        });

        // ---------------- block editor + pickers
        t.Test("colour/list: the new blocks round-trip exactly (Set a colour, Change the colour, list blocks, While a list…)", () =>
        {
            string[] rules =
            {
                "[{\"when\":\"start\",\"do\":\"setcolor\",\"var\":\"team\",\"to\":\"#FF8800\"}]",
                "[{\"when\":\"click\",\"do\":\"setcolor\",\"var\":\"mine\",\"to\":\"{team}\",\"local\":true}]",
                "[{\"when\":\"always\",\"do\":\"tint\",\"target\":\"Lamp\",\"color\":\"{team}\"}]",
                "[{\"when\":\"keypress\",\"key\":\"Q\",\"cooldown\":0.3,\"do\":\"listadd\",\"var\":\"quests\",\"value\":\"Find {item}\"}]",
                "[{\"when\":\"start\",\"do\":\"listremove\",\"var\":\"bag\",\"value\":\"apple\",\"local\":true}]",
                "[{\"when\":\"start\",\"do\":\"listclear\",\"var\":\"quests\"}]",
                "[{\"when\":\"iflist\",\"var\":\"quests\",\"op\":\"countatleast\",\"value\":\"2\",\"local\":true,\"do\":\"toggle\",\"var\":\"done\"}]",
                "[{\"when\":\"iflist\",\"var\":\"quests\",\"op\":\"empty\",\"do\":\"setvar\",\"var\":\"none\",\"to\":1}]",
            };
            return rules.All(r => t.RT(r) == r);
        });
        t.Test("colour/list: pickers — Colour and List variables have their own; numbers and texts never offer them", () =>
        {
            var pr = new Project();
            VarScope.Of(pr).SetColor("team", "#FF0000"); VarScope.Of(pr).SetList("quests", null); VarScope.Of(pr).SetText("hero", "Ana"); VarScope.Of(pr).SetNumber("coins", 0);
            var o = new GameObjectData();
            o.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = J("[{'when':'start','do':'setcolor','var':'glow','to':'#FFFFFF'},{'when':'iflist','var':'bag','op':'empty','do':'listadd','var':'log','value':'x'},{'when':'start','do':'listremove','var':'trash','value':'x'}]") } });
            o.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Label, Texts = { ["text"] = "{quests} {team} {coins}" } });
            pr.Scenes.Add(new Scene { Layers = { new Layer { Objects = { o } } } });
            var nums = ParamSources.Variables(pr); var texts = ParamSources.TextVariables(pr);
            var colors = ParamSources.ColorVariables(pr); var lists = ParamSources.ListVariables(pr);
            return colors.SequenceEqual(new[] { "glow", "team" }) && lists.SequenceEqual(new[] { "bag", "log", "quests", "trash" })
                   && texts.SequenceEqual(new[] { "hero" }) && nums.Contains("coins")
                   && !nums.Contains("team") && !nums.Contains("quests") && !nums.Contains("glow") && !nums.Contains("bag");
        });

        // ---------------- AI
        t.Test("colour/list: the AI's variables make Lists (arrays) and Colours (\"#RRGGBB\") — game, scene and object; other words stay Text", () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_FullVT_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("VT", temp);
            var ed = new MainWindow(project);
            ed.TestApplySpec(J("{'variables':{'global':{'quests':['Find the key','Open the door',3,true],'team':'#ff8800','code':'123456','word':'blue',}," + // a trailing comma, as small models write
                               "'scene':{'names':[]}},'create':[{'name':'Npc','variables':{'bag':['apple'],'eyes':'#00FF00'}}]}"));
            var g = VarScope.Of(project); var s = VarScope.Of(project.GetStartScene());
            var npc = ed.TestFindObject("Npc");
            var n = npc != null ? VarScope.Of(npc) : default;
            ed.TestApplySpec(J("{'variables':{'global':{'quests':'a, b'}}}")); // a string for a List: its comma-separated items
            bool split = string.Join("|", project.Lists["quests"]) == "a|b";
            var summary = g.Describe();
            ed.Close();
            return g.KindOf("quests") == VarKind.List && split
                   && g.KindOf("team") == VarKind.Color && g.Text("team") == "#FF8800"
                   && g.KindOf("code") == VarKind.Number && g.KindOf("word") == VarKind.Text
                   && s.KindOf("names") == VarKind.List && s.Lists["names"].Count == 0
                   && npc != null && n.KindOf("bag") == VarKind.List && n.KindOf("eyes") == VarKind.Color
                   && summary.Contains("quests=[\"a\",\"b\"]") && summary.Contains("team=#FF8800");
        });
        t.Test("colour/list: an AI array keeps numbers and yes/no as words", () =>
        {
            var s = VarScope.Of(new Project());
            using var doc = JsonDocument.Parse("[\"x\", 2.5, true]");
            return s.SetFromJson("l", doc.RootElement, out var shown) && string.Join("|", s.Lists["l"]) == "x|2.5|Yes" && shown == "l=[x, 2.5, Yes]";
        });
    }
}
