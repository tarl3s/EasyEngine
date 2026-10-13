using System;
using System.Collections.Generic;

namespace EasyEngine
{
    // The Scratch blocks that belong to a behavior the object has (BlockEditorWindow.Palette.cs: BlockDef.Needs):
    // events "landed" (Platformer), "hurt" (Health), "interact" (Interact) and actions "controls", "ability", "setjump",
    // "heal", "behavior". Each action reads all of its fields first, so on an object without that behavior it simply does
    // nothing (FullTest.Parity plays every block on a bare object).
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public HashSet<BehaviorKind>? OffKinds; // behaviors a "behavior" block turned off (they rest, like a false "only while")
            public int InteractCount;               // how many times its Interact effect fired (the "interact" event)
        }

        // the frame a behavior event happens; each rule keeps what it saw last frame in r.Last (and r.Timer for "landed")
        private static bool BehaviorEventFires(Entity en, CustomRule r)
        {
            switch (r.When)
            {
                case "landed":
                {
                    // on the ground after at least two frames in the air (an object placed on the floor isn't "landing"
                    // on its first frame, and one frame of not touching on a bumpy floor isn't a jump)
                    if (!en.Grounded) { r.Timer += 1; return false; }
                    bool fire = r.Timer >= 2;
                    r.Timer = 0;
                    return fire;
                }
                case "hurt": // its health went down, or it took a hit (also the one that knocked out a character who comes back at once)
                {
                    double hp = en.CurHealth;
                    bool fire = !double.IsNaN(r.Last) && (hp < r.Last - 1e-9 || en.Hits != (int)r.Timer);
                    r.Last = hp; r.Timer = en.Hits;
                    return fire;
                }
                case "interact":
                {
                    bool fire = !double.IsNaN(r.Last) && en.InteractCount != r.Last;
                    r.Last = en.InteractCount;
                    return fire;
                }
            }
            return false;
        }

        private void DoBehaviorBlock(Entity en, CustomRule r)
        {
            switch (r.Do)
            {
                case "controls": // the arrow keys / WASD move it, or not (its Platformer / TopDown "controls")
                {
                    bool on = r.Num("on", 0) >= 0.5;
                    foreach (var b in en.Data.Behaviors)
                        if (b.Kind is BehaviorKind.Platformer or BehaviorKind.TopDown) b.Nums["controls"] = on ? 1 : 0;
                    break;
                }
                case "ability":
                {
                    string key = r.Str("ability", "doubleJump").Trim().ToLowerInvariant() switch
                    {
                        "walljump" or "wall" => "wallJump",
                        "dash" => "dash",
                        _ => "doubleJump",
                    };
                    bool on = r.Num("on", 1) >= 0.5;
                    var plat = en.Beh(BehaviorKind.Platformer);
                    if (plat != null) plat.Nums[key] = on ? 1 : 0;
                    break;
                }
                case "setjump":
                {
                    double jump = r.Num("jump", 800);
                    var plat = en.Beh(BehaviorKind.Platformer);
                    if (plat != null) plat.Nums["jump"] = Math.Max(0, jump);
                    break;
                }
                case "heal": // this object, or another one ("target": a Heart heals the Player who touches it)
                {
                    double amount = r.Num("amount", 1);
                    string who = r.Str("target", "self").Trim();
                    var t = who.Length == 0 || who.Equals("self", StringComparison.OrdinalIgnoreCase) ? en : FindAlive(who);
                    if (t != null && t.Has(BehaviorKind.Health)) t.CurHealth = Math.Min(t.MaxHealth, t.CurHealth + amount);
                    break;
                }
                case "behavior":
                {
                    string name = r.Str("kind", "Patrol").Trim();
                    bool on = r.Num("on", 0) >= 0.5;
                    if (name.Length == 0 || char.IsDigit(name[0]) || !Enum.TryParse<BehaviorKind>(name, true, out var kind) || !OnlyWhileKinds.Contains(kind) || !en.Has(kind)) break;
                    if (on) en.OffKinds?.Remove(kind);
                    else (en.OffKinds ??= new HashSet<BehaviorKind>()).Add(kind);
                    break;
                }
            }
        }
    }
}
