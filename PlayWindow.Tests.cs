using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    // Headless driver used by the --fulltest harness. Steps the real game logic
    // (behaviors + physics + tweens + particles) without showing a window or rendering,
    // so we can assert what every behavior and every Custom-script rule actually does.
    public partial class PlayWindow
    {
        /// <summary>Seeds the game's random numbers (scattered layouts, "once" seeds, boss attacks…) — call before TLoad
        /// so a test plays the same way every run.</summary>
        public void TSeed(int seed) => _rng = new System.Random(seed);

        public void TLoad()
        {
            _headless = true; // tests never play audio; sounds are only recorded + logged
            SeedGlobalVars();
            LoadScene(_sceneName);
        }

        // Same logic tick as live play (StepLogic); only the camera/render/cleanup are skipped,
        // so dead entities stay in the list (flagged) for the assertions.
        public void TStep(double dt = 0.016)
        {
            StepLogic(dt);
            if (_pendingScene != null) { var n = _pendingScene; _pendingScene = null; LoadScene(n); }
        }

        public void TSteps(int n, double dt = 0.016) { for (int i = 0; i < n; i++) TStep(dt); }

        public double TVar(string n) => Var(n);
        public void TSetVar(string n, double v) => _vars[n] = v;
        public double TItem(string item) => Var("item:" + item);

        public void TKey(string k, bool down) { var key = ParseKey(k); if (down) _keys.Add(key); else ReleaseKey(key); }
        public void TClearKeys() => _keys.Clear();
        public void TMouseDown(bool d) => _mouseDown = d;
        public void TMouseTap() => _lmbPressLatch = true; // pressed AND released between two logic ticks
        public void TRightDown(bool d) => _rmbDown = d;
        public void TWheel(int dir) => _wheelAccum += dir * 120; // +1 up, -1 down (applied on next TStep)
        public void TMouseAt(double x, double y) { _mouseWorld = new Point(x, y); _mouseScreen = new Point(x, y); }

        // debug console log
        public DebugLog TLog => _log;
        public bool TLogHas(string text) => _log.Has(text);
        public int TLogCount(string category, string? contains = null) => _log.CountOf(category, contains);
        public string TLogText() => _log.ToText();
        public void TShowConsole(bool show) => ShowConsole(show);
        public IReadOnlyList<string> TSounds => _soundsPlayed; // file names, in the order they were played
        public void TPlaySound(string rel) => PlaySoundFile(rel, 0.35, "sfx", false, "selftest");

        // lighting
        public void TUpdateLighting() => UpdateLighting();
        public bool TInSky(string name) { var e = TE(name); return e != null && VisualTreeHelper.GetParent(e.Visual) == Sky && Root.Children.IndexOf(Sky) < Root.Children.IndexOf(World); }
        public void TSetTimeNow(string when) => SetTimeOfDay(when, "test");
        public double TW(string name) => TE(name)?.W ?? 0;
        public void TSetTime(string when) => Dispatcher.BeginInvoke(new System.Action(() => SetTimeOfDay(when, "selftest")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        public int TLightHoles => _lightMasks.Count;
        public double TDarkness => Darkness.Opacity;
        public double TGlow => Lights.Opacity;
        public string TSkyColor => (Root.Background as SolidColorBrush)?.Color.ToString() ?? "";
        public string TDarkColor => (Darkness.Fill as SolidColorBrush)?.Color.ToString() ?? "";
        public bool TLightGridOn => _gridOn;
        public bool TSmoothDarknessShown => Darkness.Visibility == Visibility.Visible;
        public (byte r, byte g, byte b, byte a)? TLightGridAt(double wx, double wy) => LightGridAt(wx, wy);
        public bool TVisible(string name) { var e = TE(name); return e != null && !e.Dead && e.Visual.Visibility == Visibility.Visible; }

        // tools / harvesting
        public string THeld => _heldItem;
        public int THarvestLeft(string name) => TE(name)?.HarvestLeft ?? -1;
        public bool TFelled(string name) => TE(name)?.Felled ?? false;
        public bool TIsUi(double x, double y) => UiHitAt(new Point(x, y)) != null;
        public Point TUiCenter(string key) => _uiRects.TryGetValue(key, out var r) ? new Point(r.X + r.Width / 2, r.Y + r.Height / 2) : new Point(-1, -1);
        public Point THotbarSlotCenter(int i)
        {
            var hb = _entities.Select(e => e.Beh(BehaviorKind.Hotbar)).FirstOrDefault(b => b != null);
            if (hb == null) return new Point(-1, -1);
            var r = HotbarSlotRects(hb, ParseHotbarItems(hb.Text("items", "")).Count)[i];
            return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }
        /// <summary>A quick click at (x,y): pressed and released before the next logic tick.</summary>
        public void TClick(double x, double y) { TMouseAt(x, y); TMouseTap(); }
        public bool TConsoleVisible => ConsoleVisible;

        // camera + dialogue (live-only in play; these let headless tests drive them)
        public void TUpdateCamera(int frames = 1) { for (int i = 0; i < frames; i++) UpdateCamera(0.016); }
        public double TCamX => _camX;
        public double TCamY => _camY;
        public void TAdvanceDialog() => AdvanceDialog();        // = pressing Enter on the dialogue box
        public string TDialogText => DialogText.Text;
        public int TDialogQueued => _dialog.Count;

        public int TParticles => _particles.Count;
        public int TTweens => _tweens.Count;
        public bool TDialog => _dialogOpen;

        private Entity? TE(string name) => _entities.FirstOrDefault(z => z.Data.Name == name);
        public bool TExists(string name) => _entities.Any(z => !z.Dead && z.Data.Name == name);
        public bool TDead(string name) { var e = TE(name); return e == null || e.Dead; }
        public int TAlive(string name) => _entities.Count(z => !z.Dead && z.Data.Name == name);
        public double TX(string name) => TE(name)?.X ?? 0;
        public double TY(string name) => TE(name)?.Y ?? 0;
        public double TOpacity(string name) => TE(name)?.Visual.Opacity ?? -1;
        public double TScale(string name) => TE(name)?.TweenScale ?? 1;
        public double THpMax(string name) => TE(name)?.MaxHealth ?? 0;
        public int TLiveCount => _entities.Count(z => !z.Dead);
        public double[] TBulletVXs() => _entities.Where(z => !z.Dead && z.Has(BehaviorKind.Projectile)).Select(z => z.VX).ToArray();
        /// <summary>Adds every entity that exists right now to <paramref name="seen"/> (count spawns over a run).</summary>
        public void TCollect(System.Collections.Generic.HashSet<object> seen) { foreach (var z in _entities) seen.Add(z); }
        public double TVX(string name) => TE(name)?.VX ?? 0;
        public double TVY(string name) => TE(name)?.VY ?? 0;
        public double TRot(string name) => TE(name)?.Rot ?? 0;
        public double THp(string name) => TE(name)?.CurHealth ?? 0;
        /// <summary>A number of one of its behaviors in play (e.g. the Platformer's "jump"), or NaN.</summary>
        public double TBehNum(string name, BehaviorKind kind, string key) => TE(name)?.Beh(kind) is { } b && b.Nums.TryGetValue(key, out var v) ? v : double.NaN;
        public bool TGrounded(string name) => TE(name)?.Grounded ?? false;
        public double TLocal(string name, string varn) { var e = TE(name); return e != null && e.Locals.TryGetValue(varn, out var v) ? v : 0; }
        public double TSpeed(string name) => TE(name)?.SpeedScale ?? 0;
        public string TScene => _scene?.Name ?? "";
        public int TTiles(string name) { var e = TE(name); return e?.Tiles?.Count(c => !c.Broken) ?? -1; }
        public string TSolids() => string.Join(" ", _solids.Where(s => !s.Dead).Select(s => $"({s.X:0},{s.Y:0},{s.W:0}x{s.H:0})"));
        public void TPlace(string name, double x, double y) { var e = TE(name); if (e != null) { e.X = x; e.Y = y; } }
        public void TDestroyAll(string name) { foreach (var e in _entities) if (e.Data.Name == name) e.Dead = true; }
        /// <summary>Removes the dead objects like the end of a live frame does (TStep keeps them for the assertions).</summary>
        public void TCleanup() => Cleanup();

        /// <summary>Everything a Custom rule can change, as one text (variables, texts, lists, every object's own state, particles,
        /// glides, the dialogue, the scene, the sounds) — two plays that end with the same one did the same.</summary>
        public string TFingerprint()
        {
            static string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var sb = new System.Text.StringBuilder("scene=" + TScene);
            foreach (var kv in _vars.OrderBy(k => k.Key, System.StringComparer.OrdinalIgnoreCase)) sb.Append($" {kv.Key}={N(kv.Value)}");
            foreach (var kv in _texts.OrderBy(k => k.Key, System.StringComparer.OrdinalIgnoreCase)) sb.Append($" {kv.Key}='{kv.Value}'");
            foreach (var kv in _lists.OrderBy(k => k.Key, System.StringComparer.OrdinalIgnoreCase)) sb.Append($" {kv.Key}=[{string.Join(",", kv.Value)}]");
            foreach (var e in _entities)
            {
                sb.Append($" | {e.Data.Name}{(e.Dead ? " dead" : "")} {N(e.X)},{N(e.Y)} v{N(e.VX)},{N(e.VY)} r{N(e.Rot)} {N(e.W)}x{N(e.H)} o{N(e.Visual.Opacity)}"
                          + $" s{N(e.SpeedScale)} f{e.Facing} hp{N(e.CurHealth)} k{N(e.TweenScale)} {e.TintRaw}");
                foreach (var kv in e.Locals.OrderBy(k => k.Key, System.StringComparer.OrdinalIgnoreCase)) sb.Append($" {kv.Key}={N(kv.Value)}");
                if (e.LocalTexts != null) foreach (var kv in e.LocalTexts.OrderBy(k => k.Key, System.StringComparer.OrdinalIgnoreCase)) sb.Append($" {kv.Key}='{kv.Value}'");
                if (e.LocalLists != null) foreach (var kv in e.LocalLists.OrderBy(k => k.Key, System.StringComparer.OrdinalIgnoreCase)) sb.Append($" {kv.Key}=[{string.Join(",", kv.Value)}]");
            }
            sb.Append($" | particles={_particles.Count} tweens={_tweens.Count} dialog={_dialogOpen}:{DialogText.Text} sounds={string.Join(",", _soundsPlayed)}");
            return sb.ToString();
        }

        // Build a throwaway 1-scene project for a single Custom-script test.
        public static PlayWindow NewScriptSim(string script, out Project project,
            double w = 64, double h = 64, PhysicsMode phys = PhysicsMode.None,
            System.Action<Project, Scene>? extra = null)
        {
            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EE_Full_" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
            System.IO.Directory.CreateDirectory(temp);
            project = ProjectIO.CreateNew("Full", temp);
            var scene = project.GetStartScene();
            scene.Width = 4000; scene.Height = 4000;
            foreach (var l in scene.Layers) l.Objects.Clear(); // start from an empty scene (drop the template's default ground)
            var layer = scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.Base) ?? scene.Layers[0];
            var o = new GameObjectData { Id = 1, Name = "A", Type = "Object", X = 200, Y = 200, Width = w, Height = h, Color = "#AAAAAA", Physics = new PhysicsData { Mode = phys } };
            o.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = script } });
            layer.Objects.Add(o);
            extra?.Invoke(project, scene);
            var pw = new PlayWindow(project, scene.Name);
            pw.TSeed(1);
            pw.TLoad();
            return pw;
        }
    }
}
