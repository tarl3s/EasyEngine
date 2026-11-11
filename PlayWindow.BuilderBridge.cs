using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BE = BuilderEngine.Script; // its World/Scene/Project clash with ours (and "World" is also this window's canvas)

namespace EasyEngine
{
    // THE BRIDGE: objects built on BuilderEngine (BuilderLink.IsBuilt) really work when EasyEngine plays.
    // A scene with such objects gets one BuilderEngine World (the starter kit + the project's own mechanisms, as BuilderEngine's
    // own Play loads them: GameBoot.NewWorld). Its objects are spawned with their params (World.LoadScene); every other
    // EasyEngine entity is a read-only mirror in that world (name, position, size, tags from its Type / Group / Builder tags,
    // "player" for the one the keys steer, solid when it is a collider) so 'touched by', find, nearest and all work across both.
    // Each frame, after EasyEngine's physics:
    //   EasyEngine positions → mirrors (and → BuilderEngine objects, so EasyEngine behaviors on one still move it), keys held →
    //   World.KeyDown/KeyUp, global number variables → World.Globals; World.Frame(dt); then BuilderEngine objects' position,
    //   size, visibility, picture, colour, facing and solidity → their EasyEngine entities (drawn as entities with no
    //   behaviors), changed globals → EasyEngine variables (a Label showing {score} sees a BuilderEngine Coin), destroy/spawn
    //   → EasyEngine entities removed/added, say/narrate/ask → EasyEngine's dialogue box, show text/show bar → HUD texts and
    //   bars, notify → a message at the top, play → EasyEngine's sound, goto_scene → EasyEngine's scene change, print and
    //   script errors → the F1 console. Commands that need BuilderEngine's own screen (camera, lights, screen effects,
    //   minimap, hotbar, tile maps…) run but show nothing here: each is logged once as not available in EasyEngine.
    // The per-frame part allocates nothing of its own (BuilderEngine's scripts are BuilderEngine's business).
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public BE.GameObject? BMirror;   // this entity as seen by BuilderEngine's scripts (read-only copy)
            public BE.GameObject? BOwn;      // the BuilderEngine object that drives this entity (built on BuilderEngine)
            public bool BLinked;             // handled by the bridge (a mirror, an owned object, or nothing to do)
            public string? BImg, BColor;     // the picture / colour last copied from its BuilderEngine object
            public bool BShown = true, BFlip, BSolid;
            public bool BPkg;                // its mirror also runs its behavior packages (BehaviorPackages.cs)
            public double BSx, BSy, BSvx, BSvy, BSang; // what was sent to that twin this frame: its changes are what differs after
        }

        private BE.World? _bWorld, _bLastWorld;          // this scene's world; the last one (a next scene's world carries its globals)
        private readonly List<Entity> _bPairs = new();     // entities with a mirror or an owner
        private readonly HashSet<BE.GameObject> _bKnown = new(ReferenceEqualityComparer.Instance); // world objects that already have an entity
        private readonly HashSet<Key> _bKeys = new();      // keys BuilderEngine was told are down
        private readonly List<Key> _bKeyScratch = new();
        private readonly Dictionary<string, double> _bSynced = new(StringComparer.OrdinalIgnoreCase); // globals as last agreed by both sides
        private readonly List<KeyValuePair<string, double>> _bPull = new();
        private readonly HashSet<string> _bWarned = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _bPictures = new(StringComparer.OrdinalIgnoreCase); // a BuilderEngine picture name → project-relative path
        private readonly Dictionary<BE.HudItem, BHud> _bHud = new(ReferenceEqualityComparer.Instance);
        private readonly List<BE.HudItem> _bHudGone = new();
        private TextBlock? _bToast;
        private string? _bToastText;
        private BE.Dialogue? _bShown; private int _bShownSel = -1; // the BuilderEngine line on EasyEngine's dialogue box
        private string[] _bSceneNames = Array.Empty<string>();      // BuilderEngine's scene names, in EasyEngine's scene order
        private int _bStamp;
        private PackageSet _pkgs = PackageSet.Empty;              // the behavior packages this project can use
        private readonly Dictionary<string, bool> _bPkgLoaded = new(StringComparer.OrdinalIgnoreCase); // package → loaded into this world (false: its script is broken)
        private static readonly ScaleTransform FlipX = MakeFlip();
        private static ScaleTransform MakeFlip() { var t = new ScaleTransform(-1, 1); t.Freeze(); return t; }

        private sealed class BHud { public FrameworkElement View = null!; public TextBlock? Text; public Border? Fill; public string? LastText, LastColor; public double LastSize = -1, LastW = -1, LastFill = -1, LastX = double.NaN, LastY = double.NaN; public int Stamp; }

        // what needs BuilderEngine's own screen: it runs (the scripts don't break) but nothing shows here
        private static readonly string[] BNotHere =
        {
            "camera follow", "camera limits", "camera move", "camera reset", "camera shake", "camera view", "camera zoom",
            "darkness", "light", "sunlight", "screen noise", "screen tint", "flash", "fade", "fade in", "fade out",
            "show minimap", "hide minimap", "minimap mark", "minimap reveal", "minimap unmark", "show hotbar", "hide hotbar", "select_slot",
            "cursor", "draggable", "caption", "label", "outline", "layer", "ui", "show screen", "hide screen", "toggle screen", "join screen",
            "place_tile", "set_tile", "fill_tiles", "dig_tile", "break_tile", "noise_tiles", "make_map", "tile_kind",
        };

        // ---- feature hooks (PlayWindow.xaml.cs) ----
        partial void BuilderSceneReset()
        {
            if (_bShown != null && _dialogOpen) { _dialog.Clear(); CloseDialog(); } // a BuilderEngine line doesn't outlive its scene
            if (_bWorld != null) _bLastWorld = _bWorld;
            _bWorld = null;
            foreach (var en in _bPairs) { en.BMirror = null; en.BOwn = null; }
            _bPairs.Clear(); _bKnown.Clear(); _bKeys.Clear(); _bHud.Clear(); // the HUD canvas was just cleared by LoadScene
            _bToast = null; _bToastText = null; _bShown = null; _bShownSel = -1;
            _bPkgLoaded.Clear(); // a new world each scene: its packages are loaded into it again
        }

        partial void BuilderSceneLoaded()
        {
            _pkgs = BehaviorPackages.For(_project);
            foreach (var problem in _pkgs.Problems)
                if (_bWarned.Add("pkgfile:" + problem)) Warn("Packages", "", "A behavior package couldn't be read: " + problem);
            bool hasBuilt = BuilderLink.HasBuilt(_scene);
            if (!hasBuilt && !UsesPackages()) return; // nothing of BuilderEngine's here: no world
            BE.Project beProject;
            try { beProject = BE.EasyLink.FromEeproj(JsonSerializer.Serialize(_project), _project.FolderPath ?? ""); }
            catch (Exception ex) { Warn("BuilderEngine", "", "Couldn't read the BuilderEngine part of this project: " + ex.Message); return; }
            int sceneIndex = _project.Scenes.FindIndex(s => s.Name == _sceneName);
            if (sceneIndex < 0 || sceneIndex >= beProject.Scenes.Count) return;
            _bSceneNames = beProject.Scenes.Select(s => s.Name).ToArray();

            var w = _bLastWorld != null ? _bLastWorld.ForNextScene() : NewBuilderWorld(beProject);
            _bWorld = w;

            // mirrors first, so Object params naming an EasyEngine object ("target: Player") find it; the objects with
            // behavior packages after the plain ones (their settings may name them)
            var built = new Dictionary<int, Entity>();
            var withPackages = new List<Entity>();
            foreach (var en in _entities)
            {
                if (en.Dead) continue;
                if (BuilderLink.IsBuilt(en.Data)) { built.TryAdd(en.Data.Id, en); en.BLinked = true; }
                else if (HasPackages(en)) withPackages.Add(en);
                else Mirror(en);
            }
            foreach (var en in withPackages) Mirror(en);
            if (!hasBuilt)
            {
                Info("Packages", "", $"Behavior packages: {_bPairs.Count(e => e.BPkg)} object(s) run theirs in BuilderEngine",
                     string.Join(", ", _bPkgLoaded.Where(kv => kv.Value).Select(kv => kv.Key)));
                return;
            }
            var beScene = beProject.Scenes[sceneIndex];
            beScene.Objects.RemoveAll(o => !built.ContainsKey(o.Id) || o.Mechanisms.Count == 0); // the ones playing here (layer on, object on)
            Dictionary<BE.GameObject, BE.SceneObject> map;
            try { map = w.LoadScene(beScene); }
            catch (BE.ScriptError ex) { Warn("BuilderEngine", "", ex.Message); map = new(); }
            foreach (var (go, so) in map)
                if (built.Remove(so.Id, out var en)) Own(en, go);
                else ((BuilderEngine.Plugin.IPluginObject)go).Destroy();
            foreach (var en in built.Values) SpawnBuilt(en); // an id BuilderEngine renumbered: spawned from its own mechanisms
            Info("BuilderEngine", "", $"Built on BuilderEngine: {_bPairs.Count(e => e.BOwn != null)} object(s) run their mechanisms here",
                 $"{w.Library.Count} mechanisms loaded · {_bPairs.Count(e => e.BMirror != null)} EasyEngine objects visible to them");
        }

        private BE.World NewBuilderWorld(BE.Project beProject)
        {
            var folder = _project.FolderPath ?? System.IO.Path.GetTempPath();
            var w = BE.GameBoot.NewWorld(beProject, folder, System.IO.Path.Combine(folder, "saves"));
            foreach (var err in w.Errors) Warn("BuilderEngine", "", err);
            w.Errors.Clear();
            // these handlers are carried to the next scenes' worlds (ForNextScene)
            w.Printed += text =>
            {
                if (text.StartsWith("⚠")) Warn("BuilderEngine", "", text.TrimStart('⚠', ' '));
                else Info("BuilderEngine", "", text);
            };
            w.SoundPlayed += BuilderSound;
            w.SoundControl += r => { if (!r.VolumeOnly) BuilderNotHere("looping sounds"); };
            w.SceneRequested += name =>
            {
                int i = Array.FindIndex(_bSceneNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                string target = i >= 0 && i < _project.Scenes.Count ? _project.Scenes[i].Name : name;
                if (_project.FindScene(target) != null) _pendingScene = target;
            };
            foreach (var name in BNotHere)
                if (w.Commands.TryGetValue(name, out var run))
                {
                    var n = name;
                    w.Commands[n] = c => { BuilderNotHere("'" + n + "'"); run(c); };
                }
            return w;
        }

        private void BuilderNotHere(string what)
        {
            if (_bWarned.Add(what)) Warn("BuilderEngine", "", $"{what} isn't available in EasyEngine (it needs BuilderEngine's own screen) — it runs, but nothing shows");
        }

        // ---- linking entities and BuilderEngine objects ----
        private void Mirror(Entity en)
        {
            en.BLinked = true;
            if (_bWorld == null) return;
            var tags = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(en.Data.Type) && en.Data.Type != "Object") tags.Add(en.Data.Type);
            if (!string.IsNullOrWhiteSpace(en.Data.Group)) tags.Add(en.Data.Group);
            if (IsPlayerSteered(en)) tags.Add("player");
            tags.AddRange(BuilderLink.Tags(en.Data));
            if (en.ScreenSpace) tags.Add("ui");
            var m = SpawnWithPackages(en, tags) ?? _bWorld.Spawn(en.Data.Name, en.X, en.Y, null, null, tags.ToArray());
            m.Width = en.W; m.Height = en.H; m.Ui = en.ScreenSpace;
            en.BMirror = m;
            if (en.BPkg) { m.Angle = en.Rot; en.BImg = m.Image; en.BColor = m.Color; en.BShown = true; en.BFlip = false; en.BSolid = false; }
            _bKnown.Add(m);
            _bPairs.Add(en);
        }

        // ---- behavior packages (BehaviorPackages.cs): an EasyEngine object whose BuilderEngine twin runs them ----
        // The object stays an EasyEngine object (its other behaviors keep running). Its twin is its mirror plus the
        // packages' mechanisms, with the behavior's settings. Each frame the twin gets the entity's x, y, vx, vy and angle;
        // after BuilderEngine's frame, what the package CHANGED is applied to the entity — so a package moves the object
        // after EasyEngine's own movers (a position change adds to theirs; a speed it sets replaces theirs from the next
        // frame), and its size, look, visibility, angle and destroy show as well.
        private bool UsesPackages()
        {
            foreach (var en in _entities) if (!en.Dead && HasPackages(en)) return true;
            foreach (var pf in _project.Prefabs) if (pf.Template.Behaviors.Any(b => b.Kind == BehaviorKind.Package)) return true; // a Spawner may bring one
            return false;
        }

        private static bool HasPackages(Entity en)
        {
            var list = en.Data.Behaviors;
            for (int i = 0; i < list.Count; i++) if (list[i].Kind == BehaviorKind.Package) return true;
            return false;
        }

        /// <summary>Loads a package's script into this scene's world once; false when it can't run (said once in the console).</summary>
        private bool PackageReady(BE.BehaviorPackage pkg)
        {
            if (_bWorld == null) return false;
            if (_bPkgLoaded.TryGetValue(pkg.Name, out var ok)) return ok;
            if (_bWorld.Package(pkg.Name) != null) return _bPkgLoaded[pkg.Name] = true; // GameBoot already loaded it (project → installed → shipped)
            try { _bWorld.Load(pkg.Script); ok = true; }
            catch (BE.ScriptError ex) { Warn("Packages", "", $"The behavior package \"{pkg.Name}\" has a script error: {ex.Message}"); ok = false; }
            return _bPkgLoaded[pkg.Name] = ok;
        }

        /// <summary>The entity's twin with its packages' mechanisms, or null when it has none that can run.</summary>
        private BE.GameObject? SpawnWithPackages(Entity en, List<string> tags)
        {
            if (_bWorld == null || !HasPackages(en)) return null;
            var names = new List<string>();
            var uses = new List<(BE.BehaviorPackage pkg, BehaviorData bh)>();
            var overrides = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in en.Data.Behaviors)
            {
                if (b.Kind != BehaviorKind.Package) continue;
                string name = BehaviorPackages.NameOf(b);
                var pkg = _pkgs.Find(name);
                if (pkg == null)
                {
                    if (_bWarned.Add("pkg:" + name))
                        Warn("Packages", en.Data.Name, $"The behavior package \"{name}\" isn't installed: put {name}.behavior in the project's behaviors folder (or in {BehaviorPackages.InstalledFolder})");
                    continue;
                }
                if (!PackageReady(pkg)) continue;
                names.Add(pkg.Name);
                uses.Add((pkg, b));
                overrides.TryAdd(pkg.Name, BehaviorPackages.RuntimeSettings(pkg, b));
            }
            if (names.Count == 0) return null;
            BE.GameObject go;
            try { go = _bWorld.Spawn(en.Data.Name, en.X, en.Y, names, overrides, tags.ToArray()); }
            catch (BE.ScriptError ex) { Warn("Packages", en.Data.Name, ex.Message); return null; }
            // a repeatable package twice on one object: each mechanism gets its own behavior's settings
            for (int i = 0; i < uses.Count && i < go.Mechanisms.Count; i++)
                foreach (var (key, value) in BehaviorPackages.RuntimeSettings(uses[i].pkg, uses[i].bh))
                    go.Mechanisms[i].Vars[key] = value is string s && uses[i].pkg.AllParams().FirstOrDefault(p => p.Name == key)?.Kind == BE.ParamKind.Object
                        ? _bWorld.FindObject(s) ?? (object)s : value;
            en.BPkg = true;
            return go;
        }

        /// <summary>After BuilderEngine's frame: what a package changed on the twin goes to the entity (no allocation).</summary>
        private void PackageBack(Entity en, BE.GameObject go, double dt)
        {
            // BuilderEngine moved the twin by its speed (no body: nothing stops it); EasyEngine's physics moves the entity by
            // its own speed — so the package's own move is the position minus that step, and a new speed is handed over
            double vx = go.Vx, vy = go.Vy;
            double sx = go.HasBody ? go.X : go.X - vx * dt, sy = go.HasBody ? go.Y : go.Y - vy * dt;
            if (Math.Abs(sx - en.BSx) > 1e-6) en.X += sx - en.BSx;
            if (Math.Abs(sy - en.BSy) > 1e-6) en.Y += sy - en.BSy;
            if (vx != en.BSvx) en.VX = vx;
            if (vy != en.BSvy) en.VY = vy;
            if (go.Angle != en.BSang) en.Rot = go.Angle;
            BuilderLook(en, go);
        }

        private bool IsPlayerSteered(Entity en)
        {
            var b = en.Beh(BehaviorKind.Platformer) ?? en.Beh(BehaviorKind.TopDown);
            return b != null && b.Num("controls", 1) >= 0.5;
        }

        private void Own(Entity en, BE.GameObject go)
        {
            en.BLinked = true;
            en.BOwn = go;
            en.BImg = go.Image; en.BColor = go.Color; en.BShown = true; en.BFlip = false; en.BSolid = false;
            _bKnown.Add(go);
            _bPairs.Add(en);
        }

        /// <summary>An entity built on BuilderEngine that appeared later (an EasyEngine spawn of a prefab with mechanisms):
        /// its BuilderEngine object is made from its own mechanisms and params.</summary>
        private void SpawnBuilt(Entity en)
        {
            en.BLinked = true;
            if (_bWorld == null) return;
            var names = new List<string>();
            var overrides = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in BuilderLink.Mechanisms(en.Data))
            {
                BE.MechanismDef? def;
                try { def = _bWorld.TryDef(m.Name); }
                catch (BE.ScriptError ex) { Warn("BuilderEngine", en.Data.Name, ex.Message); continue; }
                if (def == null) { Warn("BuilderEngine", en.Data.Name, $"There's no mechanism called '{m.Name}'."); continue; }
                if (names.Contains(def.Name, StringComparer.OrdinalIgnoreCase)) continue;
                names.Add(def.Name);
                var ov = new Dictionary<string, object?>();
                if (m.Params.ValueKind == JsonValueKind.Object)
                    foreach (var p in def.Params)
                        foreach (var kv in m.Params.EnumerateObject())
                            if (string.Equals(kv.Name, p.Name, StringComparison.OrdinalIgnoreCase)) ov[p.Name] = BE.World.ConvertParam(JsonValue(kv.Value), p.Type);
                overrides[def.Name] = ov;
            }
            var tags = BuilderLink.Tags(en.Data);
            if (en.ScreenSpace) tags.Add("ui");
            BE.GameObject go;
            try { go = _bWorld.Spawn(en.Data.Name, en.X, en.Y, names, overrides, tags.ToArray()); }
            catch (BE.ScriptError ex) { Warn("BuilderEngine", en.Data.Name, ex.Message); go = _bWorld.Spawn(en.Data.Name, en.X, en.Y, null, null, tags.ToArray()); }
            go.Width = en.W; go.Height = en.H; go.Color = en.Data.Color; go.Ui = en.ScreenSpace;
            if (!string.IsNullOrWhiteSpace(en.Data.SpritePath)) go.Image = en.Data.SpritePath;
            Own(en, go);
        }

        private static object? JsonValue(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => v.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => v.EnumerateArray().Select(JsonValue).ToList(),
            _ => null,
        };

        /// <summary>A BuilderEngine object spawned by a script: it gets an EasyEngine entity (drawn, no behaviors).</summary>
        private void AddForBuilder(BE.GameObject go)
        {
            var data = new GameObjectData
            {
                Id = 0, Name = go.Name, X = go.X, Y = go.Y, Width = Math.Max(1, go.Width), Height = Math.Max(1, go.Height),
                Color = string.IsNullOrWhiteSpace(go.Color) ? "#D97757" : go.Color!, SpritePath = BuilderPicture(go.Image),
            };
            var en = AddEntity(data, go.Ui);
            Own(en, go);
            en.BImg = go.Image;
        }

        // ---- every frame (StepLogic, after EasyEngine's physics) ----
        partial void BuilderLogic(double dt)
        {
            var w = _bWorld;
            if (w == null) return;

            // entities EasyEngine made since the last frame (a Spawner's copy, a bullet): mirrored, or run when built on BuilderEngine
            for (int i = 0; i < _entities.Count; i++)
            {
                var en = _entities[i];
                if (en.BLinked || en.Dead) continue;
                if (BuilderLink.IsBuilt(en.Data)) SpawnBuilt(en); else Mirror(en);
            }

            // EasyEngine → BuilderEngine
            for (int i = _bPairs.Count - 1; i >= 0; i--)
            {
                var en = _bPairs[i];
                var go = en.BMirror ?? en.BOwn;
                if (go == null) { _bPairs.RemoveAt(i); continue; }
                if (en.Dead)
                {
                    if (!go.Destroyed) ((BuilderEngine.Plugin.IPluginObject)go).Destroy();
                    _bKnown.Remove(go); en.BMirror = null; en.BOwn = null;
                    _bPairs.RemoveAt(i);
                    continue;
                }
                go.X = en.X; go.Y = en.Y;
                if (en.BMirror != null)
                {
                    go.Width = en.W; go.Height = en.H;
                    go.Solid = en.IsSolid && !en.SolidDisabled; go.OneWay = en.OneWay;
                    if (en.BPkg)
                    {
                        go.Vx = en.VX; go.Vy = en.VY; go.Angle = en.Rot;
                        en.BSx = en.X; en.BSy = en.Y; en.BSvx = en.VX; en.BSvy = en.VY; en.BSang = en.Rot;
                    }
                }
            }
            BuilderKeys(w);
            if (_mouseClicked && w.Dialogue is { Closed: false }) w.Click();
            foreach (var kv in _vars)
                if (!_bSynced.TryGetValue(kv.Key, out var was) || was != kv.Value) { w.Globals[kv.Key] = kv.Value; _bSynced[kv.Key] = kv.Value; }

            try { w.Frame(dt); }
            catch (Exception ex) { LogThrottled("be-frame", 1.0, LogLevel.Error, "BuilderEngine", "", ex.GetType().Name + ": " + ex.Message, ex.StackTrace ?? ""); }

            // BuilderEngine → EasyEngine
            for (int i = _bPairs.Count - 1; i >= 0; i--)
            {
                var en = _bPairs[i];
                var go = en.BOwn;
                if (go == null) continue;
                if (go.Destroyed) { en.Dead = true; _bKnown.Remove(go); en.BOwn = null; _bPairs.RemoveAt(i); continue; }
                en.X = go.X; en.Y = go.Y;
                BuilderLook(en, go);
            }
            // EasyEngine objects with behavior packages: what their packages changed
            for (int i = _bPairs.Count - 1; i >= 0; i--)
            {
                var en = _bPairs[i];
                if (!en.BPkg || en.BMirror is not BE.GameObject go) continue;
                if (go.Destroyed) { en.Dead = true; _bKnown.Remove(go); en.BMirror = null; _bPairs.RemoveAt(i); continue; } // the package destroyed it
                PackageBack(en, go, w.LastDt);
            }
            var objs = w.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var go = objs[i];
                if (!go.Destroyed && !_bKnown.Contains(go)) AddForBuilder(go);
            }
            foreach (var kv in w.Globals)
                if (kv.Value is double d && (!_bSynced.TryGetValue(kv.Key, out var was) || was != d)) _bPull.Add(new KeyValuePair<string, double>(kv.Key, d));
            for (int i = 0; i < _bPull.Count; i++) { _vars[_bPull[i].Key] = _bPull[i].Value; _bSynced[_bPull[i].Key] = _bPull[i].Value; }
            _bPull.Clear();
            BuilderDialogue(w);
            BuilderHud(w);
        }

        private void BuilderLook(Entity en, BE.GameObject go)
        {
            if (go.Width != en.W || go.Height != en.H)
            {
                en.W = Math.Max(0, go.Width); en.H = Math.Max(0, go.Height);
                en.Visual.Width = en.W; en.Visual.Height = en.H;
            }
            bool shown = go.Shown;
            if (shown != en.BShown) { en.BShown = shown; en.Visual.Visibility = shown ? Visibility.Visible : Visibility.Collapsed; }
            bool lookChanged = false;
            if (!string.Equals(go.Image, en.BImg, StringComparison.Ordinal)) { en.BImg = go.Image; lookChanged = true; }
            if (!string.Equals(go.Color, en.BColor, StringComparison.Ordinal)) { en.BColor = go.Color; lookChanged = true; }
            if (lookChanged)
            {
                var rel = BuilderPicture(go.Image);
                if (rel != null && Load(rel) is BitmapImage bmp)
                {
                    if (en.Brush == null) { en.Brush = MakeBrush(bmp, en.Data); RenderOptions.SetBitmapScalingMode(en.Visual, BitmapScalingMode.NearestNeighbor); }
                    else en.Brush.ImageSource = bmp;
                    en.Visual.Background = en.Brush;
                }
                else en.Visual.Background = FrozenBrush(string.IsNullOrWhiteSpace(go.Color) ? en.Data.Color : go.Color!, Color.FromRgb(0x9A, 0x9A, 0xA0));
            }
            if (go.FlipX != en.BFlip)
            {
                en.BFlip = go.FlipX;
                en.Visual.RenderTransformOrigin = new Point(0.5, 0.5);
                en.Visual.RenderTransform = go.FlipX ? FlipX : Transform.Identity;
            }
            bool solid = go.Solid || go.OneWay;
            if (solid != en.BSolid)
            {
                en.BSolid = solid;
                if (solid && !en.IsSolid) { _solids.Add(en); en.IsSolid = true; en.OneWay = go.OneWay; }
                else if (!solid && en.IsSolid && en.Data.Physics.Mode != PhysicsMode.Static && en.Data.Behaviors.Count == 0) { _solids.Remove(en); en.IsSolid = false; }
            }
        }

        /// <summary>A BuilderEngine picture as a project-relative path EasyEngine can load: a relative path as it is
        /// ("Assets\sprites\coin.png"), a bare name from Assets\sprites or images\; null when there is none.</summary>
        private string? BuilderPicture(string? image)
        {
            if (string.IsNullOrWhiteSpace(image)) return null;
            if (_bPictures.TryGetValue(image, out var known)) return known;
            string? rel = null;
            if (image.Contains('/') || image.Contains('\\')) rel = image.Replace('/', '\\');
            else
                foreach (var dir in new[] { System.IO.Path.Combine("Assets", "sprites"), "images" })
                    if (ProjectIO.AbsoluteAsset(_project, System.IO.Path.Combine(dir, image)) != null) { rel = System.IO.Path.Combine(dir, image); break; }
            if (rel != null && ProjectIO.AbsoluteAsset(_project, rel) == null) rel = null;
            if (rel == null) Warn("BuilderEngine", "", $"The picture \"{image}\" isn't in this project (Assets\\sprites)");
            return _bPictures[image] = rel;
        }

        private void BuilderSound(string name)
        {
            string? rel = null;
            if (name.Contains('/') || name.Contains('\\')) rel = ProjectIO.AbsoluteAsset(_project, name) != null ? name : null;
            else
                foreach (var dir in new[] { System.IO.Path.Combine("Assets", "sounds"), "sounds" })
                    if (ProjectIO.AbsoluteAsset(_project, System.IO.Path.Combine(dir, name)) != null) { rel = System.IO.Path.Combine(dir, name); break; }
            if (rel == null) { if (_bWarned.Add("sound:" + name)) Warn("BuilderEngine", "", $"The sound \"{name}\" isn't in this project (Assets\\sounds)"); return; }
            PlaySoundFile(rel, 0.8, "sfx", false, "BuilderEngine");
        }

        // keys held in EasyEngine → BuilderEngine (only when they change: no allocation per frame)
        private static readonly Dictionary<Key, string> KeyNames = new();
        /// <summary>A key as BuilderEngine's scripts name it ("space", "a", "shift") — also how a package's Key setting is sent.</summary>
        internal static string BuilderKeyName(Key k) => KeyName(k);
        private static string KeyName(Key k)
        {
            if (KeyNames.TryGetValue(k, out var n)) return n;
            n = k switch
            {
                Key.Return => "enter", Key.Escape => "escape", Key.Space => "space", Key.Back => "backspace",
                Key.LeftShift or Key.RightShift => "shift", Key.LeftCtrl or Key.RightCtrl => "ctrl", Key.LeftAlt or Key.RightAlt => "alt",
                _ => k.ToString().ToLowerInvariant(),
            };
            return KeyNames[k] = n;
        }

        private void BuilderKeys(BE.World w)
        {
            foreach (var k in _keys)
                if (!_bKeys.Contains(k)) _bKeyScratch.Add(k);
            for (int i = 0; i < _bKeyScratch.Count; i++) { _bKeys.Add(_bKeyScratch[i]); w.KeyDown(KeyName(_bKeyScratch[i])); }
            _bKeyScratch.Clear();
            foreach (var k in _bKeys)
                if (!_keys.Contains(k)) _bKeyScratch.Add(k);
            for (int i = 0; i < _bKeyScratch.Count; i++) { _bKeys.Remove(_bKeyScratch[i]); w.KeyUp(KeyName(_bKeyScratch[i])); }
            _bKeyScratch.Clear();
        }

        // say / narrate / ask → EasyEngine's dialogue box; BuilderEngine's dialogue decides, the box follows it
        private void BuilderDialogue(BE.World w)
        {
            var d = w.Dialogue;
            if (_bShown != null && (!ReferenceEquals(d, _bShown) || _bShown.Closed))
            {
                if (_dialogOpen) { _dialog.Clear(); CloseDialog(); } // closed by BuilderEngine (a key, 'say … for 2', a newer line)
                _bShown = null; _bShownSel = -1;
            }
            if (d != null && !d.Closed && !ReferenceEquals(d, _bShown))
            {
                _bShown = d; _bShownSel = d.Selected;
                _dialog.Clear(); _dialogOpen = false; _dialogTyping = false;
                ShowDialog(DialogueText(d), d.Speaker);
                return;
            }
            if (_bShown == null) return;
            if (!_dialogOpen)
            {
                // the person closed the box (Enter): BuilderEngine's line goes on too
                if (_bShown.Options == null) { _bShown.Typed = _bShown.Text.Length; w.Click(); }
                else w.Click(_bShown.Selected);
                _bShown = null; _bShownSel = -1;
            }
            else if (_bShown.Options != null && _bShown.Selected != _bShownSel)
            {
                _bShownSel = _bShown.Selected; // a choice moved with the arrows
                _dialogFull = DialogueText(_bShown); _dialogTyping = false; DialogText.Text = _dialogFull;
            }
        }

        private static string DialogueText(BE.Dialogue d)
        {
            string text = d.Text.Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/');
            if (d.Options == null) return text;
            var sb = new System.Text.StringBuilder(text);
            for (int i = 0; i < d.Options.Count; i++) sb.Append(i == d.Selected ? "   ▶ " : "   ").Append(i + 1).Append(") ").Append(d.Options[i].Replace('|', '/'));
            return sb.ToString();
        }

        // show text / show bar → HUD texts and bars; notify → a message at the top
        private void BuilderHud(BE.World w)
        {
            _bStamp++;
            var hud = w.Hud;
            for (int i = 0; i < hud.Count; i++)
            {
                var item = hud[i];
                if (!_bHud.TryGetValue(item, out var v)) _bHud[item] = v = MakeHud(item);
                v.Stamp = _bStamp;
                double x = item.X, y = item.Y;
                if (item.Anchor != null) { x += item.Anchor.X + (item.Anchor.Ui ? 0 : _camX); y += item.Anchor.Y + (item.Anchor.Ui ? 0 : _camY); }
                if (x != v.LastX) { v.LastX = x; Canvas.SetLeft(v.View, x); } // only when it moves (SetLeft boxes the number)
                if (y != v.LastY) { v.LastY = y; Canvas.SetTop(v.View, y); }
                if (v.Text != null)
                {
                    if (!string.Equals(v.LastText, item.Text, StringComparison.Ordinal)) { v.LastText = item.Text; v.Text.Text = item.Text; }
                    if (!string.Equals(v.LastColor, item.Color, StringComparison.Ordinal)) { v.LastColor = item.Color; v.Text.Foreground = FrozenBrush(item.Color, Colors.White); }
                    if (v.LastSize != item.Size) { v.LastSize = item.Size; v.Text.FontSize = Math.Max(6, item.Size); }
                }
                else if (v.Fill != null)
                {
                    if (v.LastW != item.Width) { v.LastW = item.Width; ((Border)v.View).Width = item.Width; ((Border)v.View).Height = item.Height; }
                    double f = item.Fraction;
                    if (v.LastFill != f) { v.LastFill = f; v.Fill.Width = Math.Max(0, (item.Width - 2) * f); v.Fill.Height = Math.Max(0, item.Height - 2); }
                    if (!string.Equals(v.LastColor, item.Color, StringComparison.Ordinal)) { v.LastColor = item.Color; v.Fill.Background = FrozenBrush(item.Color, Colors.IndianRed); }
                }
            }
            foreach (var kv in _bHud) if (kv.Value.Stamp != _bStamp) _bHudGone.Add(kv.Key);
            for (int i = 0; i < _bHudGone.Count; i++) { Hud.Children.Remove(_bHud[_bHudGone[i]].View); _bHud.Remove(_bHudGone[i]); }
            _bHudGone.Clear();

            string? toast = w.Toasts.Count > 0 ? w.Toasts[^1].Text : null;
            if (!string.Equals(toast, _bToastText, StringComparison.Ordinal))
            {
                _bToastText = toast;
                if (_bToast == null && toast != null)
                {
                    _bToast = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Background = FrozenBrush("#B0000000", Colors.Black), Padding = new Thickness(12, 4, 12, 4) };
                    Hud.Children.Add(_bToast);
                }
                if (_bToast != null)
                {
                    _bToast.Text = toast ?? "";
                    _bToast.Visibility = toast == null ? Visibility.Collapsed : Visibility.Visible;
                    _bToast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(_bToast, Math.Max(0, (Root.Width - _bToast.DesiredSize.Width) / 2)); Canvas.SetTop(_bToast, 24);
                }
            }
        }

        private BHud MakeHud(BE.HudItem item)
        {
            var v = new BHud();
            if (item.IsBar)
            {
                v.Fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
                v.View = new Border { Background = FrozenBrush("#80000000", Colors.Black), BorderBrush = FrozenBrush("#CCFFFFFF", Colors.White), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = v.Fill };
            }
            else
            {
                v.Text = new TextBlock { FontWeight = FontWeights.SemiBold, FontFamily = ResolveFont(null) };
                v.View = v.Text;
            }
            Hud.Children.Add(v.View);
            return v;
        }

        // ---- test hooks (FullTest.BuilderLink.cs, E2ETests.BuilderLink.cs) ----
        public bool TBuilderRunning => _bWorld != null;
        public int TBuilderOwned => _bPairs.Count(e => e.BOwn != null && !e.Dead);
        public int TBuilderMirrors => _bPairs.Count(e => e.BMirror != null && !e.Dead);
        public double TBuilderGlobal(string name) => _bWorld != null && _bWorld.Globals.TryGetValue(name, out var v) && v is double d ? d : double.NaN;
        public string TBuilderErrors => _bWorld == null ? "" : string.Join(" | ", _bWorld.Errors);
        public List<string> TBuilderHudTexts() => _bHud.Values.Where(h => h.Text != null).Select(h => h.Text!.Text).ToList();
        public bool TBuilderDialogOpen => _bWorld?.Dialogue is { Closed: false };
        public bool TBuilderWarned(string what) => _bWarned.Any(x => x.Contains(what, StringComparison.OrdinalIgnoreCase));
        public int TPackageObjects => _bPairs.Count(e => e.BPkg && e.BMirror != null && !e.Dead);
        /// <summary>A variable of the first package mechanism on an object's twin (its settings are variables there), or null.</summary>
        public object? TPackageVar(string obj, string var, int index = 0)
        {
            var en = _entities.FirstOrDefault(e => e.Data.Name == obj && e.BPkg);
            return en?.BMirror is BE.GameObject go && index < go.Mechanisms.Count && go.Mechanisms[index].Vars.TryGetValue(var, out var v) ? v : null;
        }
    }
}
