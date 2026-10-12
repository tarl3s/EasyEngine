using System;
using System.Collections.Generic;
using System.Linq;

namespace EasyEngine
{
    /// <summary>
    /// How the inspector edits a parameter. Everything is PICKED from a list/picker; typing is only for things that are
    /// really free (Text/LongText) — and new names are offered as "＋ New…" inside the list.
    /// </summary>
    public enum PickKind
    {
        Text, LongText,        // free text (a label, a dialogue line…)
        Choice,                // fixed options ("value|Label")
        Object, ObjectOrPrefab, Objects, Prefabs, Scene,
        Key, Color, Sound,
        Item, Items, ItemDamage, HotbarItems, Look,
        Variable, Currency, Requirement,
        Special                // drawn by its own editor (script, tilemap, path)
    }

    /// <summary>What a parameter's ShowIf can look at: the behavior, its object and the scene.</summary>
    public record ParamCtx(BehaviorData Bh, GameObjectData Obj, Scene Scene);

    public class NumParam
    {
        public string Key; public string Label; public double Default;
        public string[]? Options;               // "value|Label" — picked from a list instead of typed
        public Func<ParamCtx, bool>? ShowIf;    // hidden when it doesn't apply
        public NumParam(string k, string l, double d, params string[] options) { Key = k; Label = l; Default = d; Options = options.Length > 0 ? options : null; }
    }

    public class TextParam
    {
        public string Key; public string Label; public string Default;
        public PickKind Pick;
        public string[]? Options;               // for Choice: "value|Label"
        public bool AllowNone;                  // the list starts with "(none)" = ""
        public Func<ParamCtx, bool>? ShowIf;
        public TextParam(string k, string l, string d, PickKind pick = PickKind.Text, params string[] options)
        { Key = k; Label = l; Default = d; Pick = pick; Options = options.Length > 0 ? options : null; }
    }

    public class BehaviorInfo
    {
        public BehaviorKind Kind;
        public string Name = "";
        public string Icon = "•";
        public string Desc = "";
        public List<NumParam> Nums = new();
        public List<TextParam> Texts = new();
        public bool Hidden; // kept only so old projects still load; not offered in the editor or to the AI

        public BehaviorData CreateData()
        {
            var d = new BehaviorData { Kind = Kind };
            foreach (var n in Nums) d.Nums[n.Key] = n.Default;
            foreach (var t in Texts) d.Texts[t.Key] = t.Default;
            return d;
        }
    }

    /// <summary>Metadados de todos os comportamentos pré-feitos da engine.</summary>
    public static class BehaviorCatalog
    {
        // ---- ShowIf helpers ----
        private static Func<ParamCtx, bool> IfText(string key, params string[] values) =>
            c => values.Any(v => v.Equals(c.Bh.Text(key, ""), StringComparison.OrdinalIgnoreCase));
        private static Func<ParamCtx, bool> IfOn(string key) => c => c.Bh.Num(key, 0) >= 0.5;
        private static Func<ParamCtx, bool> IfOff(string key) => c => c.Bh.Num(key, 0) < 0.5;
        private static readonly Func<ParamCtx, bool> NoHotbar =
            c => !c.Scene.Layers.SelectMany(l => l.Objects).Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Hotbar));

        private static TextParam T(string k, string l, string d, PickKind p = PickKind.Text, params string[] o) => new(k, l, d, p, o);
        private static TextParam TNone(string k, string l, string d, PickKind p) => new(k, l, d, p) { AllowNone = true };
        private static TextParam TIf(TextParam t, Func<ParamCtx, bool> showIf) { t.ShowIf = showIf; return t; }
        private static NumParam NIf(NumParam n, Func<ParamCtx, bool> showIf) { n.ShowIf = showIf; return n; }

        private static readonly string[] Effects = { "say|Show a message", "setvar|Set a variable", "addvar|Add to a variable", "goscene|Go to a scene" };

        public static readonly List<BehaviorInfo> All = new()
        {
            new BehaviorInfo { Kind = BehaviorKind.Solid, Name = "Solid (ground/platform)", Icon = "⬛",
                Desc = "Static collider for ground, walls and platforms. oneWay=1 makes a jump-through platform (land on top, pass from below).",
                Nums = { new("oneWay","One-way (jump through from below) (0/1)",0) } },

            new BehaviorInfo { Kind = BehaviorKind.Platformer, Name = "Platformer Character", Icon = "🏃",
                Desc = "Tight platformer movement: run, jump (variable height), coyote time, jump buffer, optional double jump, wall jump and dash. 'controls' = respond to the default keys (arrows/A-D/Space). Turn it off for enemies so they don't copy the player — then set custom keys or move it with a script.",
                Nums = { new("controls","Default controls (arrows / A-D / Space) (0/1)",1),
                         new("speed","Speed",260), new("jump","Jump force",640),
                         new("doubleJump","Double jump (0/1)",0), new("wallJump","Wall jump (0/1)",0),
                         new("dash","Dash (0/1)",0), NIf(new("dashSpeed","Dash speed",560), IfOn("dash")),
                         new("collide","Collide with walls (0/1)",1) },
                Texts = { TIf(T("dashKey","Dash key","LeftShift", PickKind.Key), IfOn("dash")),
                          TNone("leftKey","Extra left key","", PickKind.Key), TNone("rightKey","Extra right key","", PickKind.Key), TNone("jumpKey","Extra jump key","", PickKind.Key) } },

            new BehaviorInfo { Kind = BehaviorKind.TopDown, Name = "Top-Down Movement", Icon = "✛",
                Desc = "Moves in 8 directions, no gravity. 'controls' = respond to the default keys (WASD/arrows). Turn it off for enemies (set custom keys or move it with a script).",
                Nums = { new("controls","Default controls (WASD / arrows) (0/1)",1), new("speed","Speed",220), new("collide","Collide with walls (0/1)",1) },
                Texts = { TNone("upKey","Extra up key","", PickKind.Key), TNone("downKey","Extra down key","", PickKind.Key),
                          TNone("leftKey","Extra left key","", PickKind.Key), TNone("rightKey","Extra right key","", PickKind.Key) } },

            new BehaviorInfo { Kind = BehaviorKind.FollowCamera, Name = "Follow Camera", Icon = "🎥",
                Desc = "The camera follows this object. 'clamp' keeps the view inside the scene bounds.",
                Nums = { new("smooth","Smoothing (0-1)",0.12), new("clamp","Stay inside the scene (0/1)",1) } },

            new BehaviorInfo { Kind = BehaviorKind.Projectile, Name = "Projectile", Icon = "➤",
                Desc = "Flies in a direction at a speed and disappears after a while.",
                Nums = { new("speed","Speed",420),
                         new("angle","Direction",0, "0|→ Right", "45|↘ Down-right", "90|↓ Down", "135|↙ Down-left", "180|← Left", "225|↖ Up-left", "270|↑ Up", "315|↗ Up-right"),
                         new("life","Lifetime (s)",3) } },

            new BehaviorInfo { Kind = BehaviorKind.Patrol, Name = "Patrol", Icon = "↔",
                Desc = "Moves back and forth automatically. Good for enemies. Patrols horizontally or vertically, and can ease to a smooth stop at the ends.",
                Nums = {
                    new("speed","Speed",90),
                    new("distance","Distance",200),
                    new("axis","Direction",0, "0|↔ Left and right", "1|↕ Up and down"),
                    new("smooth","Smooth stop at ends (0/1)",0),
                    NIf(new("slowSteps","Slow-down steps (0=none)",0), IfOn("smooth")),
                    new("pause","Pause at ends, sec (0=none)",0)
                } },

            new BehaviorInfo { Kind = BehaviorKind.AdvancedPatrol, Name = "Advanced Patrol", Icon = "✎",
                Desc = "Follows a path you draw with the mouse, using platformer physics — it walks the route, jumps up steps, and hops over obstacles in its way. Select it, click 'Draw path', then click points on the scene.",
                Nums = {
                    new("mode","Movement format",0), // 0=platform, 1=top-down, 2=free/fly
                    new("speed","Speed",130),
                    new("jump","Jump strength (platform)",640),
                    new("autoJump","Jump over obstacles (0/1)",1),
                    new("pathfind","Pathfinding — route around walls (top-down) (0/1)",0),
                    new("repeat","Repeat",0),
                    new("pause","Pause at the end, sec",0),
                    new("pauseStart","Pause at the start, sec",0),
                    new("collide","Collide with walls (0/1)",1)
                },
                Texts = { T("path","Path points (drawn)","", PickKind.Special) } },

            new BehaviorInfo { Kind = BehaviorKind.Chase, Name = "Chase", Icon = "🎯",
                Desc = "Chases another object by name.",
                Nums = { new("speed","Speed",140) },
                Texts = { T("target","Chases","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Spin, Name = "Spin", Icon = "↻",
                Desc = "Rotates on its own. Decorative.",
                Nums = { new("speed","Degrees per second",90) } },

            new BehaviorInfo { Kind = BehaviorKind.Spawner, Name = "Spawner", Icon = "✨",
                Desc = "Creates copies of an object. If 'key' is set, it spawns when that key is pressed; otherwise every 'interval' seconds. 'at'=top drops them from the top of the screen (random x). max=0 means unlimited. The spawned object can be a scene object or a prefab; give it physics Dynamic to make it fall.",
                Nums = { NIf(new("interval","Every (s)",1.5), c => c.Bh.Text("key", "").Length == 0), new("max","Max (0 = unlimited)",10) },
                Texts = { T("target","What to spawn","Enemy", PickKind.ObjectOrPrefab), TNone("key","Spawn when this key is pressed (none = on a timer)","", PickKind.Key),
                          T("at","Where","self", PickKind.Choice, "self|At this object", "top|Falling from the top of the screen") } },

            new BehaviorInfo { Kind = BehaviorKind.SoundEmitter, Name = "Sound", Icon = "🔊",
                Desc = "Plays a sound (on start and/or looping). channel (music/sfx) lets you control group volume with the setvolume action; final volume = volume × channel × master.",
                Nums = { new("volume","Volume (0-1)",0.8), new("onStart","Play on start (0/1)",1), new("loop","Loop (0/1)",0) },
                Texts = { T("asset","Sound file","", PickKind.Sound), T("channel","Channel","sfx", PickKind.Choice, "sfx|Sound effects", "music|Music") } },

            new BehaviorInfo { Kind = BehaviorKind.LightSource, Name = "Light", Icon = "💡",
                Desc = "Lights up the darkness around it (a soft hole in the night, plus a light tint). In full daylight it shows nothing — it matters when the scene is dark (low ambient light or night from a Day/Night cycle).",
                Nums = { new("radius","Radius",200), new("intensity","Intensity (0-1)",0.9) },
                Texts = { T("color","Color","#FFE9A8", PickKind.Color) } },

            new BehaviorInfo { Kind = BehaviorKind.Parallax, Name = "Parallax", Icon = "🏔",
                Desc = "Background that scrolls slower than the camera.",
                Nums = { new("factor","Depth (0 = far, 1 = near) (0-1)",0.5) } },

            new BehaviorInfo { Kind = BehaviorKind.DestroyOffscreen, Name = "Destroy Off-screen", Icon = "🗑",
                Desc = "Removes the object when it leaves the visible area.",
                Nums = { new("margin","Margin",120) } },

            new BehaviorInfo { Kind = BehaviorKind.Collectible, Name = "Collectible", Icon = "⭐",
                Desc = "Disappears when the Player touches it.",
                Texts = { T("collector","Picked up by","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.GoToScene, Name = "Go To Scene", Icon = "🚪",
                Desc = "Switches to another scene when the trigger touches it or it's clicked. Great for doors and menu buttons.",
                Texts = { T("scene","Target scene","", PickKind.Scene), T("trigger","Who uses it","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Shooter, Name = "Shooter", Icon = "🔫",
                Desc = "Fires projectiles when a key is pressed. 'directions' = how many bullets spread around (8 = all directions, 1 = forward).",
                Nums = { new("directions","Bullets per shot",8, "1|1 — forward", "4|4 — a cross", "8|8 — all around"), new("speed","Bullet speed",420), new("cooldown","Cooldown (s)",0.3), new("life","Bullet life (s)",2) },
                Texts = { T("key","Fire key","T", PickKind.Key), T("bullet","Bullet","Bullet", PickKind.ObjectOrPrefab) } },

            new BehaviorInfo { Kind = BehaviorKind.Custom, Name = "Custom (AI script)", Icon = "✨",
                Desc = "A custom behavior written as a tiny rule script (when → do). Usually created by the AI when the request is too complex for the other behaviors.",
                Texts = { T("script","Rules (JSON)","[]", PickKind.Special) } },

            new BehaviorInfo { Kind = BehaviorKind.Label, Name = "Label / HUD text", Icon = "🔤",
                Desc = "Shows text on the object. Use {variableName} to show a variable, e.g. \"Score: {score}\". Put it on a UI layer to keep it fixed on screen. effect animates the text: typewriter (types out), fade (fades in/out), blink, or pulse.",
                Nums = { new("fontSize","Font size",20), NIf(new("effectSpeed","Effect speed",0), c => c.Bh.Text("effect", "none") != "none") },
                Texts = { T("text","Text — {variable} shows a value","Score: {score}", PickKind.LongText), T("color","Color","#FFFFFF", PickKind.Color),
                          T("effect","Effect","none", PickKind.Choice, "none|None", "typewriter|Typewriter (types out)", "fade|Fade in/out", "blink|Blink", "pulse|Pulse"),
                          T("align","Alignment","center", PickKind.Choice, "left|Left", "center|Center", "right|Right") } },

            new BehaviorInfo { Kind = BehaviorKind.Health, Name = "Health", Icon = "❤",
                Desc = "Gives hit points. When they reach 0: respawn=1 → respawn at last checkpoint/start (for the player); respawn=0 → the object is destroyed (for enemies). Writes the value to a variable for a Label.",
                Nums = { new("max","Max health",3), new("invincible","I-frames (s)",1), new("respawn","When it dies",1, "1|Respawn (for the player)", "0|Disappear (for enemies)") },
                Texts = { T("var","Show the health in variable","health", PickKind.Variable) } },

            // Legacy: hidden from the editor/AI. Projects that still have it are converted to Tool Use when they load.
            new BehaviorInfo { Kind = BehaviorKind.Melee, Name = "Melee Attack (old — now Tool Use)", Icon = "⚔", Hidden = true,
                Desc = "Replaced by Tool Use: every item (sword, axe, pickaxe…) is used by clicking. Old projects are converted automatically.",
                Nums = { new("damage","Damage",1), new("range","Reach",40), new("knockback","Knockback",260), new("cooldown","Cooldown (s)",0.35) },
                Texts = { T("key","Key","J", PickKind.Key), TNone("requires","Needs this to swing","", PickKind.Requirement) } },

            new BehaviorInfo { Kind = BehaviorKind.Damager, Name = "Damager / Hazard", Icon = "💢",
                Desc = "Hurts a target that touches it (enemy contact, spikes, hazards), with knockback and i-frames.",
                Nums = { new("damage","Damage",1), new("knockback","Knockback",320) },
                Texts = { T("target","Hurts","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Checkpoint, Name = "Checkpoint", Icon = "🚩",
                Desc = "When the target touches it, it becomes the target's respawn point.",
                Texts = { T("target","Saves the spot for","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Tilemap, Name = "Tilemap", Icon = "▦",
                Desc = "A grid of tiles you paint in the editor (ground, walls, level geometry). Solid tiles become collision automatically. Add it to an empty object, then paint with the Tiles panel. Turn on 'Breakable' to let individual tiles be destroyed (by bullets, melee, damagers, stomping, touching, or the 'Break a tile' block).",
                Nums = { new("tileSize","Tile size",32),
                         new("breakable","Breakable (0/1)",0),
                         new("breakHits","Hits to break each tile",1),
                         new("byBullet","Break with bullets (0/1)",1),
                         new("byMelee","Break with item clicks (Tool Use) (0/1)",1),
                         new("byDamage","Break with damagers/hazards (0/1)",1),
                         new("byStomp","Break by landing on top (0/1)",0),
                         new("byTouch","Break by touching (0/1)",0),
                         new("breakParticles","Puff of particles when broken (0/1)",1),
                         new("dropAmount","How many resources each tile drops",1) },
                Texts = { T("palette","Palette (JSON)","[]", PickKind.Special), T("data","Tiles (JSON)","", PickKind.Special),
                          TNone("requires","Only breaks when","", PickKind.Requirement),
                          TNone("drop","Drops","", PickKind.Item) } },

            new BehaviorInfo { Kind = BehaviorKind.DayNight, Name = "Day / Night cycle", Icon = "🌗",
                Desc = "A day/night cycle. Add it to one object (a controller, can be invisible). It dims the scene at night and brightens it by day, and sets the variables isDay/isNight/timeOfDay you can read in the AI or in blocks (e.g. spawn certain enemies only at night). In Platformer mode, adding it creates a 'Sky' layer under all the others with a Sun and a Moon already linked (Top-down has no sky — just the light changes) — give them your own sprites there. The sky is drawn behind everything (tilemaps, trees, player), fixed to the screen; the sun and moon cross it and leave the screen completely before day/night changes. Put clouds or stars in the Sky layer too. Works for Top-down and Platformer.",
                Nums = { new("mode","Game view",0, "0|Platformer (side view, sun + moon)", "1|Top-down (seen from above)"),
                         new("dayLength","Day length (seconds)",60),
                         new("nightLength","Night length (seconds)",60),
                         new("transition","Dawn/dusk fade (seconds)",4),
                         new("dayLight","Brightness by day (0-1)",1),
                         new("nightLight","Brightness at night (0-1)",0.25),
                         new("startAtNight","Start at night (0/1)",0),
                         NIf(new("arcHeight","Sun/Moon arc height (px)",120), IfOff("mode")) },
                Texts = { TIf(TNone("sun","Sun","", PickKind.Object), IfOff("mode")), TIf(TNone("moon","Moon","", PickKind.Object), IfOff("mode")),
                          T("nightColor","Night tint","#0A1030", PickKind.Color) } },

            new BehaviorInfo { Kind = BehaviorKind.Hotbar, Name = "Hotbar / Inventory", Icon = "🎒",
                Desc = "A hotbar. Scroll the mouse wheel (or press 1-9) to pick a slot. List the slots in 'items' as name:#color, comma-separated (e.g. pickaxe:#8B5A2B,axe:#B0B0B0,sword:#AEE1FF). ANY name is an item — make your own (shovel, hammer, magic wand…) with the '＋ New item' button, or add ':look' to a slot: a built-in drawing (pickaxe, axe, sword, shovel, hammer, hoe, tile) or a picture file (e.g. wand:#FFFFFF:Assets/sprites/wand.png). A Harvestable whose 'tool' is that name only accepts it. You OWN a slot once you have item:<name> (give it with 'Give an item' / buying). The selected owned slot sets holding:<name>=1 — use that as a tilemap's 'requires' so only the right tool mines it. The held tool is drawn in the player's hand.",
                Nums = { new("slot","Starting slot",0), new("size","Slot size (px)",46) },
                Texts = { T("items","Items","pickaxe:#8B5A2B,axe:#B0B0B0,sword:#AEE1FF", PickKind.HotbarItems),
                          T("player","Shown in the hand of","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.ToolUse, Name = "Use Item (swing / attack)", Icon = "🗡",
                Desc = "Put it on the player. Click ANYWHERE in the game to use the item in hand (from the Hotbar, or the fixed 'tool'): the arm swings, the swing sound plays and, a moment later, it hits what is in reach — an enemy (anything with Health, takes damage + knockback), a tree/rock (Harvestable that accepts the item) or a breakable tile under the cursor. Every item works the same way: sword, axe, pickaxe, or your own (shovel…). Far away = a miss; wrong tool = refused; something solid in between (wall, water, a block) = blocked, the swing only hits what is in front. 'itemDamage' = damage per item (e.g. sword=2,axe=1), 'tools' = only these items swing (comma list, empty = all), 'toolLook' = look of the fixed tool. Everything shows in the F1 console.",
                Nums = { new("reach","Reach (px from the player)",80), new("cooldown","Time between swings (s)",0.35), new("swingTime","Swing duration (s)",0.25),
                         new("damage","Damage to enemies (default)",1), new("knockback","Knockback",260), new("blockedByWalls","Walls / water in between block the swing (0/1)",1) },
                Texts = { TIf(T("tool","Item in hand (no Hotbar in this scene)","sword", PickKind.Item), NoHotbar),
                          TIf(T("toolLook","How it looks","", PickKind.Look), NoHotbar),
                          T("itemDamage","Damage per item","sword=2", PickKind.ItemDamage),
                          T("tools","Items that swing","", PickKind.Items),
                          T("swingSound","Swing sound","", PickKind.Sound), T("hitSound","Hit-an-enemy sound","", PickKind.Sound), T("deniedSound","Refused sound","", PickKind.Sound) } },

            new BehaviorInfo { Kind = BehaviorKind.Harvestable, Name = "Harvestable (tree / rock)", Icon = "🌳",
                Desc = "Something you chop or mine with a tool. It takes 'hits' hits from the right tool (e.g. axe), shaking each time; on the last hit it falls and gives 'amount' of 'drop' (e.g. 5 wood). Needs a player with Tool Use.",
                Nums = { new("hits","Hits to fell",4), new("amount","Resources dropped",5), new("shake","Shake when hit (0/1)",1), new("fall","Topple when felled (0/1)",1) },
                Texts = { new("tool","Tool needed","axe", PickKind.Item) { AllowNone = true }, TNone("drop","Drops","wood", PickKind.Item),
                          T("hitSound","Hit sound","", PickKind.Sound), T("fellSound","Felled sound","", PickKind.Sound) } },

            new BehaviorInfo { Kind = BehaviorKind.Checklist, Name = "Test Checklist", Icon = "☑",
                Desc = "An on-screen checklist for testing. Each line is 'Text => condition' (one per line or separated by ';'). A line ticks ✓ by itself when its condition becomes true (e.g. holding:axe >= 1, stat:hits >= 1, item:wood >= 5) and stays ticked. Logs to the F1 console and sets check:done / check:total / check:all.",
                Nums = { new("width","Panel width (px)",300) },
                Texts = { T("title","Title","Test checklist"), T("items","Lines (Text => condition)","", PickKind.LongText),
                          T("corner","Corner","tr", PickKind.Choice, "tr|Top right", "tl|Top left") } },

            new BehaviorInfo { Kind = BehaviorKind.Shop, Name = "Shop / Crafting bench", Icon = "🛒",
                Desc = "Stand near it and press the key to pay a cost and receive an item. Set 'currency' to 'coins' for a shop, or to an item like 'item:wood' for a crafting bench (e.g. craft a sword from wood). Buys once by default.",
                Nums = { new("cost","Cost",5), new("give","Amount given",1), new("radius","How close (px)",70), new("once","Buy only once (0/1)",1) },
                Texts = { T("item","Sells","pickaxe", PickKind.Item), T("currency","Paid with","coins", PickKind.Currency),
                          T("key","Buy key","E", PickKind.Key), T("player","Buyer","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Pushable, Name = "Pushable Block", Icon = "📦",
                Desc = "A solid block the player (or 'pusher') can push around. Great for sokoban-style puzzles and block-and-switch puzzles. Give it physics Dynamic if it should also fall.",
                Nums = { new("weight","Weight (1=easy, higher=slower)",1) },
                Texts = { T("pusher","Who can push","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Switch, Name = "Switch / Lever", Icon = "🎚",
                Desc = "Toggles a variable between 0 and 1. mode=click (click it), touch (trigger touches it), or interact (trigger stands on it and presses the key). stayOn=1 makes it a one-time switch that can't turn back off.",
                Nums = { new("stayOn","Stay on once flipped (0/1)",0) },
                Texts = { T("var","Variable it turns on/off","switch1", PickKind.Variable),
                          T("mode","How it's flipped","click", PickKind.Choice, "click|Click it", "touch|Touch it", "interact|Stand on it + press a key"),
                          TIf(T("trigger","Who flips it","Player", PickKind.Object), IfText("mode", "touch", "interact")),
                          TIf(T("key","Key","E", PickKind.Key), IfText("mode", "interact")) } },

            new BehaviorInfo { Kind = BehaviorKind.Gate, Name = "Gate / Locked Door", Icon = "🚧",
                Desc = "A solid barrier that blocks the way until it opens. It opens when its variable meets the condition (e.g. var 'hasRedKey' >= 1), or needs an item. When open it stops colliding and fades out. Perfect for metroidvania ability-gates and locked doors.",
                Nums = { new("value","…this value",1) },
                Texts = { T("var","Opens when variable","hasKey", PickKind.Variable),
                          T("op","is",">=", PickKind.Choice, ">=|at least", "==|exactly", ">|more than", "<=|at most", "<|less than"),
                          TNone("item","…or when the player has","", PickKind.Item) } },

            new BehaviorInfo { Kind = BehaviorKind.Interact, Name = "Interact (press key)", Icon = "💬",
                Desc = "When the trigger is near and presses the key, fires an effect: say (show dialogue text, lines split by |), setvar, addvar or goscene. Use for NPCs, signs, chests, levers.",
                Nums = { new("radius","How near (px)",60), NIf(new("amount","Add",1), IfText("action", "addvar")), NIf(new("to","Set to",1), IfText("action", "setvar")), new("once","Only once (0/1)",0) },
                Texts = { T("action","What happens","say", PickKind.Choice, Effects),
                          TIf(T("text","Message (| = next line)","Hello!", PickKind.LongText), IfText("action", "say")), TIf(T("speaker","Who is talking",""), IfText("action", "say")),
                          TIf(T("var","Variable","", PickKind.Variable), IfText("action", "setvar", "addvar")),
                          TIf(T("scene","Scene","", PickKind.Scene), IfText("action", "goscene")),
                          T("key","Key","E", PickKind.Key), T("trigger","Who can use it","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.AbilityPickup, Name = "Ability / Key Pickup", Icon = "🗝",
                Desc = "When the collector touches it, sets a variable to 1 (an ability unlocked, a key obtained) and disappears. Pair it with a Door that opens on that variable, or a Platformer ability. Great for metroidvania progression.",
                Texts = { T("var","Turns on variable","hasDoubleJump", PickKind.Variable), T("collector","Picked up by","Player", PickKind.Object),
                          T("grant","Also unlocks","none", PickKind.Choice, "none|Nothing else", "doubleJump|Double jump", "dash|Dash", "wallJump|Wall jump") } },

            new BehaviorInfo { Kind = BehaviorKind.Area, Name = "Trigger Area / Zone", Icon = "🟦",
                Desc = "An invisible zone. When the target enters it, fires an effect: goscene, setvar, addvar or say. once=1 fires a single time. Use for level exits, cutscene triggers, hidden-room reveals, kill-zones.",
                Nums = { NIf(new("amount","Add",1), IfText("action", "addvar")), NIf(new("to","Set to",1), IfText("action", "setvar")), new("once","Only once (0/1)",1) },
                Texts = { T("action","What happens","goscene", PickKind.Choice, Effects),
                          TIf(T("scene","Scene","", PickKind.Scene), IfText("action", "goscene")),
                          TIf(T("var","Variable","", PickKind.Variable), IfText("action", "setvar", "addvar")),
                          TIf(T("text","Message","", PickKind.LongText), IfText("action", "say")),
                          T("target","Who enters","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Generator, Name = "Procedural Generator", Icon = "🎲",
                Desc = "Procedurally spawns copies of prefab(s) inside this object's rectangle (or the whole scene if it has no size) when the scene loads. 'prefab' is one or more names separated by commas (picked at random). mode=once builds a stable map (same every time you come back); mode=regen recreates it fresh on every visit (roguelike). layout=scatter (random), grid, row or column. minGap keeps scattered items apart.",
                Nums = { new("count","How many to spawn",10), NIf(new("minGap","Min gap between items",0), IfText("layout", "scatter")) },
                Texts = { T("prefab","What to spawn (random mix)","Platform", PickKind.Prefabs),
                          T("mode","Map","once", PickKind.Choice, "once|The same every time (built once)", "regen|New every visit (roguelike)"),
                          T("layout","Layout","scatter", PickKind.Choice, "scatter|Scattered at random", "grid|Grid", "row|A row", "column|A column"),
                          T("id","Unique id (keeps 'once' maps stable)","gen1") } },

            new BehaviorInfo { Kind = BehaviorKind.PathChase, Name = "Smart Chase (pathfinding)", Icon = "🧭",
                Desc = "Chases a target but navigates AROUND walls/obstacles using grid pathfinding (unlike plain Chase, which goes in a straight line). Best for top-down enemies. 'cell' is the grid resolution.",
                Nums = { new("speed","Speed",140), new("cell","Grid cell size",32), new("recalc","Recalc interval (s)",0.3) },
                Texts = { T("target","Chases","Player", PickKind.Object) } },

            new BehaviorInfo { Kind = BehaviorKind.Spring, Name = "Spring / Bounce Pad", Icon = "🔼",
                Desc = "Launches anything dynamic that lands on it (trampoline, jump pad). 'force' is the launch power; dir = up/down/left/right.",
                Nums = { new("force","Launch force",820) },
                Texts = { T("dir","Launches","up", PickKind.Choice, "up|↑ Up", "down|↓ Down", "left|← Left", "right|→ Right") } },

            new BehaviorInfo { Kind = BehaviorKind.Minimap, Name = "Minimap (HUD)", Icon = "🗺",
                Desc = "Draws a small map in a corner showing the whole scene, with a dot for the player and optional dots for other objects. Put it on a UI layer. 'markers' = comma-separated object names to show (empty = just the player).",
                Nums = { new("size","Map width (px)",180), new("opacity","Opacity (0-1)",0.8) },
                Texts = { T("corner","Corner","tr", PickKind.Choice, "tl|Top left", "tr|Top right", "bl|Bottom left", "br|Bottom right"),
                          T("player","Player","Player", PickKind.Object), T("markers","Also show","", PickKind.Objects) } },

            new BehaviorInfo { Kind = BehaviorKind.Particles, Name = "Particle Emitter", Icon = "✨",
                Desc = "Emits particles (sparks, smoke, explosions, trails). on=start bursts once when the scene loads; on=always emits continuously at 'rate'; or trigger a burst from a script with the 'emit' action. spread is the cone angle (360 = all directions).",
                Nums = { NIf(new("rate","Per second",0), IfText("on", "always")), new("burst","Burst count",16), new("life","Particle life (s)",0.8), new("speed","Speed",180), new("spread","Spread angle (deg)",360), new("angle","Base angle (deg)",0), new("size","Particle size",8), new("gravity","Gravity",400) },
                Texts = { T("color","Color","#FFD24D", PickKind.Color), T("on","When","start", PickKind.Choice, "start|One burst when the scene starts", "always|Non-stop") } },

            new BehaviorInfo { Kind = BehaviorKind.UIButton, Name = "UI Button", Icon = "🔘",
                Desc = "A clickable button. On click it runs an effect: goscene (change scene), setvar, addvar, say, save or load. Put it on a UI layer; add a Label for its text. Great for menus and HUD.",
                Nums = { NIf(new("amount","Add",1), IfText("action", "addvar")), NIf(new("to","Set to",1), IfText("action", "setvar")) },
                Texts = { T("action","On click","goscene", PickKind.Choice, Effects.Concat(new[] { "save|Save the game", "load|Load the game" }).ToArray()),
                          TIf(T("scene","Scene","", PickKind.Scene), IfText("action", "goscene")),
                          TIf(T("var","Variable","", PickKind.Variable), IfText("action", "setvar", "addvar")),
                          TIf(T("text","Message","", PickKind.LongText), IfText("action", "say")) } },

            new BehaviorInfo { Kind = BehaviorKind.Bar, Name = "Bar (health/progress)", Icon = "▭",
                Desc = "A bar that fills based on a variable, e.g. health. Fill = var / max. Put it on a UI layer for a HUD, or above an enemy for a health bar. 'max' can be a number or another variable name.",
                Nums = { NIf(new("max","Full value",3), c => c.Bh.Text("maxVar", "").Length == 0) },
                Texts = { T("var","Shows variable","health", PickKind.Variable), TNone("maxVar","Full value from a variable","", PickKind.Variable),
                          T("color","Fill color","#3BC97A", PickKind.Color), T("bg","Background color","#2A2A33", PickKind.Color) } },
        };

        public static BehaviorInfo Info(BehaviorKind kind) => All.Find(b => b.Kind == kind)!;
    }
}
