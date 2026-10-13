using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EasyEngine;

// ✨ Ask the AI inside the block editor, with canned replies (no real AI): the reply is read tolerantly (fences, a bare
// array, {"custom"}, a script string, synonyms, a list of actions), junk rules are left out and named, the prompt is
// written from the block palette (in English whatever the editor's language), the window adds the rules as blocks after
// the ones already there (retrying once when nothing was usable), says why when the AI isn't ready, and what the AI
// wrote really plays.
public partial class App
{
    partial void FeatureTestsBlockAi(FullTestKit t)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_Full_BlockAi_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var project = ProjectIO.CreateNew("BlockAi", dir);
        var scene = project.GetStartScene();
        static string Read(string reply) => BlockEditorWindow.ReadAiReply(reply).Json;

        t.Test("block AI: fenced JSON with prose around it is read; fields come out as the blocks save them", () =>
            Read("Sure! Here you go:\n```json\n{\"rules\":[{\"when\":\"timer\",\"every\":\"2\",\"do\":\"jump\",\"force\":600,},]}\n```\nHave fun.")
                == J("[{'when':'timer','every':2,'do':'jump','force':600}]"));

        t.Test("block AI: a bare array, {\"custom\"} and a script string are all read", () =>
            Read("[{\"when\":\"click\",\"do\":\"addvar\",\"var\":\"score\",\"amount\":1}]") == J("[{'when':'click','do':'addvar','var':'score','amount':1}]")
            && Read("{\"custom\":[{\"when\":\"death\",\"do\":\"emit\",\"count\":24}]}") == J("[{'when':'death','do':'emit','count':24}]")
            && Read("{\"script\":\"[{\\\"when\\\":\\\"start\\\",\\\"do\\\":\\\"say\\\",\\\"message\\\":\\\"Hi\\\"}]\"}") == J("[{'when':'start','do':'say','text':'Hi'}]"));

        t.Test("block AI: synonyms map to blocks (wait → after, fade → tween opacity, onclick, increment, every, spin, \"0,5\")", () =>
            Read(J("[{'when':'wait','seconds':5,'do':'fade'},{'when':'onClick','do':'increment','variable':'score','value':'1'},"
                 + "{'when':'every','interval':'0,5','do':'spin','degrees':90},{'when':'collision','with':'Coin','do':'addVariable','name':'coins','by':2}]"))
                == J("[{'when':'after','seconds':5,'do':'tween','prop':'opacity','to':0,'duration':1},{'when':'click','do':'addvar','var':'score','amount':1},"
                   + "{'when':'timer','every':0.5,'do':'rotate','speed':90},{'when':'touch','name':'Coin','do':'addvar','var':'coins','amount':2}]"));

        t.Test("block AI: choices by value (alpha → opacity, Music → music), a Yes/No as true, cond kept, a list of actions = one rule each", () =>
            Read(J("{'rules':[{'when':'key','key':'R','do':'tween','property':'alpha','to':0.5,'ease':'EaseOut','cond':'coins >= 3'},"
                 + "{'when':'start','do':'setvolume','channel':'Music','to':0.4},{'when':'start','do':'settext','var':'greet','to':'Hi','local':'yes'},"
                 + "{'when':'death','do':['particles',{'type':'destroy'}]}]}"))
                == J("[{'when':'key','key':'R','do':'tween','prop':'opacity','to':0.5,'ease':'easeout','cond':'coins >= 3'},"
                   + "{'when':'start','do':'setvolume','channel':'music','to':0.4},{'when':'start','do':'settext','var':'greet','to':'Hi','local':true},"
                   + "{'when':'death','do':'emit'},{'when':'death','do':'destroy'}]"));

        t.Test("block AI: rules that aren't blocks are left out and named; prose without JSON gives nothing", () =>
        {
            var got = BlockEditorWindow.ReadAiReply(J("[{'when':'dance','do':'moonwalk'},{'when':'start','do':'jump'},{'note':'no rule here'},{'when':'start'}]"));
            var none = BlockEditorWindow.ReadAiReply("I can't do that, sorry.");
            return got.Count == 1 && got.Json == J("[{'when':'start','do':'jump'}]") && got.Dropped.Contains("dance → moonwalk") && got.Dropped.Contains("start → ?")
                   && none.Count == 0 && none.Json == "[]" && BlockEditorWindow.ReadAiReply("{ broken").Count == 0;
        });

        t.Test("block AI: the prompt is written from the palette, in English, with the scene's names and examples", () =>
        {
            var player = new GameObjectData { Name = "Hero" };
            var w = new BlockEditorWindow(project, scene, "[]", player, (p, s) => Task.FromResult("[]"));
            var en = w.AiSystemPrompt("make it fade out, then setvolume");
            L.UseTableForTests("pt-BR", new Dictionary<string, string> { ["Glide a property"] = "Deslizar uma propriedade", ["When clicked"] = "Ao clicar" });
            string pt;
            try { pt = w.AiSystemPrompt("make it fade out"); } finally { L.UseTableForTests("en", null); }
            w.Close();
            string[] ids = { "start", "timer", "after", "touch", "ifvar", "iflocal", "hasitem", "click", "death", "tween", "setvar", "addlocal", "additem", "emit", "setvolume", "settext", "listadd" };
            return ids.All(id => System.Text.RegularExpressions.Regex.IsMatch(en, $@"\b{id}\b")) && en.Contains("tween (Glide a property): prop=x (x|y|opacity|rot|w|h|scale)")
                   && en.Contains("channel=master (master|music|sfx)") && en.Contains($"\"{player.Name}\"") && en.Contains("\"rules\"") && en.Contains("fade out after 5 seconds")
                   && pt.Contains("Glide a property") && !pt.Contains("Deslizar") && pt.Contains("Brazilian Portuguese");
        });

        t.Test("block AI: the window adds the AI's rules after the existing blocks, retrying once when nothing was usable", () =>
        {
            int calls = 0; string lastUser = "";
            var replies = new[] { "Sorry, here is an idea: make it jump.", "{\"rules\":[{\"when\":\"every\",\"seconds\":2,\"do\":\"jump\"},{\"when\":\"party\",\"do\":\"confetti\"}]}" };
            var w = new BlockEditorWindow(project, scene, J("[{'when':'start','do':'destroy'}]"), null,
                (user, system) => { lastUser = user; return Task.FromResult(replies[Math.Min(calls++, 1)]); });
            var status = w.TestAskAi("jump every two seconds").GetAwaiter().GetResult();
            var json = w.SerializeTest();
            w.Close();
            return calls == 2 && lastUser.Contains("no usable rule") && json == J("[{'when':'start','do':'destroy'},{'when':'timer','every':2,'do':'jump','force':600}]")
                   && status.Contains("Added 1 block") && status.Contains("party → confetti");
        });

        t.Test("block AI: no AI ready → its reason shows and nothing changes; an empty request asks for one", () =>
        {
            int calls = 0;
            var w = new BlockEditorWindow(project, scene, "[]", null, (u, s) => { calls++; throw new AiException("Choose an AI model first (AI panel)."); });
            var empty = w.TestAskAi("  ").GetAwaiter().GetResult();
            var notReady = w.TestAskAi("spin").GetAwaiter().GetResult();
            var json = w.SerializeTest();
            w.Close();
            return empty == L.T("Write what you want first.") && notReady == "⚠ Choose an AI model first (AI panel)." && calls == 1 && json == "[]";
        });

        t.Test("block AI: what the AI wrote really plays (a fade after 0.2 s ends invisible; a click-free timer jumps the score)", () =>
        {
            var fade = t.Sim(t.RT(Read(J("[{'when':'wait','seconds':0.2,'do':'fadeout','time':0.3}]"))), PhysicsMode.None, null);
            fade.TSteps(60);
            var score = t.Sim(t.RT(Read(J("[{'when':'interval','every':0.1,'do':'increment','variable':'score','value':1}]"))), PhysicsMode.None, null);
            score.TSteps(40);
            return fade.TOpacity("A") < 0.05 && score.TVar("score") >= 3;
        });

        TestScenarios.TryDeleteDir(dir);
    }
}
