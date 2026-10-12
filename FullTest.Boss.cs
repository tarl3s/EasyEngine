using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EasyEngine;

// M6 — the Boss AI played headless: waking up, phases by health, every attack step (bullets with the right counts and
// directions, dash, jump + shockwave, summon, invulnerable), heavy = no knockback, the default bullet (hurts, vanishes),
// the death sequence, the health bar, the AI's reply normalisation (canned replies, no network), and a smoke run of
// the Boss Demo.
public partial class App
{
    // A floor, a Platformer player (Health 50) on the left, and "Boss" (Health hp, var bossHP) on the right.
    private static Action<Project, Scene> BossArena(string script, Action<BehaviorData>? cfg = null, int hp = 20, Action<Project, Scene, Layer>? more = null) => (p, s) =>
    {
        var l = s.Layers.First(z => z.Kind == LayerKind.Base);
        l.Objects.Add(new GameObjectData { Id = 10, Name = "Floor", X = 0, Y = 600, Width = 4000, Height = 40, Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
        var player = new GameObjectData { Id = 11, Name = "Player", X = 300, Y = 552, Width = 32, Height = 48, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
        player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
        var php = BehaviorCatalog.Info(BehaviorKind.Health).CreateData(); php.Nums["max"] = 50; php.Texts["var"] = "hp"; player.Behaviors.Add(php);
        l.Objects.Add(player);
        var boss = new GameObjectData { Id = 12, Name = "Boss", X = 900, Y = 520, Width = 80, Height = 80, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
        var bhp = BehaviorCatalog.Info(BehaviorKind.Health).CreateData(); bhp.Nums["max"] = hp; bhp.Nums["respawn"] = 0; bhp.Nums["invincible"] = 0.3; bhp.Texts["var"] = "bossHP";
        boss.Behaviors.Add(bhp);
        var ai = BehaviorCatalog.Info(BehaviorKind.BossAI).CreateData(); ai.Texts["script"] = J(script);
        cfg?.Invoke(ai);
        boss.Behaviors.Add(ai);
        l.Objects.Add(boss);
        more?.Invoke(p, s, l);
    };

    // One attack (steps) used by a single phase, with a long pause so it fires once.
    private static string OneAttack(string steps, double pause = 10) =>
        "{'attacks':{'A':[" + steps + "]},'phases':[{'name':'P1','when':{'type':'start'},'attacks':['A'],'pause':" + pause.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}],'death':[]}";

    private static double Deg(double vx, double vy) => (Math.Atan2(vy, vx) * 180 / Math.PI + 360) % 360;

    partial void FeatureTestsBoss(FullTestKit t)
    {
        PlayWindow Boss(string script, Action<BehaviorData>? cfg = null, int hp = 20, Action<Project, Scene, Layer>? more = null)
            => t.Sim("[]", PhysicsMode.None, BossArena(script, cfg, hp, more));

        // ---------------- waking up + phases
        t.Test("boss: idles until its variable turns on, then starts in its first phase", () =>
        {
            var p = Boss(OneAttack("{'do':'wait','time':0.2}"), c => { c.Texts["starts"] = "var"; c.Texts["var"] = "wake"; });
            p.TSteps(30);
            bool asleep = !p.TBossStarted("Boss") && p.TBossPhase("Boss") == "" && p.TBossAttack("Boss") == "";
            p.TSetVar("wake", 1); p.TSteps(2);
            return asleep && p.TBossStarted("Boss") && p.TBossPhase("Boss") == "P1" && p.TLogHas("Wakes up");
        });
        t.Test("boss: 'the player comes close' wakes it only within the distance", () =>
        {
            var p = Boss(OneAttack("{'do':'wait','time':0.2}"), c => { c.Texts["starts"] = "near"; c.Nums["distance"] = 250; });
            p.TSteps(10); bool asleep = !p.TBossStarted("Boss");
            p.TPlace("Player", 760, 552); p.TSteps(2);
            return asleep && p.TBossStarted("Boss");
        });
        t.Test("boss: the phase follows its health (start → ≤60% → ≤25%), each phase's enter steps run once", () =>
        {
            var p = Boss("{'attacks':{'A':[{'do':'wait','time':0.2}]},'phases':[{'name':'P1','when':{'type':'start'},'attacks':['A']}," +
                         "{'name':'P2','when':{'type':'health','value':60},'attacks':['A'],'enter':[{'do':'addvar','var':'p2','amount':1}]}," +
                         "{'name':'P3','when':{'type':'health','value':25},'attacks':['A'],'enter':[{'do':'addvar','var':'p3','amount':1}]}],'death':[]}");
            p.TSteps(3); bool p1 = p.TBossPhase("Boss") == "P1";
            p.TSetHp("Boss", 11); p.TSteps(3); bool p2 = p.TBossPhase("Boss") == "P2";
            p.TSetHp("Boss", 4); p.TSteps(3); bool p3 = p.TBossPhase("Boss") == "P3";
            p.TSteps(20);
            return p1 && p2 && p3 && p.TVar("p2") == 1 && p.TVar("p3") == 1 && p.TLogHas("P3 begins");
        });
        t.Test("boss: attacks loop — random never repeats the same one twice in a row; 'in order' cycles", () =>
        {
            var seen = new List<string>();
            var p = Boss("{'attacks':{'A':[{'do':'wait','time':0.05}],'B':[{'do':'wait','time':0.05}],'C':[{'do':'wait','time':0.05}]},'phases':[{'name':'P1','when':{'type':'start'},'attacks':['A','B','C'],'order':'random','pause':0}],'death':[]}");
            for (int i = 0; i < 200; i++) { p.TStep(); var a = p.TBossAttack("Boss"); if (a.Length > 0 && (seen.Count == 0 || seen[^1] != a)) seen.Add(a); }
            var q = Boss("{'attacks':{'A':[{'do':'wait','time':0.05}],'B':[{'do':'wait','time':0.05}]},'phases':[{'name':'P1','when':{'type':'start'},'attacks':['A','B'],'order':'inorder','pause':0.1}],'death':[]}");
            var order = new List<string>();
            for (int i = 0; i < 120; i++) { q.TStep(); var a = q.TBossAttack("Boss"); if (a.Length > 0 && (order.Count == 0 || order[^1] != a)) order.Add(a); }
            return seen.Count >= 10 && seen.Distinct().Count() == 3 && order.Count >= 6 && order.Take(6).SequenceEqual(new[] { "A", "B", "A", "B", "A", "B" });
        });

        // ---------------- attack steps
        t.Test("boss shoot ring: 12 bullets, 30° apart, all at the given speed", () =>
        {
            var p = Boss(OneAttack("{'do':'shoot','pattern':'ring','count':12,'speed':300}"));
            p.TSteps(3);
            var v = p.TVels("bullet");
            var angles = v.Select(x => Math.Round(Deg(x.vx, x.vy))).OrderBy(a => a).ToList();
            return v.Count == 12 && v.All(x => Math.Abs(Math.Sqrt(x.vx * x.vx + x.vy * x.vy) - 300) < 0.5) && angles.SequenceEqual(Enumerable.Range(0, 12).Select(i => i * 30.0));
        });
        t.Test("boss shoot spread: a 60° fan of 5 toward the player's side (left)", () =>
        {
            var p = Boss(OneAttack("{'do':'shoot','pattern':'spread','count':5,'spread':60,'speed':300}"));
            p.TSteps(3);
            var a = p.TVels("bullet").Select(x => Math.Round(Deg(x.vx, x.vy))).OrderBy(x => x).ToList();
            return a.SequenceEqual(new[] { 150.0, 165, 180, 195, 210 });
        });
        t.Test("boss shoot aimed: straight at the target (and 3 volleys, turning)", () =>
        {
            var p = Boss(OneAttack("{'do':'shoot','pattern':'aimed','count':1,'speed':400,'volleys':3,'every':0.2}"), c => c.Texts["target"] = "Dummy",
                         more: (pr, sc, l) => l.Objects.Add(new GameObjectData { Id = 20, Name = "Dummy", X = 620, Y = 240, Width = 40, Height = 40 }));
            p.TSteps(3);
            var first = p.TVels("bullet");
            double want = Deg(640 - 940, 260 - 560);
            p.TSteps(30);
            return first.Count == 1 && Math.Abs(Deg(first[0].vx, first[0].vy) - want) < 0.5 && p.TAlive("bullet") == 3;
        });
        t.Test("boss dash: rushes toward the target for its time, then stops", () =>
        {
            var p = Boss(OneAttack("{'do':'dash','speed':500,'time':0.4}"));
            p.TSteps(3); double x0 = p.TX("Boss");
            p.TSteps(15); double x1 = p.TX("Boss");
            p.TSteps(30); double x2 = p.TX("Boss"); p.TSteps(5);
            return x1 < x0 - 80 && Math.Abs(p.TVX("Boss")) < 0.01 && Math.Abs(p.TX("Boss") - x2) < 0.5 && p.TFacing("Boss") == -1;
        });
        t.Test("boss walk: walks toward the target", () =>
        {
            var p = Boss(OneAttack("{'do':'walk','speed':200,'time':0.5}"));
            p.TSteps(3); double x0 = p.TX("Boss"); p.TSteps(20);
            return p.TX("Boss") < x0 - 40;
        });
        t.Test("boss: a Platformer/Chase on it is ignored (its dash still moves it); it never respawns, and its Health on 'health' leaves the player's alone", () =>
        {
            var p = Boss(OneAttack("{'do':'dash','speed':500,'time':0.4}"), more: (pr, sc, l) =>
            {
                var b = l.Objects.First(o => o.Name == "Boss");
                b.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
                b.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Chase).CreateData());
                var h = b.Behaviors.First(x => x.Kind == BehaviorKind.Health); h.Texts["var"] = "health"; h.Nums["respawn"] = 1;
                l.Objects.First(o => o.Name == "Player").Behaviors.First(x => x.Kind == BehaviorKind.Health).Texts["var"] = "health";
            });
            p.TSteps(3); double x0 = p.TX("Boss"); p.TSteps(15);
            bool moved = p.TX("Boss") < x0 - 80;
            p.TSetHp("Boss", 0); p.TSteps(2);
            return moved && p.TLogHas("movement behavior(s) ignored") && p.TDead("Boss") && p.TVar("health") == 50;
        });
        t.Test("health: an enemy on the default 'health' variable doesn't overwrite the player's; its own variable shows its health", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, (pr, sc) =>
            {
                var l = sc.Layers.First(z => z.Kind == LayerKind.Base);
                var player = new GameObjectData { Id = 11, Name = "Player", X = 300, Y = 300, Width = 32, Height = 48 };
                player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.TopDown).CreateData());
                var php = BehaviorCatalog.Info(BehaviorKind.Health).CreateData(); php.Nums["max"] = 5; player.Behaviors.Add(php);
                l.Objects.Add(player);
                foreach (var (id, name, v) in new[] { (12, "Slime", "health"), (13, "Golem", "golemHp") })
                {
                    var e = new GameObjectData { Id = id, Name = name, X = 900, Y = 300 + id * 60, Width = 32, Height = 32 };
                    var hp = BehaviorCatalog.Info(BehaviorKind.Health).CreateData(); hp.Nums["max"] = 2; hp.Texts["var"] = v; e.Behaviors.Add(hp);
                    l.Objects.Add(e);
                }
            });
            p.TSteps(2);
            return p.TVar("health") == 5 && p.TVar("golemHp") == 2;
        });
        t.Test("boss jump: leaves the ground, lands, and the shockwave sends 2 waves + shakes the screen", () =>
        {
            var p = Boss(OneAttack("{'do':'jump','height':200,'toward':0,'shockwave':1}"));
            p.TSteps(1); double y0 = p.TY("Boss"), minY = y0; // it takes off on the next tick
            bool landedShake = false;
            for (int i = 0; i < 90; i++) { p.TStep(); minY = Math.Min(minY, p.TY("Boss")); if (p.TAlive("bullet") == 2 && p.TShaking) landedShake = true; }
            return minY < y0 - 150 && Math.Abs(p.TY("Boss") - y0) < 1 && p.TGrounded("Boss") && landedShake;
        });
        t.Test("boss jump toward the target lands near it", () =>
        {
            var p = Boss(OneAttack("{'do':'jump','height':200,'toward':1,'shockwave':0}"));
            p.TSteps(80);
            return Math.Abs(p.TX("Boss") + 40 - 316) < 60;
        });
        t.Test("boss glide (moveto) and telegraph", () =>
        {
            var p = Boss(OneAttack("{'do':'telegraph','time':0.3},{'do':'moveto','to':'','x':600,'y':300,'time':0.5},{'do':'setvar','var':'done','to':1}"));
            p.TSteps(5); bool glow = p.TBossTelegraph("Boss");
            p.TSteps(60);
            return glow && p.TVar("done") == 1 && Math.Abs(p.TX("Boss") - 600) < 1;
        });
        t.Test("boss summon: N minions around it", () =>
        {
            var p = Boss(OneAttack("{'do':'summon','object':'Minion','count':3,'radius':100}"),
                         more: (pr, sc, l) => pr.Prefabs.Add(new PrefabDef { Name = "Minion", Template = new GameObjectData { Name = "Minion", Width = 30, Height = 30 } }));
            p.TSteps(3);
            return p.TAlive("Minion") == 3;
        });
        t.Test("boss summon: its minions vanish when it falls (other copies of that prefab stay)", () =>
        {
            var p = Boss(OneAttack("{'do':'summon','object':'Minion','count':2,'radius':100}"),
                         more: (pr, sc, l) =>
                         {
                             pr.Prefabs.Add(new PrefabDef { Name = "Minion", Template = new GameObjectData { Name = "Minion", Width = 30, Height = 30 } });
                             l.Objects.Add(new GameObjectData { Id = 77, Name = "Minion", X = 100, Y = 100, Width = 30, Height = 30 }); // not summoned
                         });
            p.TSteps(3);
            bool summoned = p.TAlive("Minion") == 3;
            p.TSetHp("Boss", 0); p.TSteps(3);
            return summoned && p.TAlive("Minion") == 1 && p.TDead("Boss");
        });
        t.Test("boss invulnerable: no damage while it flickers", () =>
        {
            var p = Boss(OneAttack("{'do':'invulnerable','time':2},{'do':'wait','time':5}"));
            p.TSteps(3); p.THurt("Boss", 5, 0);
            return p.THp("Boss") == 20 && p.TInv("Boss") > 1;
        });
        t.Test("boss heavy: no knockback; a light boss is pushed", () =>
        {
            var heavy = Boss(OneAttack("{'do':'wait','time':5}"));
            heavy.TSteps(3); heavy.THurt("Boss", 1, 2000);
            var light = Boss(OneAttack("{'do':'wait','time':5}"), c => c.Nums["heavy"] = 0);
            light.TSteps(3); light.THurt("Boss", 1, 2000);
            return heavy.THp("Boss") == 19 && heavy.TVX("Boss") == 0 && light.TVX("Boss") < -100;
        });
        t.Test("boss bullet (default): hurts the target once and vanishes", () =>
        {
            var p = Boss(OneAttack("{'do':'shoot','pattern':'aimed','count':1,'speed':600,'life':3}"));
            p.TSteps(3); bool flying = p.TAlive("bullet") == 1;
            p.TSteps(80);
            return flying && p.THp("Player") == 49 && p.TAlive("bullet") == 0;
        });
        t.Test("damager: 'Disappears after hitting' — on (gone after the hit) vs off (stays)", () =>
        {
            bool Vanishes(double on)
            {
                var p = Boss(OneAttack("{'do':'wait','time':5}"), more: (pr, sc, l) =>
                {
                    var d = BehaviorCatalog.Info(BehaviorKind.Damager).CreateData(); d.Nums["vanish"] = on;
                    l.Objects.Add(new GameObjectData { Id = 21, Name = "Spike", X = 300, Y = 560, Width = 20, Height = 20, Behaviors = { d } });
                });
                p.TSteps(3);
                return p.THp("Player") == 49 && p.TDead("Spike");
            }
            return Vanishes(1) && !Vanishes(0);
        });

        // ---------------- death + health bar
        t.Test("boss death: plays its death steps (harmless, invulnerable), then Custom 'death' rules run and it's removed", () =>
        {
            var p = Boss("{'attacks':{'A':[{'do':'wait','time':5}]},'phases':[{'name':'P1','when':{'type':'start'},'attacks':['A']}]," +
                         "'death':[{'do':'setvar','var':'dying','to':1},{'do':'shake','strength':9,'time':0.3},{'do':'wait','time':0.5},{'do':'setvar','var':'after','to':1}]}",
                         more: (pr, sc, l) =>
                         {
                             var b = l.Objects.First(o => o.Name == "Boss");
                             b.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Damager, Texts = { ["target"] = "Player" }, Nums = { ["damage"] = 1 } });
                             b.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = "[{\"when\":\"death\",\"do\":\"setvar\",\"var\":\"customDeath\",\"to\":1}]" } });
                         });
            p.TSteps(3); p.TSetHp("Boss", 0); p.TSteps(3);
            bool dying = p.TBossDying("Boss") && !p.TDead("Boss") && p.TVar("dying") == 1 && p.TVar("customDeath") == 0;
            p.TPlace("Player", p.TX("Boss"), p.TY("Boss")); p.TSteps(5);
            bool harmless = p.THp("Player") == 50;
            p.THurt("Boss", 3, 0); bool noHurt = p.THp("Boss") <= 0;
            p.TSteps(40);
            return dying && harmless && noHurt && p.TDead("Boss") && p.TVar("after") == 1 && p.TVar("customDeath") == 1 && p.TLogHas("death sequence");
        });
        t.Test("boss death: without death steps it's removed right away", () =>
        {
            var p = Boss(OneAttack("{'do':'wait','time':5}"));
            p.TSteps(3); p.TSetHp("Boss", 0); p.TSteps(2);
            return p.TDead("Boss") && !p.TBossDying("Boss");
        });
        t.Test("boss health bar: hidden until it wakes, then its title and the health left", () =>
        {
            var p = Boss(OneAttack("{'do':'wait','time':5}"), c => { c.Texts["starts"] = "var"; c.Texts["var"] = "wake"; c.Texts["title"] = "THE KING"; });
            p.TSteps(3); bool hidden = !p.TBossBarShown;
            p.TSetVar("wake", 1); p.TSteps(2); p.TSetHp("Boss", 10); p.TStep();
            var off = Boss(OneAttack("{'do':'wait','time':5}"), c => c.Nums["hpbar"] = 0); off.TSteps(3);
            return hidden && p.TBossBarShown && p.TBossBarTitle == "THE KING" && Math.Abs(p.TBossBarFill - 0.5) < 0.01 && !off.TBossBarShown;
        });
        t.Test("boss: a flyer has no gravity; without phases it uses every attack", () =>
        {
            var p = Boss("{'attacks':{'Up':[{'do':'moveto','to':'','x':900,'y':200,'time':0.3},{'do':'wait','time':5}]}}", c => c.Texts["moves"] = "fly");
            p.TSteps(60);
            return Math.Abs(p.TY("Boss") - 200) < 1 && p.TBossPhase("Boss") == "Phase 1" && p.TBossAttack("Boss") == "Up";
        });
        t.Test("boss: a new Boss AI's starter script fights out of the box", () =>
        {
            var p = Boss(BehaviorCatalog.Info(BehaviorKind.BossAI).CreateData().Text("script").Replace('"', '\''));
            p.TSteps(200);
            return p.TLogHas("Attack: Charge") || p.TLogHas("Attack: Ring");
        });

        // ---------------- the AI (canned replies — never the network)
        var aiDir = Path.Combine(Path.GetTempPath(), "EE_Full_SeqAi_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var aiProject = ProjectIO.CreateNew("SeqAi", aiDir);
        aiProject.Prefabs.Add(new PrefabDef { Name = "Bat", Template = new GameObjectData { Name = "Bat" } });
        var lastPrompts = new List<string>(); // what the last Ask sent as the user prompt(s)
        SeqAiResult Ask(SeqMode mode, string current, params string[] replies)
        {
            int i = 0; var prompts = lastPrompts = new List<string>();
            var r = SequenceAi.MakeAsync((user, system) => { prompts.Add(user); return Task.FromResult(replies[Math.Min(i++, replies.Length - 1)]); },
                                         mode, "a big angry boss", current, aiProject, aiProject.GetStartScene(), compact: true).GetAwaiter().GetResult();
            return r;
        }
        t.Test("boss ai: the prompt documents every step id (from the palette), the project's prefabs and the {\"boss\":…} format", () =>
        {
            var sys = SequenceAi.SystemPrompt(SeqMode.Boss, aiProject, aiProject.GetStartScene(), compact: true);
            var cut = SequenceAi.SystemPrompt(SeqMode.Cutscene, aiProject, aiProject.GetStartScene(), compact: false);
            return StepCatalog.Boss.All(d => sys.Contains("  " + d.Id)) && sys.Contains("Bat") && sys.Contains("{\"boss\":")
                   && StepCatalog.Cutscene.All(d => cut.Contains("  " + d.Id)) && !cut.Contains("  dash:") && cut.Contains("{\"steps\":");
        });
        t.Test("boss ai: a messy reply is normalised (fences, synonyms, numbers as text, unknown steps/attacks dropped, missing phases)", () =>
        {
            var r = Ask(SeqMode.Boss, "", "Sure! ```json\n{\"boss\":{\"attacks\":{\"Smash\":[{\"action\":\"Charge\",\"speed\":\"500\",\"duration\":\"0.5\"},{\"do\":\"lasers\"}]," +
                                          "\"Rain\":[{\"type\":\"fire\",\"pattern\":\"ring\",\"count\":\"8\"}]},\"phases\":[{\"name\":\"P1\",\"when\":\"start\",\"attacks\":[\"Smash\",\"Nope\"]},{\"name\":\"P2\",\"health\":\"40%\"}]}}\n```");
            var b = SequenceJson.ParseBoss(r.Json, out var err);
            var dash = b.Attack("Smash")?.Steps.FirstOrDefault();
            return r.Ok && err == null && r.Calls == 1 && dash?.Do == "dash" && dash.Num("speed") == 500 && dash.Num("time") == 0.5 && b.Attack("Smash")!.Steps.Count == 1
                   && b.Attack("Rain")!.Steps[0].Do == "shoot" && b.Attack("Rain")!.Steps[0].Num("count") == 8
                   && b.Phases.Count == 2 && b.Phases[0].Attacks.SequenceEqual(new[] { "Smash" }) && b.Phases[1].When == "health" && b.Phases[1].Value == 40
                   && b.Phases[1].Attacks.Count == 2 && r.Notes.Any(n => n.Contains("lasers")) && r.Notes.Any(n => n.Contains("Nope"));
        });
        t.Test("boss ai: an unusable reply is retried once with the problem explained", () =>
        {
            var r = Ask(SeqMode.Boss, "", "I cannot do that.", "{\"attacks\":{\"Hop\":[{\"do\":\"jump\"}]},\"phases\":[{\"name\":\"Only\",\"attacks\":[\"Hop\"]}]}");
            return r.Ok && r.Calls == 2 && lastPrompts.Count == 2 && lastPrompts[1].Contains("could not be used") && SequenceJson.ParseBoss(r.Json, out _).Attacks.Count == 1;
        });
        t.Test("boss ai: two unusable replies fail with the reason (nothing is applied)", () =>
        {
            var r = Ask(SeqMode.Boss, "", "{\"attacks\":{}}", "{\"phases\":[]}");
            return !r.Ok && r.Error.Contains("no attacks") && r.Json == "";
        });
        t.Test("boss ai: the current boss goes into the request (change it, keep the rest); the starter boss counts as empty", () =>
        {
            Ask(SeqMode.Boss, BossDemo.BossScriptJson(), "{\"attacks\":{\"A\":[{\"do\":\"wait\"}]}}");
            bool withCurrent = lastPrompts[0].Contains("CURRENT BOSS") && lastPrompts[0].Contains("Jump slam") && lastPrompts[0].Contains("Keep everything");
            Ask(SeqMode.Boss, BehaviorCatalog.Info(BehaviorKind.BossAI).CreateData().Text("script"), "{\"attacks\":{\"A\":[{\"do\":\"wait\"}]}}");
            return withCurrent && !lastPrompts[0].Contains("CURRENT BOSS");
        });
        t.Test("cutscene ai: a bare step array in prose is accepted; an empty one is retried", () =>
        {
            var r = Ask(SeqMode.Cutscene, "", "Here: [{\"do\":\"say\",\"text\":\"Hi\"},{\"do\":\"camera\",\"object\":\"Player\",\"time\":\"1.5\"}] done.");
            var r2 = Ask(SeqMode.Cutscene, "", "{\"steps\":[]}", "{\"steps\":[{\"do\":\"wait\"}]}");
            var c = SequenceJson.ParseCutscene(r.Json, out _);
            return r.Ok && c.Steps.Count == 2 && c.Steps[1].Num("time") == 1.5 && r2.Ok && r2.Calls == 2;
        });

        // ---------------- the demo
        t.Test("boss demo: builds, loads, and 600 frames play without a console error", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_BossDemo_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            var project = ProjectIO.Load(BossDemo.Build(dir));
            var p = new PlayWindow(project, project.StartScene!); t.Sims.Add(p);
            p.TLoad(); p.TSteps(600);
            return p.TLog.Errors == 0 && p.TLog.Warns == 0 && p.TCutsceneRunning("Intro") && p.TExists("SlimeKing");
        });
    }
}
