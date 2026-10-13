using System.Collections.Generic;

namespace EasyEngine
{
    /// <summary>
    /// Short step-by-step "how do I use it" guides, shown to the AI in Learn mode next to each behavior's catalog entry.
    /// Written for beginners and with the exact names the editor shows (fields as labelled in the behavior card).
    /// </summary>
    public static class BehaviorGuides
    {
        public static readonly Dictionary<BehaviorKind, string> All = new()
        {
            [BehaviorKind.Parallax] =
                "1. Make a big background object (wider than the screen) and give it an image in Inspector → Static image (\"Image fit → Tile (repeat)\" repeats it).\n" +
                "2. Put it on the Background layer so it is drawn behind the level.\n" +
                "3. Inspector → ＋ Add behavior → 🏔 Parallax.\n" +
                "4. Set Depth: 0 = far away (barely moves, e.g. mountains, sky), 1 = near (moves with the level).\n" +
                "5. For a 3D feeling, stack several layers (mountains 0.2, hills 0.5, bushes 0.8).\n" +
                "6. The player needs 🎥 Follow Camera — parallax only shows when the camera moves. Press ▶ Test and walk.",

            [BehaviorKind.Tilemap] =
                "1. Click \"＋ Empty Object\" (Prefabs panel) and put it on the Base layer.\n" +
                "2. Inspector → ＋ Add behavior → ▦ Tilemap. Set Tile size, Columns and Rows in its card.\n" +
                "3. In the card's TILES list, click a tile: set its Tile color, or give it a picture (🎨 Draw at the tile's size, or Choose image…) or frames (🎞 Animate — water, lava, torches; every copy plays in sync; ✕ goes back to the colour), and tick \"Solid (collision)\" for ground/walls.\n" +
                "4. On the scene: left-drag paints, right-drag erases.\n" +
                "5. Optional: turn on Breakable so tiles can be destroyed (by bullets, item clicks, damagers, stomping or touching); \"Only breaks when\" can demand a tool, e.g. holding a pickaxe; \"Drops\" gives an item per tile.",

            [BehaviorKind.Generator] =
                "1. Save the things to spawn as prefabs first (select an object → \"Save as Prefab\").\n" +
                "2. Make an object whose rectangle is the area to fill (no size = the whole scene).\n" +
                "3. Inspector → ＋ Add behavior → 🎲 Procedural Generator.\n" +
                "4. Pick \"What to spawn (random mix)\" (one or more prefabs) and \"How many to spawn\".\n" +
                "5. Map: \"The same every time\" keeps the map stable; \"New every visit (roguelike)\" rebuilds it each time.\n" +
                "6. Layout: Scattered at random (use \"Min gap between items\"), Grid, A row or A column. Press ▶ Test.",

            [BehaviorKind.Terrain] =
                "1. Click \"＋ Empty Object\" and put it on the Base layer.\n" +
                "2. Inspector → ＋ Add behavior → ⛰ Terrain Generator.\n" +
                "3. Choose a seed (same seed = same world), the ground level, how hilly it is, caves and ores.\n" +
                "4. Turn on \"Endless to the left and right\" for an infinite world (the minimap follows the player there).\n" +
                "5. Give the player 🗡 Use Item + a 🎒 Hotbar with a pickaxe: clicking a block mines it.\n" +
                "6. Press ▶ Test — the world is generated when the scene starts.",

            [BehaviorKind.AdvancedPatrol] =
                "1. Select the enemy → Inspector → ＋ Add behavior → ✎ Advanced Patrol.\n" +
                "2. Pick the Format: Platformer (gravity + jumps), Top-down (free + pathfinding) or Free / flying.\n" +
                "3. Click \"✎ Draw path\" (the grid turns on), then click points on the scene to trace the route; click \"✓ Finish\".\n" +
                "4. Set Speed, Repeat (back and forth, loop or once) and the pauses at the start/end.\n" +
                "5. Platformer: \"Jump over obstacles\" makes it hop over things in the way. Press ▶ Test.",

            [BehaviorKind.DayNight] =
                "1. Add it to ONE object (an empty controller object is fine): Inspector → ＋ Add behavior → 🌗 Day / Night cycle.\n" +
                "2. Game view: Platformer creates a Sky layer behind everything with a Sun and a Moon already linked (give them your own pictures); Top-down only changes the light.\n" +
                "3. Set Day length, Night length, the Dawn/dusk fade, and the brightness by day and at night.\n" +
                "4. Turn on \"Darker under solid blocks (underground)\" to make caves dark. \"True black darkness\" makes the dark pure black (no night tint) and fully dark deep down.\n" +
                "5. Add 💡 Light to torches/lamps so they glow at night. Put clouds or stars in the Sky layer.\n" +
                "6. Lighting style: Smooth = soft circles of light; Blocks = light worked out block by block. With Blocks, pick the Light block size and turn on/off: Solid blocks stop the light, Light colours what it touches, Every light adds up.\n" +
                "7. Read isDay / isNight / timeOfDay in blocks, e.g. spawn certain enemies only at night.",

            [BehaviorKind.Hotbar] =
                "1. Put it on the player (or any object): Inspector → ＋ Add behavior → 🎒 Hotbar / Inventory.\n" +
                "2. Edit Items: each slot has a name and a design (pickaxe, axe, sword…).\n" +
                "3. \"＋ New item (tool / weapon)…\" makes your own item (shovel, wand…) with a built-in drawing or your own picture.\n" +
                "4. You OWN an item once you have item:<name> (give it with a pickup, a shop, or the 'Give an item' block).\n" +
                "5. In game: mouse wheel or 1-9 picks a slot; the held item is drawn in the player's hand.\n" +
                "6. Add 🗡 Use Item to the player so clicking uses the item.",

            [BehaviorKind.ToolUse] =
                "1. Select the player → Inspector → ＋ Add behavior → 🗡 Use Item (swing / attack).\n" +
                "2. With a 🎒 Hotbar, the item in hand is the selected slot; without one, pick \"Item in hand\" and \"How it looks\".\n" +
                "3. In game, click anywhere to use it: it hits enemies (anything with ❤ Health), trees/rocks (🌳 Harvestable) or breakable tiles in Reach.\n" +
                "4. \"Damage per item\" sets each item's damage (e.g. sword 2, axe 1); \"Items that swing\" limits which items attack.\n" +
                "5. Walls / water between you and the target block the swing (turn that off with the checkbox).\n" +
                "6. Add swing/hit sounds if you like. Press F1 while testing to see every swing in the console.",

            [BehaviorKind.Harvestable] =
                "1. Make the tree/rock object → Inspector → ＋ Add behavior → 🌳 Harvestable (tree / rock).\n" +
                "2. Pick \"Tool needed\" (e.g. axe; none = any item) and \"Hits to fell\".\n" +
                "3. Pick what it Drops and how many (Resources dropped).\n" +
                "4. The player needs 🗡 Use Item (and usually a 🎒 Hotbar with that tool).\n" +
                "5. Press ▶ Test, hold the tool and click the tree until it falls.",

            [BehaviorKind.Shop] =
                "1. Make the shop/bench object → Inspector → ＋ Add behavior → 🛒 Shop / Crafting bench.\n" +
                "2. Pick what it Sells and the Cost.\n" +
                "3. \"Paid with\": coins for a shop, or an item (e.g. wood) for a crafting bench.\n" +
                "4. Set the Buy key and How close the player must stand; \"Buy only once\" for unique items.\n" +
                "5. Press ▶ Test, walk up to it and press the key.",

            [BehaviorKind.Gate] =
                "1. Make the door/barrier object → Inspector → ＋ Add behavior → 🚧 Gate / Locked Door.\n" +
                "2. Pick the variable it waits for (\"Opens when variable\", e.g. hasRedKey), \"is\" at least \"…this value\" 1.\n" +
                "3. Or pick \"…or when the player has\" an item.\n" +
                "4. Make a key: another object with 🗝 Ability / Key Pickup that turns on the same variable.\n" +
                "5. Press ▶ Test: the gate blocks the way until the key is picked up, then fades out.",

            [BehaviorKind.AbilityPickup] =
                "1. Make the key/power-up object → Inspector → ＋ Add behavior → 🗝 Ability / Key Pickup.\n" +
                "2. Pick \"Turns on variable\" (e.g. hasRedKey) — a 🚧 Gate can wait for it.\n" +
                "3. \"Also unlocks\" can give the player Double jump, Dash or Wall jump right away.\n" +
                "4. Press ▶ Test and touch it: it disappears and the variable becomes 1.",

            [BehaviorKind.Interact] =
                "1. Select the NPC/sign/chest → Inspector → ＋ Add behavior → 💬 Interact (press key).\n" +
                "2. \"What happens\": Show a message, Set a variable, Add to a variable or Go to a scene.\n" +
                "3. For a message, type it (| starts the next line) and \"Who is talking\".\n" +
                "4. Set the Key and How near the player must be; \"Only once\" for one-time events.\n" +
                "5. Press ▶ Test, walk up to it and press the key.",

            [BehaviorKind.Switch] =
                "1. Make the lever object → Inspector → ＋ Add behavior → 🎚 Switch / Lever.\n" +
                "2. Pick the variable it turns on/off (e.g. switch1).\n" +
                "3. \"How it's flipped\": Click it, Touch it, or Stand on it + press a key.\n" +
                "4. \"Stay on once flipped\" makes it one-time.\n" +
                "5. Use the variable elsewhere, e.g. a 🚧 Gate that opens when switch1 is at least 1.",

            [BehaviorKind.Area] =
                "1. Make an object covering the zone (it is invisible in the game) → Inspector → ＋ Add behavior → 🟦 Trigger Area / Zone.\n" +
                "2. Pick \"Who enters\" (usually Player).\n" +
                "3. \"What happens\": Go to a scene (level exit), Set/Add to a variable, or Show a message.\n" +
                "4. \"Only once\" fires a single time. Press ▶ Test and walk in.",

            [BehaviorKind.Custom] =
                "1. Select the object → Inspector → ＋ Add behavior → ✨ Custom (AI script).\n" +
                "2. Click \"🧩 Build with blocks (easy)\" — a Scratch-like editor opens.\n" +
                "3. Click \"➕ Add a block\": start with a \"When…\" block (When it spawns, When a key is pressed, When it touches an object, When clicked…) and add what it does under it (move, set a variable, play a sound, go to a scene…).\n" +
                "4. Click Done; the rules are saved on the object.\n" +
                "5. Or ask the AI in Build mode to write the rules for you. Press ▶ Test.\n" +
                "Words and yes/no: in Variables (top bar) each variable has a type — Number, Yes/No, Text, Colour or List. The blocks \"Set a text\" (e.g. Hello {playerName}!), \"Add to a text\", \"Switch a Yes/No\" and \"While a text variable…\" (is / is not / contains / is empty) use them; tick \"Its own\" for the object's own variable.\n" +
                "Colours and lists: \"Set a colour\" fills a Colour variable and \"Change the colour\" paints an object with a colour or a colour variable. \"Add to a list\", \"Remove from a list\", \"Empty a list\" and \"While a list…\" (contains / is empty / has at least N items) work on a List (quests, names you collected); a Label shows {quests} as \"a, b, c\" and {count:quests} as how many — count:quests also works in conditions.",

            [BehaviorKind.BossAI] =
                "1. Select the boss → Inspector → ＋ Add behavior → 👑 Boss AI. Give it ❤ Health too (its own variable, e.g. bossHP).\n" +
                "2. Pick Moves (on the ground or flying), Fights (usually Player), and \"Starts when\": the scene starts, the player comes close, or a variable turns on (e.g. set at the end of a 🎬 Cutscene).\n" +
                "3. Click \"🧩 Advanced Scratch…\": in Attacks, build each attack from steps (Warn (flash), Dash at the target, Jump, Shoot — a ring, a spread or aimed —, Summon minions, Wait…); in Phases, choose which attacks each phase uses and when it starts (health at or below N %); in When it dies, its last moments.\n" +
                "4. Or edit the JSON box — or describe the boss you want in \"✨ Ask the AI\" under it and press \"Make it\".\n" +
                "5. \"Boss health bar\" shows its name and health at the top during the fight. Press ▶ Test.",

            [BehaviorKind.Cutscene] =
                "1. Make an object (an invisible controller is fine) → Inspector → ＋ Add behavior → 🎬 Cutscene.\n" +
                "2. \"Plays when\": the scene starts, the player enters this object's area, a variable turns on, or a key is pressed. \"Only once\" plays it a single time.\n" +
                "3. Click \"🧩 Advanced Scratch…\" and add steps in order: Say (waits for Enter), Wait, Move the camera, Camera back to the player, Big title, Fade to a colour / Fade back in, Move an object, Shake the screen, Set a variable…\n" +
                "4. \"Lock the player\" stops them moving during it; \"Cinema bars\" adds black bars top and bottom; the \"Skip key\" jumps to the end.\n" +
                "5. Or describe the scene in \"✨ Ask the AI\" and press \"Make it\". Press ▶ Test to watch it.",

            [BehaviorKind.Minimap] =
                "1. Make an object on the UI layer (fixed on screen) → Inspector → ＋ Add behavior → 🗺 Minimap (HUD).\n" +
                "2. Pick the Corner, Map width and Opacity; Player = who the map follows (and its colour).\n" +
                "3. \"Also show\": press ＋ Add for each kind of thing to show (an object, every copy of a prefab, or a whole group) and click its dot to pick a colour — e.g. slimes purple, coins yellow.\n" +
                "4. \"Can be maximized\" + \"Key to open the big map\": in game that key opens a big map (press it again to close). Press ▶ Test.",

            [BehaviorKind.Particles] =
                "1. Select the object → Inspector → ＋ Add behavior → ✨ Particle Emitter.\n" +
                "2. When: \"One burst when the scene starts\" or \"Non-stop\" (set Per second).\n" +
                "3. Set Color, Particle size, Speed and Particle life.\n" +
                "4. Spread angle 360 = all directions; smaller = a cone pointing at Base angle. Gravity pulls them down (0 = float).\n" +
                "5. Blocks can also fire a burst with the 'emit' action. Press ▶ Test.",

            [BehaviorKind.LightSource] =
                "1. Select the torch/lamp → Inspector → ＋ Add behavior → 💡 Light.\n" +
                "2. Set Radius, Intensity and Color.\n" +
                "3. Lights only show when it is dark: lower the scene's Ambient light (click an empty spot of the scene → LIGHT) or add a 🌗 Day / Night cycle.\n" +
                "4. Press ▶ Test (at night).",

            [BehaviorKind.FollowCamera] =
                "1. Select the player → Inspector → ＋ Add behavior → 🎥 Follow Camera.\n" +
                "2. Smoothing: 0 = locked to the player, higher = a softer, lazier camera.\n" +
                "3. \"Stay inside the scene\" stops the camera at the scene's edges (scene Width/Height: click an empty spot → SCENE).\n" +
                "4. Only one object should have it. Press ▶ Test.",

            [BehaviorKind.Health] =
                "1. Select the player or enemy → Inspector → ＋ Add behavior → ❤ Health. Set Max health.\n" +
                "2. \"When it dies\": Respawn (player, at the last 🚩 Checkpoint) or Disappear (enemies).\n" +
                "3. Something must hurt it: 💢 Damager / Hazard on spikes/enemies (pick who it Hurts), or 🗡 Use Item / 🔫 Shooter.\n" +
                "4. To show it: a ▭ Bar on the UI layer that shows the same variable (health), or a 🔤 Label with \"HP: {health}\".\n" +
                "5. Give each enemy its own variable name so their bars don't mix. Press ▶ Test.",

            [BehaviorKind.Damager] =
                "1. Select the spikes/enemy → Inspector → ＋ Add behavior → 💢 Damager / Hazard.\n" +
                "2. Pick who it Hurts (usually Player), the Damage and the Knockback.\n" +
                "3. The target needs ❤ Health. Press ▶ Test and touch it.",

            [BehaviorKind.Bar] =
                "1. Make an object on the UI layer (or above an enemy) → Inspector → ＋ Add behavior → ▭ Bar (health/progress).\n" +
                "2. \"Shows variable\": the variable it fills from (e.g. health from ❤ Health).\n" +
                "3. Set the Full value (or take it from a variable), Fill color and Background color.\n" +
                "4. Size the object to the bar's size. Press ▶ Test.",

            [BehaviorKind.Platformer] =
                "1. Select the player → Inspector → ＋ Add behavior → 🏃 Platformer Character. Set PHYSICS Mode to Dynamic.\n" +
                "2. The ground and platforms need ⬛ Solid (ground/platform) (or a solid ▦ Tilemap).\n" +
                "3. Tune Speed and Jump force; tick Double jump, Wall jump or Dash if you want them.\n" +
                "4. For enemies, untick \"Default controls\" so they don't copy the player's keys.\n" +
                "5. Add 🎥 Follow Camera to the player. Press ▶ Test (arrows / A-D to run, Space to jump).",

            [BehaviorKind.TopDown] =
                "1. Select the player → Inspector → ＋ Add behavior → ✛ Top-Down Movement.\n" +
                "2. Set Speed; walls need ⬛ Solid so it can't walk through them.\n" +
                "3. For enemies, untick \"Default controls\" (use 🧭 Smart Chase or ✎ Advanced Patrol to move them).\n" +
                "4. Add 🎥 Follow Camera. Press ▶ Test (WASD / arrows).",

            [BehaviorKind.Label] =
                "1. Make an object on the UI layer (fixed on screen) → Inspector → ＋ Add behavior → 🔤 Label / HUD text.\n" +
                "2. Type the Text; {variable} shows a value, e.g. \"Score: {score}\" or \"Coins: {coins}\"; a Text variable shows its words (\"Hello {playerName}\"), a List its items (\"Quests: {quests}\") and {count:quests} how many.\n" +
                "3. Set Font size, Color, Alignment and an Effect (typewriter, fade, blink, pulse).\n" +
                "4. Press ▶ Test — the text updates when the variable changes.",
        };

        public static string? For(BehaviorKind kind) => All.TryGetValue(kind, out var g) ? g : null;
    }
}
