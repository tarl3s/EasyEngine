using System;
using System.Linq;

namespace EasyEngine;

// "↔ Reverse" on a DO block: the action does its opposite (PlayWindow.RuleReverse.cs), and the blocks keep the tick.
public partial class App
{
    partial void FeatureTestsRuleReverse(FullTestKit t)
    {
        static string J(string s) => s.Replace('\'', '"');
        t.Test("reverse: add subtracts (a number and a formula), give takes, speeds go the other way", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'setvar','var':'a','to':10},{'when':'start','do':'additem','item':'key','amount':3},"
                          + "{'when':'keypress','key':'E','do':'addvar','var':'a','amount':4,'reverse':true},{'when':'keypress','key':'E','do':'addvar','var':'a','expr':'1 + 1','reverse':true},"
                          + "{'when':'keypress','key':'E','do':'additem','item':'key','amount':1,'reverse':true},{'when':'keypress','key':'V','do':'velocity','vx':120,'reverse':true}]"), PhysicsMode.None, null);
            p.TSteps(2); p.TKey("E", true); p.TStep(); p.TKey("E", false); p.TKey("V", true); p.TStep(); p.TKey("V", false); p.TSteps(1);
            return p.TVar("a") == 4 && p.TItem("key") == 2 && p.TVX("A") < -100;
        });
        t.Test("reverse: show hides, heal hurts, behavior on turns it off", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'show','reverse':true},{'when':'start','do':'behavior','kind':'Patrol','on':'on','reverse':true}]"), PhysicsMode.None,
                (pr, s) => s.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A").Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Patrol).CreateData()));
            p.TSteps(3); double x0 = p.TX("A"); p.TSteps(20);
            var q = t.Sim(J("[{'when':'start','do':'heal','amount':2,'reverse':true}]"), PhysicsMode.None,
                (pr, s) => s.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A").Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Health).CreateData()));
            double hp0 = q.THpMax("A"); q.TSteps(3);
            return p.TOpacity("A") < 0.05 && Math.Abs(p.TX("A") - x0) < 0.5 && q.THp("A") == hp0 - 2;
        });
        t.Test("reverse: a reversed sound stops instead of playing; nothing plays", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'playsound','sound':'Assets/sounds/x.wav','reverse':true}]"), PhysicsMode.None, null);
            p.TSteps(2);
            return p.TSounds.Count == 0 && p.TLogHas("stopped") && !p.TLogHas("Unknown action");
        });
        t.Test("ai fix: an object written inside \"level\" becomes an object to create; 'add score * 2' doubles it", () =>
        {
            var shaped = System.Text.Json.Nodes.JsonNode.Parse(AiSpecFix.Shape(J("{'level':{'coins':3,'boss':{'behaviors':[{'kind':'Boss'}]}}}")))!;
            var doubled = AiSpecFix.ForRequest(J("[{'when':'touch','name':'Player','do':'addvar','var':'score','amount':'score * 2'}]"), "the star doubles the score", "Star");
            var plain = AiSpecFix.ForRequest(J("[{'when':'touch','name':'Player','do':'addvar','var':'score','amount':'score + 1'}]"), "", "Star");
            return shaped["level"]!["boss"] == null && (string?)shaped["create"]![0]!["name"] == "Boss" && (int)shaped["level"]!["coins"]! == 3
                   && doubled == J("[{'when':'touch','name':'Player','do':'setvar','var':'score','to':'score * 2'}]") && plain.Contains("addvar");
        });
        t.Test("reverse: the blocks keep the tick through open → save, and only reversible actions offer it", () =>
        {
            var js = J("[{'when':'start','do':'show','target':'self','reverse':true}]");
            return t.RT(js) == js && PlayWindow.ReversibleActions.Contains("playsound") && !PlayWindow.ReversibleActions.Contains("goscene");
        });
    }
}
