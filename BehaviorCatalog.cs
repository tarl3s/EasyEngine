using System.Collections.Generic;

namespace EasyEngine
{
    public class NumParam
    {
        public string Key; public string Label; public double Default;
        public NumParam(string k, string l, double d) { Key = k; Label = l; Default = d; }
    }

    public class TextParam
    {
        public string Key; public string Label; public string Default;
        public TextParam(string k, string l, string d) { Key = k; Label = l; Default = d; }
    }

    public class BehaviorInfo
    {
        public BehaviorKind Kind;
        public string Name = "";
        public string Icon = "•";
        public string Desc = "";
        public List<NumParam> Nums = new();
        public List<TextParam> Texts = new();

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
        public static readonly List<BehaviorInfo> All = new()
        {
            new BehaviorInfo { Kind = BehaviorKind.Solid, Name = "Solid (ground/platform)", Icon = "⬛",
                Desc = "Static collider for ground, walls and platforms. oneWay=1 makes a jump-through platform (land on top, pass from below).",
                Nums = { new("oneWay","One-way (0/1)",0) } },

            new BehaviorInfo { Kind = BehaviorKind.Platformer, Name = "Platformer Character", Icon = "🏃",
                Desc = "Tight platformer movement: run, jump (variable height), coyote time, jump buffer, optional double jump, wall jump and dash. 'controls' = respond to the default keys (arrows/A-D/Space). Turn it off for enemies so they don't copy the player — then set custom keys or move it with a script.",
                Nums = { new("controls","Default controls (0/1)",1),
                         new("speed","Speed",260), new("jump","Jump force",640),
                         new("doubleJump","Double jump (0/1)",0), new("wallJump","Wall jump (0/1)",0),
                         new("dash","Dash (0/1)",0), new("dashSpeed","Dash speed",560) },
                Texts = { new("dashKey","Dash key","LeftShift"), new("leftKey","Left key",""), new("rightKey","Right key",""), new("jumpKey","Jump key","") } },

            new BehaviorInfo { Kind = BehaviorKind.TopDown, Name = "Top-Down Movement", Icon = "✛",
                Desc = "Moves in 8 directions, no gravity. 'controls' = respond to the default keys (WASD/arrows). Turn it off for enemies (set custom keys or move it with a script).",
                Nums = { new("controls","Default controls (0/1)",1), new("speed","Speed",220) },
                Texts = { new("upKey","Up key",""), new("downKey","Down key",""), new("leftKey","Left key",""), new("rightKey","Right key","") } },

            new BehaviorInfo { Kind = BehaviorKind.FollowCamera, Name = "Follow Camera", Icon = "🎥",
                Desc = "The camera follows this object. 'clamp' keeps the view inside the scene bounds.",
                Nums = { new("smooth","Smoothing (0-1)",0.12), new("clamp","Clamp to scene (0/1)",1) } },

            new BehaviorInfo { Kind = BehaviorKind.Projectile, Name = "Projectile", Icon = "➤",
                Desc = "Flies in a direction at a speed and disappears after a while.",
                Nums = { new("speed","Speed",420), new("angle","Angle (degrees)",0), new("life","Lifetime (s)",3) } },

            new BehaviorInfo { Kind = BehaviorKind.Patrol, Name = "Patrol", Icon = "↔",
                Desc = "Moves back and forth automatically. Good for enemies.",
                Nums = { new("speed","Speed",90), new("distance","Distance",200), new("axis","Axis (0=X,1=Y)",0) } },

            new BehaviorInfo { Kind = BehaviorKind.Chase, Name = "Chase", Icon = "🎯",
                Desc = "Chases another object by name.",
                Nums = { new("speed","Speed",140) },
                Texts = { new("target","Target (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.Spin, Name = "Spin", Icon = "↻",
                Desc = "Rotates on its own. Decorative.",
                Nums = { new("speed","Degrees per second",90) } },

            new BehaviorInfo { Kind = BehaviorKind.Spawner, Name = "Spawner", Icon = "✨",
                Desc = "Creates copies of an object. If 'key' is set, it spawns when that key is pressed; otherwise every 'interval' seconds. 'at'=top drops them from the top of the screen (random x). max=0 means unlimited. The spawned object can be a scene object or a prefab; give it physics Dynamic to make it fall.",
                Nums = { new("interval","Interval (s)",1.5), new("max","Max (0 = unlimited)",10) },
                Texts = { new("target","Object/prefab to spawn (name)","Enemy"), new("key","Key (optional)",""), new("at","Position (self/top)","self") } },

            new BehaviorInfo { Kind = BehaviorKind.SoundEmitter, Name = "Sound", Icon = "🔊",
                Desc = "Plays a sound (on start and/or looping). channel (music/sfx) lets you control group volume with the setvolume action; final volume = volume × channel × master.",
                Nums = { new("volume","Volume (0-1)",0.8), new("onStart","Play on start (0/1)",1), new("loop","Loop (0/1)",0) },
                Texts = { new("asset","Sound file",""), new("channel","Channel (music/sfx)","sfx") } },

            new BehaviorInfo { Kind = BehaviorKind.LightSource, Name = "Light", Icon = "💡",
                Desc = "Emits a 2D glow around it. Depends on the scene's ambient light.",
                Nums = { new("radius","Radius",200), new("intensity","Intensity (0-1)",0.9) },
                Texts = { new("color","Color (hex)","#FFE9A8") } },

            new BehaviorInfo { Kind = BehaviorKind.Parallax, Name = "Parallax", Icon = "🏔",
                Desc = "Background that scrolls slower than the camera.",
                Nums = { new("factor","Factor (0-1)",0.5) } },

            new BehaviorInfo { Kind = BehaviorKind.DestroyOffscreen, Name = "Destroy Off-screen", Icon = "🗑",
                Desc = "Removes the object when it leaves the visible area.",
                Nums = { new("margin","Margin",120) } },

            new BehaviorInfo { Kind = BehaviorKind.Collectible, Name = "Collectible", Icon = "⭐",
                Desc = "Disappears when the Player touches it.",
                Texts = { new("collector","Collector (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.GoToScene, Name = "Go To Scene", Icon = "🚪",
                Desc = "Switches to another scene when the trigger touches it or it's clicked. Great for doors and menu buttons.",
                Texts = { new("scene","Target scene",""), new("trigger","Trigger (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.Shooter, Name = "Shooter", Icon = "🔫",
                Desc = "Fires projectiles when a key is pressed. 'directions' = how many bullets spread around (8 = all directions, 1 = forward).",
                Nums = { new("directions","Directions (1/4/8)",8), new("speed","Bullet speed",420), new("cooldown","Cooldown (s)",0.3), new("life","Bullet life (s)",2) },
                Texts = { new("key","Key","T"), new("bullet","Bullet prefab (name)","Bullet") } },

            new BehaviorInfo { Kind = BehaviorKind.Custom, Name = "Custom (AI script)", Icon = "✨",
                Desc = "A custom behavior written as a tiny rule script (when → do). Usually created by the AI when the request is too complex for the other behaviors.",
                Texts = { new("script","Rules (JSON)","[]") } },

            new BehaviorInfo { Kind = BehaviorKind.Label, Name = "Label / HUD text", Icon = "🔤",
                Desc = "Shows text on the object. Use {variableName} to show a variable, e.g. \"Score: {score}\". Put it on a UI layer to keep it fixed on screen. effect animates the text: typewriter (types out), fade (fades in/out), blink, or pulse.",
                Nums = { new("fontSize","Font size",20), new("effectSpeed","Effect speed",0) },
                Texts = { new("text","Text","Score: {score}"), new("color","Color (hex)","#FFFFFF"), new("effect","Effect (none/typewriter/fade/blink/pulse)","none") } },

            new BehaviorInfo { Kind = BehaviorKind.Health, Name = "Health", Icon = "❤",
                Desc = "Gives hit points. When they reach 0: respawn=1 → respawn at last checkpoint/start (for the player); respawn=0 → the object is destroyed (for enemies). Writes the value to a variable for a Label.",
                Nums = { new("max","Max health",3), new("invincible","I-frames (s)",1), new("respawn","Respawn on death (0/1)",1) },
                Texts = { new("var","Variable name","health") } },

            new BehaviorInfo { Kind = BehaviorKind.Melee, Name = "Melee Attack", Icon = "⚔",
                Desc = "Press a key to swing a melee hitbox in the facing direction, hurting enemies that have Health.",
                Nums = { new("damage","Damage",1), new("range","Reach",40), new("knockback","Knockback",260), new("cooldown","Cooldown (s)",0.35) },
                Texts = { new("key","Key","J") } },

            new BehaviorInfo { Kind = BehaviorKind.Damager, Name = "Damager / Hazard", Icon = "💢",
                Desc = "Hurts a target that touches it (enemy contact, spikes, hazards), with knockback and i-frames.",
                Nums = { new("damage","Damage",1), new("knockback","Knockback",320) },
                Texts = { new("target","Target (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.Checkpoint, Name = "Checkpoint", Icon = "🚩",
                Desc = "When the target touches it, it becomes the target's respawn point.",
                Texts = { new("target","Target (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.Tilemap, Name = "Tilemap", Icon = "▦",
                Desc = "A grid of tiles you paint in the editor (ground, walls, level geometry). Solid tiles become collision automatically. Add it to an empty object, then paint with the Tiles panel.",
                Nums = { new("tileSize","Tile size",32) },
                Texts = { new("palette","Palette (JSON)","[]"), new("data","Tiles (JSON)","") } },

            new BehaviorInfo { Kind = BehaviorKind.Pushable, Name = "Pushable Block", Icon = "📦",
                Desc = "A solid block the player (or 'pusher') can push around. Great for sokoban-style puzzles and block-and-switch puzzles. Give it physics Dynamic if it should also fall.",
                Nums = { new("weight","Weight (1=easy, higher=slower)",1) },
                Texts = { new("pusher","Who can push (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.Switch, Name = "Switch / Lever", Icon = "🎚",
                Desc = "Toggles a variable between 0 and 1. mode=click (click it), touch (trigger touches it), or interact (trigger stands on it and presses the key). stayOn=1 makes it a one-time switch that can't turn back off.",
                Nums = { new("stayOn","Stay on once flipped (0/1)",0) },
                Texts = { new("var","Variable to toggle","switch1"), new("mode","Mode (click/touch/interact)","click"), new("trigger","Trigger (name)","Player"), new("key","Interact key","E") } },

            new BehaviorInfo { Kind = BehaviorKind.Gate, Name = "Gate / Locked Door", Icon = "🚧",
                Desc = "A solid barrier that blocks the way until it opens. It opens when its variable meets the condition (e.g. var 'hasRedKey' >= 1), or needs an item. When open it stops colliding and fades out. Perfect for metroidvania ability-gates and locked doors.",
                Nums = { new("value","Opens when var is >= this",1) },
                Texts = { new("var","Variable that opens it","hasKey"), new("op","Compare (>=,==,>, <=, <)",">="), new("item","...or require this item","") } },

            new BehaviorInfo { Kind = BehaviorKind.Interact, Name = "Interact (press key)", Icon = "💬",
                Desc = "When the trigger is near and presses the key, fires an effect: say (show dialogue text, lines split by |), setvar, addvar or goscene. Use for NPCs, signs, chests, levers.",
                Nums = { new("radius","How near (px)",60), new("amount","addvar amount",1), new("to","setvar value",1), new("once","Only once (0/1)",0) },
                Texts = { new("key","Key","E"), new("action","Effect (say/setvar/addvar/goscene)","say"), new("text","Say text","Hello!"), new("speaker","Speaker",""), new("var","Variable",""), new("scene","Scene",""), new("trigger","Trigger (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.AbilityPickup, Name = "Ability / Key Pickup", Icon = "🗝",
                Desc = "When the collector touches it, sets a variable to 1 (an ability unlocked, a key obtained) and disappears. Pair it with a Door that opens on that variable, or a Platformer ability. Great for metroidvania progression.",
                Texts = { new("var","Variable to set to 1","hasDoubleJump"), new("collector","Collector (name)","Player"), new("grant","Also grant ability (none/doubleJump/dash/wallJump)","none") } },

            new BehaviorInfo { Kind = BehaviorKind.Area, Name = "Trigger Area / Zone", Icon = "🟦",
                Desc = "An invisible zone. When the target enters it, fires an effect: goscene, setvar, addvar or say. once=1 fires a single time. Use for level exits, cutscene triggers, hidden-room reveals, kill-zones.",
                Nums = { new("amount","addvar amount",1), new("to","setvar value",1), new("once","Only once (0/1)",1) },
                Texts = { new("action","Effect (goscene/setvar/addvar/say)","goscene"), new("scene","Scene",""), new("var","Variable",""), new("text","Say text",""), new("target","Target (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.Generator, Name = "Procedural Generator", Icon = "🎲",
                Desc = "Procedurally spawns copies of prefab(s) inside this object's rectangle (or the whole scene if it has no size) when the scene loads. 'prefab' is one or more names separated by commas (picked at random). mode=once builds a stable map (same every time you come back); mode=regen recreates it fresh on every visit (roguelike). layout=scatter (random), grid, row or column. minGap keeps scattered items apart.",
                Nums = { new("count","How many to spawn",10), new("minGap","Min gap between items (scatter)",0) },
                Texts = { new("prefab","Prefab(s) to spawn (comma-separated)","Platform"), new("mode","Mode (once/regen)","once"), new("layout","Layout (scatter/grid/row/column)","scatter"), new("id","Unique id (for stable 'once' maps)","gen1") } },

            new BehaviorInfo { Kind = BehaviorKind.PathChase, Name = "Smart Chase (pathfinding)", Icon = "🧭",
                Desc = "Chases a target but navigates AROUND walls/obstacles using grid pathfinding (unlike plain Chase, which goes in a straight line). Best for top-down enemies. 'cell' is the grid resolution.",
                Nums = { new("speed","Speed",140), new("cell","Grid cell size",32), new("recalc","Recalc interval (s)",0.3) },
                Texts = { new("target","Target (name)","Player") } },

            new BehaviorInfo { Kind = BehaviorKind.Spring, Name = "Spring / Bounce Pad", Icon = "🔼",
                Desc = "Launches anything dynamic that lands on it (trampoline, jump pad). 'force' is the launch power; dir = up/down/left/right.",
                Nums = { new("force","Launch force",820) },
                Texts = { new("dir","Direction (up/down/left/right)","up") } },

            new BehaviorInfo { Kind = BehaviorKind.Minimap, Name = "Minimap (HUD)", Icon = "🗺",
                Desc = "Draws a small map in a corner showing the whole scene, with a dot for the player and optional dots for other objects. Put it on a UI layer. 'markers' = comma-separated object names to show (empty = just the player).",
                Nums = { new("size","Map width (px)",180), new("opacity","Opacity (0-1)",0.8) },
                Texts = { new("corner","Corner (tl/tr/bl/br)","tr"), new("player","Player name","Player"), new("markers","Extra markers (names, comma)","") } },

            new BehaviorInfo { Kind = BehaviorKind.Particles, Name = "Particle Emitter", Icon = "✨",
                Desc = "Emits particles (sparks, smoke, explosions, trails). on=start bursts once when the scene loads; on=always emits continuously at 'rate'; or trigger a burst from a script with the 'emit' action. spread is the cone angle (360 = all directions).",
                Nums = { new("rate","Per second (0 = burst only)",0), new("burst","Burst count",16), new("life","Particle life (s)",0.8), new("speed","Speed",180), new("spread","Spread angle (deg)",360), new("angle","Base angle (deg)",0), new("size","Particle size",8), new("gravity","Gravity",400) },
                Texts = { new("color","Color (hex)","#FFD24D"), new("on","When (start/always)","start") } },

            new BehaviorInfo { Kind = BehaviorKind.UIButton, Name = "UI Button", Icon = "🔘",
                Desc = "A clickable button. On click it runs an effect: goscene (change scene), setvar, addvar, say, save or load. Put it on a UI layer; add a Label for its text. Great for menus and HUD.",
                Nums = { new("amount","addvar amount",1), new("to","setvar value",1) },
                Texts = { new("action","Effect (goscene/setvar/addvar/say/save/load)","goscene"), new("scene","Scene",""), new("var","Variable",""), new("text","Say text","") } },

            new BehaviorInfo { Kind = BehaviorKind.Bar, Name = "Bar (health/progress)", Icon = "▭",
                Desc = "A bar that fills based on a variable, e.g. health. Fill = var / max. Put it on a UI layer for a HUD, or above an enemy for a health bar. 'max' can be a number or another variable name.",
                Nums = { new("max","Full value",3) },
                Texts = { new("var","Variable to track","health"), new("maxVar","...or a variable for max (optional)",""), new("color","Fill color","#3BC97A"), new("bg","Background color","#2A2A33") } },
        };

        public static BehaviorInfo Info(BehaviorKind kind) => All.Find(b => b.Kind == kind)!;
    }
}
