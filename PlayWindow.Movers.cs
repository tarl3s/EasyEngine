using System;

namespace EasyEngine
{
    // Who moves an object when several of its behaviors want to (the "mover" rules). Before, every mover simply wrote
    // VX/VY in list order, so the last one won, a resting one stopped the others, and a hit's knockback was erased the
    // next tick. Now, each tick:
    //   1. Knockback first: for a moment after a hit (KnockTimer) no behavior steers it, then it stops — it doesn't keep
    //      sliding (gravity has no friction) or drift away (an object without gravity has nothing to stop it).
    //   2. Player keys: a Platformer / Top-Down with a key held steers it (its jump / gravity always work).
    //   3. One automatic mover drives it — the strongest one that is on: Chase / Smart Chase (while its target exists and
    //      is within its "Only within" distance) > Advanced Patrol > Patrol; on a tie, the later one in the list.
    //      So Patrol + Chase patrols until the target comes near, then chases, then goes back to patrolling.
    //   4. A Platformer / Top-Down with no key held leaves the speed to that driver (it only adds gravity / walls).
    //   5. A mover resting ("Only while" false, or turned off by a block) — or a chaser with nothing (near) to chase — stops
    //      the object only when nothing else drives it, no key is held and it is not a Projectile (a homing shot flies on).
    //   6. On an object with gravity (Dynamic or Platformer) Chase and Smart Chase only steer left/right: it walks after
    //      its target instead of flying, and they never overshoot (no shaking on top of the target).
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public BehaviorData? Driver;          // the automatic mover that steers it this tick (null = none)
            public long PatrolFrame;              // Patrol: the tick it last moved, the place it was and the speed it asked for
            public double PatrolLast, PatrolWant; //   (so a wall or an edge that stops it turns it around)
            public double BlockedVX;              // the horizontal speed a wall took away in the last physics step (Pushable reads it)
            public bool IsSolid;                  // in the colliders list: a touch is "against it", not "inside it"
            public bool InputHeld;                // a Platformer / Top-Down key is held: the player steers (rule 2)
        }

        private const double KnockTime = 0.2; // seconds a hit's knockback keeps the movers' hands off

        internal static bool IsAutoMover(BehaviorKind k) =>
            k is BehaviorKind.Patrol or BehaviorKind.AdvancedPatrol or BehaviorKind.Chase or BehaviorKind.PathChase;

        private static int MoverRank(BehaviorKind k) => k switch
        {
            BehaviorKind.Chase or BehaviorKind.PathChase => 3,
            BehaviorKind.AdvancedPatrol => 2,
            BehaviorKind.Patrol => 1,
            _ => 0
        };

        /// <summary>Rule 3: the automatic mover that steers it this tick (null = none is on, or a chaser has nothing to chase).</summary>
        private BehaviorData? PickDriver(Entity en)
        {
            BehaviorData? best = null; int bestRank = 0;
            foreach (var bh in en.Data.Behaviors)
            {
                int rank = MoverRank(bh.Kind);
                if (rank == 0 || rank < bestRank || !BehaviorOn(en, bh)) continue;
                if (rank == 3 && ChaseTarget(en, bh) == null) continue; // nothing (near) to chase: a patrol may walk
                best = bh; bestRank = rank;
            }
            return best;
        }

        /// <summary>The live object a Chase / Smart Chase goes after — null when there's none, or it is farther than "Only within".</summary>
        private Entity? ChaseTarget(Entity en, BehaviorData bh)
        {
            var name = bh.Text("target", "Player");
            Entity? t = null;
            foreach (var x in _entities) if (!x.Dead && x != en && x.Data.Name == name) { t = x; break; }
            if (t == null) return null;
            double range = bh.Num("range", 0);
            if (range <= 0) return t;
            double dx = t.CX - en.CX, dy = t.CY - en.CY;
            return dx * dx + dy * dy <= range * range ? t : null;
        }

        /// <summary>Rule 1: counts the knockback down; when it ends the object stops (a Projectile keeps flying). True while knocked.</summary>
        private bool KnockTick(Entity en, double dt)
        {
            if (en.KnockTimer <= 0) return false;
            en.KnockTimer -= dt;
            if (en.KnockTimer > 0) return true;
            if (!en.Has(BehaviorKind.Projectile)) { en.VX = 0; if (!IsDynamic(en)) en.VY = 0; }
            return false;
        }

        /// <summary>Rule 5: a resting mover (or a chaser with nothing to chase) stops the object — unless another mover drives it.</summary>
        private void RestMover(Entity en)
        {
            if (en.Driver != null || en.KnockTimer > 0 || en.InputHeld || en.Has(BehaviorKind.Projectile)) return; // a projectile keeps its flight (a homing shot whose target is gone flies on)
            en.VX = 0;
            if (!IsDynamic(en)) en.VY = 0; // gravity still pulls a Dynamic or Platformer one
        }

        /// <summary>Rule 6: steer toward (tx, ty) at speed, never past it; with gravity only left/right.</summary>
        private void SteerToward(Entity en, double tx, double ty, double speed, double dt)
        {
            double dx = tx - en.CX, dy = ty - en.CY;
            double step = Math.Max(1e-6, dt);
            if (IsDynamic(en))
            {
                en.VX = Math.Abs(dx) > 1 ? Math.Sign(dx) * Math.Min(speed, Math.Abs(dx) / step) : 0;
                return;
            }
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1) { en.VX = 0; en.VY = 0; return; }
            double v = Math.Min(speed, len / step);
            en.VX = dx / len * v; en.VY = dy / len * v;
        }

        /// <summary>A touch with a solid object is being against it (standing on it); with anything else it is overlapping.</summary>
        private static bool Contact(Entity self, Entity other)
            => self.IsSolid && !self.SolidDisabled ? Touching(self, other) : Overlap(self, other);
    }
}
