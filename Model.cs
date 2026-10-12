using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace EasyEngine
{
    /// <summary>Shared parser for an AdvancedPatrol route stored as a JSON array of {x,y} points (editor + runtime).</summary>
    public static class PathPoints
    {
        public static List<(double x, double y)> Parse(string json)
        {
            var list = new List<(double x, double y)>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        double x = el.TryGetProperty("x", out var xp) ? xp.GetDouble() : 0;
                        double y = el.TryGetProperty("y", out var yp) ? yp.GetDouble() : 0;
                        list.Add((x, y));
                    }
            }
            catch { }
            return list;
        }
    }

    /// <summary>
    /// A scene with a Day/Night cycle gets a "Sky" layer at the very bottom holding its Sun and Moon
    /// (editor, AI and project load all go through here, so every game looks the same in the editor and in play).
    /// </summary>
    public static class SkySetup
    {
        /// <summary>Returns what it changed (null = nothing). createMissing=false only gathers the bodies that already exist.</summary>
        public static string? Ensure(Scene scene, bool createMissing = true)
        {
            var all = scene.Layers.SelectMany(l => l.Objects).ToList();
            var dnObj = all.FirstOrDefault(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.DayNight));
            if (dnObj == null) return null;
            var dn = dnObj.Behaviors.First(b => b.Kind == BehaviorKind.DayNight);
            bool topdown = dn.Num("mode", 0) >= 0.5; // Top-down: the ground covers the sky, so no sun/moon is created
            if (topdown) createMissing = false;

            GameObjectData? Find(string key, string defName)
            {
                string name = dn.Text(key, "").Trim();
                return all.FirstOrDefault(o => o != dnObj && o.Name.Equals(name.Length > 0 ? name : defName, StringComparison.OrdinalIgnoreCase));
            }
            var sun = Find("sun", "Sun");
            var moon = topdown ? null : Find("moon", "Moon");
            bool makeSun = sun == null && createMissing, makeMoon = moon == null && !topdown && createMissing;
            if (sun == null && moon == null && !makeSun && !makeMoon) return null;

            var notes = new List<string>();
            var sky = scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.Sky);
            if (sky == null)
            {
                sky = new Layer { Name = "Sky", Kind = LayerKind.Sky, ScreenSpace = true };
                scene.Layers.Insert(0, sky);
                notes.Add(L.T("added a Sky layer under all the others"));
            }
            else if (scene.Layers[0] != sky) { scene.Layers.Remove(sky); scene.Layers.Insert(0, sky); notes.Add(L.T("moved the Sky layer to the bottom")); }

            int nextId = all.Count == 0 ? 1 : all.Max(o => o.Id) + 1;
            void Place(GameObjectData? body, bool make, string key, string defName, double x, double size, string color)
            {
                if (body == null && make)
                {
                    string name = dn.Text(key, "").Trim();
                    if (name.Length == 0) name = defName;
                    body = new GameObjectData { Id = nextId++, Name = name, X = x, Y = 70, Width = size, Height = size, Color = color, CornerRadius = size / 2 };
                    sky.Objects.Add(body);
                    notes.Add(defName == "Sun" ? L.F("created the sun \"{0}\"", name) : L.F("created the moon \"{0}\"", name));
                }
                if (body == null) return;
                dn.Texts[key] = body.Name;
                if (sky.Objects.Contains(body)) return;
                scene.Layers.FirstOrDefault(l => l.Objects.Contains(body))?.Objects.Remove(body);
                sky.Objects.Add(body);
                notes.Add(L.F("moved \"{0}\" into the Sky layer", body.Name));
            }
            Place(sun, makeSun, "sun", "Sun", 120, 46, "#FFD24D");
            Place(moon, makeMoon, "moon", "Moon", 200, 38, "#D8E4FF");
            return notes.Count == 0 ? null : L.F("Day/Night: {0}", string.Join(", ", notes));
        }
    }

    // ======================= ENUMS =======================
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LayerKind { Background, Base, UI, Custom, Sky } // Sky = behind every layer, fixed to the screen (sun, moon, clouds)

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PhysicsMode { None, Static, Dynamic }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BehaviorKind
    {
        Solid,            // chão / plataforma (colisor estático)
        Platformer,       // personagem de plataforma (gravidade + pulo)
        TopDown,          // movimento top-down (4/8 direções)
        FollowCamera,     // câmera segue este objeto
        Projectile,       // move numa direção a uma velocidade
        Patrol,           // vai e volta entre dois pontos
        Chase,            // persegue um alvo
        Spin,             // gira sozinho (decorativo)
        Spawner,          // cria cópias de um objeto ao longo do tempo
        SoundEmitter,     // toca um som
        LightSource,      // emite luz 2D
        Parallax,         // fundo que desliza com a câmera
        DestroyOffscreen, // disappears when leaving the screen
        Collectible,      // collectible (disappears when the player touches it)
        GoToScene,        // switches to another scene (door / menu button)
        Shooter,          // fires projectiles when a key is pressed
        Custom,           // AI-authored behavior as a tiny rule script (when → do)
        Label,            // shows text (supports {variable} substitution) — great for HUD/score
        Health,           // has hit points; respawns at checkpoint on death
        Damager,          // hurts a target on touch (enemies, spikes, hazards)
        Checkpoint,       // sets the player's respawn point when touched
        Melee,            // LEGACY — converted to ToolUse when a project loads (kept so old project files still parse)
        Tilemap,          // a grid of tiles painted in the editor (ground/level geometry)
        Pushable,         // a solid block the player can push around (puzzles/sokoban)
        Switch,           // click/touch/interact to toggle a variable on/off (puzzles, levers)
        Gate,             // solid barrier that opens when a variable/item condition is met (metroidvania gate / locked door)
        Interact,         // press a key near it to fire an effect (talk to NPC, read sign, pull lever)
        AbilityPickup,    // on pickup, grants an ability flag (variable=1) and disappears (metroidvania)
        Area,             // a trigger zone: fires an effect when a target enters it
        Generator,        // procedurally spawns prefabs in a region (once = stable map, regen = recreates each visit)
        PathChase,        // chases a target while navigating around walls (grid pathfinding)
        Spring,           // bounce pad: launches whatever lands on it
        Minimap,          // HUD minimap showing the scene + markers
        Particles,        // particle emitter (explosions, sparkles, trails)
        UIButton,         // clickable UI button that runs an effect
        Bar,              // HUD bar that fills based on a variable (health/progress)
        AdvancedPatrol,   // follows a hand-drawn path with platformer physics (jumps over obstacles)
        DayNight,         // day/night cycle: animates ambient light, moves a sun/moon, sets isDay/isNight vars
        Hotbar,           // inventory hotbar: pick a slot with the wheel/number keys, shows the held tool in hand
        Shop,             // buy/craft: press a key nearby to pay a cost (coins or an item) and receive an item
        ToolUse,          // click anywhere to swing the held tool; it hits a Harvestable within reach
        Harvestable,      // a tree/rock/ore: takes N hits from the right tool, then falls and drops a resource
        Checklist,        // on-screen test checklist: each line ticks itself when its condition becomes true
        BossAI,           // a boss: phases by health, attack sequences (Advanced Scratch steps), death sequence
        Cutscene,         // a scripted scene: camera, dialogue, moves, fades — steps run one after another
        Terrain           // procedural terrain (seeded noise), streamed in chunks — can be endless
    }

    // ======================= DADOS =======================
    public class BehaviorData
    {
        public BehaviorKind Kind { get; set; }
        public Dictionary<string, double> Nums { get; set; } = new();
        public Dictionary<string, string> Texts { get; set; } = new();

        public double Num(string k, double def = 0) => Nums.TryGetValue(k, out var v) ? v : def;
        public string Text(string k, string def = "") => Texts.TryGetValue(k, out var v) ? v : def;
    }

    public class PhysicsData
    {
        public PhysicsMode Mode { get; set; } = PhysicsMode.None;
        public double GravityScale { get; set; } = 1.0;
        public double Bounciness { get; set; } = 0.0;
    }

    /// <summary>A named animation: an ordered set of frames (relative image paths).</summary>
    public class SpriteAnimation
    {
        public string Name { get; set; } = "idle";
        public List<string> Frames { get; set; } = new();
        public double Fps { get; set; } = 10;
        public bool Loop { get; set; } = true;
    }

    public class GameObjectData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "Object";
        public string Type { get; set; } = "Object"; // cosmetic (Player/Enemy/...)
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 64;
        public double Height { get; set; } = 64;
        public double Rotation { get; set; } = 0;
        public string Color { get; set; } = "#9A9AA0";
        public string? SpritePath { get; set; }   // optional single static image (relative path)
        public bool TileImage { get; set; } = false; // true = repeat the image, false = stretch it
        // --- appearance / aesthetics (per object) ---
        public double Opacity { get; set; } = 1.0;       // 0 = invisible, 1 = solid
        public string? BorderColor { get; set; }          // outline color (null = none)
        public double BorderWidth { get; set; } = 0;      // outline thickness in px
        public double CornerRadius { get; set; } = -1;    // -1 = auto (default rounding); >=0 = explicit
        public string? FontFamily { get; set; }           // for Label text (null = theme default)
        public bool Bold { get; set; } = false;           // bold Label text
        public List<SpriteAnimation> Animations { get; set; } = new();
        public string? DefaultAnimation { get; set; }
        public PhysicsData Physics { get; set; } = new();
        public List<BehaviorData> Behaviors { get; set; } = new();
        public Dictionary<string, double> Variables { get; set; } = new(); // per-object (each copy gets its own)
        // Yes/No variables keep their 1/0 in Variables (so every behavior and condition reads them); Flags names them.
        private HashSet<string> _flags = VarScope.NewFlags(null);
        public HashSet<string> Flags { get => _flags; set => _flags = VarScope.NewFlags(value); }
        private Dictionary<string, string> _texts = new();
        public Dictionary<string, string> Texts { get => _texts; set => _texts = value ?? new(); } // text variables
        public string Group { get; set; } = ""; // organization tag (groups objects in the scene list)

        public SpriteAnimation? ResolveDefaultAnimation()
        {
            if (Animations.Count == 0) return null;
            return Animations.Find(a => a.Name == DefaultAnimation) ?? Animations[0];
        }
    }

    public class Layer
    {
        public string Name { get; set; } = "Layer";
        public LayerKind Kind { get; set; } = LayerKind.Base;
        public bool Visible { get; set; } = true;
        public bool ScreenSpace { get; set; } = false; // fixed to the screen (ignores camera) — for UI/HUD
        public List<GameObjectData> Objects { get; set; } = new();
    }

    public class Scene
    {
        public string Name { get; set; } = "Scene 1";
        public double Width { get; set; } = 960;
        public double Height { get; set; } = 540;
        public string BackgroundColor { get; set; } = "#1A1A20";
        public double AmbientLight { get; set; } = 1.0; // 1 = no darkness, 0 = fully dark
        public Dictionary<string, double> Variables { get; set; } = new(); // scene-local (reset when the scene loads)
        // Yes/No variables keep their 1/0 in Variables (so every behavior and condition reads them); Flags names them.
        private HashSet<string> _flags = VarScope.NewFlags(null);
        public HashSet<string> Flags { get => _flags; set => _flags = VarScope.NewFlags(value); }
        private Dictionary<string, string> _texts = new();
        public Dictionary<string, string> Texts { get => _texts; set => _texts = value ?? new(); } // text variables
        public List<Layer> Layers { get; set; } = new();
    }

    /// <summary>A reusable object template shown in the palette (Player, Enemy, …).</summary>
    public class PrefabDef
    {
        public string Name { get; set; } = "Object";
        public string Icon { get; set; } = "▢";
        public string Category { get; set; } = "General"; // palette grouping for larger projects
        public GameObjectData Template { get; set; } = new();
    }

    /// <summary>Default prefab library that ships with every new project.</summary>
    public static class PrefabFactory
    {
        public static List<PrefabDef> Defaults()
        {
            PrefabDef P(string name, string icon, string category, Action<GameObjectData> cfg)
            {
                var t = new GameObjectData { Type = name, Name = name };
                cfg(t);
                return new PrefabDef { Name = name, Icon = icon, Category = category, Template = t };
            }
            return new List<PrefabDef>
            {
                P("Player", "🎮", "Characters", t => { t.Color = "#3BC97A"; t.Width = 48; t.Height = 64; t.Physics = new PhysicsData { Mode = PhysicsMode.Dynamic };
                    t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
                    t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.FollowCamera).CreateData());
                    t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Health).CreateData());
                    t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.ToolUse).CreateData()); }), // click to attack with a sword
                P("Enemy", "👾", "Characters", t => { t.Color = "#E5484B"; t.Width = 52; t.Height = 52;
                    t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Patrol).CreateData());
                    t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Damager).CreateData());
                    var hp = BehaviorCatalog.Info(BehaviorKind.Health).CreateData(); hp.Nums["respawn"] = 0; hp.Nums["max"] = 2; t.Behaviors.Add(hp); }),
                P("Bullet", "•", "Combat", t => { t.Color = "#F4C03B"; t.Width = 18; t.Height = 18; t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Projectile).CreateData()); }),
                P("Particle", "✦", "FX", t => { t.Color = "#4DC8E5"; t.Width = 16; t.Height = 16; t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Spin).CreateData()); }),
                P("Platform", "⬛", "Terrain", t => { t.Color = "#3A3A46"; t.Width = 220; t.Height = 32; t.Physics = new PhysicsData { Mode = PhysicsMode.Static }; t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Solid).CreateData()); }),
                P("Door", "🚪", "Terrain", t => { t.Color = "#8B6FD6"; t.Width = 48; t.Height = 72; t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.GoToScene).CreateData()); }),
                P("Tilemap", "▦", "Terrain", t => { t.Color = "#9A9AA0"; t.Width = 32; t.Height = 32; t.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Tilemap).CreateData()); }),
            };
        }
    }

    /// <summary>In-game look &amp; feel: fonts and the dialogue box styling (what the player sees, not the editor).</summary>
    public class GameTheme
    {
        public string FontFamily { get; set; } = "Segoe UI";   // default font for all in-game text
        public string DialogBg { get; set; } = "#111418";      // dialogue box background
        public string DialogBorder { get; set; } = "#3A3A46";  // dialogue box border
        public string DialogText { get; set; } = "#FFFFFF";    // dialogue text color
        public string DialogSpeaker { get; set; } = "#F4C03B"; // speaker name color
        public double DialogFontSize { get; set; } = 18;
        public double DialogCorner { get; set; } = 8;          // dialogue box corner radius
        public string DialogEffect { get; set; } = "typewriter"; // none | typewriter
        public double DialogSpeed { get; set; } = 45;          // typewriter speed (chars/sec)
    }

    public class Project
    {
        public string Name { get; set; } = "New Project";
        public string AiModel { get; set; } = "qwen2.5-coder:1.5b";
        public List<Scene> Scenes { get; set; } = new();
        public List<PrefabDef> Prefabs { get; set; } = new();
        public Dictionary<string, double> Variables { get; set; } = new(); // global (shared across the whole game)
        // Yes/No variables keep their 1/0 in Variables (so every behavior and condition reads them); Flags names them.
        private HashSet<string> _flags = VarScope.NewFlags(null);
        public HashSet<string> Flags { get => _flags; set => _flags = VarScope.NewFlags(value); }
        private Dictionary<string, string> _texts = new();
        public Dictionary<string, string> Texts { get => _texts; set => _texts = value ?? new(); } // text variables
        public GameTheme Theme { get; set; } = new();          // in-game look & feel
        public string? StartScene { get; set; } // scene the game starts on
        public bool ConsoleOnStart { get; set; } // open the F1 debug console when play starts (test scenarios)

        [JsonIgnore] public string? FilePath { get; set; } // .eeproj path on disk
        [JsonIgnore] public string? FolderPath { get; set; }

        private int _nextId = 1;
        [JsonIgnore]
        public int NextId
        {
            get
            {
                // guarantees a unique id even after loading
                foreach (var s in Scenes)
                    foreach (var l in s.Layers)
                        foreach (var o in l.Objects)
                            if (o.Id >= _nextId) _nextId = o.Id + 1;
                return _nextId++;
            }
        }

        public Scene GetStartScene() => Scenes.FirstOrDefault(s => s.Name == StartScene) ?? Scenes[0];
        public Scene? FindScene(string name) => Scenes.FirstOrDefault(s => s.Name == name);
    }

    public class RecentProject
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string LastOpened { get; set; } = "";
    }
}
