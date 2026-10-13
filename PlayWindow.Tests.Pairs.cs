using System.Collections.Generic;

namespace EasyEngine
{
    // Probes for the --pairtest dev tool (App.PairTest.cs) and the behavior-combination tests (FullTest.BehaviorCombos.cs):
    // what one object is doing right now, read without LINQ so a probe every tick stays cheap.
    public partial class PlayWindow
    {
        public readonly record struct TState(double X, double Y, double VX, double VY, bool Dead, double Hp, double MaxHp,
                                            bool Grounded, bool Dynamic, double Knock);

        /// <summary>The first object with that name (alive or not), or null when there is none.</summary>
        public TState? TProbe(string name)
        {
            foreach (var e in _entities)
                if (e.Data.Name == name) return new TState(e.X, e.Y, e.VX, e.VY, e.Dead, e.CurHealth, e.MaxHealth, e.Grounded, e.FrameDynamic, e.KnockTimer);
            return null;
        }

        /// <summary>Every live object's position and speed — the pair test looks for NaN / ∞ / absurd values in all of them.</summary>
        public IEnumerable<(string name, double x, double y, double vx, double vy)> TAll()
        {
            foreach (var e in _entities) if (!e.Dead) yield return (e.Data.Name, e.X, e.Y, e.VX, e.VY);
        }

        /// <summary>The console's warnings and errors, one line each (for the pair test's report).</summary>
        public IEnumerable<string> TProblems()
        {
            foreach (var e in _log.Entries) if (e.Level != LogLevel.Info) yield return (e.Level == LogLevel.Error ? "ERROR " : "WARN ") + e.Line;
        }

        /// <summary>Hurts an object the way a Damager or a sword does (damage + knockback from fromX).</summary>
        public void THit(string name, double amount, double knockback, double fromX)
        {
            foreach (var e in _entities) if (e.Data.Name == name && !e.Dead) { DamageEntity(e, amount, knockback, fromX); return; }
        }
    }
}
