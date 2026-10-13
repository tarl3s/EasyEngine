using System;
using System.Collections.Generic;

namespace EasyEngine
{
    // Custom rules ("Scratch"): the shared bits of the rule runtime that live outside PlayWindow.xaml.cs —
    // - the rules of EVERY Custom behavior an object has, in one list that runs once a frame, after its other behaviors;
    // - a rule's guards ("Only run if" = "cond", and the old if/ifop/ifval), for normal and "when it dies" rules alike;
    // - "died this step": an object that disappeared during this step still counts for the touch events of the same step,
    //   so "Player touches Heart" and the Heart's own "touch Player → destroy" both happen whichever runs first;
    // - the events made for what beginners kept failing to build with blocks:
    //     {"when":"keynear","key":"E","name":"Player","radius":100}  a key pressed while that object is near (once per press)
    //     {"when":"keyidle","key":"J","seconds":1}                   once, when the key hasn't been pressed for that long
    //     {"when":"becomes","var":"combo","op":">=","value":3}        the moment a comparison turns true (not every frame)
    //     {"when":"allgone","name":"Coin"}                           when none of an object are left (Coin, Coin_2, Coin3…)
    public partial class PlayWindow
    {
        private static long s_steps; // every logic step ever played (any window): Entity.Clock

        private partial class Entity
        {
            private bool _dead;
            public long DiedAt = -1;
            /// <summary>Gone (destroyed, collected, turned off); remembers the step it happened in.</summary>
            public bool Dead { get => _dead; set { if (value && !_dead) DiedAt = Clock; _dead = value; } }
            /// <summary>It disappeared during the step being played (it is still in the list until the end of the frame).</summary>
            public bool DiedThisStep => _dead && DiedAt == Clock;
            internal static long Clock;             // the step being played (StepLogic)
            public int Hits;                         // hits taken (DamageEntity): the "hurt" event also sees a hit that knocked it out
        }

        // Every Custom behavior's rules, in order, as one list; a second behavior's blocks never join the first one's.
        private void BuildCustomRules(Entity en, GameObjectData obj)
        {
            int groupBase = 0;
            foreach (var b in obj.Behaviors)
            {
                if (b.Kind != BehaviorKind.Custom) continue;
                var list = ParseCustom(b.Text("script", "[]"), obj.Name);
                int top = groupBase;
                foreach (var r in list)
                {
                    r.Eval = x => EvalExpr(x, en); // values written as expressions
                    if (r.Group != 0) { r.Group += groupBase; top = Math.Max(top, r.Group); }
                }
                groupBase = top;
                if (en.Custom == null) en.Custom = list; else en.Custom.AddRange(list);
            }
        }

        // a rule's guards: the old "if"/"ifop"/"ifval" (a global variable) and "cond" (a formula; empty = no condition)
        private bool GuardsAllow(Entity en, CustomRule r)
        {
            if (r.T.ContainsKey("if") && !CompareVar(Var(r.Str("if")), r.Str("ifop", ">="), r.Num("ifval"))) return false;
            if (r.T.TryGetValue("cond", out var c) && !string.IsNullOrWhiteSpace(c) && EvalExpr(c, en) == 0) return false;
            return true;
        }

        // the new events (each reads all of its fields every frame, so the parity test sees them)
        private bool NewEventFires(Entity en, CustomRule r, double dt)
        {
            switch (r.When)
            {
                case "keynear": // the key goes down while the object named is near (once per press; held doesn't repeat)
                {
                    bool down = _keys.Contains(ParseKey(r.WStr("key", "E")));
                    string who = r.WStr("name", "Player");
                    double radius = Math.Max(0, r.WNum("radius", 100));
                    bool pressed = down && !r.WasTouching; // (WasTouching: the key was down last frame)
                    r.WasTouching = down;
                    return pressed && IsNear(en, who, radius);
                }
                case "keyidle": // once, when the key has been left alone that long after a press (the next press arms it again)
                {
                    bool down = _keys.Contains(ParseKey(r.WStr("key", "Space")));
                    double secs = Math.Max(0, r.WNum("seconds", 1));
                    if (down) { r.Started = true; r.Timer = 0; return false; }
                    if (!r.Started) return false;
                    r.Timer += dt;
                    if (r.Timer < secs) return false;
                    r.Started = false;
                    return true;
                }
                case "becomes": // the moment the comparison turns true (already true when it appears isn't "becoming": a variable
                {               // another object sets at the start would fire it by mistake)
                    bool now = CompareVar(Var(r.WStr("var")), r.WStr("op", ">="), r.WNum("value"));
                    bool fire = r.Started && now && !r.WasTouching; // (WasTouching: it was true last frame)
                    r.Started = true; r.WasTouching = now;
                    return fire;
                }
                case "allgone": // there were some, and now none is left
                {
                    string name = r.WStr("name");
                    int n = 0;
                    foreach (var x in _entities) if (!x.Dead && InFamily(x.Data.Name, name)) n++;
                    bool fire = n == 0 && r.Last >= 1; // r.Last: how many there were last frame (NaN at first)
                    r.Last = n;
                    return fire;
                }
            }
            return false;
        }

        // another live object of that name (or one of its numbered copies) within radius of this one's centre, or touching it
        private bool IsNear(Entity en, string who, double radius)
        {
            foreach (var x in _entities)
            {
                if (x == en || x.Dead || !InFamily(x.Data.Name, who)) continue;
                double dx = x.CX - en.CX, dy = x.CY - en.CY;
                if (dx * dx + dy * dy <= radius * radius || Touching(en, x)) return true;
            }
            return false;
        }

        /// <summary>"Coin" names Coin and its numbered copies (Coin_2, Coin 3, Coin4…), capital letters aside. No allocation.</summary>
        internal static bool InFamily(string name, string want)
        {
            if (want.Length == 0 || name.Length < want.Length || string.Compare(name, 0, want, 0, want.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            int i = want.Length;
            if (i == name.Length) return true;
            if (name[i] is '_' or ' ' or '-') i++;
            if (i >= name.Length) return false;
            for (; i < name.Length; i++) if (!char.IsDigit(name[i])) return false;
            return true;
        }
    }
}
