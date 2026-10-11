using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EasyEngine
{
    /// <summary>Play mode: runs a scene for real (game loop + physics + behaviors + animations).</summary>
    public partial class PlayWindow : Window
    {
        private const double Gravity = 2000.0;

        private class Entity
        {
            public GameObjectData Data = null!;
            public double X, Y, VX, VY, W, H, Rot;
            public bool Grounded, Dead;
            public Border Visual = null!;
            public ImageBrush? Brush;
            public double Life = -1;
            public double StartX, StartY;
            public int Dir = 1;
            public double PatrolPause; // smooth patrol: seconds remaining stopped at an end
            // AdvancedPatrol (hand-drawn path following)
            public List<(double x, double y)>? PathPts;
            public bool PathParsed;
            public int PathIndex;
            public int PathStep = 1; // +1 forward, -1 backward (ping-pong)
            public double PathTry;   // time spent trying to reach the current waypoint
            public int APMode;       // AdvancedPatrol format: 0=platform, 1=top-down, 2=free/fly
            public double SpawnTimer;
            public int Spawned;
            public bool SpawnKeyWas;
            public double ShootTimer;
            public bool ScreenSpace;
            public List<CustomRule>? Custom;
            public TextBlock? LabelBlock;
            public string LabelText = "";
            public string LabelEffect = "none";
            public double EffectSpeed, EffectT, LabelReveal; // text-effect state
            public double PathTimer, PathNX, PathNY;          // pathfinding waypoint
            public Canvas? MiniCanvas;                         // minimap overlay canvas
            public bool ParticleStarted; public double ParticleTimer; // emitter state
            public double TweenScale = 1;                      // current tweened scale
            public System.Windows.Shapes.Rectangle? BarFill;   // bar fill rectangle
            // breakable tilemap runtime state (null unless this tilemap is breakable)
            public List<TileCell>? Tiles;
            public TileCell?[]? TileGrid;                      // r*cols+c → cell, for O(1) lookup
            public double TileTS, TileX0, TileY0; public int TileCols, TileRows;

            // platformer controller state
            public double Coyote, JumpBuf, DashTimer, DashVX, DashVY;
            public int JumpsLeft, DashLeft;
            public bool PrevJump, PrevDash;
            // one-way / moving platform
            public bool OneWay;
            public double PlatDX, PlatDY;
            public bool FrameDynamic, FrameTopDown, FrameCollide; // physics category, classified once per frame
            // phase-2 behavior state
            public bool SolidDisabled;                 // Door: opened → stops colliding
            public bool SwitchWas, InteractWas, AreaWas; // edge-trigger memory
            public bool InteractFired, AreaFired;        // 'once' latches
            // health / combat
            public double CurHealth, MaxHealth, InvTimer, RespawnX, RespawnY, MeleeTimer;
            // per-entity variables (counters for custom scripts — each copy has its own)
            public Dictionary<string, double> Locals = new(StringComparer.OrdinalIgnoreCase);
            public double SpeedScale = 1; // movement speed multiplier (changed by boost/setspeed)
            public Ellipse? LightVisual;
            public int Facing = 1;

            public SpriteAnimation? CurAnim;
            public int FrameIndex;
            public double FrameTimer;

            public double CX => X + W / 2;
            public double CY => Y + H / 2;
            public bool Has(BehaviorKind k) => Data.Behaviors.Any(b => b.Kind == k);
            public BehaviorData? Beh(BehaviorKind k) => Data.Behaviors.FirstOrDefault(b => b.Kind == k);
        }

        // One tile of a breakable tilemap: its own visual + collider so it can be removed on break.
        private class TileCell
        {
            public int R, C; public double X, Y, W, H; public bool Solid; public int Hp = 1; public bool Broken;
            public string Color = "#888888";
            public System.Windows.Shapes.Rectangle? Visual;
            public Entity? Collider;
        }

        private readonly Project _project;
        private string _sceneName;
        private Scene _scene = null!;

        private readonly List<Entity> _entities = new();
        private readonly List<Entity> _solids = new();
        private readonly HashSet<Key> _keys = new();
        private readonly List<MediaPlayer> _players = new();
        private readonly Dictionary<string, BitmapImage> _imgCache = new();

        private Entity? _cameraTarget;
        private double _camX, _camY;
        private readonly TranslateTransform _camTransform = new();
        private readonly TranslateTransform _lightTransform = new();

        // mouse input (world + screen space)
        private Point _mouseWorld, _mouseScreen;
        private bool _mouseDown, _mousePrevDown, _mouseClicked;
        private bool _rmbDown, _rmbPrevDown, _rmbClicked;

        private readonly Stopwatch _clock = new();
        private double _last;
        private int _frames; private double _fpsAccum;
        private string? _pendingScene;

        // gameplay state (persists across scene switches within a play session)
        private readonly Dictionary<string, double> _vars = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<(string speaker, string text)> _dialog = new();
        private bool _dialogOpen;
        private string _dialogFull = "";   // full current line (for typewriter reveal)
        private double _dialogReveal;       // chars revealed so far
        private bool _dialogTyping;         // currently revealing characters

        private readonly List<Particle> _particles = new();
        private readonly List<Tween> _tweens = new();

        private class Particle { public Border V = null!; public double X, Y, VX, VY, Life, MaxLife, Grav, Size; }
        private class Tween { public Entity E = null!; public string Prop = ""; public double From, To, T, Dur; public string Ease = "linear"; public bool Loop, Yoyo, Dead; }

        // a parsed rule for the Custom behavior / mini-language
        public class CustomRule
        {
            public string When = "always", Do = "";
            // action params (everything after "do") — read by DoCustom
            public Dictionary<string, double> N = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> T = new(StringComparer.OrdinalIgnoreCase);
            // trigger params (everything before "do") — read by the WHEN events.
            // Kept separate so an event param and an action param can share a name
            // (e.g. "while var:g >= 5  →  setvar var:ok") without colliding.
            public Dictionary<string, double> WN = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> WT = new(StringComparer.OrdinalIgnoreCase);
            public double Timer; public bool Started; public bool WasTouching;
            public double Num(string k, double d = 0) => N.TryGetValue(k, out var v) ? v : d;
            public string Str(string k, string d = "") => T.TryGetValue(k, out var v) ? v : d;
            // trigger reads: prefer the trigger bucket, fall back to the action bucket
            // (so single-occurrence params written either side still resolve).
            public double WNum(string k, double d = 0) => WN.TryGetValue(k, out var v) ? v : (N.TryGetValue(k, out var v2) ? v2 : d);
            public string WStr(string k, string d = "") => WT.TryGetValue(k, out var v) ? v : (T.TryGetValue(k, out var v2) ? v2 : d);
        }

        public PlayWindow(Project project, string startSceneName)
        {
            InitializeComponent();
            _project = project;
            _sceneName = startSceneName;

            World.RenderTransform = _camTransform;
            Lights.RenderTransform = _lightTransform;

            Loaded += (s, e) => Start();
            Closed += (s, e) => Stop();
            KeyDown += (s, e) =>
            {
                _keys.Add(e.Key);
                if (e.Key == Key.Escape) Close();
                if (_dialogOpen && (e.Key == Key.Enter || e.Key == Key.Return)) { AdvanceDialog(); e.Handled = true; }
            };
            KeyUp += (s, e) => _keys.Remove(e.Key);

            MouseMove += (s, e) => UpdateMouse(e);
            MouseDown += (s, e) =>
            {
                UpdateMouse(e);
                if (e.ChangedButton == MouseButton.Left) _mouseDown = true;
                else if (e.ChangedButton == MouseButton.Right) _rmbDown = true;
            };
            MouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) _mouseDown = false;
                else if (e.ChangedButton == MouseButton.Right) _rmbDown = false;
            };
        }

        private void UpdateMouse(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            _mouseScreen = p;
            // World is drawn through _camTransform (a translation), so undo it to get world coords.
            _mouseWorld = new Point(p.X - _camX, p.Y - _camY);
        }

        private void Start()
        {
            Focusable = true; Focus();
            SizeChanged += (s, e) => SizeDarkness();
            ApplyTheme();
            foreach (var kv in _project.Variables) _vars[kv.Key] = kv.Value; // global vars
            // continue from a saved game if one exists (metroidvania/RPG progression)
            bool hadSave = LoadGame();
            string first = (hadSave && _pendingScene != null) ? _pendingScene : _sceneName;
            _pendingScene = null;
            LoadScene(first);

            _clock.Start();
            _last = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnFrame;
        }

        // ============================================================
        //  HEADLESS BEHAVIOR TEST (no window shown, no render loop)
        //  Builds entities from the scene and steps the behavior logic
        //  directly to verify the Phase-2 behaviors actually work.
        // ============================================================
        public List<string> RunBehaviorChecks()
        {
            var log = new List<string>();
            void Check(string name, bool ok) => log.Add((ok ? "PASS" : "FAIL") + "  " + name);
            Entity? Find(string n) => _entities.FirstOrDefault(e => e.Data.Name == n);

            foreach (var kv in _project.Variables) _vars[kv.Key] = kv.Value;
            LoadScene(_sceneName); // builds _entities / _solids without a render loop

            var player = Find("Player");
            void MovePlayer(double x, double y) { if (player != null) { player.X = x; player.Y = y; } }

            // ---- GATE (player far away so nothing else fires) ----
            MovePlayer(-5000, 0);
            var gate = Find("Gate1");
            if (gate != null)
            {
                _vars["hasKey"] = 0; UpdateBehaviors(0.016); Check("Gate blocks when hasKey=0", !gate.SolidDisabled);
                _vars["hasKey"] = 1; UpdateBehaviors(0.016); Check("Gate opens when hasKey=1", gate.SolidDisabled);
            }
            else Check("Gate entity exists", false);

            // ---- SWITCH (touch, edge-triggered) ----
            var sw = Find("Switch1");
            if (sw != null)
            {
                _vars["sw"] = 0; sw.SwitchWas = false;
                MovePlayer(sw.X, sw.Y);
                UpdateBehaviors(0.016); bool on = Var("sw") >= 0.5;
                UpdateBehaviors(0.016); bool stillOn = Var("sw") >= 0.5; // must not re-toggle while held
                Check("Switch toggles on touch", on);
                Check("Switch is edge-triggered (no re-toggle while held)", stillOn);
                MovePlayer(-5000, 0);
            }
            else Check("Switch entity exists", false);

            // ---- ABILITY PICKUP (grants dash, disappears) ----
            var pk = Find("Pickup1");
            if (pk != null && player != null)
            {
                MovePlayer(pk.X, pk.Y);
                UpdateBehaviors(0.016);
                Check("AbilityPickup sets its var", Var("hasDash") >= 1);
                Check("AbilityPickup disappears", pk.Dead);
                Check("AbilityPickup grants the dash ability", (player.Beh(BehaviorKind.Platformer)?.Num("dash", 0) ?? 0) >= 1);
                MovePlayer(-5000, 0);
            }
            else Check("Pickup/Player exist", false);

            // ---- AREA (fires setvar once on enter) ----
            var ar = Find("Area1");
            if (ar != null)
            {
                MovePlayer(ar.X, ar.Y);
                UpdateBehaviors(0.016);
                Check("Area fires its effect on enter", Var("entered") >= 1);
                MovePlayer(-5000, 0);
            }
            else Check("Area entity exists", false);

            // ---- PATROL + PLATFORMER coexist (patrol must still move the enemy) ----
            var walker = Find("Walker");
            if (walker != null)
            {
                walker.X = walker.StartX; walker.Dir = 1;
                UpdateBehaviors(0.016);
                Check("Patrol still moves a Platformer enemy (no VX clobber)", Math.Abs(walker.VX) > 1);
            }
            else Check("Walker entity exists", false);

            // ---- VERTICAL PATROL ----
            var vw = Find("VWalker");
            if (vw != null)
            {
                vw.X = vw.StartX; vw.Y = vw.StartY; vw.Dir = 1; vw.PatrolPause = 0;
                UpdateBehaviors(0.016);
                Check("Vertical patrol moves on Y, not X", Math.Abs(vw.VY) > 1 && Math.Abs(vw.VX) < 0.01);
            }
            else Check("VWalker entity exists", false);

            // ---- SMOOTH PATROL (pauses at the ends) ----
            var sm = Find("SmoothWalker");
            if (sm != null)
            {
                sm.Dir = 1; sm.PatrolPause = 0; sm.X = sm.StartX + 155; // just past the far end
                UpdateBehaviors(0.016);
                Check("Smooth patrol pauses + turns around at an end", sm.PatrolPause > 0 && sm.Dir == -1 && Math.Abs(sm.VX) < 0.01);
                UpdateBehaviors(0.016);
                Check("Smooth patrol stays still while paused", Math.Abs(sm.VX) < 0.01);
            }
            else Check("SmoothWalker entity exists", false);

            // ---- ADVANCED PATROL (follows a drawn path) ----
            var pathWalker = Find("PathWalker");
            if (pathWalker != null)
            {
                pathWalker.PathParsed = false; // reparse
                UpdateBehaviors(0.016);
                Check("Advanced patrol parses its path", pathWalker.PathPts != null && pathWalker.PathPts.Count == 2);
                Check("Advanced patrol moves toward the first waypoint", pathWalker.VX > 1);
            }
            else Check("PathWalker entity exists", false);

            var pathFinder = Find("PathFinder");
            if (pathFinder != null)
            {
                pathFinder.PathParsed = false; pathFinder.PathTimer = 0;
                UpdateBehaviors(0.016);
                Check("Advanced patrol with pathfinding moves toward its route", Math.Abs(pathFinder.VX) > 1);
            }
            else Check("PathFinder entity exists", false);

            // ---- INTERACT (press key near NPC fires effect) ----
            var npc = Find("NPC1");
            if (npc != null && player != null)
            {
                MovePlayer(npc.X, npc.Y); npc.InteractWas = false;
                UpdateBehaviors(0.016);
                Check("Interact does NOT fire without the key", Var("talked") < 0.5);
                _keys.Add(Key.E);
                UpdateBehaviors(0.016);
                Check("Interact fires when near + key pressed", Var("talked") >= 1);
                _keys.Remove(Key.E);
                MovePlayer(-5000, 0);
            }
            else Check("NPC/Player exist", false);

            // ---- PUSHABLE (pusher shoves the block) ----
            var block = Find("Block1");
            var pusher = Find("Pusher");
            if (block != null && pusher != null)
            {
                double x0 = block.X;
                pusher.VX = 150;
                UpdateBehaviors(0.05);
                Check("Pushable block moves when pushed", block.X > x0 + 1);
            }
            else Check("Block/Pusher exist", false);

            // ---- GENERATOR (procedural) ----
            int tiles = _entities.Count(e => e.Data.Name == "Tile" && !e.Dead);
            Check("Generator spawned the right count", tiles == 12);
            Check("Generator 'once' persisted a stable seed", Var("seed:genA") >= 1);
            double savedSeed = Var("seed:genA");

            // ---- PERSISTENCE (collected item stays gone across scene reload) ----
            var gem = Find("Gem1");
            if (gem != null && player != null)
            {
                MovePlayer(gem.X, gem.Y);
                UpdateBehaviors(0.016);
                Check("Collectible with id is taken", gem.Dead && Var("got:gem1") >= 1);
                LoadScene(_sceneName); // revisit the scene
                var gem2 = Find("Gem1");
                Check("Collected item stays gone on revisit", gem2 == null || gem2.Dead);
                Check("Generator 'once' reused the same seed on revisit", Var("seed:genA") == savedSeed);
            }
            else Check("Gem/Player exist", false);

            // ---- SAVE / LOAD progress ----
            _vars["persistCheck"] = 42;
            SaveGame();
            _vars["persistCheck"] = 0;
            bool loaded = LoadGame();
            Check("SaveGame + LoadGame restores variables", loaded && Var("persistCheck") == 42);

            // ---- AESTHETICS: per-object styling ----
            var styled = Find("StyledBox");
            if (styled?.Visual is Border sb)
            {
                Check("Object opacity applied", Math.Abs(sb.Opacity - 0.5) < 0.01);
                Check("Object corner radius applied", Math.Abs(sb.CornerRadius.TopLeft - 16) < 0.01);
                Check("Object border applied", sb.BorderThickness.Left >= 3);
            }
            else Check("StyledBox visual exists", false);

            // ---- TEXT EFFECT: Label typewriter reveals gradually ----
            var title = Find("Title");
            if (title?.LabelBlock != null)
            {
                Check("Label reads typewriter effect", title.LabelEffect == "typewriter");
                title.LabelReveal = 0;
                UpdateLabel(title, 0.1); // ~3 chars at speed 30
                int partial = title.LabelBlock.Text.Length;
                Check("Typewriter reveals partially", partial > 0 && partial < "HELLO WORLD".Length);
                UpdateLabel(title, 100); // finish
                Check("Typewriter reaches full text", title.LabelBlock.Text == "HELLO WORLD");
            }
            else Check("Title label exists", false);

            // ---- THEME: dialogue box styling + typewriter ----
            ApplyTheme();
            Check("Theme applied to dialog box", Math.Abs(DialogBox.CornerRadius.TopLeft - 14) < 0.01);
            ShowDialog("Hi there", "");
            UpdateDialogTypewriter(0.05);
            int dlgPartial = DialogText.Text.Length;
            Check("Dialogue typewriter reveals partially", dlgPartial > 0 && dlgPartial < "Hi there".Length);
            UpdateDialogTypewriter(100);
            Check("Dialogue typewriter reaches full text", DialogText.Text == "Hi there");

            // ---- PATHFINDING (routes around a wall) ----
            var chaser = Find("Chaser");
            if (chaser != null && player != null)
            {
                // 1) clear path → first step heads toward the target
                MovePlayer(600, 2500); chaser.X = 200; chaser.Y = 2500;
                var (cnx, _) = NextPathStep(chaser, player, 20);
                Check("PathChase steps toward target when clear", cnx > chaser.CX);

                // 2) wall (PWall) between them → following the path reaches the far side
                MovePlayer(600, 1500); chaser.X = 200; chaser.Y = 1500;
                bool reached = false;
                for (int i = 0; i < 250; i++)
                {
                    var (nx, ny) = NextPathStep(chaser, player, 20);
                    chaser.X = nx - chaser.W / 2; chaser.Y = ny - chaser.H / 2; // follow the path cell by cell
                    if (Math.Abs(chaser.CX - player.CX) < 30 && Math.Abs(chaser.CY - player.CY) < 30) { reached = true; break; }
                }
                Check("PathChase navigates around a wall to the target", reached);
                MovePlayer(-5000, 0);
            }
            else Check("Chaser/Player exist", false);

            // ---- SPRING (launches a faller) ----
            var faller = Find("Faller");
            if (faller != null)
            {
                faller.VY = 0;
                UpdateBehaviors(0.016);
                Check("Spring launches what lands on it", faller.VY < -100);
            }
            else Check("Faller exists", false);

            // ---- BOUNCE (restitution off a floor) ----
            var bounceBall = Find("Ball");
            if (bounceBall != null)
            {
                bounceBall.X = 700; bounceBall.Y = 1500; bounceBall.VX = 0; bounceBall.VY = 400;
                UpdatePhysics(0.1); // falls into BFloor and should bounce
                Check("Bouncy object rebounds off the floor", bounceBall.VY < -50);
            }
            else Check("Ball exists", false);

            // ---- MINIMAP (builds without crashing) ----
            var map = Find("Map");
            Check("Minimap overlay was built", map?.MiniCanvas != null);

            // ---- PARTICLES (burst emits, then fades out) ----
            UpdateBehaviors(0.016); // trigger the 'on:start' burst
            Check("Particles emitted a burst", _particles.Count >= 10);
            UpdateParticles(5.0);   // long dt → short-lived particles die
            Check("Particles fade out and are removed", _particles.Count == 0);

            // ---- TWEEN (eases a property to a target) ----
            var tweeny = Find("Tweeny");
            if (tweeny != null)
            {
                tweeny.X = 0;
                _tweens.Clear();
                _tweens.Add(new Tween { E = tweeny, Prop = "x", From = 0, To = 100, Dur = 1.0, Ease = "linear" });
                UpdateTweens(0.5);
                Check("Tween animates toward target", tweeny.X > 40 && tweeny.X < 60);
                UpdateTweens(1.0);
                Check("Tween reaches the target", Math.Abs(tweeny.X - 100) < 0.5);
            }
            else Check("Tweeny exists", false);

            // ---- UI BUTTON (click runs effect) ----
            var btn = Find("MenuBtn");
            if (btn != null)
            {
                _vars["clicked"] = 0;
                _mouseWorld = new Point(btn.X + btn.W / 2, btn.Y + btn.H / 2);
                _mouseScreen = _mouseWorld;
                _mouseClicked = true;
                UpdateBehaviors(0.016);
                _mouseClicked = false;
                Check("UIButton click runs its effect", Var("clicked") >= 1);
            }
            else Check("MenuBtn exists", false);

            // ---- BAR (fills from a variable) ----
            var hb = Find("HealthBar");
            var hbB = hb?.Beh(BehaviorKind.Bar);
            if (hb != null && hbB != null)
            {
                _vars["hp"] = 2; UpdateBar(hb, hbB);
                Check("Bar fills to half at hp=2/4", hb.BarFill != null && Math.Abs(hb.BarFill.Width - hb.W * 0.5) < 0.5);
                _vars["hp"] = 4; UpdateBar(hb, hbB);
                Check("Bar is full at hp=4/4", hb.BarFill != null && Math.Abs(hb.BarFill.Width - hb.W) < 0.5);
            }
            else Check("HealthBar exists", false);

            // ---- TOP-DOWN COLLISION (runs a physics loop; kept LAST so it can't disturb other checks) ----
            var tdWall = Find("TDWall");
            var tdGuy = Find("TDGuy");
            if (tdGuy != null && tdWall != null)
            {
                tdGuy.X = 4100; tdGuy.Y = 2760; tdGuy.VX = 0; tdGuy.VY = 0;
                _keys.Add(Key.D); // hold "right" toward the wall
                for (int i = 0; i < 80; i++) { UpdateBehaviors(0.016); UpdatePhysics(0.016); }
                _keys.Remove(Key.D);
                Check("Top-down entity is blocked by a solid wall", tdGuy.X + tdGuy.W <= tdWall.X + 1);
            }
            else Check("TDGuy/TDWall exist", false);

            var tdGhost = Find("TDGhost");
            if (tdGhost != null && tdWall != null)
            {
                tdGhost.X = 4100; tdGhost.Y = 2760; tdGhost.VX = 0; tdGhost.VY = 0;
                _keys.Add(Key.D);
                for (int i = 0; i < 80; i++) { UpdateBehaviors(0.016); UpdatePhysics(0.016); }
                _keys.Remove(Key.D);
                Check("Hitbox off (collide=0) passes through the wall", tdGhost.X > tdWall.X + tdWall.W);
            }
            else Check("TDGhost exists", false);

            var pGhost = Find("PGhost");
            if (pGhost != null)
            {
                pGhost.X = 5600; pGhost.Y = 100; pGhost.VX = 0; pGhost.VY = 0; pGhost.Dead = false;
                for (int i = 0; i < 80; i++) { UpdateBehaviors(0.016); UpdatePhysics(0.016); }
                Check("Hitbox-off platformer doesn't fall forever", !pGhost.Dead && Math.Abs(pGhost.Y - 100) < 5);
            }
            else Check("PGhost exists", false);

            var apTd = Find("APTD");
            if (apTd != null)
            {
                apTd.X = 5000; apTd.Y = 2760; apTd.PathParsed = false; apTd.PathTry = 0;
                double minDistToFar = 99999, maxX = apTd.CX;
                for (int i = 0; i < 300; i++)
                {
                    UpdateBehaviors(0.016); UpdatePhysics(0.016);
                    minDistToFar = Math.Min(minDistToFar, Math.Abs(5300 - apTd.CX));
                    maxX = Math.Max(maxX, apTd.CX);
                }
                Check("Top-down AP reaches its far waypoint closely (within 12px)", minDistToFar < 12);
            }

            return log;
        }

        // ============================================================
        //  SCENE LOADING (also used for scene switching)
        // ============================================================
        private void LoadScene(string name)
        {
            // stop sounds from previous scene
            foreach (var mp in _players) { try { mp.Stop(); mp.Close(); } catch { } }
            _players.Clear();

            World.Children.Clear();
            Lights.Children.Clear();
            Hud.Children.Clear();
            _entities.Clear();
            _solids.Clear();
            _particles.Clear();
            _tweens.Clear();
            _cameraTarget = null;
            _camX = _camY = 0;

            var original = _project.FindScene(name) ?? _project.Scenes[0];
            _sceneName = original.Name;
            // clone so gameplay doesn't mutate the editor data
            _scene = System.Text.Json.JsonSerializer.Deserialize<Scene>(System.Text.Json.JsonSerializer.Serialize(original))!;
            foreach (var kv in _scene.Variables) _vars[kv.Key] = kv.Value; // scene-local vars (reset on scene load)
            Title = $"Test — {_project.Name} · {_scene.Name}";

            Darkness.Opacity = Math.Clamp(1.0 - _scene.AmbientLight, 0, 1);
            SizeDarkness();

            foreach (var layer in _scene.Layers)
            {
                bool screen = layer.ScreenSpace || layer.Kind == LayerKind.UI;
                foreach (var obj in layer.Objects)
                    AddEntity(obj, screen);
            }

            // procedural generation: build the map before the first frame
            foreach (var en in _entities.Where(e => e.Has(BehaviorKind.Generator)).ToList())
            {
                var gb = en.Beh(BehaviorKind.Generator);
                if (gb != null) RunGenerator(en, gb);
            }

            _cameraTarget = _entities.FirstOrDefault(en => en.Has(BehaviorKind.FollowCamera));

            foreach (var en in _entities)
            {
                var snd = en.Beh(BehaviorKind.SoundEmitter);
                if (snd != null && snd.Num("onStart", 1) >= 0.5) PlaySound(snd);
            }
        }

        // Spawn a prefab/scene-object at an exact position.
        private Entity? SpawnAt(string name, double x, double y)
        {
            var template = FindTemplate(name)
                ?? _project.Prefabs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Template;
            if (template == null) return null;
            var clone = JsonClone(template);
            clone.X = x; clone.Y = y;
            return AddEntity(clone);
        }

        // Procedurally fills the generator's region with prefabs.
        private void RunGenerator(Entity gen, BehaviorData bh)
        {
            var names = bh.Text("prefab", "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Length == 0) return;
            int count = Math.Max(0, (int)Math.Round(bh.Num("count", 10)));
            string mode = bh.Text("mode", "once").ToLowerInvariant();
            string layout = bh.Text("layout", "scatter").ToLowerInvariant();
            string id = bh.Text("id", "");
            gen.Visual.Opacity = 0; // the generator marker is invisible in play

            // region = this object's rectangle, or the whole scene if it has no real size
            double rx = gen.X, ry = gen.Y, rw = gen.W, rh = gen.H;
            if (rw < 2 || rh < 2) { rx = 0; ry = 0; rw = _scene.Width; rh = _scene.Height; }

            // seed: 'regen' re-rolls each visit; 'once' is stable (persisted per id, survives save/load)
            int seed;
            if (mode == "regen") seed = _rng.Next(1, int.MaxValue);
            else
            {
                string key = "seed:" + (string.IsNullOrEmpty(id) ? gen.Data.Name : id);
                if (Var(key) >= 1) seed = (int)Var(key);
                else { seed = _rng.Next(1, int.MaxValue); _vars[key] = seed; }
            }
            var rng = new Random(seed);

            double minGap = bh.Num("minGap", 0);
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
            int rows = Math.Max(1, (int)Math.Ceiling((double)count / cols));
            var placed = new List<(double x, double y)>();

            for (int i = 0; i < count; i++)
            {
                var name = names[rng.Next(names.Length)];
                var template = FindTemplate(name)
                    ?? _project.Prefabs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Template;
                if (template == null) continue;
                double w = template.Width, h = template.Height, x = 0, y = 0;
                int tries = 0;
                do
                {
                    switch (layout)
                    {
                        case "grid":
                            x = rx + (i % cols) * (rw / cols) + (rw / cols - w) / 2;
                            y = ry + (i / cols) * (rh / rows) + (rh / rows - h) / 2;
                            break;
                        case "row":
                            x = rx + (count <= 1 ? 0 : (rw - w) * i / (count - 1));
                            y = ry + (rh - h) / 2;
                            break;
                        case "column":
                            x = rx + (rw - w) / 2;
                            y = ry + (count <= 1 ? 0 : (rh - h) * i / (count - 1));
                            break;
                        default: // scatter
                            x = rx + rng.NextDouble() * Math.Max(1, rw - w);
                            y = ry + rng.NextDouble() * Math.Max(1, rh - h);
                            break;
                    }
                    tries++;
                } while (minGap > 0 && layout == "scatter" && tries < 25 &&
                         placed.Any(p => Math.Abs(p.x - x) < minGap && Math.Abs(p.y - y) < minGap));
                placed.Add((x, y));
                SpawnAt(name, x, y);
            }
        }

        private void SizeDarkness() { Darkness.Width = Root.ActualWidth; Darkness.Height = Root.ActualHeight; }

        // Apply the project's in-game theme to the dialogue box + default fonts.
        private void ApplyTheme()
        {
            var t = _project.Theme ?? new GameTheme();
            try
            {
                DialogBox.Background = Brush(t.DialogBg);
                DialogBox.BorderBrush = Brush(t.DialogBorder);
                DialogBox.CornerRadius = new CornerRadius(Math.Max(0, t.DialogCorner));
                DialogText.Foreground = Brush(t.DialogText);
                DialogText.FontFamily = ResolveFont(null);
                DialogText.FontSize = Math.Max(8, t.DialogFontSize);
                DialogSpeaker.Foreground = Brush(t.DialogSpeaker);
                DialogSpeaker.FontFamily = ResolveFont(null);
            }
            catch { }
        }

        private FontFamily ResolveFont(string? name)
        {
            var fam = string.IsNullOrWhiteSpace(name) ? (_project.Theme?.FontFamily ?? "Segoe UI") : name;
            try { return new FontFamily(string.IsNullOrWhiteSpace(fam) ? "Segoe UI" : fam); }
            catch { return new FontFamily("Segoe UI"); }
        }

        private Entity AddEntity(GameObjectData obj, bool screenSpace = false)
        {
            var en = new Entity
            {
                Data = obj, X = obj.X, Y = obj.Y, W = obj.Width, H = obj.Height, Rot = obj.Rotation,
                StartX = obj.X, StartY = obj.Y, ScreenSpace = screenSpace
            };
            en.Visual = MakeVisual(obj, en);
            (screenSpace ? Hud : World).Children.Add(en.Visual);

            if (obj.Behaviors.Any(b => b.Kind == BehaviorKind.GoToScene))
            {
                en.Visual.Cursor = Cursors.Hand;
                en.Visual.MouseLeftButtonDown += (s, e) => { RequestScene(en); e.Handled = true; };
            }

            var proj = en.Beh(BehaviorKind.Projectile);
            if (proj != null)
            {
                double ang = proj.Num("angle") * Math.PI / 180.0, spd = proj.Num("speed", 420);
                en.VX = Math.Cos(ang) * spd; en.VY = Math.Sin(ang) * spd;
                en.Life = proj.Num("life", 3);
            }

            var light = en.Beh(BehaviorKind.LightSource);
            if (light != null) { en.LightVisual = MakeLight(light); Lights.Children.Add(en.LightVisual); }

            var mini = en.Beh(BehaviorKind.Minimap);
            if (mini != null) BuildMinimap(en, mini);

            var bar = en.Beh(BehaviorKind.Bar);
            if (bar != null) BuildBar(en, bar);

            var customB = en.Beh(BehaviorKind.Custom);
            if (customB != null) en.Custom = ParseCustom(customB.Text("script", "[]"));

            var solidB = en.Beh(BehaviorKind.Solid);
            en.OneWay = solidB != null && solidB.Num("oneWay") >= 0.5;

            var hp = en.Beh(BehaviorKind.Health);
            if (hp != null) { en.MaxHealth = Math.Max(1, hp.Num("max", 3)); en.CurHealth = en.MaxHealth; en.RespawnX = obj.X; en.RespawnY = obj.Y; }

            foreach (var kv in obj.Variables) en.Locals[kv.Key] = kv.Value; // per-object vars

            if (en.Has(BehaviorKind.Tilemap)) BuildTilemap(obj, screenSpace, en);

            // persistence: a collectible/pickup with an "id" that was already taken stays gone (backtracking)
            foreach (var b in obj.Behaviors)
                if ((b.Kind == BehaviorKind.Collectible || b.Kind == BehaviorKind.AbilityPickup) &&
                    !string.IsNullOrEmpty(b.Text("id", "")) && Var("got:" + b.Text("id", "")) >= 1)
                { en.Dead = true; en.Visual.Visibility = Visibility.Collapsed; }

            _entities.Add(en);
            if (obj.Physics.Mode == PhysicsMode.Static || en.Has(BehaviorKind.Solid) || en.Has(BehaviorKind.Gate) || en.Has(BehaviorKind.Pushable) || en.Has(BehaviorKind.Spring))
                _solids.Add(en);
            return en;
        }

        private Border MakeVisual(GameObjectData obj, Entity en)
        {
            double corner = obj.CornerRadius >= 0
                ? obj.CornerRadius
                : (obj.Type is "Bullet" or "Particle" ? Math.Min(obj.Width, obj.Height) / 2 : 4);
            var border = new Border
            {
                Width = obj.Width, Height = obj.Height,
                CornerRadius = new CornerRadius(corner),
                Opacity = Math.Clamp(obj.Opacity, 0, 1)
            };
            if (!string.IsNullOrWhiteSpace(obj.BorderColor) && obj.BorderWidth > 0)
            {
                border.BorderBrush = Brush(obj.BorderColor!);
                border.BorderThickness = new Thickness(obj.BorderWidth);
            }

            var anim = obj.ResolveDefaultAnimation();
            BitmapImage? first = null;
            if (anim != null && anim.Frames.Count > 0) first = Load(anim.Frames[0]);
            else if (!string.IsNullOrEmpty(obj.SpritePath)) first = Load(obj.SpritePath!);

            if (first != null)
            {
                en.Brush = MakeBrush(first, obj);
                border.Background = en.Brush;
                RenderOptions.SetBitmapScalingMode(border, BitmapScalingMode.NearestNeighbor);
            }
            else border.Background = Brush(obj.Color);

            var label = en.Beh(BehaviorKind.Label);
            if (label != null)
            {
                en.LabelText = label.Text("text", "");
                var tb = new TextBlock
                {
                    Text = en.LabelText,
                    Foreground = Brush(label.Text("color", "#FFFFFF")),
                    FontSize = Math.Max(6, label.Num("fontSize", 20)),
                    FontFamily = ResolveFont(obj.FontFamily),
                    FontWeight = obj.Bold ? FontWeights.Bold : FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                en.LabelBlock = tb;
                en.LabelEffect = label.Text("effect", "none").ToLowerInvariant();
                en.EffectSpeed = label.Num("effectSpeed", 0);
                if (en.EffectSpeed <= 0) en.EffectSpeed = en.LabelEffect == "typewriter" ? 30 : 4;
                border.Child = tb;
                if (string.IsNullOrEmpty(obj.SpritePath) && obj.Animations.Count == 0)
                    border.Background = Brushes.Transparent;
            }
            return border;
        }

        private ImageBrush MakeBrush(BitmapSource bmp, GameObjectData obj)
        {
            if (obj.TileImage)
                return new ImageBrush(bmp)
                {
                    Stretch = Stretch.None, TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight),
                    ViewportUnits = BrushMappingMode.Absolute,
                    AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
                };
            return new ImageBrush(bmp) { Stretch = Stretch.Fill };
        }

        private Ellipse MakeLight(BehaviorData light)
        {
            double r = light.Num("radius", 200);
            double intensity = Math.Clamp(light.Num("intensity", 0.9), 0, 1);
            var color = ParseColor(light.Text("color", "#FFE9A8"), Colors.White);
            var center = Color.FromArgb((byte)(intensity * 220), color.R, color.G, color.B);
            var edge = Color.FromArgb(0, color.R, color.G, color.B);
            var brush = new RadialGradientBrush { GradientStops = { new GradientStop(center, 0), new GradientStop(edge, 1) } };
            return new Ellipse { Width = r * 2, Height = r * 2, Fill = brush, IsHitTestVisible = false };
        }

        private BitmapImage? Load(string relative)
        {
            if (_imgCache.TryGetValue(relative, out var c)) return c;
            var abs = ProjectIO.AbsoluteAsset(_project, relative);
            if (abs == null) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(abs); bmp.EndInit();
                _imgCache[relative] = bmp; return bmp;
            }
            catch { return null; }
        }

        // ============================================================
        //  LOOP
        // ============================================================
        private void OnFrame(object? sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - _last, 0.05);
            _last = now;

            _mouseClicked = _mouseDown && !_mousePrevDown; _mousePrevDown = _mouseDown;
            _rmbClicked = _rmbDown && !_rmbPrevDown; _rmbPrevDown = _rmbDown;

            UpdateBehaviors(dt);
            UpdateBreakables(dt);
            UpdatePhysics(dt);
            UpdateTweens(dt);
            UpdateParticles(dt);
            UpdateCamera(dt);
            Render(dt);
            Cleanup();

            if (_pendingScene != null) { var n = _pendingScene; _pendingScene = null; LoadScene(n); }

            _frames++; _fpsAccum += dt;
            if (_fpsAccum >= 0.5) { FpsText.Text = $"{_frames / _fpsAccum:0} fps · {_entities.Count} objs · {_scene.Name}"; _frames = 0; _fpsAccum = 0; }
        }

        private bool K(params Key[] keys) => keys.Any(_keys.Contains);
        private double Axis(Key n, Key n2, Key p, Key p2) => (K(p, p2) ? 1 : 0) - (K(n, n2) ? 1 : 0);

        private void UpdateBehaviors(double dt)
        {
            foreach (var en in _entities.ToList())
            {
                foreach (var bh in en.Data.Behaviors)
                {
                    switch (bh.Kind)
                    {
                        case BehaviorKind.Platformer:
                            UpdatePlatformer(en, bh, dt);
                            break;
                        case BehaviorKind.Health:
                        {
                            if (en.InvTimer > 0) en.InvTimer -= dt;
                            _vars[bh.Text("var", "health")] = en.CurHealth;
                            if (en.CurHealth <= 0)
                            {
                                if (bh.Num("respawn", 1) >= 0.5)
                                {
                                    en.CurHealth = en.MaxHealth;
                                    en.X = en.RespawnX; en.Y = en.RespawnY; en.VX = 0; en.VY = 0;
                                    en.InvTimer = Math.Max(0.5, bh.Num("invincible", 1));
                                }
                                else { RunDeathRules(en, dt); en.Dead = true; } // enemies destroyed (run 'death' rules first)
                            }
                            break;
                        }
                        case BehaviorKind.Damager:
                        {
                            var t = _entities.FirstOrDefault(x => !x.Dead && x != en && x.Has(BehaviorKind.Health) && x.Data.Name == bh.Text("target", "Player"));
                            if (t != null && Overlap(en, t))
                                DamageEntity(t, bh.Num("damage", 1), bh.Num("knockback", 320), en.CX);
                            break;
                        }
                        case BehaviorKind.Melee:
                        {
                            if (en.MeleeTimer > 0) { en.MeleeTimer -= dt; break; }
                            if (_keys.Contains(ParseKey(bh.Text("key", "J"))))
                            {
                                en.MeleeTimer = Math.Max(0.1, bh.Num("cooldown", 0.35));
                                double range = bh.Num("range", 40);
                                double hx = en.Facing >= 0 ? en.X + en.W : en.X - range;
                                double hy = en.Y, hw = range, hh = en.H;
                                foreach (var t in _entities.ToList())
                                {
                                    if (t == en || t.Dead || !t.Has(BehaviorKind.Health)) continue;
                                    if (t.X < hx + hw && t.X + t.W > hx && t.Y < hy + hh && t.Y + t.H > hy)
                                        DamageEntity(t, bh.Num("damage", 1), bh.Num("knockback", 260), en.CX);
                                }
                                // melee also breaks breakable tilemaps caught in the swing
                                foreach (var map in _entities)
                                    if (map.Tiles != null) { var mb = map.Beh(BehaviorKind.Tilemap); if (mb != null && mb.Num("byMelee", 1) >= 0.5) BreakOverlap(map, mb, hx, hy, hw, hh, (int)Math.Max(1, bh.Num("damage", 1))); }
                                ShowSlash(hx, hy, hw, hh);
                            }
                            break;
                        }
                        case BehaviorKind.Checkpoint:
                        {
                            var t = _entities.FirstOrDefault(x => !x.Dead && x.Data.Name == bh.Text("target", "Player"));
                            if (t != null && Overlap(en, t)) { t.RespawnX = en.X; t.RespawnY = en.Y; }
                            break;
                        }
                        case BehaviorKind.TopDown:
                        {
                            bool ctrl = bh.Num("controls", 1) >= 0.5;
                            double hx = ctrl ? Axis(Key.Left, Key.A, Key.Right, Key.D) : 0;
                            double hy = ctrl ? Axis(Key.Up, Key.W, Key.Down, Key.S) : 0;
                            if (KeyHeld(bh, "rightKey")) hx += 1;
                            if (KeyHeld(bh, "leftKey")) hx -= 1;
                            if (KeyHeld(bh, "downKey")) hy += 1;
                            if (KeyHeld(bh, "upKey")) hy -= 1;
                            double sp = bh.Num("speed", 220) * en.SpeedScale;
                            en.VX = Math.Clamp(hx, -1, 1) * sp; en.VY = Math.Clamp(hy, -1, 1) * sp;
                            break;
                        }
                        case BehaviorKind.Patrol:
                        {
                            double spd = bh.Num("speed", 90), dist = Math.Max(1, bh.Num("distance", 200));
                            bool vertical = bh.Num("axis", 0) >= 0.5;
                            bool smooth = bh.Num("smooth", 0) >= 0.5;
                            double pause = Math.Max(0, bh.Num("pause", 0));
                            int slowSteps = (int)Math.Round(Math.Max(0, bh.Num("slowSteps", 0)));

                            double pos = vertical ? en.Y : en.X;
                            double start = vertical ? en.StartY : en.StartX;
                            double end = start + dist;

                            void SetV(double v) { if (vertical) { en.VY = v; en.VX = 0; } else { en.VX = v; if (!IsDynamic(en)) en.VY = 0; } }

                            // stopped at an end (smooth patrol pause)
                            if (en.PatrolPause > 0) { en.PatrolPause -= dt; SetV(0); break; }

                            bool atEnd = (en.Dir > 0 && pos >= end) || (en.Dir < 0 && pos <= start);

                            if (!smooth)
                            {
                                if (atEnd) en.Dir = -en.Dir;
                                SetV(en.Dir * spd);
                                break;
                            }

                            if (atEnd)
                            {
                                double b = en.Dir > 0 ? end : start;
                                if (vertical) en.Y = b; else en.X = b;
                                en.Dir = -en.Dir;
                                if (pause > 0) { en.PatrolPause = pause; SetV(0); }
                                else SetV(en.Dir * spd * 0.12); // ease away from the end
                                break;
                            }

                            double mult = 1.0;
                            if (slowSteps > 0)
                            {
                                double remaining = en.Dir > 0 ? end - pos : pos - start;
                                double zone = dist * 0.5; // slow over the final half of the path
                                if (zone > 0 && remaining < zone)
                                {
                                    double frac = Math.Max(0, remaining) / zone;              // 1 at zone start → 0 at end
                                    int level = Math.Max(1, (int)Math.Ceiling(frac * slowSteps));
                                    mult = Math.Clamp((double)level / slowSteps, 0.12, 1.0);  // discrete steps, never fully 0 before the end
                                }
                            }
                            SetV(en.Dir * spd * mult);
                            break;
                        }
                        case BehaviorKind.AdvancedPatrol:
                        {
                            if (!en.PathParsed) { en.PathPts = ParsePathPoints(bh.Text("path", "")); en.PathParsed = true; en.PathIndex = 0; en.PathStep = 1; en.PatrolPause = 0; }
                            en.APMode = (int)Math.Round(bh.Num("mode", 0));
                            bool platform = en.APMode == 0;
                            void Stop() { Stop(); }
                            var pts = en.PathPts;
                            if (pts == null || pts.Count == 0) { Stop(); break; }

                            double apSpeed = bh.Num("speed", 130), apJump = bh.Num("jump", 640);
                            bool autoJump = bh.Num("autoJump", 1) >= 0.5;
                            int repeat = (int)Math.Round(bh.Num("repeat", 0)); // 0=back-and-forth, 1=loop, 2=once
                            bool pathfind = bh.Num("pathfind", 0) >= 0.5 && en.APMode == 1; // grid pathfinding fits top-down
                            double pauseEnd = Math.Max(0, bh.Num("pause", 0));
                            double pauseStart = Math.Max(0, bh.Num("pauseStart", 0));

                            if (en.PatrolPause > 0) { en.PatrolPause -= dt; Stop(); break; }

                            en.PathIndex = Math.Clamp(en.PathIndex, 0, pts.Count - 1);
                            var (tx, ty) = pts[en.PathIndex];

                            // reached the current waypoint? tolerance tracks the per-frame step (not the object size),
                            // so it stops ON the point instead of turning around early. (platform ignores Y — gravity handles it)
                            double reachTol = Math.Max(6, apSpeed * dt * 1.2);
                            bool reachX = Math.Abs(tx - en.CX) < reachTol;
                            bool reachY = Math.Abs(ty - en.CY) < reachTol;
                            bool reached = platform ? reachX : (reachX && reachY);

                            en.PathTry += dt;
                            if (reached || en.PathTry > 4.0)
                            {
                                en.PathTry = 0;
                                bool atLast = en.PathIndex >= pts.Count - 1;
                                bool atFirst = en.PathIndex <= 0;
                                if (atLast && pauseEnd > 0) en.PatrolPause = pauseEnd;
                                else if (atFirst && pauseStart > 0) en.PatrolPause = pauseStart;

                                int next = en.PathIndex + en.PathStep;
                                if (next >= pts.Count || next < 0)
                                {
                                    if (repeat == 1) en.PathIndex = en.PathStep > 0 ? 0 : pts.Count - 1;      // loop (circuit)
                                    else if (repeat == 2) { Stop(); break; }      // once: stop at the end
                                    else { en.PathStep = -en.PathStep; en.PathIndex = Math.Clamp(en.PathIndex + en.PathStep, 0, pts.Count - 1); } // back-and-forth (retrace)
                                }
                                else en.PathIndex = next;

                                if (en.PatrolPause > 0) { Stop(); break; }
                                (tx, ty) = pts[en.PathIndex];
                            }

                            // where to steer: straight to the waypoint, or the next grid step (pathfinding, top-down only)
                            double steerX = tx, steerY = ty;
                            if (pathfind)
                            {
                                en.PathTimer -= dt;
                                if (en.PathTimer <= 0) { en.PathTimer = 0.25; var (nx, ny) = NextPathStep(en, tx, ty, Math.Max(16, en.W)); en.PathNX = nx; en.PathNY = ny; }
                                steerX = en.PathNX; steerY = en.PathNY;
                            }

                            if (platform)
                            {
                                double sdx = tx - en.CX;
                                int dir = sdx > 0.5 ? 1 : (sdx < -0.5 ? -1 : 0);
                                bool atX = Math.Abs(sdx) < reachTol;
                                en.VX = atX ? 0 : dir * apSpeed;
                                if (dir != 0) en.Facing = dir;
                                bool wantUp = ty < en.CY - en.H * 0.5;
                                bool obstacle = autoJump && !atX && SolidBeside(en, dir != 0 ? dir : en.Facing);
                                if (en.Grounded && (wantUp || obstacle)) en.VY = -apJump;
                            }
                            else // top-down / free
                            {
                                double ddx = steerX - en.CX, ddy = steerY - en.CY;
                                if (pathfind)
                                {
                                    // move one axis at a time (grid is 4-connected) so the hitbox doesn't catch tile corners
                                    if (Math.Abs(ddx) >= Math.Abs(ddy)) { en.VX = Math.Abs(ddx) > 1 ? Math.Sign(ddx) * apSpeed : 0; en.VY = 0; }
                                    else { en.VY = Math.Abs(ddy) > 1 ? Math.Sign(ddy) * apSpeed : 0; en.VX = 0; }
                                }
                                else
                                {
                                    double len = Math.Sqrt(ddx * ddx + ddy * ddy);
                                    if (len > 1) { en.VX = ddx / len * apSpeed; en.VY = ddy / len * apSpeed; }
                                    else { en.VX = 0; en.VY = 0; }
                                }
                                if (Math.Abs(ddx) > 1) en.Facing = ddx > 0 ? 1 : -1;
                            }
                            break;
                        }
                        case BehaviorKind.Chase:
                        {
                            var t = _entities.FirstOrDefault(x => x.Data.Name == bh.Text("target", "Player") && !x.Dead);
                            if (t != null) { double dx = t.CX - en.CX, dy = t.CY - en.CY, len = Math.Sqrt(dx * dx + dy * dy); if (len > 1) { double spd = bh.Num("speed", 140); en.VX = dx / len * spd; en.VY = dy / len * spd; } }
                            break;
                        }
                        case BehaviorKind.Spin: en.Rot += bh.Num("speed", 90) * dt; break;
                        case BehaviorKind.Projectile: if (en.Life >= 0) { en.Life -= dt; if (en.Life <= 0) en.Dead = true; } break;
                        case BehaviorKind.Spawner:
                        {
                            bool atTop = bh.Text("at", "").Equals("top", StringComparison.OrdinalIgnoreCase);
                            double max = bh.Num("max", 10);
                            var key = bh.Text("key", "");
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                // spawn once each time the key is pressed
                                bool down = _keys.Contains(ParseKey(key));
                                if (down && !en.SpawnKeyWas && (max <= 0 || en.Spawned < max))
                                { SpawnFrom(en, bh.Text("target", "Enemy"), atTop); en.Spawned++; }
                                en.SpawnKeyWas = down;
                            }
                            else
                            {
                                en.SpawnTimer += dt;
                                if (en.SpawnTimer >= bh.Num("interval", 1.5) && (max <= 0 || en.Spawned < max))
                                { en.SpawnTimer = 0; SpawnFrom(en, bh.Text("target", "Enemy"), atTop); en.Spawned++; }
                            }
                            break;
                        }
                        case BehaviorKind.Collectible:
                        {
                            var col = _entities.FirstOrDefault(x => x.Data.Name == bh.Text("collector", "Player") && !x.Dead);
                            if (col != null && Overlap(en, col))
                            {
                                var item = bh.Text("item", "");
                                if (!string.IsNullOrEmpty(item)) _vars["item:" + item] = Var("item:" + item) + bh.Num("amount", 1);
                                MarkCollected(bh);
                                en.Dead = true;
                            }
                            break;
                        }
                        case BehaviorKind.DestroyOffscreen:
                        {
                            double m = bh.Num("margin", 120), sx = en.X + _camTransform.X, sy = en.Y + _camTransform.Y;
                            if (sx + en.W < -m || sx > Root.ActualWidth + m || sy + en.H < -m || sy > Root.ActualHeight + m) en.Dead = true;
                            break;
                        }
                        case BehaviorKind.GoToScene:
                        {
                            var trig = _entities.FirstOrDefault(x => x.Data.Name == bh.Text("trigger", "Player") && !x.Dead);
                            if (trig != null && Overlap(en, trig)) RequestScene(en);
                            break;
                        }
                        case BehaviorKind.Shooter:
                        {
                            if (en.ShootTimer > 0) en.ShootTimer -= dt;
                            var key = ParseKey(bh.Text("key", "T"));
                            if (en.ShootTimer <= 0 && _keys.Contains(key))
                            {
                                int dirs = Math.Max(1, (int)Math.Round(bh.Num("directions", 8)));
                                double spd = bh.Num("speed", 420), life = bh.Num("life", 2);
                                var bulletName = bh.Text("bullet", "Bullet");
                                for (int i = 0; i < dirs; i++)
                                {
                                    double ang = dirs == 1 ? (en.Facing >= 0 ? 0 : 180) : i * 360.0 / dirs;
                                    SpawnBullet(en, ang, spd, life, bulletName);
                                }
                                en.ShootTimer = Math.Max(0.05, bh.Num("cooldown", 0.3));
                            }
                            break;
                        }
                        case BehaviorKind.Pushable:
                        {
                            en.VX = 0;
                            var pusher = _entities.FirstOrDefault(x => !x.Dead && x != en && x.Data.Name == bh.Text("pusher", "Player"));
                            if (pusher != null)
                            {
                                bool vOverlap = pusher.Y < en.Y + en.H - 4 && pusher.Y + pusher.H > en.Y + 4;
                                if (vOverlap)
                                {
                                    double weight = Math.Max(1, bh.Num("weight", 1));
                                    // pusher on the LEFT moving right into the block
                                    if (pusher.VX > 10 && pusher.X + pusher.W <= en.X + 10 && pusher.X + pusher.W >= en.X - 8)
                                    {
                                        double step = pusher.VX / weight * dt;
                                        if (!BlockedHoriz(en, step)) en.X += step;
                                    }
                                    // pusher on the RIGHT moving left into the block
                                    else if (pusher.VX < -10 && pusher.X >= en.X + en.W - 10 && pusher.X <= en.X + en.W + 8)
                                    {
                                        double step = pusher.VX / weight * dt;
                                        if (!BlockedHoriz(en, step)) en.X += step;
                                    }
                                }
                            }
                            break;
                        }
                        case BehaviorKind.Switch:
                        {
                            string mode = bh.Text("mode", "click").ToLowerInvariant();
                            string vr = bh.Text("var", "switch1");
                            bool stayOn = bh.Num("stayOn", 0) >= 0.5;
                            bool trigger = false;
                            if (mode == "click") trigger = _mouseClicked && PointInEntity(en);
                            else
                            {
                                var t = _entities.FirstOrDefault(x => !x.Dead && x.Data.Name == bh.Text("trigger", "Player"));
                                bool near = t != null && Overlap(en, t);
                                bool active = mode == "interact" ? (near && _keys.Contains(ParseKey(bh.Text("key", "E")))) : near;
                                trigger = active && !en.SwitchWas;
                                en.SwitchWas = active;
                            }
                            if (trigger)
                            {
                                double cur = Var(vr);
                                _vars[vr] = stayOn ? 1 : (cur >= 0.5 ? 0 : 1);
                                en.Visual.Opacity = _vars[vr] >= 0.5 ? 1.0 : 0.55;
                            }
                            break;
                        }
                        case BehaviorKind.Gate:
                        {
                            bool open;
                            string item = bh.Text("item", "");
                            if (!string.IsNullOrEmpty(item)) open = Var("item:" + item) >= 1;
                            else open = CompareVar(Var(bh.Text("var", "hasKey")), bh.Text("op", ">="), bh.Num("value", 1));
                            en.SolidDisabled = open;
                            en.Visual.Opacity = open ? 0.2 : 1.0;
                            break;
                        }
                        case BehaviorKind.Interact:
                        {
                            var t = _entities.FirstOrDefault(x => !x.Dead && x.Data.Name == bh.Text("trigger", "Player"));
                            if (t == null) break;
                            double rad = bh.Num("radius", 60), dx = t.CX - en.CX, dy = t.CY - en.CY;
                            bool near = dx * dx + dy * dy <= rad * rad;
                            bool keyDown = _keys.Contains(ParseKey(bh.Text("key", "E")));
                            bool active = near && keyDown;
                            bool fire = active && !en.InteractWas;
                            en.InteractWas = active;
                            if (fire && !(bh.Num("once", 0) >= 0.5 && en.InteractFired))
                            {
                                en.InteractFired = true;
                                DoEffect(bh.Text("action", "say"), bh);
                            }
                            break;
                        }
                        case BehaviorKind.AbilityPickup:
                        {
                            var col = _entities.FirstOrDefault(x => !x.Dead && x.Data.Name == bh.Text("collector", "Player"));
                            if (col != null && Overlap(en, col))
                            {
                                _vars[bh.Text("var", "hasDoubleJump")] = 1;
                                string grant = bh.Text("grant", "none").ToLowerInvariant();
                                var plat = col.Beh(BehaviorKind.Platformer);
                                if (plat != null)
                                {
                                    if (grant.Contains("double")) plat.Nums["doubleJump"] = 1;
                                    else if (grant.Contains("dash")) plat.Nums["dash"] = 1;
                                    else if (grant.Contains("wall")) plat.Nums["wallJump"] = 1;
                                }
                                MarkCollected(bh);
                                en.Dead = true;
                            }
                            break;
                        }
                        case BehaviorKind.Area:
                        {
                            en.Visual.Opacity = 0; // trigger zones are invisible in play
                            var t = _entities.FirstOrDefault(x => !x.Dead && x.Data.Name == bh.Text("target", "Player"));
                            bool inside = t != null && Overlap(en, t);
                            bool fire = inside && !en.AreaWas;
                            en.AreaWas = inside;
                            if (fire && !(bh.Num("once", 1) >= 0.5 && en.AreaFired))
                            {
                                en.AreaFired = true;
                                DoEffect(bh.Text("action", "goscene"), bh);
                            }
                            break;
                        }
                        case BehaviorKind.PathChase:
                        {
                            var t = _entities.FirstOrDefault(x => !x.Dead && x.Data.Name == bh.Text("target", "Player"));
                            if (t == null) break;
                            en.PathTimer -= dt;
                            if (en.PathTimer <= 0)
                            {
                                en.PathTimer = Math.Max(0.05, bh.Num("recalc", 0.3));
                                var (nx, ny) = NextPathStep(en, t, Math.Max(8, bh.Num("cell", 32)));
                                en.PathNX = nx; en.PathNY = ny;
                            }
                            double dx = en.PathNX - en.CX, dy = en.PathNY - en.CY, len = Math.Sqrt(dx * dx + dy * dy);
                            if (len > 1) { double sp = bh.Num("speed", 140) * en.SpeedScale; en.VX = dx / len * sp; en.VY = dy / len * sp; }
                            break;
                        }
                        case BehaviorKind.Spring:
                        {
                            double force = bh.Num("force", 820);
                            string dir = bh.Text("dir", "up").ToLowerInvariant();
                            foreach (var o in _entities)
                            {
                                if (o == en || o.Dead || !IsDynamic(o)) continue;
                                // expanded overlap so resting right on top of the pad still triggers
                                bool near = o.X < en.X + en.W + 4 && o.X + o.W + 4 > en.X && o.Y < en.Y + en.H + 6 && o.Y + o.H + 6 > en.Y;
                                if (!near) continue;
                                switch (dir)
                                {
                                    case "down": o.VY = force; break;
                                    case "left": o.VX = -force; break;
                                    case "right": o.VX = force; break;
                                    default: o.VY = -force; break; // up
                                }
                            }
                            break;
                        }
                        case BehaviorKind.Particles:
                        {
                            string on = bh.Text("on", "start").ToLowerInvariant();
                            if (on == "start")
                            {
                                if (!en.ParticleStarted) { en.ParticleStarted = true; EmitFrom(en, bh, (int)bh.Num("burst", 16)); }
                            }
                            else // always: continuous at 'rate' per second
                            {
                                double rate = bh.Num("rate", 0);
                                if (rate > 0)
                                {
                                    en.ParticleTimer += dt;
                                    double per = 1.0 / rate;
                                    while (en.ParticleTimer >= per) { en.ParticleTimer -= per; EmitFrom(en, bh, 1); }
                                }
                            }
                            break;
                        }
                        case BehaviorKind.UIButton:
                        {
                            bool over = PointInEntity(en);
                            en.Visual.Opacity = over ? 1.0 : 0.88; // hover feedback
                            if (_mouseClicked && over) DoEffect(bh.Text("action", "goscene"), bh);
                            break;
                        }
                        case BehaviorKind.Bar: /* updated in Render */ break;
                        case BehaviorKind.Custom:
                            RunCustom(en, dt);
                            break;
                    }
                }
            }
        }

        // ---- bar (health / progress) --------------------------------------------------
        private void BuildBar(Entity en, BehaviorData bh)
        {
            en.Visual.Background = Brush(bh.Text("bg", "#2A2A33"));
            var fill = new System.Windows.Shapes.Rectangle
            {
                Fill = Brush(bh.Text("color", "#3BC97A")),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = en.W, Height = en.H
            };
            en.Visual.Child = fill;
            en.BarFill = fill;
        }

        private double BarMax(BehaviorData bh)
        {
            var mv = bh.Text("maxVar", "");
            if (!string.IsNullOrWhiteSpace(mv)) { double m = Var(mv); if (m > 0) return m; }
            return Math.Max(0.0001, bh.Num("max", 3));
        }

        private void UpdateBar(Entity en, BehaviorData bh)
        {
            if (en.BarFill == null) return;
            double frac = Math.Clamp(Var(bh.Text("var", "health")) / BarMax(bh), 0, 1);
            en.BarFill.Width = Math.Max(0, en.W * frac);
            en.BarFill.Height = en.H;
        }

        // ---- particles ----------------------------------------------------------------
        private void EmitFrom(Entity en, BehaviorData bh, int count)
            => EmitParticles(en.CX, en.CY, count, bh.Num("speed", 180), bh.Num("spread", 360), bh.Num("angle", 0),
                             bh.Num("life", 0.8), bh.Num("size", 8), bh.Num("gravity", 400), bh.Text("color", "#FFD24D"));

        private void EmitParticles(double cx, double cy, int count, double speed, double spread, double baseAngle, double life, double size, double grav, string colorHex)
        {
            var brush = Brush(colorHex);
            count = Math.Clamp(count, 0, 300);
            for (int i = 0; i < count; i++)
            {
                double ang = (baseAngle + (_rng.NextDouble() - 0.5) * spread) * Math.PI / 180.0;
                double sp = speed * (0.4 + _rng.NextDouble());
                var p = new Particle { X = cx, Y = cy, VX = Math.Cos(ang) * sp, VY = Math.Sin(ang) * sp, Life = life, MaxLife = life, Grav = grav, Size = Math.Max(1, size) };
                p.V = new Border { Width = p.Size, Height = p.Size, Background = brush, CornerRadius = new CornerRadius(p.Size / 2), IsHitTestVisible = false };
                World.Children.Add(p.V);
                _particles.Add(p);
            }
        }

        private void UpdateParticles(double dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Life -= dt;
                if (p.Life <= 0) { World.Children.Remove(p.V); _particles.RemoveAt(i); continue; }
                p.VY += p.Grav * dt;
                p.X += p.VX * dt; p.Y += p.VY * dt;
                Canvas.SetLeft(p.V, p.X - p.Size / 2); Canvas.SetTop(p.V, p.Y - p.Size / 2);
                p.V.Opacity = Math.Clamp(p.Life / p.MaxLife, 0, 1);
            }
        }

        // ---- tweens -------------------------------------------------------------------
        private static double EaseT(string ease, double u) => ease switch
        {
            "in" or "easein" => u * u,
            "out" or "easeout" => 1 - (1 - u) * (1 - u),
            "inout" or "easeinout" => u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2,
            _ => u // linear
        };

        private double TweenGet(Entity e, string p) => p switch
        {
            "x" => e.X, "y" => e.Y, "opacity" => e.Visual.Opacity,
            "rot" or "rotation" => e.Rot, "w" or "width" => e.W, "h" or "height" => e.H,
            "scale" => e.TweenScale, _ => 0
        };

        private void TweenApply(Entity e, string p, double v)
        {
            switch (p)
            {
                case "x": e.X = v; break;
                case "y": e.Y = v; break;
                case "opacity": e.Visual.Opacity = Math.Clamp(v, 0, 1); break;
                case "rot": case "rotation": e.Rot = v; break;
                case "w": case "width": e.W = Math.Max(1, v); e.Visual.Width = e.W; break;
                case "h": case "height": e.H = Math.Max(1, v); e.Visual.Height = e.H; break;
                case "scale": e.TweenScale = v; e.Visual.RenderTransformOrigin = new Point(0.5, 0.5); e.Visual.RenderTransform = new ScaleTransform(v, v); break;
            }
        }

        private void UpdateTweens(double dt)
        {
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var tw = _tweens[i];
                if (tw.Dead || tw.E.Dead) { _tweens.RemoveAt(i); continue; }
                tw.T += dt;
                double u = Math.Clamp(tw.T / tw.Dur, 0, 1);
                TweenApply(tw.E, tw.Prop, tw.From + (tw.To - tw.From) * EaseT(tw.Ease, u));
                if (tw.T >= tw.Dur)
                {
                    if (tw.Yoyo) { (tw.From, tw.To) = (tw.To, tw.From); tw.T = 0; }
                    else if (tw.Loop) tw.T = 0;
                    else tw.Dead = true;
                }
            }
        }

        // Grid BFS: returns the world-space center of the next cell to move toward (routes around solids).
        private (double x, double y) NextPathStep(Entity en, Entity target, double cell) => NextPathStep(en, target.CX, target.CY, cell);

        private (double x, double y) NextPathStep(Entity en, double targetX, double targetY, double cell)
        {
            int cols = Math.Max(1, (int)Math.Ceiling(_scene.Width / cell));
            int rows = Math.Max(1, (int)Math.Ceiling(_scene.Height / cell));
            int Cx(double wx) => Math.Clamp((int)(wx / cell), 0, cols - 1);
            int Cy(double wy) => Math.Clamp((int)(wy / cell), 0, rows - 1);

            // blocked grid (a cell is blocked if a solid overlaps it)
            var blocked = new bool[cols, rows];
            foreach (var s in _solids)
            {
                if (s == en || s.Dead || s.SolidDisabled) continue;
                int c0 = Cx(s.X), c1 = Cx(s.X + s.W), r0 = Cy(s.Y), r1 = Cy(s.Y + s.H);
                for (int c = c0; c <= c1; c++) for (int r = r0; r <= r1; r++) blocked[c, r] = true;
            }
            int sc = Cx(en.CX), sr = Cy(en.CY), gc = Cx(targetX), gr = Cy(targetY);
            blocked[sc, sr] = false; blocked[gc, gr] = false;

            // BFS
            var prev = new (int c, int r)[cols, rows];
            var seen = new bool[cols, rows];
            var q = new Queue<(int c, int r)>();
            q.Enqueue((sc, sr)); seen[sc, sr] = true;
            int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
            bool found = false;
            while (q.Count > 0)
            {
                var (c, r) = q.Dequeue();
                if (c == gc && r == gr) { found = true; break; }
                for (int i = 0; i < 4; i++)
                {
                    int nc = c + dc[i], nr = r + dr[i];
                    if (nc < 0 || nr < 0 || nc >= cols || nr >= rows || seen[nc, nr] || blocked[nc, nr]) continue;
                    seen[nc, nr] = true; prev[nc, nr] = (c, r); q.Enqueue((nc, nr));
                }
            }
            if (!found) return (targetX, targetY); // no path → head straight

            // reconstruct path goal→start; the FIRST step from start is the last node we collect
            var path = new List<(int c, int r)>();
            var cur = (c: gc, r: gr);
            int guard = cols * rows + 1;
            while (!(cur.c == sc && cur.r == sr) && guard-- > 0)
            {
                path.Add(cur);
                cur = prev[cur.c, cur.r];
            }
            if (path.Count == 0) return (targetX, targetY); // already in the goal cell
            var step = path[^1];
            return ((step.c + 0.5) * cell, (step.r + 0.5) * cell);
        }

        // Shared effect dispatcher for Interact / Area behaviors.
        private void DoEffect(string action, BehaviorData bh)
        {
            switch (action.ToLowerInvariant())
            {
                case "say": ShowDialog(bh.Text("text", ""), bh.Text("speaker", "")); break;
                case "setvar": _vars[bh.Text("var", "")] = bh.Num("to", 1); break;
                case "addvar": _vars[bh.Text("var", "")] = Var(bh.Text("var", "")) + bh.Num("amount", 1); break;
                case "save": SaveGame(); break;
                case "load": LoadGame(); break;
                case "goscene":
                {
                    var sc = bh.Text("scene", "");
                    if (!string.IsNullOrEmpty(sc) && _project.FindScene(sc) != null) _pendingScene = sc;
                    break;
                }
            }
        }

        // ---- progress save / load (metroidvania / RPG) -------------------------------
        private string? SavePath()
        {
            var dir = _project.FolderPath ?? (string.IsNullOrEmpty(_project.FilePath) ? null : System.IO.Path.GetDirectoryName(_project.FilePath));
            return dir == null ? null : System.IO.Path.Combine(dir, "save.json");
        }

        // Saves all global/persistent variables + the current scene so the player can continue later.
        public void SaveGame()
        {
            var path = SavePath();
            if (path == null) return;
            try
            {
                var data = new SaveData { Scene = _sceneName, Vars = new Dictionary<string, double>(_vars) };
                System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data));
            }
            catch { }
        }

        // Loads saved variables (and queues the saved scene). Returns true if a save existed.
        public bool LoadGame()
        {
            var path = SavePath();
            if (path == null || !System.IO.File.Exists(path)) return false;
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<SaveData>(System.IO.File.ReadAllText(path));
                if (data == null) return false;
                if (data.Vars != null) foreach (var kv in data.Vars) _vars[kv.Key] = kv.Value;
                if (!string.IsNullOrEmpty(data.Scene) && _project.FindScene(data.Scene) != null) _pendingScene = data.Scene;
                return true;
            }
            catch { return false; }
        }

        private void MarkCollected(BehaviorData bh)
        {
            var id = bh.Text("id", "");
            if (!string.IsNullOrEmpty(id)) _vars["got:" + id] = 1;
        }

        private class SaveData { public string Scene { get; set; } = ""; public Dictionary<string, double>? Vars { get; set; } }

        // Would moving this block by dx collide with another solid (walls, other blocks)?
        private bool BlockedHoriz(Entity block, double dx)
        {
            double nx = block.X + dx;
            foreach (var s in _solids)
            {
                if (s == block || s.Dead || s.SolidDisabled || s.OneWay) continue;
                if (nx < s.X + s.W && nx + block.W > s.X && block.Y < s.Y + s.H && block.Y + block.H > s.Y)
                    return true;
            }
            return false;
        }

        // ============================================================
        //  CUSTOM MINI-LANGUAGE (when → do)
        // ============================================================
        private List<CustomRule> ParseCustom(string json)
        {
            var list = new List<CustomRule>();
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var r = new CustomRule();
                    bool seenDo = false;
                    foreach (var p in el.EnumerateObject())
                    {
                        var name = p.Name.ToLowerInvariant();
                        if (name == "when") { r.When = (p.Value.GetString() ?? "always").ToLowerInvariant(); continue; }
                        if (name == "do") { r.Do = (p.Value.GetString() ?? "").ToLowerInvariant(); seenDo = true; continue; }
                        // guards always live in the action bucket, wherever they appear, so the guard code finds them
                        bool guard = name is "cond" or "if" or "ifop" or "ifval";
                        bool toTrigger = !seenDo && !guard; // params before "do" belong to the WHEN trigger
                        if (p.Value.ValueKind == JsonValueKind.Number)
                        {
                            double v = p.Value.GetDouble();
                            if (toTrigger) r.WN[name] = v; else r.N[name] = v;
                        }
                        else if (p.Value.ValueKind == JsonValueKind.String)
                        {
                            string v = p.Value.GetString() ?? "";
                            if (toTrigger) r.WT[name] = v; else r.T[name] = v;
                        }
                    }
                    list.Add(r);
                }
            }
            catch { }
            return list;
        }

        private void RunCustom(Entity en, double dt)
        {
            if (en.Custom == null) return;
            foreach (var r in en.Custom)
            {
                bool fire = false;
                switch (r.When)
                {
                    case "start": if (!r.Started) { fire = true; r.Started = true; } break;
                    case "always": fire = true; break;
                    case "key":
                    case "keydown": fire = _keys.Contains(ParseKey(r.WStr("key", "Space"))); break;
                    case "keypress":
                        r.Timer -= dt;
                        if (_keys.Contains(ParseKey(r.WStr("key", "Space"))) && r.Timer <= 0) { fire = true; r.Timer = Math.Max(0.05, r.WNum("cooldown", 0.3)); }
                        break;
                    case "timer":
                        r.Timer -= dt;
                        if (r.Timer <= 0) { fire = true; r.Timer = Math.Max(0.05, r.WNum("every", 1)); }
                        break;
                    case "touch":
                    {
                        var o = _entities.FirstOrDefault(x => x != en && !x.Dead && x.Data.Name.Equals(r.WStr("name"), StringComparison.OrdinalIgnoreCase) && Overlap(en, x));
                        bool now = o != null;
                        fire = now && !r.WasTouching; // only on the frame contact STARTS (not every frame)
                        r.WasTouching = now;
                        break;
                    }
                    case "ifvar":
                        fire = CompareVar(Var(r.WStr("var")), r.WStr("op", ">="), r.WNum("value"));
                        break;
                    case "iflocal":
                        fire = CompareVar(en.Locals.TryGetValue(r.WStr("var"), out var lv) ? lv : 0, r.WStr("op", ">="), r.WNum("value"));
                        break;
                    case "hasitem": fire = Var("item:" + r.WStr("item")) >= 1; break;
                    case "expr": fire = EvalExpr(r.WStr("expr"), en) != 0; break;
                    case "click": fire = _mouseClicked && PointInEntity(en); break;
                    case "rightclick": fire = _rmbClicked && PointInEntity(en); break;
                    case "mousedown": fire = _mouseDown && PointInEntity(en); break;
                    case "mouseover": case "hover": fire = PointInEntity(en); break;
                    case "mousepress": fire = _mouseClicked; break; // anywhere on screen
                }
                // optional extra guard on any rule: "if"/"ifop"/"ifval" (checks a global variable)
                if (fire && r.T.ContainsKey("if"))
                    fire = CompareVar(Var(r.Str("if")), r.Str("ifop", ">="), r.Num("ifval"));
                // universal guard: "cond" is a full expression that must be truthy for the rule to fire
                if (fire && r.T.ContainsKey("cond"))
                    fire = EvalExpr(r.Str("cond"), en) != 0;
                if (fire) DoCustom(en, r, dt);
            }
        }

        private void RunDeathRules(Entity en, double dt)
        {
            if (en.Custom == null) return;
            foreach (var r in en.Custom) if (r.When == "death") DoCustom(en, r, dt);
        }

        private void DoCustom(Entity en, CustomRule r, double dt)
        {
            switch (r.Do)
            {
                case "velocity": en.VX = r.Num("vx"); en.VY = r.Num("vy"); break;
                case "move": en.X += r.Num("dx") * dt; en.Y += r.Num("dy") * dt; break;
                case "accelerate":
                case "accel": en.VX += r.Num("ax") * dt; en.VY += r.Num("ay") * dt; break;
                case "jump": if (en.Grounded) en.VY = -r.Num("force", 600); break;
                case "rotate": en.Rot += r.Num("speed", 90) * dt; break;
                case "shoot":
                {
                    int dirs = Math.Max(1, (int)Math.Round(r.Num("directions", 8)));
                    for (int i = 0; i < dirs; i++)
                    {
                        double ang = dirs == 1 ? (en.Facing >= 0 ? 0 : 180) : i * 360.0 / dirs;
                        SpawnBullet(en, ang, r.Num("speed", 420), r.Num("life", 2), r.Str("bullet", "Bullet"));
                    }
                    break;
                }
                case "spawn": SpawnFrom(en, r.T.ContainsKey("object") ? r.Str("object") : r.Str("name", "Enemy"), r.Str("at", "").Equals("top", StringComparison.OrdinalIgnoreCase)); break;
                case "destroy": en.Dead = true; break;
                case "goscene":
                {
                    var sc = r.Str("scene");
                    if (!string.IsNullOrEmpty(sc) && _project.FindScene(sc) != null) _pendingScene = sc;
                    break;
                }
                case "setvar": _vars[r.Str("var")] = RuleValue(en, r, 0, "to", "value"); break;
                case "addvar": _vars[r.Str("var")] = Var(r.Str("var")) + RuleValue(en, r, 1, "amount"); break;
                case "setlocal": en.Locals[r.Str("var")] = RuleValue(en, r, 0, "to", "value"); break;
                case "addlocal": en.Locals[r.Str("var")] = (en.Locals.TryGetValue(r.Str("var"), out var cl) ? cl : 0) + RuleValue(en, r, 1, "amount"); break;
                case "set": // set a property of self (or a named object) to an expression/number
                {
                    var who = r.Str("target", "self");
                    var tgt = who.Equals("self", StringComparison.OrdinalIgnoreCase) ? en
                        : _entities.FirstOrDefault(x => !x.Dead && x.Data.Name.Equals(who, StringComparison.OrdinalIgnoreCase));
                    if (tgt != null) SetProp(tgt, r.Str("prop").ToLowerInvariant(), RuleValue(en, r, 0, "to", "value"));
                    break;
                }
                case "additem": _vars["item:" + r.Str("item")] = Var("item:" + r.Str("item")) + r.Num("amount", 1); break;
                case "removeitem": _vars["item:" + r.Str("item")] = Math.Max(0, Var("item:" + r.Str("item")) - r.Num("amount", 1)); break;
                case "boost": en.SpeedScale *= r.Num("factor", 2); break;
                case "setspeed": en.SpeedScale = r.Num("factor", 1); break;
                case "say": ShowDialog(r.Str("text"), r.Str("speaker")); break;
                case "save": SaveGame(); break;
                case "load": LoadGame(); break;
                case "emit":
                    EmitParticles(en.CX, en.CY, (int)r.Num("count", 16), r.Num("speed", 180), r.Num("spread", 360),
                        r.Num("angle", 0), r.Num("life", 0.8), r.Num("size", 8), r.Num("gravity", 400), r.Str("color", "#FFD24D"));
                    break;
                case "tween":
                {
                    string prop = r.Str("prop", "x").ToLowerInvariant();
                    var tw = new Tween { E = en, Prop = prop, To = RuleValue(en, r, 0, "to", "value"), Dur = Math.Max(0.01, r.Num("duration", 0.5)), Ease = r.Str("ease", "linear").ToLowerInvariant(), Loop = r.Num("loop", 0) >= 0.5, Yoyo = r.Num("yoyo", 0) >= 0.5 };
                    tw.From = TweenGet(en, prop);
                    _tweens.Add(tw);
                    break;
                }
                case "setvolume": _vars["vol." + r.Str("channel", "master")] = r.Num("to", 1); break;
                case "breaktile":
                {
                    string where = r.Str("where", "touching").ToLowerInvariant();
                    foreach (var map in _entities)
                    {
                        if (map.Tiles == null) continue;
                        var mb = map.Beh(BehaviorKind.Tilemap); if (mb == null) continue;
                        double ts = map.TileTS;
                        switch (where)
                        {
                            case "atmouse": { var cell = CellAt(map, _mouseWorld.X, _mouseWorld.Y); if (cell != null) DamageCell(map, mb, cell, 9999); break; }
                            case "below": BreakOverlap(map, mb, en.X + 2, en.Y + en.H, en.W - 4, ts, 9999); break;
                            case "above": BreakOverlap(map, mb, en.X + 2, en.Y - ts, en.W - 4, ts, 9999); break;
                            case "left": BreakOverlap(map, mb, en.X - ts, en.Y + 2, ts, en.H - 4, 9999); break;
                            case "right": BreakOverlap(map, mb, en.X + en.W, en.Y + 2, ts, en.H - 4, 9999); break;
                            case "front": { double fx = en.Facing >= 0 ? en.X + en.W : en.X - ts; BreakOverlap(map, mb, fx, en.Y + 2, ts, en.H - 4, 9999); break; }
                            default: BreakOverlap(map, mb, en.X, en.Y, en.W, en.H, 9999); break; // touching
                        }
                    }
                    break;
                }
            }
        }

        private double Var(string name) => _vars.TryGetValue(name, out var v) ? v : 0;
        private static bool CompareVar(double a, string op, double b) => op switch
        {
            ">" => a > b, "<" => a < b, ">=" => a >= b, "<=" => a <= b,
            "==" => Math.Abs(a - b) < 0.0001, "!=" => Math.Abs(a - b) >= 0.0001, _ => a >= b
        };

        // ---- expression support (general scripting) ----------------------------------

        // A rule value: use the "expr" text as an expression if present; else the first numeric key found; else fallback.
        private double RuleValue(Entity en, CustomRule r, double fallback, params string[] numKeys)
        {
            if (r.T.ContainsKey("expr")) return EvalExpr(r.Str("expr"), en);
            foreach (var k in numKeys) if (r.N.ContainsKey(k)) return r.Num(k);
            return fallback;
        }

        private double EvalExpr(string expr, Entity? self)
            => Expr.Eval(expr, name => ResolveName(name, self), CallFunc);

        // Resolve an identifier to a number: mouse/time constants, obj.prop, local.x, or a variable/item count.
        private double ResolveName(string name, Entity? self)
        {
            switch (name.ToLowerInvariant())
            {
                case "mousex": return _mouseWorld.X;
                case "mousey": return _mouseWorld.Y;
                case "mousescreenx": return _mouseScreen.X;
                case "mousescreeny": return _mouseScreen.Y;
                case "mousedown": return _mouseDown ? 1 : 0;
                case "rightdown": return _rmbDown ? 1 : 0;
                case "time": return _clock.Elapsed.TotalSeconds;
                case "true": return 1;
                case "false": return 0;
                case "pi": return Math.PI;
                case "scenew": case "scenewidth": return _scene?.Width ?? 0;
                case "sceneh": case "sceneheight": return _scene?.Height ?? 0;
            }

            int dot = name.IndexOf('.');
            if (dot > 0)
            {
                string who = name.Substring(0, dot);
                string prop = name.Substring(dot + 1);
                if (who.Equals("local", StringComparison.OrdinalIgnoreCase))
                    return self != null && self.Locals.TryGetValue(prop, out var lv) ? lv : 0;

                Entity? e = who.Equals("self", StringComparison.OrdinalIgnoreCase) ? self
                    : _entities.FirstOrDefault(x => !x.Dead && x.Data.Name.Equals(who, StringComparison.OrdinalIgnoreCase));
                if (e == null) return 0;
                switch (prop.ToLowerInvariant())
                {
                    case "x": return e.X; case "y": return e.Y;
                    case "vx": return e.VX; case "vy": return e.VY;
                    case "w": case "width": return e.W; case "h": case "height": return e.H;
                    case "cx": return e.CX; case "cy": return e.CY;
                    case "rot": return e.Rot; case "facing": return e.Facing;
                    case "health": case "hp": return e.CurHealth;
                    case "grounded": return e.Grounded ? 1 : 0;
                    case "dead": return e.Dead ? 1 : 0;
                    case "exists": return 1;
                    default: return (self != null && e == self && self.Locals.TryGetValue(prop, out var sv)) ? sv : 0;
                }
            }
            return Var(name); // globals, scene vars, "item:xxx" counts
        }

        private double CallFunc(string name, double[] a)
        {
            double A(int i) => i < a.Length ? a[i] : 0;
            switch (name.ToLowerInvariant())
            {
                case "abs": return Math.Abs(A(0));
                case "min": return a.Length == 0 ? 0 : a.Min();
                case "max": return a.Length == 0 ? 0 : a.Max();
                case "floor": return Math.Floor(A(0));
                case "ceil": return Math.Ceiling(A(0));
                case "round": return Math.Round(A(0));
                case "sqrt": return A(0) <= 0 ? 0 : Math.Sqrt(A(0));
                case "sign": return Math.Sign(A(0));
                case "sin": return Math.Sin(A(0));
                case "cos": return Math.Cos(A(0));
                case "atan2": return Math.Atan2(A(0), A(1));
                case "clamp": return Math.Clamp(A(0), A(1), A(2));
                case "rand": return a.Length >= 2 ? A(0) + _rng.NextDouble() * (A(1) - A(0)) : _rng.NextDouble();
                case "randi": return a.Length >= 2 ? _rng.Next((int)A(0), (int)A(1) + 1) : _rng.Next();
                case "dist": return a.Length >= 4 ? Math.Sqrt((A(2) - A(0)) * (A(2) - A(0)) + (A(3) - A(1)) * (A(3) - A(1))) : 0;
                default: return 0;
            }
        }

        // Is the mouse currently inside this entity (screen-space objects use screen coords)?
        private bool PointInEntity(Entity e)
        {
            var p = e.ScreenSpace ? _mouseScreen : _mouseWorld;
            return p.X >= e.X && p.X <= e.X + e.W && p.Y >= e.Y && p.Y <= e.Y + e.H;
        }

        // Set a named property on an entity (used by the "set" action).
        private static void SetProp(Entity e, string prop, double v)
        {
            switch (prop)
            {
                case "x": e.X = v; break;
                case "y": e.Y = v; break;
                case "vx": e.VX = v; break;
                case "vy": e.VY = v; break;
                case "rot": e.Rot = v; break;
                case "facing": e.Facing = v >= 0 ? 1 : -1; break;
                case "health": case "hp": e.CurHealth = v; break;
                case "w": case "width": e.W = Math.Max(1, v); break;
                case "h": case "height": e.H = Math.Max(1, v); break;
                case "speed": case "speedscale": e.SpeedScale = v; break;
            }
        }

        private string Substitute(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, @"\{([^}]+)\}", m =>
            {
                var key = m.Groups[1].Value.Trim();
                var val = Var(key);
                return (val == Math.Floor(val)) ? ((long)val).ToString() : val.ToString("0.##");
            });
        }

        // ---- dialogue ----
        private void ShowDialog(string text, string speaker)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var line in text.Split('|'))
                _dialog.Enqueue((speaker, line.Trim()));
            if (!_dialogOpen) AdvanceDialog();
        }

        private void AdvanceDialog()
        {
            // if a line is still typing out, first reveal it fully instead of skipping ahead
            if (_dialogTyping) { _dialogReveal = _dialogFull.Length; DialogText.Text = _dialogFull; _dialogTyping = false; return; }

            if (_dialog.Count == 0) { CloseDialog(); return; }
            var (speaker, text) = _dialog.Dequeue();
            DialogSpeaker.Text = Substitute(speaker);
            DialogSpeaker.Visibility = string.IsNullOrWhiteSpace(speaker) ? Visibility.Collapsed : Visibility.Visible;
            _dialogFull = Substitute(text);
            DialogBox.Visibility = Visibility.Visible;
            _dialogOpen = true;

            bool typewriter = (_project.Theme?.DialogEffect ?? "typewriter").ToLowerInvariant() == "typewriter";
            if (typewriter) { _dialogReveal = 0; _dialogTyping = true; DialogText.Text = ""; }
            else { DialogText.Text = _dialogFull; _dialogTyping = false; }
        }

        private void UpdateDialogTypewriter(double dt)
        {
            if (!_dialogTyping) return;
            _dialogReveal += Math.Max(1, _project.Theme?.DialogSpeed ?? 45) * dt;
            int n = Math.Min(_dialogFull.Length, (int)_dialogReveal);
            DialogText.Text = _dialogFull.Substring(0, n);
            if (n >= _dialogFull.Length) _dialogTyping = false;
        }

        private void CloseDialog()
        {
            DialogBox.Visibility = Visibility.Collapsed;
            _dialogOpen = false;
            _dialogTyping = false;
        }

        private void RequestScene(Entity en)
        {
            var target = en.Beh(BehaviorKind.GoToScene)?.Text("scene", "");
            if (!string.IsNullOrEmpty(target) && _project.FindScene(target) != null) _pendingScene = target;
        }

        // Platformer/AdvancedPatrol(platform) get gravity + collision. With the hitbox off (collide=0) a character
        // becomes a free mover (no gravity, no collision, no fall-off-world) so it doesn't fall forever.
        private bool IsDynamic(Entity en) => (en.Data.Physics.Mode == PhysicsMode.Dynamic || en.Has(BehaviorKind.Platformer) || (en.Has(BehaviorKind.AdvancedPatrol) && en.APMode == 0)) && !en.Has(BehaviorKind.TopDown) && CollisionEnabled(en);

        private static List<(double x, double y)> ParsePathPoints(string json) => PathPoints.Parse(json);

        // Whether this character's hitbox is active (collides with solids/tilemaps). Default on; a "collide" num of 0 turns it off.
        private static bool CollisionEnabled(Entity en)
        {
            var bh = en.Beh(BehaviorKind.Platformer) ?? en.Beh(BehaviorKind.TopDown) ?? en.Beh(BehaviorKind.AdvancedPatrol);
            return bh == null || bh.Num("collide", 1) >= 0.5;
        }

        private void UpdatePhysics(double dt)
        {
            // classify each entity once per frame (avoids re-running IsDynamic/CollisionEnabled several times each)
            foreach (var en in _entities)
            {
                if (en.Dead) continue;
                bool collide = CollisionEnabled(en);
                bool topDownBehav = en.Has(BehaviorKind.TopDown);
                bool dyn = (en.Data.Physics.Mode == PhysicsMode.Dynamic || en.Has(BehaviorKind.Platformer) || (en.Has(BehaviorKind.AdvancedPatrol) && en.APMode == 0)) && !topDownBehav && collide;
                en.FrameCollide = collide;
                en.FrameDynamic = dyn;
                en.FrameTopDown = !dyn && collide && (topDownBehav || (en.Has(BehaviorKind.AdvancedPatrol) && en.APMode == 1));
            }

            // 1) move non-dynamic movers (platforms, patrol, projectiles, free-flying) without collision
            foreach (var en in _entities)
            {
                if (en.Dead || en.FrameDynamic || en.FrameTopDown) continue;
                double ox = en.X, oy = en.Y;
                en.X += en.VX * dt; en.Y += en.VY * dt;
                en.PlatDX = en.X - ox; en.PlatDY = en.Y - oy;
            }

            // 1b) top-down movers: move with wall/tilemap collision, no gravity
            foreach (var en in _entities)
            {
                if (en.Dead || !en.FrameTopDown) continue;
                en.X += en.VX * dt; ResolveX(en);
                en.Grounded = false;
                en.Y += en.VY * dt; ResolveY(en, dt);
            }

            // 2) dynamic entities (with gravity, collision, moving-platform carry)
            foreach (var en in _entities)
            {
                if (en.Dead || !en.FrameDynamic) continue;

                bool collide = en.FrameCollide;

                var ground = collide ? SolidUnder(en) : null;
                if (ground != null) { en.X += ground.PlatDX; en.Y += ground.PlatDY; }

                if (en.DashTimer <= 0) en.VY += Gravity * en.Data.Physics.GravityScale * dt;

                en.X += en.VX * dt;
                if (collide) ResolveX(en);

                en.Grounded = false;
                en.Y += en.VY * dt;
                if (collide) ResolveY(en, dt);

                // fell off the world → lose a hit point and respawn (prevents falling forever)
                if (en.Y > _scene.Height + 320)
                {
                    if (en.Beh(BehaviorKind.Health) != null)
                    {
                        en.CurHealth -= 1; en.X = en.RespawnX; en.Y = en.RespawnY; en.VX = 0; en.VY = 0; en.InvTimer = 1;
                    }
                    else en.Dead = true;
                }
            }
        }

        private void ResolveX(Entity en)
        {
            foreach (var s in _solids)
            {
                if (s == en || s.Dead || s.SolidDisabled || s.OneWay || !Overlap(en, s)) continue;
                if (en.VX > 0) en.X = s.X - en.W; else if (en.VX < 0) en.X = s.X + s.W;
                double b = en.Data.Physics.Bounciness;
                en.VX = (b > 0.01 && Math.Abs(en.VX) > 120) ? -en.VX * b : 0;
            }
        }

        private void ResolveY(Entity en, double dt)
        {
            foreach (var s in _solids)
            {
                if (s == en || s.Dead || s.SolidDisabled || !Overlap(en, s)) continue;
                if (s.OneWay)
                {
                    // land only when falling and the feet were above the platform last frame
                    if (en.VY > 0)
                    {
                        double prevBottom = (en.Y - en.VY * dt) + en.H;
                        if (prevBottom <= s.Y + 6) { en.Y = s.Y - en.H; en.Grounded = true; en.VY = 0; }
                    }
                    continue;
                }
                double b = en.Data.Physics.Bounciness;
                if (en.VY > 0)
                {
                    en.Y = s.Y - en.H;
                    if (b > 0.01 && en.VY > 120) en.VY = -en.VY * b; // bounce
                    else { en.Grounded = true; en.VY = 0; }
                }
                else if (en.VY < 0) { en.Y = s.Y + s.H; en.VY = (b > 0.01 && -en.VY > 120) ? -en.VY * b : 0; }
            }
        }

        private Entity? SolidUnder(Entity en)
        {
            double x = en.X + 2, y = en.Y + en.H, w = en.W - 4, h = 4;
            foreach (var s in _solids)
            {
                if (s == en || s.Dead || s.SolidDisabled) continue;
                if (x < s.X + s.W && x + w > s.X && y < s.Y + s.H && y + h > s.Y) return s;
            }
            return null;
        }

        private bool SolidBeside(Entity en, int dir)
        {
            double x = en.X + dir * 3, y = en.Y + 3, w = en.W, h = en.H - 6;
            foreach (var s in _solids)
            {
                if (s == en || s.Dead || s.SolidDisabled || s.OneWay) continue;
                if (x < s.X + s.W && x + w > s.X && y < s.Y + s.H && y + h > s.Y) return true;
            }
            return false;
        }

        // Tight platformer controller: variable jump, coyote time, jump buffer, double jump, wall jump, dash.
        private void UpdatePlatformer(Entity en, BehaviorData bh, double dt)
        {
            double speed = bh.Num("speed", 260), jump = bh.Num("jump", 640);
            bool doubleJump = bh.Num("doubleJump") >= 0.5;
            bool wallJump = bh.Num("wallJump") >= 0.5;
            bool dashOn = bh.Num("dash") >= 0.5;
            double dashSpeed = bh.Num("dashSpeed", 560);
            var dashKey = ParseKey(bh.Text("dashKey", "LeftShift"));

            bool ctrl = bh.Num("controls", 1) >= 0.5;
            double h = ctrl ? Axis(Key.Left, Key.A, Key.Right, Key.D) : 0;
            if (KeyHeld(bh, "rightKey")) h += 1;
            if (KeyHeld(bh, "leftKey")) h -= 1;
            h = Math.Clamp(h, -1, 1);
            speed *= en.SpeedScale;
            bool jumpDown = (ctrl && K(Key.Space, Key.Up, Key.W)) || KeyHeld(bh, "jumpKey");

            if (en.Grounded) { en.Coyote = 0.1; en.JumpsLeft = doubleJump ? 1 : 0; en.DashLeft = 1; }
            else en.Coyote -= dt;

            bool jumpPressed = jumpDown && !en.PrevJump;
            if (jumpPressed) en.JumpBuf = 0.12; else en.JumpBuf -= dt;

            // during a dash, lock velocity and skip normal control/gravity
            if (en.DashTimer > 0)
            {
                en.DashTimer -= dt;
                en.VX = en.DashVX; en.VY = en.DashVY;
                en.PrevJump = jumpDown;
                en.PrevDash = dashOn && _keys.Contains(dashKey);
                return;
            }

            // If another behavior drives horizontal movement (patrol/chase) and there's no
            // player input, don't clobber its velocity — lets Patrol+Platformer coexist.
            bool otherDrivesX = en.Has(BehaviorKind.Patrol) || en.Has(BehaviorKind.Chase) || en.Has(BehaviorKind.PathChase);
            if (h != 0 || !otherDrivesX) en.VX = h * speed;

            int wall = 0;
            if (wallJump && !en.Grounded)
            {
                if (SolidBeside(en, +1)) wall = 1;
                else if (SolidBeside(en, -1)) wall = -1;
                if (wall != 0 && en.VY > 0) en.VY = Math.Min(en.VY, 120); // wall slide
            }

            if (en.JumpBuf > 0)
            {
                if (en.Coyote > 0 || en.Grounded) { en.VY = -jump; en.JumpBuf = 0; en.Coyote = 0; }
                else if (wall != 0) { en.VY = -jump; en.VX = -wall * speed * 1.1; en.JumpBuf = 0; }
                else if (doubleJump && en.JumpsLeft > 0) { en.VY = -jump; en.JumpsLeft--; en.JumpBuf = 0; }
            }

            // variable jump height: releasing the button cuts the rise
            if (en.PrevJump && !jumpDown && en.VY < 0) en.VY *= 0.45;

            bool dashHeld = dashOn && _keys.Contains(dashKey);
            if (dashHeld && !en.PrevDash && en.DashLeft > 0)
            {
                double dx = h, dy = (K(Key.Up, Key.W) ? -1 : 0) + (K(Key.Down, Key.S) ? 1 : 0);
                if (dx == 0 && dy == 0) dx = en.Facing;
                double len = Math.Sqrt(dx * dx + dy * dy); dx /= len; dy /= len;
                en.DashVX = dx * dashSpeed; en.DashVY = dy * dashSpeed;
                en.DashTimer = 0.15; en.DashLeft--;
            }

            en.PrevJump = jumpDown;
            en.PrevDash = dashHeld;
        }

        private static bool Overlap(Entity a, Entity b)
            => a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;

        private void BuildTilemap(GameObjectData obj, bool screenSpace, Entity en)
        {
            en.Visual.Background = Brushes.Transparent;
            var bh = obj.Behaviors.First(b => b.Kind == BehaviorKind.Tilemap);

            double ts = Math.Max(4, bh.Num("tileSize", 32));
            var pal = new List<(string color, bool solid)>();
            try
            {
                var pj = bh.Text("palette", "");
                if (!string.IsNullOrWhiteSpace(pj))
                    using (var doc = JsonDocument.Parse(pj))
                        foreach (var e in doc.RootElement.EnumerateArray())
                        {
                            string color = e.TryGetProperty("color", out var c) ? (c.GetString() ?? "#888888") : "#888888";
                            bool solid = e.TryGetProperty("solid", out var s) && (s.ValueKind == JsonValueKind.True || (s.ValueKind == JsonValueKind.Number && s.GetDouble() >= 0.5));
                            pal.Add((color, solid));
                        }
            }
            catch { }

            int cols = 0, rows = 0; int[] cells = Array.Empty<int>();
            try
            {
                var dj = bh.Text("data", "");
                if (!string.IsNullOrWhiteSpace(dj))
                    using (var doc = JsonDocument.Parse(dj))
                    {
                        var r = doc.RootElement;
                        cols = r.GetProperty("cols").GetInt32();
                        rows = r.GetProperty("rows").GetInt32();
                        cells = new int[cols * rows];
                        int i = 0;
                        foreach (var v in r.GetProperty("cells").EnumerateArray()) { if (i < cells.Length) cells[i++] = v.GetInt32(); }
                    }
            }
            catch { }
            if (cols == 0 || rows == 0) return;

            if (bh.Num("breakable", 0) >= 0.5)
            {
                en.Tiles = new List<TileCell>();
                en.TileGrid = new TileCell?[cols * rows];
                en.TileTS = ts; en.TileX0 = obj.X; en.TileY0 = obj.Y; en.TileCols = cols; en.TileRows = rows;
            }

            var host = screenSpace ? Hud : World;
            bool Solid(int r, int c) { int v = cells[r * cols + c]; return v > 0 && v <= pal.Count && pal[v - 1].solid; }

            // draw cells
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int v = cells[r * cols + c];
                    if (v <= 0 || v > pal.Count) continue;
                    var rect = new System.Windows.Shapes.Rectangle { Width = ts + 0.5, Height = ts + 0.5, Fill = Brush(pal[v - 1].color), IsHitTestVisible = false };
                    Canvas.SetLeft(rect, obj.X + c * ts); Canvas.SetTop(rect, obj.Y + r * ts);
                    host.Children.Add(rect);
                    if (en.Tiles != null) // breakable: keep each painted cell so we can remove it later
                    {
                        var cell = new TileCell { R = r, C = c, X = obj.X + c * ts, Y = obj.Y + r * ts, W = ts, H = ts, Solid = Solid(r, c), Hp = Math.Max(1, (int)bh.Num("breakHits", 1)), Color = pal[v - 1].color, Visual = rect };
                        en.Tiles.Add(cell);
                        en.TileGrid![r * cols + c] = cell;
                    }
                }

            if (en.Tiles != null)
            {
                // breakable: one collider per solid tile (so a single tile can be removed)
                foreach (var cell in en.Tiles)
                    if (cell.Solid)
                    {
                        cell.Collider = new Entity { Data = obj, X = cell.X, Y = cell.Y, W = cell.W, H = cell.H };
                        _solids.Add(cell.Collider);
                    }
                return;
            }

            // non-breakable: merge horizontal runs of solid cells into one box each (cheaper)
            for (int r = 0; r < rows; r++)
            {
                int c = 0;
                while (c < cols)
                {
                    if (!Solid(r, c)) { c++; continue; }
                    int start = c;
                    while (c < cols && Solid(r, c)) c++;
                    _solids.Add(new Entity { Data = obj, X = obj.X + start * ts, Y = obj.Y + r * ts, W = (c - start) * ts, H = ts });
                }
            }
        }

        // ---- breakable tilemaps ----
        // Damage one tile; when its hit points reach 0, remove its visual + collider and puff particles.
        private void DamageCell(Entity map, BehaviorData bh, TileCell cell, int amount)
        {
            if (cell.Broken) return;
            cell.Hp -= Math.Max(1, amount);
            if (cell.Hp > 0) return;
            cell.Broken = true;
            var host = map.ScreenSpace ? Hud : World;
            if (cell.Visual != null) host.Children.Remove(cell.Visual);
            if (cell.Collider != null) { cell.Collider.Dead = true; _solids.Remove(cell.Collider); }
            if (map.TileGrid != null) { int idx = cell.R * map.TileCols + cell.C; if (idx >= 0 && idx < map.TileGrid.Length) map.TileGrid[idx] = null; }
            if (bh.Num("breakParticles", 1) >= 0.5)
                EmitParticles(cell.X + cell.W / 2, cell.Y + cell.H / 2, 10, 160, 360, 0, 0.5, Math.Max(3, cell.W / 6), 500, cell.Color);
        }

        // The live cell at a world point (or null).
        private TileCell? CellAt(Entity map, double wx, double wy)
        {
            if (map.TileGrid == null) return null;
            int c = (int)Math.Floor((wx - map.TileX0) / map.TileTS);
            int r = (int)Math.Floor((wy - map.TileY0) / map.TileTS);
            if (c < 0 || r < 0 || c >= map.TileCols || r >= map.TileRows) return null;
            var cell = map.TileGrid[r * map.TileCols + c];
            return (cell != null && !cell.Broken) ? cell : null;
        }

        // Break the first live cell overlapping an AABB; true if one was broken.
        private bool BreakOverlap(Entity map, BehaviorData bh, double ax, double ay, double aw, double ah, int amount)
        {
            if (map.TileGrid == null) return false;
            int c0 = Math.Max(0, (int)Math.Floor((ax - map.TileX0) / map.TileTS));
            int c1 = Math.Min(map.TileCols - 1, (int)Math.Floor((ax + aw - 0.001 - map.TileX0) / map.TileTS));
            int r0 = Math.Max(0, (int)Math.Floor((ay - map.TileY0) / map.TileTS));
            int r1 = Math.Min(map.TileRows - 1, (int)Math.Floor((ay + ah - 0.001 - map.TileY0) / map.TileTS));
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    var cell = map.TileGrid[r * map.TileCols + c];
                    if (cell != null && !cell.Broken) { DamageCell(map, bh, cell, amount); return true; }
                }
            return false;
        }

        // Per-frame: let bullets / damagers / stomping / touching break breakable tilemaps.
        private void UpdateBreakables(double dt)
        {
            foreach (var map in _entities)
            {
                if (map.Dead || map.Tiles == null) continue;
                var bh = map.Beh(BehaviorKind.Tilemap); if (bh == null) continue;
                bool byBullet = bh.Num("byBullet", 1) >= 0.5;
                bool byDamage = bh.Num("byDamage", 1) >= 0.5;
                bool byTouch = bh.Num("byTouch", 0) >= 0.5;
                bool byStomp = bh.Num("byStomp", 0) >= 0.5;
                if (!(byBullet || byDamage || byTouch || byStomp)) continue;

                foreach (var a in _entities.ToList())
                {
                    if (a.Dead || a == map) continue;
                    if (byBullet && a.Has(BehaviorKind.Projectile))
                    {
                        if (BreakOverlap(map, bh, a.X, a.Y, a.W, a.H, 1)) a.Dead = true;
                        continue;
                    }
                    if (byDamage && a.Has(BehaviorKind.Damager))
                    {
                        BreakOverlap(map, bh, a.X, a.Y, a.W, a.H, (int)Math.Max(1, a.Beh(BehaviorKind.Damager)!.Num("damage", 1)));
                        continue;
                    }
                    bool character = a.Has(BehaviorKind.Platformer) || a.Has(BehaviorKind.TopDown) || a.Data.Physics.Mode == PhysicsMode.Dynamic;
                    if (!character) continue;
                    if (byStomp && a.VY >= -1) // landing on / standing on a tile top (not jumping up through it)
                        if (BreakOverlap(map, bh, a.X + 2, a.Y + a.H - 1, a.W - 4, 5, 1) && a.VY > 10) a.VY = -Math.Abs(a.VY) * 0.5;
                    if (byTouch) BreakOverlap(map, bh, a.X, a.Y, a.W, a.H, 1);
                }
            }
        }

        private void DamageEntity(Entity t, double amount, double kb, double fromCX)
        {
            if (t.InvTimer > 0) return;
            t.CurHealth -= amount;
            t.InvTimer = Math.Max(0.3, t.Beh(BehaviorKind.Health)?.Num("invincible", 1) ?? 0.5);
            double dir = t.CX < fromCX ? -1 : 1;
            t.VX = dir * kb; t.VY = -kb * 0.6;
        }

        private void ShowSlash(double x, double y, double w, double h)
        {
            var fe = new Border
            {
                Width = w, Height = h,
                Background = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                CornerRadius = new CornerRadius(4), IsHitTestVisible = false
            };
            Canvas.SetLeft(fe, x); Canvas.SetTop(fe, y);
            World.Children.Add(fe);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.12) };
            t.Tick += (s, e) => { t.Stop(); World.Children.Remove(fe); };
            t.Start();
        }

        private readonly Random _rng = new();

        private void SpawnFrom(Entity spawner, string targetName, bool atTop = false)
        {
            // find the thing to spawn: an object in the scene, OR a prefab from the library
            var template = FindTemplate(targetName)
                ?? _project.Prefabs.FirstOrDefault(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))?.Template;
            if (template == null) return;
            var clone = System.Text.Json.JsonSerializer.Deserialize<GameObjectData>(System.Text.Json.JsonSerializer.Serialize(template))!;
            if (atTop)
            {
                clone.X = _rng.NextDouble() * Math.Max(1, _scene.Width - clone.Width); // random x
                clone.Y = -clone.Height;                                               // just above the top → falls in
            }
            else
            {
                clone.X = spawner.CX - clone.Width / 2;
                clone.Y = spawner.CY - clone.Height / 2;
            }
            AddEntity(clone);
        }

        private void SpawnBullet(Entity shooter, double angleDeg, double speed, double life, string bulletName)
        {
            GameObjectData data;
            var prefab = _project.Prefabs.FirstOrDefault(p => p.Name.Equals(bulletName, StringComparison.OrdinalIgnoreCase));
            if (prefab != null) data = JsonClone(prefab.Template);
            else data = new GameObjectData { Type = "Bullet", Color = "#F4C03B", Width = 14, Height = 14 };

            var proj = data.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Projectile);
            if (proj == null) { proj = BehaviorCatalog.Info(BehaviorKind.Projectile).CreateData(); data.Behaviors.Add(proj); }
            proj.Nums["angle"] = angleDeg; proj.Nums["speed"] = speed; proj.Nums["life"] = life;

            data.X = shooter.CX - data.Width / 2;
            data.Y = shooter.CY - data.Height / 2;
            data.Name = "bullet";
            AddEntity(data);
        }

        private static Key ParseKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Key.T;
            return Enum.TryParse<Key>(s.Trim(), ignoreCase: true, out var k) ? k : Key.T;
        }

        private bool KeyHeld(BehaviorData bh, string param)
        {
            var s = bh.Text(param, "");
            return !string.IsNullOrWhiteSpace(s) && _keys.Contains(ParseKey(s));
        }

        private static T JsonClone<T>(T v) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(v))!;

        private GameObjectData? FindTemplate(string name)
        {
            foreach (var layer in _scene.Layers)
            {
                var o = layer.Objects.FirstOrDefault(x => x.Name == name);
                if (o != null) return o;
            }
            return null;
        }

        private void UpdateCamera(double dt)
        {
            if (_cameraTarget == null) return;
            var cam = _cameraTarget.Beh(BehaviorKind.FollowCamera);
            double smooth = Math.Clamp(cam?.Num("smooth", 0.12) ?? 0.12, 0.01, 1);
            double tx = -(_cameraTarget.CX - Root.ActualWidth / 2);
            double ty = -(_cameraTarget.CY - Root.ActualHeight / 2);
            _camX += (tx - _camX) * smooth;
            _camY += (ty - _camY) * smooth;

            if ((cam?.Num("clamp", 1) ?? 1) >= 0.5)
            {
                double vw = Root.ActualWidth, vh = Root.ActualHeight;
                _camX = _scene.Width > vw ? Math.Clamp(_camX, -(_scene.Width - vw), 0) : 0;
                _camY = _scene.Height > vh ? Math.Clamp(_camY, -(_scene.Height - vh), 0) : 0;
            }
        }

        // Builds the minimap overlay (added to the HUD canvas, independent of the object's box).
        private void BuildMinimap(Entity en, BehaviorData bh)
        {
            double size = Math.Max(60, bh.Num("size", 180));
            double ratio = _scene.Height / Math.Max(1, _scene.Width);
            var inner = new Canvas { Width = size, Height = size * ratio, ClipToBounds = true };
            var box = new Border
            {
                Width = size,
                Height = size * ratio,
                Background = new SolidColorBrush(Color.FromArgb(150, 20, 20, 28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 120, 120, 140)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Opacity = Math.Clamp(bh.Num("opacity", 0.8), 0.1, 1),
                Child = inner
            };
            en.Visual.Opacity = 0; // hide the placeholder box; the overlay is the map
            Hud.Children.Add(box);
            en.MiniCanvas = inner;
        }

        private void UpdateMinimap(Entity en, BehaviorData bh)
        {
            var inner = en.MiniCanvas;
            if (inner?.Parent is not Border box) return;
            double size = inner.Width, mh = inner.Height, m = 10;
            string corner = bh.Text("corner", "tr").ToLowerInvariant();
            double vw = Root.ActualWidth > 0 ? Root.ActualWidth : 960, vh = Root.ActualHeight > 0 ? Root.ActualHeight : 540;
            Canvas.SetLeft(box, corner.Contains("l") ? m : vw - size - m);
            Canvas.SetTop(box, corner.Contains("b") ? vh - mh - m : m);

            inner.Children.Clear();
            double sx = size / Math.Max(1, _scene.Width), sy = mh / Math.Max(1, _scene.Height);
            void Dot(Entity e, Brush b, double r)
            {
                var d = new Ellipse { Width = r * 2, Height = r * 2, Fill = b };
                Canvas.SetLeft(d, Math.Clamp(e.CX * sx - r, 0, size)); Canvas.SetTop(d, Math.Clamp(e.CY * sy - r, 0, mh));
                inner.Children.Add(d);
            }
            var gray = new SolidColorBrush(Color.FromRgb(180, 180, 190));
            foreach (var nm in bh.Text("markers", "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                foreach (var e in _entities.Where(x => !x.Dead && x.Data.Name.Equals(nm, StringComparison.OrdinalIgnoreCase)))
                    Dot(e, gray, 2);
            var player = _entities.FirstOrDefault(x => !x.Dead && x.Data.Name == bh.Text("player", "Player"));
            if (player != null) Dot(player, new SolidColorBrush(Color.FromRgb(0, 229, 255)), 3);
        }

        // Label text + optional text effect (typewriter / fade / blink / pulse).
        private void UpdateLabel(Entity en, double dt)
        {
            var tb = en.LabelBlock!;
            string full = en.LabelText.Contains('{') ? Substitute(en.LabelText) : en.LabelText;
            switch (en.LabelEffect)
            {
                case "typewriter":
                    en.LabelReveal += en.EffectSpeed * dt;
                    tb.Text = full.Substring(0, Math.Min(full.Length, (int)en.LabelReveal));
                    break;
                case "fade":
                    en.EffectT += dt * en.EffectSpeed;
                    tb.Opacity = (Math.Sin(en.EffectT) + 1) / 2;
                    tb.Text = full;
                    break;
                case "blink":
                    en.EffectT += dt * en.EffectSpeed;
                    tb.Opacity = Math.Sin(en.EffectT) > 0 ? 1 : 0.1;
                    tb.Text = full;
                    break;
                case "pulse":
                    en.EffectT += dt * en.EffectSpeed;
                    double s = 1 + 0.15 * Math.Sin(en.EffectT);
                    tb.RenderTransformOrigin = new Point(0.5, 0.5);
                    tb.RenderTransform = new ScaleTransform(s, s);
                    tb.Text = full;
                    break;
                default:
                    if (en.LabelText.Contains('{')) tb.Text = full;
                    break;
            }
        }

        private void Render(double dt)
        {
            _camTransform.X = _camX; _camTransform.Y = _camY;
            _lightTransform.X = _camX; _lightTransform.Y = _camY;
            UpdateDialogTypewriter(dt);

            foreach (var en in _entities)
            {
                if (en.Dead) continue;
                Animate(en, dt);

                if (en.LabelBlock != null) UpdateLabel(en, dt);

                var mm = en.Beh(BehaviorKind.Minimap);
                if (mm != null) UpdateMinimap(en, mm);

                var barB = en.Beh(BehaviorKind.Bar);
                if (barB != null) UpdateBar(en, barB);

                double px = en.X, py = en.Y;
                if (!en.ScreenSpace)
                {
                    var par = en.Beh(BehaviorKind.Parallax);
                    if (par != null)
                    {
                        double factor = Math.Clamp(par.Num("factor", 0.5), 0, 1);
                        px += _camX * (1 - factor) * -1; py += _camY * (1 - factor) * -1;
                    }
                }
                Canvas.SetLeft(en.Visual, px);
                Canvas.SetTop(en.Visual, py);

                // facing flip for platformer characters
                if (en.Has(BehaviorKind.Platformer))
                {
                    if (en.VX > 1) en.Facing = 1; else if (en.VX < -1) en.Facing = -1;
                    en.Visual.RenderTransformOrigin = new Point(0.5, 0.5);
                    en.Visual.RenderTransform = new ScaleTransform(en.Facing, 1);
                }
                else if (Math.Abs(en.Rot) > 0.001)
                {
                    en.Visual.RenderTransformOrigin = new Point(0.5, 0.5);
                    en.Visual.RenderTransform = new RotateTransform(en.Rot);
                }

                if (en.LightVisual != null)
                {
                    double r = en.LightVisual.Width / 2;
                    Canvas.SetLeft(en.LightVisual, en.CX - r);
                    Canvas.SetTop(en.LightVisual, en.CY - r);
                }
            }
        }

        // picks idle/walk automatically, else default animation
        private void Animate(Entity en, double dt)
        {
            if (en.Brush == null || en.Data.Animations.Count == 0) return;
            var anim = ChooseAnim(en);
            if (anim == null || anim.Frames.Count == 0) return;

            if (!ReferenceEquals(anim, en.CurAnim)) { en.CurAnim = anim; en.FrameIndex = 0; en.FrameTimer = 0; }
            en.FrameTimer += dt;
            double spf = 1.0 / Math.Max(1, anim.Fps);
            while (en.FrameTimer >= spf)
            {
                en.FrameTimer -= spf;
                en.FrameIndex++;
                if (en.FrameIndex >= anim.Frames.Count) en.FrameIndex = anim.Loop ? 0 : anim.Frames.Count - 1;
            }
            var src = Load(anim.Frames[Math.Min(en.FrameIndex, anim.Frames.Count - 1)]);
            if (src != null) en.Brush.ImageSource = src;
        }

        private static readonly string[] JumpNames = { "jump", "fall", "air" };
        private static readonly string[] CrouchNames = { "crouch", "duck", "slide" };
        private static readonly string[] WalkNames = { "walk", "run", "move" };
        private static readonly string[] IdleNames = { "idle", "stand" };

        private SpriteAnimation? ChooseAnim(Entity en)
        {
            var anims = en.Data.Animations;
            if (anims.Count == 0) return null;
            SpriteAnimation? Find(string[] names)
            {
                foreach (var a in anims)
                    foreach (var n in names)
                        if (a.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) return a;
                return null;
            }
            bool plat = en.Has(BehaviorKind.Platformer);

            // airborne → jump/fall animation
            if (plat && !en.Grounded) { var j = Find(JumpNames); if (j != null) return j; }
            // the controlled player holding down on the ground → crouch (not enemies that don't read keys)
            bool controlled = (en.Beh(BehaviorKind.Platformer)?.Num("controls", 1) ?? 0) >= 0.5;
            bool crouching = plat && controlled && en.Grounded && K(Key.Down, Key.S);
            if (crouching) { var c = Find(CrouchNames); if (c != null) return c; }

            bool moving = Math.Abs(en.VX) > 5 || (Math.Abs(en.VY) > 5 && !plat);
            if (moving && !crouching) { var w = Find(WalkNames); if (w != null) return w; }
            var idle = Find(IdleNames); if (idle != null) return idle;
            return en.Data.ResolveDefaultAnimation();
        }

        private void Cleanup()
        {
            for (int i = _entities.Count - 1; i >= 0; i--)
            {
                var en = _entities[i];
                if (!en.Dead) continue;
                World.Children.Remove(en.Visual);
                if (en.LightVisual != null) Lights.Children.Remove(en.LightVisual);
                _solids.Remove(en);
                _entities.RemoveAt(i);
            }
        }

        private void PlaySound(BehaviorData snd)
        {
            var path = ProjectIO.AbsoluteAsset(_project, snd.Text("asset", ""));
            if (path == null) return;
            try
            {
                // mixing: final volume = sound volume × channel volume × master volume (vars default to 1)
                string channel = snd.Text("channel", "sfx").ToLowerInvariant();
                double chVol = _vars.TryGetValue("vol." + channel, out var cv) ? cv : 1;
                double master = _vars.TryGetValue("vol.master", out var mv) ? mv : 1;
                var mp = new MediaPlayer { Volume = Math.Clamp(snd.Num("volume", 0.8) * chVol * master, 0, 1) };
                mp.Open(new Uri(path));
                if (snd.Num("loop", 0) >= 0.5) mp.MediaEnded += (s, e) => { mp.Position = TimeSpan.Zero; mp.Play(); };
                mp.Play();
                _players.Add(mp);
            }
            catch { }
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => Close();

        private void Stop()
        {
            CompositionTarget.Rendering -= OnFrame;
            foreach (var mp in _players) { try { mp.Stop(); mp.Close(); } catch { } }
            _players.Clear();
        }

        private SolidColorBrush Brush(string hex) => new(ParseColor(hex, Color.FromRgb(0x9A, 0x9A, 0xA0)));
        private static Color ParseColor(string hex, Color fallback) { try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; } }
    }
}
