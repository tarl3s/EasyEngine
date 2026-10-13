using System;
using System.Linq;

namespace EasyEngine
{
    /// <summary>
    /// Gentle warnings for behaviors that can't work together on one object, shown on the behavior's card in the
    /// inspector. Most combinations are sorted out by the game itself (PlayWindow.Movers.cs: one mover steers at a time,
    /// Chase beats Patrol while its target is near…); these are the ones where a behavior would simply never do anything.
    /// </summary>
    public static class BehaviorCombos
    {
        private static bool Has(GameObjectData o, BehaviorKind k) => o.Behaviors.Any(b => b.Kind == k);

        /// <summary>The warning for this behavior on this object (translated), or null when it works as expected.</summary>
        public static string? Warning(GameObjectData obj, BehaviorData bh)
        {
            switch (bh.Kind)
            {
                case BehaviorKind.Patrol or BehaviorKind.AdvancedPatrol when Has(obj, BehaviorKind.BossAI):
                case BehaviorKind.Chase or BehaviorKind.PathChase when Has(obj, BehaviorKind.BossAI):
                case BehaviorKind.Platformer or BehaviorKind.TopDown when Has(obj, BehaviorKind.BossAI):
                    return L.T("The Boss AI moves this object by itself, so this movement is ignored in the game.");

                case BehaviorKind.Patrol or BehaviorKind.AdvancedPatrol when AlwaysChases(obj):
                    return L.T("Chase moves it whenever its target exists, so this patrol never shows. Set the Chase's 'Only when closer than' to patrol until the target comes near.");

                case BehaviorKind.Patrol when Has(obj, BehaviorKind.AdvancedPatrol):
                    return L.T("Advanced Patrol moves it, so this Patrol never does. Keep one of them.");

                case BehaviorKind.Chase or BehaviorKind.PathChase when bh.Text("target", "Player").Equals(obj.Name, StringComparison.OrdinalIgnoreCase):
                    return L.T("It is set to chase itself. Pick another object to chase.");

                case BehaviorKind.Platformer when Has(obj, BehaviorKind.TopDown):
                    return L.T("Top-Down Movement on the same object turns gravity off, so this Platformer can't fall or jump. Keep one of them.");

                case BehaviorKind.Pushable when obj.Behaviors.Any(b => b.Kind is BehaviorKind.Patrol or BehaviorKind.AdvancedPatrol or BehaviorKind.Chase or BehaviorKind.PathChase):
                    return L.T("A pushable block is moved by pushing; a Patrol or Chase on it fights the pushing. For a moving platform use Solid + Patrol.");
            }
            return null;
        }

        // a Chase / Smart Chase that is always on and has no distance limit: nothing else ever gets to move the object
        private static bool AlwaysChases(GameObjectData o) =>
            o.Behaviors.Any(b => b.Kind is BehaviorKind.Chase or BehaviorKind.PathChase && b.Num("range", 0) <= 0
                                 && (!b.Texts.TryGetValue("cond", out var c) || string.IsNullOrWhiteSpace(c))
                                 && !b.Text("target", "Player").Equals(o.Name, StringComparison.OrdinalIgnoreCase));
    }
}
