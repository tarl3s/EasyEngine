using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace EasyEngine
{
    // The Boss AI behavior: it waits until it is woken up, then fights in phases (the current phase = the last one whose
    // condition holds: "at the start", or "health at or below N %"). Entering a phase runs its "enter" steps once; then
    // it loops: pick an attack (at random without repeating, or in order), run its steps, rest, repeat. With death steps
    // it plays them at 0 HP (harmless and invulnerable), then runs its Custom 'death' rules and disappears.
    public partial class PlayWindow
    {
        private sealed class BossState
        {
            public BossScript Script = new();
            public bool Started, Dying, Heavy, Fly, Bar;
            public string Title = "BOSS", Target = "Player";
            public int Phase = -1;
            public readonly HashSet<int> Entered = new();
            public SeqRunner? Runner;
            public string Attack = "", LastAttack = "";
            public int NextInOrder;
            public double RestUntil;
            public bool Moving;                       // a movement step drove it this tick
            public double TelegraphUntil; public string TelegraphColor = "#FF4040";
            public double FlickerUntil; public bool Flickering;
            public readonly List<Entity> Minions = new();   // what it summoned: they go when it falls
            public readonly ScaleTransform Flip = new();    // its drawing: facing + the telegraph pulse (RenderBosses)
            public DropShadowEffect? Glow; public string GlowColor = "";
        }

        private partial class Entity { public BossState? Boss; }

        // The Boss AI moves its boss itself: other movement behaviors on it would fight it (a Platformer zeroes its speed
        // every frame), so the running copy drops them. A boss never respawns.
        partial void BossEntityAdded(Entity en)
        {
            if (!en.Has(BehaviorKind.BossAI)) return;
            int dropped = en.Data.Behaviors.RemoveAll(b => b.Kind is BehaviorKind.Platformer or BehaviorKind.TopDown or BehaviorKind.Patrol
                                                       or BehaviorKind.Chase or BehaviorKind.PathChase or BehaviorKind.AdvancedPatrol);
            if (dropped > 0) Info("Boss", en.Data.Name, $"{dropped} movement behavior(s) ignored — the Boss AI moves it");
            var hp = en.Beh(BehaviorKind.Health);
            if (hp != null) hp.Nums["respawn"] = 0;
        }

        partial void UpdateBoss(Entity en, BehaviorData bh, double dt)
        {
            var b = en.Boss ??= InitBoss(en, bh);
            var target = BossTarget(en);
            if (!b.Dying && b.Started && b.Phase >= 0 && !b.Moving && target != null && Math.Abs(target.CX - en.CX) > 4) en.Facing = target.CX > en.CX ? 1 : -1;
            if (!b.Moving) // nothing drives it: stand still (knockback fades out on a light boss)
            {
                en.VX = b.Heavy ? 0 : en.VX * 0.85;
                if (b.Fly) en.VY = b.Heavy ? 0 : en.VY * 0.85;
            }
            b.Moving = false;
            if (b.Dying) return; // its death steps are playing

            if (!b.Started)
            {
                bool wake = bh.Text("starts", "start").ToLowerInvariant() switch
                {
                    "near" => target != null && Math.Sqrt(Math.Pow(target.CX - en.CX, 2) + Math.Pow(target.CY - en.CY, 2)) <= bh.Num("distance", 400),
                    "var" => bh.Text("var", "").Length > 0 && Var(bh.Text("var", "")) >= 0.5,
                    _ => true
                };
                if (!wake) return;
                b.Started = true;
                Info("Boss", en.Data.Name, "Wakes up!", $"{b.Script.Phases.Count} phase(s) · {b.Script.Attacks.Count} attack(s) · fights {b.Target}");
            }

            int want = PhaseFor(en, b);
            if (want != b.Phase) EnterPhase(en, b, want);
            if (b.Runner == null && _gameTime >= b.RestUntil) NextAttack(en, b);
        }

        private BossState InitBoss(Entity en, BehaviorData bh)
        {
            var notes = new List<string>();
            var b = new BossState
            {
                Script = SequenceJson.ParseBoss(bh.Text("script", ""), out var err, notes),
                Fly = bh.Text("moves", "ground").Equals("fly", StringComparison.OrdinalIgnoreCase),
                Heavy = bh.Num("heavy", 1) >= 0.5,
                Bar = bh.Num("hpbar", 1) >= 0.5,
                Title = bh.Text("title", "BOSS"),
                Target = bh.Text("target", "Player"),
            };
            if (err != null) Warn("Boss", en.Data.Name, "Its script can't be read — it will just stand there", err);
            else if (notes.Count > 0) Warn("Boss", en.Data.Name, $"{notes.Count} part(s) of its script were ignored", string.Join("\n", notes));
            b.Script.FillDefaultPhases(); // no phases written: one phase with every attack
            // it moves itself: physics on (walls stop it); a flyer has no gravity
            en.Data.Physics.Mode = PhysicsMode.Dynamic;
            if (b.Fly) en.Data.Physics.GravityScale = 0;
            en.NoKnockback = b.Heavy;   // a heavy boss isn't pushed around
            en.OnZeroHealth = BossDying; // at 0 HP: its death sequence first
            return b;
        }

        private Entity? BossTarget(Entity boss) => FindNamed(boss.Boss?.Target ?? "Player");

        private static double HealthPercent(Entity en) => en.Has(BehaviorKind.Health) && en.MaxHealth > 0 ? en.CurHealth / en.MaxHealth * 100 : 100;

        // The phase of the lowest health threshold reached ("at or below N %"), whatever their order in the list; before
        // any is reached, the (last) "at the start" phase — else the first one.
        private static int PhaseFor(Entity en, BossState b)
        {
            var phases = b.Script.Phases;
            if (phases.Count == 0) return -1;
            double pct = HealthPercent(en);
            int reached = -1, start = 0;
            for (int i = 0; i < phases.Count; i++)
            {
                var p = phases[i];
                if (p.When != "health") start = i;
                else if (pct <= p.Value + 1e-9 && (reached < 0 || p.Value < phases[reached].Value)) reached = i;
            }
            return reached >= 0 ? reached : start;
        }

        private void EnterPhase(Entity en, BossState b, int idx)
        {
            if (b.Runner != null) { AbortRunner(b.Runner); b.Runner = null; }
            b.Phase = idx; b.Attack = ""; b.NextInOrder = 0; b.RestUntil = 0;
            if (idx < 0) return;
            var phase = b.Script.Phases[idx];
            Info("Boss", en.Data.Name, $"{phase.Name} begins", $"health {HealthPercent(en):0}% · attacks: {(phase.Attacks.Count > 0 ? string.Join(", ", phase.Attacks) : "none")}");
            if (phase.Enter.Count > 0 && b.Entered.Add(idx))
                b.Runner = StartRunner(en, SeqMode.Boss, phase.Name + " start", phase.Enter, _ => { b.Runner = null; b.RestUntil = _gameTime + phase.Pause; });
        }

        // Pick the next attack: in order, or at random without doing the same one twice in a row.
        private void NextAttack(Entity en, BossState b)
        {
            if (b.Phase < 0) return;
            var phase = b.Script.Phases[b.Phase];
            var names = phase.Attacks.Where(n => b.Script.Attack(n) != null).ToList();
            if (names.Count == 0) return;
            string name;
            if (phase.Order == "inorder") name = names[b.NextInOrder++ % names.Count];
            else
            {
                var pool = names.Count > 1 ? names.Where(n => n != b.LastAttack).ToList() : names;
                name = pool[_rng.Next(pool.Count)];
            }
            b.LastAttack = b.Attack = name;
            LogThrottled($"boss-attack:{en.Data.Name}:{name}", 3, LogLevel.Info, "Boss", en.Data.Name, $"Attack: {name}", phase.Name);
            b.Runner = StartRunner(en, SeqMode.Boss, $"attack '{name}'", b.Script.Attack(name)!.Steps, _ => { b.Runner = null; b.Attack = ""; b.RestUntil = _gameTime + phase.Pause; });
        }

        // Its OnZeroHealth: a boss with death steps plays them first, harmless (true = handled, don't destroy it yet).
        private bool BossDying(Entity en)
        {
            var b = en.Boss;
            if (b == null) return false;
            if (!b.Dying) DismissMinions(b); // when the boss falls, what it summoned goes too
            if (b.Script.Death.Count == 0) return false;
            if (b.Dying) return true;
            b.Dying = true; en.Harmless = true;
            if (b.Runner != null) AbortRunner(b.Runner);
            en.InvTimer = double.MaxValue; en.VX = 0;
            Info("Boss", en.Data.Name, "Defeated — playing its death sequence", $"{b.Script.Death.Count} steps");
            b.Runner = StartRunner(en, SeqMode.Boss, "death", b.Script.Death, _ =>
            {
                b.Runner = null;
                Info("Boss", en.Data.Name, "Gone");
                RunDeathRules(en, 0);
                en.Dead = true;
            });
            b.Runner.OutlivesOwner = true; // a 'destroy self' among them doesn't cut the rest (nor its 'death' rules) short
            return true;
        }

        // its minions vanish in a puff (a fight is over when the boss is)
        private void DismissMinions(BossState b)
        {
            foreach (var m in b.Minions)
                if (!m.Dead) { EmitParticles(m.CX, m.CY, 12, 150, 360, 0, 0.5, 6, 200, "#C9A6FF"); m.Dead = true; }
            b.Minions.Clear();
        }

        private void BossSceneReset() { BossBar.Visibility = Visibility.Collapsed; }

        // ================================================================== boss steps
        private bool RunBossStep(SeqRunner r, SeqStep s)
        {
            var en = r.Owner;
            var b = en.Boss;
            if (b == null) return false;
            var target = BossTarget(en);
            switch (s.Do)
            {
                case "telegraph":
                    b.TelegraphUntil = _gameTime + Math.Max(0, s.Num("time", 0.6)); b.TelegraphColor = RuleColor(s.Str("color", "#FF4040"), en);
                    r.WaitUntil = b.TelegraphUntil;
                    return true;
                case "dash":
                {
                    double speed = s.Num("speed", 600), end = _gameTime + Math.Max(0, s.Num("time", 0.8));
                    var (dx, dy) = Toward(en, target, b.Fly);
                    if (Math.Abs(dx) > 0.01) en.Facing = dx > 0 ? 1 : -1;
                    r.Until = () =>
                    {
                        if (en.Dead || _gameTime >= end) return true;
                        b.Moving = true; en.VX = dx * speed; if (b.Fly) en.VY = dy * speed;
                        return false;
                    };
                    r.OnStop = () => { en.VX = 0; if (b.Fly) en.VY = 0; };
                    return true;
                }
                case "walk":
                {
                    double speed = s.Num("speed", 160), end = _gameTime + Math.Max(0, s.Num("time", 1.5));
                    r.Until = () =>
                    {
                        var t = BossTarget(en);
                        if (en.Dead || t == null || _gameTime >= end) return true;
                        b.Moving = true;
                        var (dx, dy) = Toward(en, t, b.Fly);
                        bool there = Math.Abs(t.CX - en.CX) < 12 && (!b.Fly || Math.Abs(t.CY - en.CY) < 12);
                        en.VX = there ? 0 : dx * speed; if (b.Fly) en.VY = there ? 0 : dy * speed;
                        if (Math.Abs(dx) > 0.01) en.Facing = dx > 0 ? 1 : -1;
                        return false;
                    };
                    r.OnStop = () => { en.VX = 0; if (b.Fly) en.VY = 0; };
                    return true;
                }
                case "jump":
                    BossJump(r, s, en, b);
                    return true;
                case "moveto":
                {
                    var m = MoveTo(r, en, MoveGoal(r, s, en, "to"), s.Num("time", 1));
                    r.Until = () => { b.Moving = true; return m.Done || en.Dead; };
                    return true;
                }
                case "shoot":
                {
                    int volleys = Math.Max(1, (int)Math.Round(s.Num("volleys", 1)));
                    double every = Math.Max(0.05, s.Num("every", 0.4)), next = _gameTime + every;
                    int k = 0;
                    Volley(r, s, en, k++);
                    if (volleys > 1)
                        r.Until = () =>
                        {
                            while (k < volleys && _gameTime >= next && !en.Dead) { Volley(r, s, en, k++); next += every; }
                            return k >= volleys || en.Dead;
                        };
                    return true;
                }
                case "summon":
                {
                    var name = s.Str("object");
                    int count = Math.Clamp((int)Math.Round(s.Num("count", 2)), 1, 30);
                    double radius = s.Num("radius", 120);
                    var tpl = name.Length > 0 ? FindTemplateOrPrefab(name) : null;
                    if (tpl == null) { StepWarn(r, s, $"nothing named '{name}' to summon (an object in this scene or a prefab)"); return true; }
                    for (int i = 0; i < count; i++)
                    {
                        double ox = count == 1 ? radius * en.Facing : -radius + 2 * radius * i / (count - 1);
                        // created right there, so it patrols and respawns from where it appeared
                        var m = SpawnAt(name, en.CX + ox - tpl.Width / 2, b.Fly ? en.CY - tpl.Height / 2 : en.Y + en.H - tpl.Height)!;
                        b.Minions.Add(m);
                        EmitParticles(m.CX, m.CY, 14, 160, 360, 0, 0.5, 6, 200, "#C9A6FF");
                    }
                    return true;
                }
                case "invulnerable":
                {
                    double time = Math.Max(0, s.Num("time", 1.5));
                    en.InvTimer = Math.Max(en.InvTimer, time);
                    b.FlickerUntil = _gameTime + time;
                    return true;
                }
            }
            return false;
        }

        // A unit direction toward the target: sideways only on the ground, straight at it when flying.
        private static (double dx, double dy) Toward(Entity en, Entity? t, bool fly)
        {
            if (t == null) return (en.Facing, 0);
            double dx = t.CX - en.CX, dy = fly ? t.CY - en.CY : 0, len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1 ? (en.Facing, 0) : (dx / len, dy / len);
        }

        // Leap (landing by the target), then optionally a shockwave: dust, a screen shake and a wave both ways.
        private void BossJump(SeqRunner r, SeqStep s, Entity en, BossState b)
        {
            if (b.Fly) { StepWarn(r, s, "a flying boss doesn't jump (use Glide to)"); return; }
            double g = Gravity * Math.Max(0.05, en.Data.Physics.GravityScale);
            double vy = Math.Sqrt(2 * g * Math.Max(10, s.Num("height", 220)));
            int stage = 0; // 0 = waiting to stand on the ground, 1 = launched, 2 = in the air
            double vx = 0, deadline = _gameTime + 4;
            r.Until = () =>
            {
                if (en.Dead || _gameTime > deadline) return true;
                b.Moving = true;
                switch (stage)
                {
                    case 0:
                        if (!en.Grounded && _gameTime < deadline - 3) return false; // give it a moment to land first
                        var t = BossTarget(en);
                        double flight = 2 * vy / g;
                        vx = s.On("toward", true) && t != null ? Math.Clamp((t.CX - en.CX) / flight, -700, 700) : 0;
                        if (Math.Abs(vx) > 1) en.Facing = vx > 0 ? 1 : -1;
                        en.VY = -vy; en.VX = vx; stage = 1;
                        return false;
                    case 1:
                        en.VX = vx;
                        if (!en.Grounded) stage = 2;
                        return false;
                    default:
                        en.VX = vx;
                        if (!en.Grounded) return false;
                        en.VX = 0;
                        if (s.On("shockwave", true)) Shockwave(en, b, s.Str("wave"));
                        return true;
                }
            };
            r.OnStop = () => en.VX = 0;
        }

        private void Shockwave(Entity en, BossState b, string waveName)
        {
            EmitParticles(en.CX, en.Y + en.H, 34, 280, 150, -90, 0.7, 7, 600, "#CDB48C");
            Shake(12, 0.45);
            var t = BossTarget(en);
            foreach (int dir in new[] { -1, 1 })
            {
                var wave = new GameObjectData { Type = "Bullet", Color = "#FFB347", Width = 30, Height = 18, CornerRadius = 9, BorderColor = "#FFF0C0", BorderWidth = 2 };
                wave.Behaviors.Add(HurtsTarget(b, t));
                var w = SpawnBullet(en, dir > 0 ? 0 : 180, 380, 1.4, waveName, wave);
                w.X = dir > 0 ? en.X + en.W : en.X - w.W;
                w.Y = en.Y + en.H - w.H;
            }
        }

        // One volley of bullets: a ring all around, a fan forward, or a fan aimed at the target.
        private void Volley(SeqRunner r, SeqStep s, Entity en, int volley)
        {
            var b = en.Boss!;
            int count = Math.Clamp((int)Math.Round(s.Num("count", 12)), 1, 72);
            double speed = s.Num("speed", 320), fan = s.Num("spread", 60), life = s.Num("life", 3), turn = s.Num("turn", 0) * volley;
            var t = BossTarget(en);
            var angles = new List<double>();
            switch (s.Str("pattern", "ring"))
            {
                case "spread":
                case "aimed":
                {
                    double center = s.Str("pattern") == "aimed" && t != null
                        ? Math.Atan2(t.CY - en.CY, t.CX - en.CX) * 180 / Math.PI
                        : (t != null ? (t.CX >= en.CX ? 0 : 180) : (en.Facing >= 0 ? 0 : 180));
                    for (int i = 0; i < count; i++) angles.Add(center + turn + (count == 1 ? 0 : -fan / 2 + fan * i / (count - 1)));
                    break;
                }
                default:
                    for (int i = 0; i < count; i++) angles.Add(turn + i * 360.0 / count);
                    break;
            }
            foreach (var a in angles)
            {
                var ball = new GameObjectData { Type = "Bullet", Color = "#FF6B4A", Width = 16, Height = 16, BorderColor = "#FFE0A0", BorderWidth = 2 };
                ball.Behaviors.Add(HurtsTarget(b, t));
                SpawnBullet(en, a, speed, life, s.Str("bullet"), ball);
            }
        }

        // The default boss bullet/wave hurts the boss's target and vanishes when it hits. A Damager matches the exact name,
        // so it is given the name of the target found (which may differ in case from the one typed).
        private static BehaviorData HurtsTarget(BossState b, Entity? target)
        {
            var d = BehaviorCatalog.Info(BehaviorKind.Damager).CreateData();
            d.Texts["target"] = target?.Data.Name ?? b.Target; d.Nums["damage"] = 1; d.Nums["knockback"] = 240; d.Nums["vanish"] = 1;
            return d;
        }

        // ================================================================== drawing (live only)
        private void RenderBosses()
        {
            foreach (var en in _entities)
            {
                var b = en.Boss;
                if (b == null || en.Dead) continue;

                double scale = 1;
                bool telegraph = _gameTime < b.TelegraphUntil;
                if (telegraph || b.Dying)
                {
                    var hex = b.Dying ? "#FF3030" : b.TelegraphColor;
                    var glow = b.Glow ??= new DropShadowEffect { ShadowDepth = 0, BlurRadius = 36 };
                    if (b.GlowColor != hex) { glow.Color = ParseColor(hex, Colors.Red); b.GlowColor = hex; }
                    glow.Opacity = 0.6 + 0.4 * Math.Abs(Math.Sin(_gameTime * 18));
                    if (en.Visual.Effect != glow) en.Visual.Effect = glow;
                    scale = 1 + 0.05 * Math.Sin(_gameTime * 40);
                }
                else if (en.Visual.Effect is DropShadowEffect) en.Visual.Effect = null;
                if (en.Visual.RenderTransform != b.Flip) { en.Visual.RenderTransformOrigin = new Point(0.5, 0.5); en.Visual.RenderTransform = b.Flip; }
                b.Flip.ScaleX = en.Facing * scale; b.Flip.ScaleY = scale;

                bool flicker = _gameTime < b.FlickerUntil || b.Dying;
                if (flicker) { en.Visual.Opacity = ((int)(_gameTime * (b.Dying ? 24 : 14)) % 2 == 0) ? 0.35 : 1; b.Flickering = true; }
                else if (b.Flickering) { en.Visual.Opacity = Math.Clamp(en.Data.Opacity, 0, 1); b.Flickering = false; }
            }

            var barBoss = BarBoss();
            BossBar.Visibility = barBoss != null ? Visibility.Visible : Visibility.Collapsed;
            if (barBoss == null) return;
            BossBarName.Text = barBoss.Boss!.Title;
            BossBarFill.Width = Math.Max(0, (BossBar.Width - 6) * BossBarFraction(barBoss));
        }

        // The boss whose health bar shows: the first awake one that has a bar.
        private Entity? BarBoss()
        {
            foreach (var e in _entities) if (!e.Dead && e.Boss is { Bar: true, Started: true }) return e;
            return null;
        }

        private static double BossBarFraction(Entity en) => en.MaxHealth > 0 ? Math.Clamp(en.CurHealth / en.MaxHealth, 0, 1) : 1;
    }
}
