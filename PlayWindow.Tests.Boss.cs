using System.Collections.Generic;
using System.Linq;

namespace EasyEngine
{
    // Headless test hooks for the Boss AI (PlayWindow.Boss.cs) and the combat it relies on.
    public partial class PlayWindow
    {
        private Entity? TBoss(string name) => FindNamed(name) is { Boss: not null } e ? e : null;

        public string TBossPhase(string name) { var b = TBoss(name)?.Boss; return b != null && b.Phase >= 0 && b.Phase < b.Script.Phases.Count ? b.Script.Phases[b.Phase].Name : ""; }
        public string TBossAttack(string name) => TBoss(name)?.Boss?.Attack ?? "";
        public bool TBossDying(string name) => TBoss(name)?.Boss?.Dying == true;
        public bool TBossStarted(string name) => TBoss(name)?.Boss?.Started == true;
        public bool TBossTelegraph(string name) { var b = TBoss(name)?.Boss; return b != null && _gameTime < b.TelegraphUntil; }
        public bool TBossBarShown => BarBoss() != null;
        public double TBossBarFill => BarBoss() is Entity e ? BossBarFraction(e) : 0;
        public string TBossBarTitle => BarBoss()?.Boss?.Title ?? "";
        public void TSetHp(string name, double hp) { var e = TE(name); if (e != null) e.CurHealth = hp; }
        public void THurt(string name, double amount, double fromX) { var e = TE(name); if (e != null) DamageEntity(e, amount, 300, fromX); }
        public double TInv(string name) => TE(name)?.InvTimer ?? 0;
        public int TFacing(string name) => TE(name)?.Facing ?? 0;
        public List<(double vx, double vy)> TVels(string name) => _entities.Where(e => !e.Dead && e.Data.Name == name).Select(e => (e.VX, e.VY)).ToList();
    }
}
