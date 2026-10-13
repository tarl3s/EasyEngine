using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // The Lives behavior: a number of tries, kept in a variable (default "lives", so a Label "Lives: {lives}" or a rule can
    // use it). A life is lost when:
    //   - its Health runs out (Health's "respawn" is taken over: the life is lost instead, "when it dies" rules still run),
    //   - it touches the hazard picked ("Lava", or any of its numbered copies),
    //   - it falls out of the level,
    //   - anything else takes 1 from the variable (a rule "touch Lava → addvar lives -1" works the same).
    // Losing one sends it back to the last checkpoint (or the start, or nowhere) with a short safe time (it blinks); none
    // left goes to the scene picked (Game Over), or starts this scene again. The variable is set to the starting lives the
    // first time a scene with it is played, and again whenever it is 0 (after a game over) — so it carries on to Level 2.
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public bool LivesReady;                 // the variable was checked and the hearts made
            public double LivesSeen;                // the lives it had last frame (a drop = a life lost)
            public double LivesSafe;                // blink time left after losing one
            public TextBlock? LivesHud;             // the hearts at the top right (screen space)
            public double LivesShown = double.NaN;  // the count the hearts show
        }

        private readonly System.Collections.Generic.HashSet<string> _livesStarted = new(StringComparer.OrdinalIgnoreCase); // this play session
        private static readonly SolidColorBrush HeartBrush = Frozen(Color.FromRgb(0xFF, 0x4D, 0x6D));
        private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static string LivesVar(BehaviorData bh) { var v = bh.Text("var", "lives").Trim(); return v.Length > 0 ? v : "lives"; }

        private void UpdateLives(Entity en, BehaviorData bh, double dt)
        {
            string v = LivesVar(bh);
            if (!en.LivesReady) StartLives(en, bh, v);
            if (!en.Has(BehaviorKind.Health) && en.InvTimer > 0) en.InvTimer -= dt; // (Health counts it down itself)
            double n = Var(v);

            // the hazard: touching it costs a life (again after the safe time, if it is still touching it)
            string hz = bh.Text("hazard", "").Trim();
            if (hz.Length > 0 && en.InvTimer <= 0)
                foreach (var x in _entities)
                    if (x != en && !x.Dead && Touching(en, x) && InFamily(x.Data.Name, hz))
                    {
                        _vars[v] = n -= 1;
                        Info("Lives", en.Data.Name, $"Touched {x.Data.Name}: lost a life", $"{Math.Max(0, n):0} left");
                        break;
                    }
            // out of the level (before the fall would remove it)
            if (en.Y > Math.Max(_scene.Height, _terrainBottom) + 200 && n >= en.LivesSeen - 1e-9)
            {
                _vars[v] = n -= 1;
                Info("Lives", en.Data.Name, "Fell out of the level: lost a life", $"{Math.Max(0, n):0} left");
            }

            if (n < en.LivesSeen - 1e-9) LoseLife(en, bh, n);
            en.LivesSeen = n;

            if (en.LivesSafe > 0)
            {
                en.LivesSafe -= dt;
                en.Visual.Opacity = en.LivesSafe <= 0 ? Math.Clamp(en.Data.Opacity, 0, 1) : ((int)(en.LivesSafe * 12) % 2 == 0 ? 0.3 : 1);
            }
            if (en.LivesHud != null && n != en.LivesShown) { en.LivesShown = n; en.LivesHud.Text = HeartsText(n); }
        }

        private void StartLives(Entity en, BehaviorData bh, string v)
        {
            en.LivesReady = true;
            double start = Math.Max(1, Math.Round(bh.Num("lives", 3)));
            // a new game (the first time this session, unless the scene sets the variable itself) or after a game over: full
            // lives; else they carry on (Level 2)
            bool fresh = _livesStarted.Add(v);
            if (Var(v) <= 0 || fresh && !_scene.Variables.ContainsKey(v)) _vars[v] = start;
            en.LivesSeen = Var(v);
            if (!en.Has(BehaviorKind.Health)) { en.RespawnX = en.StartX; en.RespawnY = en.StartY; } // (Health sets them itself)
            else if (en.OnZeroHealth == null) en.OnZeroHealth = LivesZeroHealth;
            if (bh.Num("show", 1) >= 0.5)
            {
                en.LivesHud = new TextBlock
                {
                    Width = 300, TextAlignment = TextAlignment.Right, FontSize = 26, FontWeight = FontWeights.Bold,
                    Foreground = HeartBrush, IsHitTestVisible = false,
                };
                Canvas.SetLeft(en.LivesHud, Root.Width - 316); Canvas.SetTop(en.LivesHud, 60); // top right, below the test bar
                Hud.Children.Add(en.LivesHud);
            }
            Info("Lives", en.Data.Name, $"{Var(v):0} lives", $"in the variable \"{v}\"");
        }

        private static string HeartsText(double n)
        {
            int k = (int)Math.Max(0, Math.Round(n));
            return k == 0 ? "" : k <= 8 ? string.Join(" ", System.Linq.Enumerable.Repeat("♥", k)) : "♥ × " + k;
        }

        // its Health ran out: a life is lost instead of Health's own respawn ("when it dies" rules still run)
        private bool LivesZeroHealth(Entity en)
        {
            var bh = en.Beh(BehaviorKind.Lives);
            if (bh == null) return false;
            RunDeathRules(en, 0);
            string v = LivesVar(bh);
            _vars[v] = Var(v) - 1;
            en.CurHealth = en.MaxHealth;
            Info("Lives", en.Data.Name, "Health ran out: lost a life", $"{Math.Max(0, Var(v)):0} left");
            if (en.LivesReady && Var(v) < en.LivesSeen - 1e-9) { LoseLife(en, bh, Var(v)); en.LivesSeen = Var(v); } // at once (it may run before Lives)
            return true;
        }

        private void LoseLife(Entity en, BehaviorData bh, double left)
        {
            if (left <= 0) // game over
            {
                var sc = bh.Text("scene", "").Trim();
                bool toScene = sc.Length > 0 && _project.FindScene(sc) != null;
                _pendingScene = toScene ? sc : _sceneName;
                Info("Lives", en.Data.Name, toScene ? $"No lives left → scene \"{sc}\"" : "No lives left → the scene starts again");
                return;
            }
            switch (bh.Text("back", "checkpoint").Trim().ToLowerInvariant())
            {
                case "stay": break;
                case "start": en.X = en.StartX; en.Y = en.StartY; break;
                default: en.X = en.RespawnX; en.Y = en.RespawnY; break; // the last checkpoint (or the start)
            }
            if (en.Y > Math.Max(_scene.Height, _terrainBottom)) { en.X = en.RespawnX; en.Y = en.RespawnY; } // "stays" after a fall: back anyway
            en.VX = 0; en.VY = 0;
            if (en.Has(BehaviorKind.Health)) en.CurHealth = en.MaxHealth;
            double safe = Math.Max(0.2, bh.Num("invincible", 1.5));
            en.InvTimer = Math.Max(en.InvTimer, safe); en.LivesSafe = safe;
        }

        // ---- test hooks ----
        internal string TLivesHud(string name) => TE(name)?.LivesHud?.Text ?? "";
    }
}
