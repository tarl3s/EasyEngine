using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    // The 🔨 Build prompt: the JSON shape, the behavior catalog, the rules, recipes and examples, and the scene.
    public partial class MainWindow
    {
        // ============================================================
        //  AI TEST HARNESS (run many prompts through the real pipeline)
        // ============================================================
        private void ResetTestScene()
        {
            _project.Prefabs = PrefabFactory.Defaults();
            _project.Variables.Clear(); _project.Flags.Clear(); _project.Texts.Clear(); _project.Colors.Clear(); _project.Lists.Clear();

            var scene = new Scene { Name = "Scene 1" };
            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            baseL.Objects.Add(new GameObjectData { Id = 1, Name = "Ground", Type = "Object", X = 0, Y = 460, Width = 960, Height = 80, Color = "#3A3A46", Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { BehaviorCatalog.Info(BehaviorKind.Solid).CreateData() } });
            var player = new GameObjectData { Id = 2, Name = "Player", Type = "Player", X = 120, Y = 360, Width = 48, Height = 64, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
            player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.FollowCamera).CreateData());
            player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Health).CreateData());
            baseL.Objects.Add(player);
            scene.Layers.Add(baseL);
            scene.Layers.Add(new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true });

            _project.Scenes = new List<Scene> { scene };
            _project.StartScene = scene.Name;
            _scene = scene;
            _selection.Clear(); _selected = null; _activeLayer = baseL;
        }

        private string DumpState()
        {
            var sb = new StringBuilder();
            foreach (var o in _scene.Layers.SelectMany(l => l.Objects))
            {
                sb.Append($"{{{o.Name} {o.Physics.Mode}");
                foreach (var b in o.Behaviors) sb.Append(" " + b.Kind + DumpBeh(b));
                if (VarScope.Of(o).Count > 0) sb.Append(" vars(" + VarScope.Of(o).Describe() + ")");
                sb.Append("} ");
            }
            var extraPrefabs = _project.Prefabs.Where(p => !new[] { "Player", "Enemy", "Bullet", "Particle", "Platform", "Door", "Tilemap" }.Contains(p.Name)).Select(p => p.Name).ToList();
            if (extraPrefabs.Count > 0) sb.Append("PREFABS[" + string.Join(",", extraPrefabs) + "] ");
            if (VarScope.Of(_project).Count > 0) sb.Append("GLOBAL(" + VarScope.Of(_project).Describe() + ") ");
            if (VarScope.Of(_scene).Count > 0) sb.Append("SCENE(" + VarScope.Of(_scene).Describe() + ") ");
            return sb.ToString();
        }

        private static string DumpBeh(BehaviorData b)
        {
            var parts = new List<string>();
            foreach (var kv in b.Nums) parts.Add($"{kv.Key}={kv.Value:0.##}");
            foreach (var kv in b.Texts) parts.Add(kv.Key == "script" ? "script=" + Compact(kv.Value) : $"{kv.Key}={kv.Value}");
            return parts.Count > 0 ? "(" + string.Join(",", parts) + ")" : "";
        }

        private static string Compact(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();


        private string BuildProjectPrompt(bool compact = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the game-builder AI of a 2D engine. Output ONLY a JSON object. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"create\": [ {\"name\",\"x\",\"y\",\"width\",\"height\",\"color\",\"layer\",\"physics\":\"None|Static|Dynamic\",\"behaviors\":[{\"kind\",\"nums\":{},\"texts\":{}}],\"custom\":[...],\"animations\":[{\"name\",\"frames\":[],\"fps\"}],\"sprite\":\"file.png\"} ],");
            sb.AppendLine("  \"edit\":   [ {\"name\":\"<existing object>\",\"behaviors\":[...],\"remove\":[\"Kind\"],\"custom\":[...],\"animations\":[...],\"color\",\"width\",\"height\"} ],");
            sb.AppendLine("  \"layers\": [ {\"name\",\"screenSpace\":true,\"visible\":true} ],");
            sb.AppendLine("  \"delete\": [\"<existing object name>\"],");
            sb.AppendLine("  \"variables\": {\"global\":{\"name\":value}, \"scene\":{\"name\":value}}, \"removeVariables\": {\"global\":[\"name\"], \"scene\":[\"name\"]}");
            sb.AppendLine("}");
            sb.AppendLine("Only include the fields/arrays you need. Omit empty ones.");
            sb.AppendLine("ORDER: \"z\":n on a create/edit object draws it in front of (higher) or behind (lower) the others in its layer (default 0). Objects marked LOCKED can't be changed — leave them out.");
            sb.AppendLine("VARIABLES: top-level \"variables\".global/.scene create or set the game's global/scene variables (initial values); \"removeVariables\" deletes them. Per-object variables go inside that object's edit/create as \"variables\":{\"name\":value} (and \"removeVariables\":[\"name\"]). You can change ANY variable this way. A value is a number, true/false (a Yes/No), \"words\" (a Text, e.g. \"playerName\":\"Hero\"), a colour \"#RRGGBB\" (a Colour, e.g. \"teamColor\":\"#FF8800\") or an array of words (a List, e.g. \"quests\":[\"Find the key\"]).");
            sb.AppendLine();
            sb.AppendLine("KEY RULES:");
            sb.AppendLine("- To make a NEW thing (player, enemy, boss, coin, platform…), add it to \"create\" and it will be placed in the scene. ONE object can and should have MANY behaviors.");
            sb.AppendLine("- To change an EXISTING object, add it to \"edit\" by its name. Behaviors are ADDED/UPDATED; keep existing ones unless you list them in \"remove\". Its \"custom\" rules are ADDED to the ones it has (a rule with the same event+action replaces the old one); to rewrite them all, send the full list with \"replaceCustom\":true. To delete objects: \"delete\":[\"Name\"].");
            sb.AppendLine("- If the user talks about \"the player\" but there is an object with Platformer/TopDown, that IS the player — edit it by its name. Don't require it to be selected.");
            sb.AppendLine("- If an object with that name ALREADY EXISTS in the scene, put it in \"edit\" (never create a duplicate).");
            sb.AppendLine("- To SPAWN objects during play (rocks falling, enemies appearing on a key/timer): create the thing with \"asPrefab\":true (it stays OUT of the scene until spawned; give it physics Dynamic if it should fall), then spawn it with a Spawner behavior or a custom spawn rule.");
            sb.AppendLine("- Use EXACT kind names from this list. Never invent kinds. One object, many behaviors.");
            sb.AppendLine();
            sb.AppendLine("EXAMPLES (the shape of an answer — never copy one unless it is exactly what was asked):");
            sb.AppendLine("User: \"make a player that walks, jumps, has health and can attack\" =>");
            sb.AppendLine("{\"create\":[{\"name\":\"Player\",\"x\":120,\"y\":300,\"width\":48,\"height\":64,\"color\":\"#3BC97A\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":260,\"jump\":640}},{\"kind\":\"FollowCamera\"},{\"kind\":\"Health\",\"nums\":{\"max\":5,\"respawn\":1}},{\"kind\":\"ToolUse\",\"texts\":{\"tool\":\"sword\"}}]}]}");
            sb.AppendLine("User: \"give the player more health\" (player already exists) =>");
            sb.AppendLine("{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":8}}]}]}");
            sb.AppendLine("User: \"create a global score at 0 and lives at 3, and set the scene timer to 60\" => {\"variables\":{\"global\":{\"score\":0,\"lives\":3},\"scene\":{\"timer\":60}}}");
            if (_aiImages.Count > 0)
                sb.AppendLine("User: \"with these images make a boss with full movement\" => {\"create\":[{\"name\":\"Boss\",\"x\":700,\"y\":200,\"width\":120,\"height\":120,\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":120,\"jump\":500}},{\"kind\":\"Health\",\"nums\":{\"max\":20,\"respawn\":0}},{\"kind\":\"Chase\",\"texts\":{\"target\":\"Player\"}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}],\"animations\":[{\"name\":\"idle\",\"frames\":[\"<idle images>\"],\"fps\":6},{\"name\":\"walk\",\"frames\":[\"<walk images>\"],\"fps\":10}]}]}");
            sb.AppendLine();
            sb.AppendLine("Behavior kinds — nums(texts):");
            foreach (var info in BehaviorCatalog.All)
            {
                if (info.Kind == BehaviorKind.Tilemap || info.Hidden) continue; // painted by hand / legacy — never via AI
                var nums = string.Join(",", info.Nums.Select(n => n.Key));
                var texts = string.Join(",", info.Texts.Select(t => t.Key));
                sb.AppendLine($"  {info.Kind}: {nums}" + (texts.Length > 0 ? $" ({texts})" : ""));
            }
            // bosses and cutscenes keep their steps in texts.script (a JSON string); the card's "Ask the AI" writes them in detail
            sb.AppendLine("BOSS = an object with BossAI + Health (respawn=0, texts.var its own variable e.g. bossHP) + Damager (target Player). BossAI texts: moves=ground|fly, starts=start|near|var (+var), target, title. Its texts.script may stay empty: a new BossAI already charges and shoots; the user designs the attacks/phases in its card (🧩 Advanced Scratch / ✨ Ask the AI).");
            sb.AppendLine("CUTSCENE = an object with Cutscene: texts.trigger=start|enter|var|key (+var/key/who), nums once/lock/bars. Its texts.script is a JSON STRING {\"steps\":[{\"do\":\"say\",\"text\":\"...\",\"speaker\":\"...\"},...]} with step ids: " +
                          string.Join(",", StepCatalog.Cutscene.Select(d => d.Id)) + ". Keep it short or leave it for the card's ✨ Ask the AI.");
            sb.AppendLine();
            if (compact)
            {
                sb.AppendLine("For logic the kinds can't do, use a \"custom\" rule list: [{\"when\":<start|always|key|keypress|timer|after|touch|death|ifvar|iflocal|iftext|iflist|hasitem|expr|click|rightclick|mouseover|mousepress>,...params...,\"do\":<velocity|move|accelerate|jump|rotate|shoot|spawn|destroy|hide|show|goscene|setvar|addvar|setlocal|addlocal|settext|toggle|setcolor|tint|listadd|listremove|listclear|set|additem|removeitem|boost|setspeed|say|save|load|tween|emit>,...params...}]. Params by name: key, every, name, var/op/value, force, speed, vx/vy, dx/dy, to, amount, factor, text, scene, item, directions, prop. Texts: settext(var,to:\"Hi {name}\"), toggle(var) flips a Yes/No, iftext(var,op:is|isnot|contains|empty,value). Colours: setcolor(var,to:\"#FF8800\"), tint(color,target) paints an object; any colour param can be \"{colourVar}\". Lists: listadd/listremove(var,value), listclear(var), iflist(var,op:contains|notcontains|empty|notempty|countis|countatleast,value); {quests} shows the items, count:quests is how many. ANY value can be an \"expr\" string (math; reads self.x/self.cx/self.health, OtherObj.cx, a variable, item:NAME, count:LIST, mousex, time, dist(x1,y1,x2,y2)). Guard any rule with \"cond\":\"<expr>\". timer repeats; after(seconds) fires once. Fade/grow/slide = do:tween(prop=opacity|scale|x|y, to, duration), e.g. {\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":3}. setcolor only sets a Colour variable. 'WHEN X happens, effect' is ALWAYS a custom rule (condition→action), never a static param.");
                sb.AppendLine("Player = Platformer, physics Dynamic, controls=1 (dash/doubleJump/wallJump are 0/1 nums). Enemies: controls=0; prefer Patrol/Chase/custom. Killable enemy = Health respawn=0; player = respawn=1. Moving platform = Solid+Patrol. To spawn during play: make it asPrefab:true, then a Spawner or a custom spawn rule. Per-object looks: opacity, cornerRadius, borderColor+borderWidth, color, font. Draw art: \"pixelArt\":{\"palette\":{\"X\":\"#RRGGBB\"},\"rows\":[\"..XX..\"]} ('.'=transparent, <=16px). Image with empty space around the drawing: \"fitHitbox\":true (collides with the drawing, not the box). Only touch the exact object(s)/layer(s) the user names.");
            }
            else
            {
            sb.AppendLine("Intent → kind: walk+jump=Platformer; top-down/8-dir=TopDown; ground/wall/platform=Solid; camera=FollowCamera; patrol=Patrol; chase player=Chase; shoot on key=Shooter; melee/sword/attack/use an item by clicking=ToolUse (texts tool=sword, or a Hotbar for several items); hp/lives=Health; spike/hazard/contact damage=Damager; checkpoint=Checkpoint; coin/pickup=Collectible; door/next level=GoToScene; HUD/score text=Label; glow=LightSource; spinning=Spin; spawner=Spawner; parallax bg=Parallax.");
            sb.AppendLine("Intent → kind (NEW): pushable crate/box/sokoban block=Pushable; lever/switch/toggle=Switch; locked gate/ability-gate/barrier that opens with a key=Gate (NOT GoToScene — Gate blocks the way, GoToScene changes scene); NPC/sign/chest/press-E-to-talk=Interact; pick up an ability or key that unlocks something=AbilityPickup; invisible trigger zone/region/level-exit area=Area.");
            sb.AppendLine("Gate opens when its var meets the condition: {\"kind\":\"Gate\",\"texts\":{\"var\":\"hasRedKey\"},\"nums\":{\"value\":1}}. Give the key with AbilityPickup {\"kind\":\"AbilityPickup\",\"texts\":{\"var\":\"hasRedKey\"}}. Metroidvania ability+gate: AbilityPickup texts{var:hasDash,grant:dash} then Gate texts{var:hasDash}.");
            sb.AppendLine("PROCEDURAL maps: put a Generator on an empty object sized to the region to fill (or scene-sized). It spawns prefab(s) when the scene loads. mode=once makes a STABLE map (same when you return); mode=regen RECREATES it on every visit (roguelike). Make the spawned things prefabs with asPrefab:true. e.g. {\"create\":[{\"name\":\"Coin\",\"asPrefab\":true,...}],\"edit\":[{\"name\":\"World\",\"behaviors\":[{\"kind\":\"Generator\",\"texts\":{\"prefab\":\"Coin,Enemy\",\"mode\":\"regen\",\"layout\":\"scatter\"},\"nums\":{\"count\":20,\"minGap\":80}}]}]}.");
            sb.AppendLine("Persistence: a Collectible/AbilityPickup with a texts \"id\" stays gone after it's taken (survives revisits + save). Save/continue progress with the save/load actions (custom do:save / do:load, or Interact action:save) — saves all global vars + current scene. Collectible can also carry texts{item:NAME}+nums{amount} to add to an inventory count.");
            sb.AppendLine("AESTHETICS (per object, in create/edit): \"opacity\"(0-1), \"cornerRadius\"(px, 0=square), \"borderColor\"(hex)+\"borderWidth\"(px outline), \"font\"(family name) + \"bold\"(true/false) for Label text, \"color\"(fill). Example rounded glowing box: {\"edit\":[{\"name\":\"Box\",\"cornerRadius\":16,\"borderColor\":\"#00E5FF\",\"borderWidth\":3,\"opacity\":0.9}]}.");
            sb.AppendLine("TEXT EFFECTS: a Label takes texts{effect: none|typewriter|fade|blink|pulse} + nums{effectSpeed}. e.g. a title that types out: {\"kind\":\"Label\",\"texts\":{\"text\":\"GAME OVER\",\"effect\":\"typewriter\"},\"nums\":{\"fontSize\":48}}.");
            sb.AppendLine("GAME THEME (top-level \"theme\" object, styles what the PLAYER sees): {\"theme\":{\"font\":\"Consolas\",\"dialogBg\":\"#101418\",\"dialogBorder\":\"#00E5FF\",\"dialogText\":\"#FFFFFF\",\"dialogSpeaker\":\"#F4C03B\",\"dialogFontSize\":18,\"dialogCorner\":10,\"dialogEffect\":\"typewriter\",\"dialogSpeed\":45}}. Use it when the user wants to restyle dialogue boxes or the game font.");
            sb.AppendLine("Intent → kind (MORE): enemy that chases AROUND walls (not straight) = PathChase (best for top-down); trampoline/bounce pad/jump pad = Spring (nums{force},texts{dir:up/down/left/right}); minimap/radar = Minimap (put on a UI layer, texts{corner:tl/tr/bl/br,player,markers}). Bouncy objects: set the object's physics Bounciness (0-1) in \"physics\" — a ball with bounciness 0.7 bounces off floors/walls. A character/enemy whose image has empty (transparent) space around the drawing: \"fitHitbox\":true in its create/edit — it then collides with the drawing, not the whole box.");
            sb.AppendLine("Intent → kind (FX/UI): explosion/sparks/smoke/trail = Particles (on=start bursts once, on=always is continuous at nums{rate}; nums{burst,life,speed,spread,angle,size,gravity}, texts{color}); clickable menu/HUD button = UIButton on a UI layer (same action params as Area: goscene/setvar/addvar/say/save/load) — add a Label for its text; health/mana/progress bar = Bar (texts{var,color,bg}, nums{max} or texts{maxVar}).");
            sb.AppendLine("ACTIONS (MORE): do:emit (particle burst — count,speed,spread,angle,life,size,gravity,color); do:tween (smoothly animate a property: prop=x|y|opacity|scale|rot|w|h, to, nums{duration,loop,yoyo}, ease=linear|in|out|inout) e.g. a pulsing title {\"when\":\"start\",\"do\":\"tween\",\"prop\":\"scale\",\"to\":1.2,\"duration\":0.6,\"loop\":1,\"yoyo\":1}; do:emit on death for explosions {\"when\":\"death\",\"do\":\"emit\",\"count\":24,\"color\":\"#E5484B\"}; do:setvolume (channel=music/sfx/master, to=0-1). SoundEmitter texts{channel:music|sfx}.");
            sb.AppendLine("Platformer/TopDown obey the default keys only when their num \"controls\"=1 (that's the PLAYER). If you give Platformer/TopDown to an enemy/boss, set controls=0 so it does NOT copy the player's keys (move it with a custom script or custom keys). For simple enemies prefer Patrol/Chase/custom (physics Dynamic if it should fall/jump).");
            sb.AppendLine("Player uses Platformer physics=Dynamic (dash/doubleJump/wallJump are nums on it, each 0 or 1). Enemy you can kill = Health respawn=0. Player = Health respawn=1.");
            sb.AppendLine();
            sb.AppendLine("AVOID THESE MISTAKES:");
            sb.AppendLine("- A new thing the user names (coin, platform, enemy, boss, NPC, door, spike, wall, obstacle, crystal…) is a NEW object → put it in \"create\" with that name. NEVER put Solid/Collectible/Patrol/Chase/GoToScene/Parallax/Projectile/DestroyOffscreen on the Player.");
            sb.AppendLine("- An ability/stat for the player (health, speed, dash, jump, wall jump, attack, shoot, light) goes in \"edit\" of the existing player — do NOT create a new object for it, and do NOT invent a new player.");
            sb.AppendLine("- 0/1 flags (controls,doubleJump,wallJump,dash,oneWay,respawn,onStart,loop) must be exactly 0 or 1.");
            sb.AppendLine("- 'X times faster' / 'double speed' ON AN EVENT => custom boost(factor). NEVER use velocity for speed. A permanent speed change => set the Platformer \"speed\" num.");
            sb.AppendLine("- To spawn on a timer you MUST add a Spawner behavior (or a custom timer→spawn rule); creating the prefab alone spawns nothing.");
            sb.AppendLine("- An enemy that shoots AT the player => custom {\"when\":\"timer\",\"every\":2,\"do\":\"shoot\",\"directions\":1,\"speed\":400}. Projectile makes the object ITSELF fly, so don't put Projectile on a stationary shooter.");
            sb.AppendLine("- When changing layers, ONLY change the exact layer(s) the user names.");
            sb.AppendLine("- \"when it dies, do X\" (explode, drop item, spawn particles) => a custom rule with \"when\":\"death\".");
            sb.AppendLine("- A MOVING platform = a new object with Solid + Patrol (NOT Platformer). A one-way platform = Solid oneWay=1 (keep physics Static).");
            sb.AppendLine("- Patrol nums: speed, distance, axis (0=horizontal, 1=vertical), smooth (1=ease to a stop at the ends), slowSteps (how many times it slows near an end; 0=no slowing), pause (seconds stopped at each end; 0=none). E.g. vertical lift that waits: Patrol axis=1, smooth=1, pause=1.5.");
            sb.AppendLine("- AdvancedPatrol = follows a free route the USER draws in the editor. You can ADD it but cannot draw the route — tell the user to select it and click 'Draw path'. Nums: mode (0=platformer gravity+jumps, 1=top-down free, 2=free/flying), speed, jump, autoJump, pathfind (route around walls; top-down only), repeat (0=back-and-forth, 1=loop, 2=once), pause, pauseStart, collide (1=has hitbox, 0=passes through walls).");
            sb.AppendLine("- Platformer/TopDown/AdvancedPatrol have a 'collide' num (default 1). Set collide=0 to make a character ignore walls/tilemaps (no hitbox / ghost).");
            sb.AppendLine();
            sb.AppendLine("DRAW SPRITES (pixel art): you can DRAW an object's look. On a create/edit object add \"pixelArt\":{\"palette\":{\"X\":\"#RRGGBB\",...},\"rows\":[\"..XX..\",...]}. Each row is a string; each character is a palette key; '.' or space = transparent. Keep it small (<=16x16 is best, up to 32). Match the object's width/height to the art size for crisp pixels. Only do this when the user asks you to draw/make art, or when no image was given and art would help.");
            sb.AppendLine("For animations with drawn frames: \"animations\":[{\"name\":\"walk\",\"fps\":8,\"loop\":true,\"frames\":[{\"palette\":{...},\"rows\":[...]},{\"palette\":{...},\"rows\":[...]}]}]. Name them idle/walk/jump/crouch so the engine auto-plays them. Example tiny coin: {\"pixelArt\":{\"palette\":{\"Y\":\"#ffd700\",\"o\":\"#b8860b\"},\"rows\":[\".YYY.\",\"YoYoY\",\"YoYoY\",\"YoYoY\",\".YYY.\"]}}.");
            sb.AppendLine();
            sb.AppendLine("CUSTOM behavior — for ANY special/unique logic the kinds can't do (enemy patterns, dialogue, variables, inventory, conditions). ALWAYS use \"custom\" for things like 'every N do X', timed attacks, phases:");
            sb.AppendLine("CRUCIAL: 'WHEN <something> happens, <effect>' (e.g. 'when the player hits X, go 2x faster', 'when touch spike, lose a life') is ALWAYS a custom rule (condition → action). NEVER express it by changing a static param — a static param would apply from the start, not on the event.");
            sb.AppendLine("\"custom\":[{\"when\":<start|always|key|keypress|timer|after|touch|death|ifvar|iflocal|iftext|iflist|hasitem|expr|click|rightclick|mousedown|mouseover|mousepress>, ...params, \"do\":<velocity|move|accelerate|jump|rotate|shoot|spawn|destroy|hide|show|goscene|setvar|addvar|setlocal|addlocal|settext|addtext|toggle|setcolor|tint|listadd|listremove|listclear|set|additem|removeitem|boost|setspeed|say|save|load|tween|emit>, ...params}]");
            sb.AppendLine("Condition params: key(key); keypress(key,cooldown); timer(every); touch(name) [fires once when contact starts]; death [fires when this object's health hits 0]; ifvar/iflocal(var,op,value); iftext(var,op=is|isnot|contains|empty,value) [compares a Text variable]; iflist(var,op=contains|notcontains|empty|notempty|countis|countatleast,value) [checks a List: an item, or how many]; hasitem(item); expr(expr) [any expression truthy]; click/rightclick/mousedown/mouseover [mouse over THIS object]; mousepress [mouse clicked anywhere].");
            sb.AppendLine("Action params: velocity(vx,vy); move(dx,dy); accelerate(ax,ay); jump(force); rotate(speed); shoot(directions,speed); spawn(object); goscene(scene); setvar/setlocal(var,to); addvar/addlocal(var,amount); settext/addtext(var,to) [to = words, {name} puts a variable in: \"Hello {playerName}!\"]; toggle(var) [flips a Yes/No]; setcolor(var,to) [to = \"#RRGGBB\": a Colour variable]; tint(color,[target]) [paints a plain-colour object, default target=self; color can be \"{teamColor}\" — so can emit's color]; listadd/listremove(var,value) [adds an item / removes the first equal one; value may hold {name}]; listclear(var); {quests} in a text shows a List's items, count:quests (in an expr or {count:quests}) is how many; settext/toggle/iftext/setcolor/list actions/iflist take \"local\":true for the object's own variable; set(prop,to,[target]) [prop=x|y|vx|vy|rot|facing|health|w|h — set a property, default target=self]; boost(factor); setspeed(factor); say(text,speaker). Timing: when:timer(every) REPEATS; when:after(seconds) fires ONCE that many seconds after the object appears. Gradual changes (fade, grow, slide) are do:tween(prop=opacity|scale|x|y|rot|w|h, to, duration) — e.g. fade out after 5 s over 3 s: {\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":3}. setcolor only stores a colour in a Colour VARIABLE; it never changes how an object looks (use tint or tween).");
            sb.AppendLine("EXPRESSIONS (the big unlock): any set/add/compare value can be an \"expr\" string instead of a fixed number. Supports + - * / %, comparisons > < >= <= == !=, logic && || !, parentheses, functions abs min max floor ceil round sqrt sign sin cos atan2 clamp(x,lo,hi) rand(a,b) randi(a,b) dist(x1,y1,x2,y2). Read values: a variable name (score), item:NAME, self.x/self.y/self.vx/self.cx/self.health/self.facing/local.NAME, OtherObjectName.x/.cy/..., mousex/mousey (world), mousescreenx/y, time, scenew/sceneh.");
            sb.AppendLine("Universal guard: add \"cond\":\"<expression>\" to ANY rule and it only fires when the expression is truthy. e.g. {\"when\":\"click\",\"cond\":\"coins >= 15\",\"do\":\"addvar\",\"var\":\"coins\",\"expr\":\"coins - 15\"}.");
            sb.AppendLine("Expression examples: make an object follow the mouse => {\"when\":\"always\",\"do\":\"set\",\"prop\":\"x\",\"expr\":\"mousex - self.w/2\"}. Score from a formula => {\"when\":\"touch\",\"name\":\"Coin\",\"do\":\"setvar\",\"var\":\"score\",\"expr\":\"score + 10 * lives\"}. Enemy that charges only when the player is close => {\"when\":\"always\",\"cond\":\"dist(self.cx,self.cy,Player.cx,Player.cy) < 200\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"sign(Player.cx - self.cx) * 180\"}. Click to buy/select => {\"when\":\"click\",\"do\":\"setvar\",\"var\":\"selected\",\"to\":1}.");
            sb.AppendLine("Example — \"when the player hits the Orb, move 2x faster\": {\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"touch\",\"name\":\"Orb\",\"do\":\"boost\",\"factor\":2}]}]}");
            sb.AppendLine("IMPORTANT: a set action uses \"to\" (NOT \"value\") so it never clashes with a condition's \"value\". e.g. reset a counter with {\"when\":\"iflocal\",\"var\":\"jumps\",\"op\":\">=\",\"value\":3,\"do\":\"setlocal\",\"var\":\"jumps\",\"to\":0}.");
            sb.AppendLine("LOCAL vars (setlocal/addlocal/iflocal) are PER-OBJECT counters — each enemy copy has its own. Use them for 'every N' patterns. GLOBAL vars (setvar/addvar/ifvar) are shared (score, lives).");
            sb.AppendLine("'jump' needs physics Dynamic and some ground. vx right+, vy down+ (negative vy = up).");
            sb.AppendLine("Show variables with a Label whose text uses {name} (e.g. \"HP: {health}\"). Dialogue: say with text lines split by |.");
            sb.AppendLine("Example — \"enemy that jumps and every 3 jumps dashes forward\":");
            sb.AppendLine("{\"create\":[{\"name\":\"Jumper\",\"x\":640,\"y\":300,\"width\":48,\"height\":48,\"color\":\"#E5484B\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":3,\"respawn\":0}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}],\"custom\":[{\"when\":\"timer\",\"every\":1.1,\"do\":\"jump\",\"force\":540},{\"when\":\"timer\",\"every\":1.1,\"do\":\"addlocal\",\"var\":\"jumps\",\"amount\":1},{\"when\":\"iflocal\",\"var\":\"jumps\",\"op\":\">=\",\"value\":3,\"do\":\"velocity\",\"vx\":-320,\"vy\":-200},{\"when\":\"iflocal\",\"var\":\"jumps\",\"op\":\">=\",\"value\":3,\"do\":\"setlocal\",\"var\":\"jumps\",\"to\":0}]}]}");
            sb.AppendLine("Example — \"spawn rocks that fall from the sky when I press Y\":");
            sb.AppendLine("{\"create\":[{\"name\":\"Rock\",\"width\":40,\"height\":40,\"color\":\"#8A7A6A\",\"physics\":\"Dynamic\",\"asPrefab\":true}],\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Spawner\",\"texts\":{\"target\":\"Rock\",\"key\":\"Y\",\"at\":\"top\"},\"nums\":{\"max\":0}}]}]}");
            }
            sb.Append(Recipes);
            sb.AppendLine();

            // scene inventory so the AI can target/avoid-dup
            sb.AppendLine("CURRENT SCENE \"" + _scene.Name + "\":");
            sb.AppendLine("Layers: " + string.Join(", ", _scene.Layers.Select(l => $"\"{l.Name}\"({l.Kind}{(l.ScreenSpace || l.Kind == LayerKind.UI ? ",fixed" : "")})")));
            var allObjs = _scene.Layers.SelectMany(l => l.Objects).ToList();
            if (allObjs.Count == 0) sb.AppendLine("Objects: (none yet)");
            int ruleBudget = 3000; // the rules shown in all: a big project must not blow the free tiers' token limits
            if (allObjs.Count > 0) foreach (var o in allObjs)
            {
                var ov = VarScope.Of(o).Count > 0 ? " vars{" + VarScope.Of(o).Describe() + "}" : "";
                var script = Compact(o.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Custom)?.Text("script", "") ?? "");
                var shown = script.Length > 600 ? script[..600] + "…" : script;
                var rules = script.Length <= 2 ? "" : shown.Length <= ruleBudget ? " custom=" + shown : " custom=(has rules)"; // so a change can keep or retune them
                if (script.Length > 2 && shown.Length <= ruleBudget) ruleBudget -= shown.Length;
                sb.AppendLine($"  \"{o.Name}\" [{string.Join(",", o.Behaviors.Select(b => b.Kind))}]{ov}{(o.Locked ? " LOCKED" : "")}{(o.Z != 0 ? $" z{o.Z}" : "")}{rules}");
            }
            if (VarScope.Of(_project).Count > 0) sb.AppendLine("Global variables: " + VarScope.Of(_project).Describe());
            if (VarScope.Of(_scene).Count > 0) sb.AppendLine("Scene variables: " + VarScope.Of(_scene).Describe());
            if (_selection.Count > 0)
                sb.AppendLine($"The user SELECTED these object(s): {string.Join(", ", _selection.Select(o => o.Name))}. Apply the requested change ONLY to the selected object(s) via \"edit\" (one entry per selected object, by name). Do NOT create new objects or touch other objects.");
            sb.AppendLine();

            if (_aiImages.Count > 0)
            {
                sb.AppendLine("IMAGES the user provided (use them in \"animations\" frames or \"sprite\"): " + string.Join(", ", _aiImages.Select(Path.GetFileName)));
                sb.AppendLine("Group frames by their names (e.g. idle*, walk*, attack*) into animations with those names.");
            }
            if (_aiSounds.Count > 0)
            {
                sb.AppendLine("SOUNDS the user provided: " + string.Join(", ", _aiSounds.Select(Path.GetFileName)));
                sb.AppendLine("To play a sound, add a SoundEmitter behavior with texts.asset = the sound filename (nums onStart=1 to play at start, loop=1 to loop, volume 0-1).");
            }
            if (_aiImages.Count > 0 || _aiSounds.Count > 0) sb.AppendLine();

            sb.AppendLine();
            sb.AppendLine("Now answer ONLY the user's request below: change just what it asks, edit objects that already exist instead of re-creating them, and keep everything else as it is.");
            return sb.ToString();
        }
    }
}
