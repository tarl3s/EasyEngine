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


        // ================================================================================================================
        //  The 🔨 Build prompt, made for each request: a short fixed core (the JSON shape, the essential rules, every
        //  behavior/event/action by name), then only what the request is about — the hints whose words it uses (each one a
        //  line or two, most with the exact JSON to copy), the details of the behaviors involved, and the scene. Small and
        //  local models read it fast and aren't distracted; free cloud tiers spend far fewer tokens.
        // ================================================================================================================

        internal record PromptHint(string[] Tags, string Text, BehaviorKind[] Kinds);
        private static PromptHint H(string tags, string text, params BehaviorKind[] kinds) => new(tags.Split(' ', StringSplitOptions.RemoveEmptyEntries), text, kinds);

        // The words of a hint are compared as 5-letter stems with the request (MainWindow.AiRules.cs Words: "coins" = "coin",
        // Portuguese/Spanish mapped to English), so "moedas" finds the coin hint.
        private static readonly PromptHint[] Hints =
        {
            H("player jogador hero heroi faster rapido speed velocid jump pula pulo higher health vida hearts coraç dash double duplo wall bigger",
              "The PLAYER is the object with Platformer (or TopDown): change it with \"edit\" by its name, never create another. Platformer nums: speed, jump, doubleJump/wallJump/dash (0 or 1). Hearts = Health nums.max.",
              BehaviorKind.Platformer, BehaviorKind.Health),
            H("enemy enemies inimigo monster monstro slime patrol patrulha walks anda back forth chase chases persegue follows segue flee foge away near perto",
              "Enemy = physics Dynamic + Health (respawn 0) + Damager (target Player) + a mover: Patrol (back and forth) or Chase (target Player). Run away from the player (always): {\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"to\":\"sign(self.cx - Player.cx) * 160\"}.",
              BehaviorKind.Health, BehaviorKind.Damager, BehaviorKind.Patrol, BehaviorKind.Chase),
            H("near perto close closer proximo distance distancia within pixels",
              "Only when the player is near: two rules — {\"when\":\"always\",\"cond\":\"dist(self.cx,self.cy,Player.cx,Player.cy) < 250\",\"do\":\"set\",\"prop\":\"vx\",\"to\":\"sign(Player.cx - self.cx) * 150\"} and the same with \"cond\":\"dist(self.cx,self.cy,Player.cx,Player.cy) >= 250\" setting vx to 0."),
            H("shoot shoots atira atirar turret torre gun arma bullet bala fire dispara",
              "Shooting by itself: Shooter texts{key:\"\",aim:\"Player\"} nums{directions:1,cooldown:2} (no key = fires on its own). Or a rule {\"when\":\"timer\",\"every\":1,\"do\":\"shoot\",\"directions\":1,\"at\":\"Player\"} (add a \"cond\" to limit it, e.g. \"Player.cx < self.cx\" = player on its left).",
              BehaviorKind.Shooter),
            H("spawn spawns appear aparec surge rain chuva fall falling cai caem drop rocks pedras every cada wave onda",
              "To make things appear during play: create it with \"asPrefab\":true (it waits outside the scene), then a Spawner behavior (texts target, at:\"top\" = falls from a random x at the top; nums interval, max 0 = no limit) or a rule {\"do\":\"spawn\",\"object\":\"Rock\",\"at\":\"top\"}.",
              BehaviorKind.Spawner),
            H("every cada seconds segundos second after depois later wait espera timer countdown contagem time tempo",
              "Timing: {\"when\":\"timer\",\"every\":2,…} repeats (the first time after 2 s); {\"when\":\"after\",\"seconds\":5,…} happens once. 60 s countdown: variables {\"scene\":{\"timer\":60}} + rule {\"when\":\"timer\",\"every\":1,\"cond\":\"timer > 0\",\"do\":\"addvar\",\"var\":\"timer\",\"amount\":-1} + a Label \"Time: {timer}\"."),
            H("fade sumir some desaparec vanish glide desliz grow cresce shrink encolhe pulse pulsa blink pisca opacity invisible invisivel transpar slowly devagar smooth hide esconde show",
              "Smooth change: {\"do\":\"tween\",\"prop\":\"opacity|scale|x|y|rot|w|h\",\"to\":0,\"duration\":2} (+ \"loop\":1,\"yoyo\":1 to repeat back and forth). Fade out after 5 s: {\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":3}. Blink: {\"when\":\"start\",\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":0.5,\"loop\":1,\"yoyo\":1}. Invisible at once: the object's \"opacity\":0, or do:hide / do:show."),
            H("move moving moves mexe anda right left direita esquerda circle circulo follow follows segue mouse teleport teleporta position posição keeps",
              "Movement rules: keeps moving right {\"when\":\"always\",\"do\":\"velocity\",\"vx\":150,\"vy\":0}; follow the mouse {\"when\":\"always\",\"do\":\"set\",\"prop\":\"x\",\"to\":\"mousex - self.w/2\"} and the same for y; a circle {\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"to\":\"-sin(time*2) * 200\"} plus vy with cos; teleport {\"do\":\"set\",\"prop\":\"x\",\"to\":600}."),
            H("press presses key tecla aperta apertar hold holds segura near perto button",
              "Keys: {\"when\":\"keypress\",\"key\":\"E\",…} once per press; {\"when\":\"key\",\"key\":\"R\",…} every moment while held. Only near something (on the player): \"cond\":\"dist(self.cx,self.cy,Shopkeeper.cx,Shopkeeper.cy) < 120\"."),
            H("touch touches encosta encostar toca tocar collect collects coleta pega pegar hit bate collide step steps pisa pisar lands",
              "{\"when\":\"touch\",\"name\":\"Coin\",…} fires once when it starts touching that object (standing on it counts). Put it on either of the two; \"name\" is the OTHER one."),
            H("score pontos pontu points point coin coins moeda counter conta lives vidas variable variavel double dobra reaches chega",
              "Variables: top-level \"variables\":{\"global\":{\"score\":0,\"lives\":3}}. Rules addvar/setvar (var; amount/to may be a formula like \"score * 2\"); {\"when\":\"ifvar\",\"var\":\"score\",\"op\":\">=\",\"value\":10,…}. Show it: a Label text \"Score: {score}\" on the UI layer. A coin worth 10: Collectible texts{var:\"score\"} nums{points:10}.",
              BehaviorKind.Label, BehaviorKind.Collectible),
            H("scene cena level fase gameover next proxima door porta exit saida go vai",
              "Change scene: rule {\"do\":\"goscene\",\"scene\":\"Level2\"}; a door: GoToScene texts{scene}. Use a scene that exists (see SCENE).",
              BehaviorKind.GoToScene),
            H("die dies morre morrer death morte kill mata explode explodes dead runs out acabam",
              "{\"when\":\"death\",…} runs when its health reaches 0 (also the player's, who then comes back). Explode: {\"when\":\"death\",\"do\":\"emit\",\"count\":30}. Lives that run out: a lives variable — {\"when\":\"ifvar\",\"var\":\"lives\",\"op\":\"<=\",\"value\":0,\"do\":\"goscene\",\"scene\":\"GameOver\"}; lose one: addvar lives -1, back to the start: set x/y."),
            H("say says diz dizer fala message mensagem dialog dialogo talk conversa shows mostra hello ola",
              "{\"do\":\"say\",\"text\":\"Hello!\",\"speaker\":\"Bob\"} shows a dialogue box (lines split by |)."),
            H("item items sword espada buy compra shop loja inventory inventario pay paga",
              "Items: {\"do\":\"additem\",\"item\":\"Sword\",\"amount\":1}, removeitem; check with \"cond\":\"item:Sword >= 1\". Pay 10 coins: two rules on the same key with \"cond\":\"coins >= 10\" — addvar coins -10 and additem (the item rule FIRST)."),
            H("color colour cor red vermelho blue azul green verde yellow amarelo black preto white branco purple roxo",
              "Colour: \"color\":\"#RRGGBB\" on the object; during play {\"do\":\"tint\",\"color\":\"#FF0000\"} (\"target\" for another object)."),
            H("big bigger maior small smaller menor size tamanho twice dobro huge",
              "Size: \"width\"/\"height\" in pixels on the object (twice as big = double both)."),
            H("level fase stage room sala map mapa platforms plataformas stairs staircase escada build monte constroi coins moedas row fileira",
              "A WHOLE LEVEL: {\"level\":{\"platforms\":3,\"coins\":5,\"enemies\":2,\"spikes\":1,\"door\":\"Level2\"}} — the engine lays it out (also \"stairs\":5, \"checkpoint\":true, \"enemyKind\":\"chase\", a walled top-down room \"style\":\"topdown\"). Use it for any level, stage, room or map."),
            H("platform plataforma ledge moving elevator elevador lift wall parede ground chao floor piso oneway",
              "Platform/wall = Solid with physics Static. Moving platform = Solid + Patrol (nums axis 1 = up and down, distance, speed). One-way = Solid nums oneWay 1.",
              BehaviorKind.Solid, BehaviorKind.Patrol),
            H("boss chefe chefao",
              "Boss = Health (respawn 0) + Damager + BossAI (texts moves:ground|fly, starts:start|near; its attacks come from its card) — or Chase + Health + Damager for a simple one.",
              BehaviorKind.BossAI, BehaviorKind.Health, BehaviorKind.Damager),
            H("bounce bounces quica pula ball bola gravity gravidade float flutua",
              "Physics: \"physics\":{\"mode\":\"Dynamic\",\"bounciness\":0.8,\"gravity\":1} (Dynamic falls; bounciness 0-1)."),
            H("screen tela leave sai sair inside dentro edge borda wrap",
              "Keep it inside the screen/scene: StayInside behavior.", BehaviorKind.StayInside),
            H("delete deleta apaga apagar remove remova tira destroy",
              "\"delete\":[\"Name\"] removes objects. In an edit, \"remove\":[\"Health\"] takes a behavior off. Stop a rule: send the object's full new \"custom\" list with \"replaceCustom\":true."),
            H("stop para parar pare don't nao never",
              "To STOP something an object does: edit it with \"replaceCustom\":true and its \"custom\" list without that rule (or \"remove\":[\"Spin\"] for a behavior)."),
            H("button botao menu start iniciar click clique clicked",
              "Clicked: {\"when\":\"click\",…}. A menu button: UIButton texts{action:\"goscene\",scene} on the UI layer, with a Label for its text.",
              BehaviorKind.UIButton),
            H("npc sign placa chest bau shopkeeper vendedor",
              "Press E to talk/read/open: Interact behavior.", BehaviorKind.Interact),
            H("lock locked tranca trancada key chave gate portao",
              "Locked door + key: door = Gate texts{var:\"hasKey\"}; key = AbilityPickup texts{var:\"hasKey\"}. Or a rule on the player with \"cond\":\"hasKey >= 1\".",
              BehaviorKind.Gate, BehaviorKind.AbilityPickup),
            H("spike espinho trap armadilha lava hazard hurt hurts dano damage machuca",
              "Hurts on touch: Damager (target Player, nums damage) on the hazard; the player needs Health.", BehaviorKind.Damager),
            H("trampoline trampolim spring mola jump pad",
              "Spring behavior (nums force).", BehaviorKind.Spring),
            H("crate caixa push empurra",
              "Pushable behavior.", BehaviorKind.Pushable),
            H("checkpoint bandeira flag respawn",
              "Checkpoint behavior.", BehaviorKind.Checkpoint),
            H("night noite day dia sun sol moon lua",
              "Day and night: a DayNight object. In rules: {\"when\":\"isnight\",…} or \"cond\":\"isNight >= 1\" (\"isNight < 1\" = day).",
              BehaviorKind.DayNight),
            H("particle particles particulas explosion explosao sparks faiscas smoke fumaca",
              "Particles: a burst {\"do\":\"emit\",\"count\":24,\"color\":\"#FF8800\"}, or the Particles behavior.", BehaviorKind.Particles),
            H("light luz glow brilho torch tocha lamp", "LightSource behavior.", BehaviorKind.LightSource),
            H("bar barra healthbar progress", "A bar: Bar texts{var:\"health\"} on the UI layer.", BehaviorKind.Bar),
            H("draw desenha desenhe pixel sprite art arte",
              "Draw its look: \"pixelArt\":{\"palette\":{\"X\":\"#RRGGBB\"},\"rows\":[\"..XX..\",\".XXXX.\"]} ('.' = transparent, ≤16 px wide)."),
            H("round arredond border borda font fonte bold style estilo look",
              "Looks: \"opacity\" 0-1, \"cornerRadius\" px, \"borderColor\"+\"borderWidth\", \"font\", \"bold\"."),
            H("hitbox transparent transparente empty vazio margin margem",
              "Its picture has empty space around it: \"fitHitbox\":true (collides with the drawing)."),
            H("front frente behind atras order ordem",
              "Draw order: \"z\":n (higher = in front) on the object."),
            H("procedural random aleatorio generate gera generator",
              "Generator behavior: fills its area with prefabs when the scene loads.", BehaviorKind.Generator),
        };

        /// <summary>Every hint's text (tests check that each rule they show survives the block editor).</summary>
        internal static string HintsText => string.Join("\n", Hints.Select(h => h.Text));

        // the most relevant hints for the request (by shared word stems), at most 6, in library order
        internal static List<PromptHint> HintsFor(string request)
        {
            var asked = Words(request);
            return Hints.Select((h, i) => (h, i, score: h.Tags.Count(t => Words(t).Overlaps(asked))))
                        .Where(x => x.score > 0).OrderByDescending(x => x.score).ThenBy(x => x.i).Take(6).OrderBy(x => x.i).Select(x => x.h).ToList();
        }

        private string BuildProjectPrompt(string request)
        {
            var sb = new StringBuilder();
            var kinds = BehaviorCatalog.All.Where(i => !i.Hidden && i.Kind != BehaviorKind.Tilemap).ToList();
            sb.AppendLine("You build 2D games in EasyEngine. Answer ONLY one JSON object, using only the keys you need:");
            sb.AppendLine("{\"create\":[OBJ],\"edit\":[OBJ],\"delete\":[\"Name\"],\"variables\":{\"global\":{\"score\":0}},\"level\":{…}}");
            sb.AppendLine("OBJ = {\"name\",\"x\",\"y\",\"width\",\"height\",\"color\":\"#RRGGBB\",\"layer\",\"physics\":\"None|Static|Dynamic\",\"behaviors\":[{\"kind\",\"nums\":{},\"texts\":{}}],\"custom\":[RULE]}");
            sb.AppendLine("RULE = {\"when\":EVENT, event params, \"do\":ACTION, action params, \"cond\":\"optional formula\"} — custom rules are ADDED to an object's rules.");
            sb.AppendLine("- New thing → \"create\" (inside the scene). Existing object (SCENE below) → \"edit\" with its exact name. Change only what is asked.");
            sb.AppendLine("- Any number may be a formula: \"Player.x - self.x\", \"rand(1,3)\", \"score * 2\" (self.cx/cy/w/h/health, Name.x, time, mousex). x grows right, y grows DOWN.");
            var examples = ExamplesFor(request);
            if (examples.Count > 0) foreach (var (ask, json) in examples) sb.AppendLine($"Example — \"{ask}\" → {json}");
            else sb.AppendLine("Shapes: change a behavior{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":400}}]}]} · a new thing {\"create\":[{\"name\":\"Coin\",\"x\":500,\"y\":420,\"width\":24,\"height\":24,\"color\":\"#FFD700\",\"behaviors\":[{\"kind\":\"Collectible\"}]}]} · a rule {\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"timer\",\"every\":2,\"do\":\"jump\",\"force\":600}]}]}");
            sb.AppendLine("Behaviors: " + string.Join(", ", kinds.Select(i => i.Kind)));
            sb.AppendLine("EVENTS: " + string.Join(", ", AiSpecFix.Events.Where(e => e is not ("keydown" or "hover"))));
            sb.AppendLine("ACTIONS: " + string.Join(", ", AiSpecFix.Actions.Where(a => a != "accel")));

            // what the request is about: hints, then the behaviors they (and the request) name
            var hints = HintsFor(request);
            if (hints.Count > 0) { sb.AppendLine("HOW TO (for this request):"); foreach (var h in hints) sb.AppendLine("- " + h.Text); }
            var detail = hints.SelectMany(h => h.Kinds).Concat(MatchBehaviors(request)).Distinct().Take(7).ToList();
            foreach (var k in detail) sb.AppendLine(BehaviorLine(BehaviorCatalog.Info(k)));
            // the params of the events/actions those hints use, from the block palette (the blocks and the AI share one language)
            var used = hints.SelectMany(h => System.Text.RegularExpressions.Regex.Matches(h.Text, "\"(?:when|do)\":\"(\\w+)\"").Select(m => m.Groups[1].Value)).ToHashSet();
            var docs = BlockEditorWindow.RuleDocs().Where(d => used.Contains(d.id)).Take(10).ToList();
            if (docs.Count > 0) sb.AppendLine("PARAMS: " + string.Join(" · ", docs.Select(d => d.doc)));

            // the scene
            sb.AppendLine($"SCENE \"{_scene.Name}\" {_scene.Width:0}x{_scene.Height:0}" + (GroundTop() is double g ? $", ground top y={g:0}" : "") +
                          $". Layers: {string.Join(", ", _scene.Layers.Select(l => l.Name + (l.ScreenSpace || l.Kind == LayerKind.UI ? "(fixed UI)" : "")))}. Scenes: {string.Join(", ", _project.Scenes.Select(s => s.Name))}.");
            int ruleBudget = 1200; // a big project must not blow the free tiers' token limits
            foreach (var o in _scene.Layers.SelectMany(l => l.Objects))
            {
                var script = Compact(o.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Custom)?.Text("script", "") ?? "");
                var shown = script.Length > 400 ? script[..400] + "…" : script;
                var rules = script.Length <= 2 ? "" : shown.Length <= ruleBudget ? " custom=" + shown : " custom=(has rules)"; // so a change can keep or retune them
                if (script.Length > 2 && shown.Length <= ruleBudget) ruleBudget -= shown.Length;
                var ov = VarScope.Of(o).Count > 0 ? " vars{" + VarScope.Of(o).Describe() + "}" : "";
                sb.AppendLine($"  \"{o.Name}\" [{string.Join(",", o.Behaviors.Where(b => b.Kind != BehaviorKind.Custom).Select(b => b.Kind))}] at {o.X:0},{o.Y:0}{ov}{(o.Locked ? " LOCKED" : "")}{(o.Z != 0 ? $" z{o.Z}" : "")}{rules}");
            }
            if (VarScope.Of(_project).Count > 0) sb.AppendLine("Global variables: " + VarScope.Of(_project).Describe());
            if (VarScope.Of(_scene).Count > 0) sb.AppendLine("Scene variables: " + VarScope.Of(_scene).Describe());
            var prefabs = _project.Prefabs.Select(p => p.Name).Where(n => !PrefabFactory.Defaults().Any(d => d.Name == n)).ToList();
            if (prefabs.Count > 0) sb.AppendLine("Prefabs (spawnable): " + string.Join(", ", prefabs));
            if (_selection.Count > 0)
                sb.AppendLine($"SELECTED: {string.Join(", ", _selection.Select(o => o.Name))} — change ONLY these, with \"edit\".");
            if (_aiImages.Count > 0) sb.AppendLine("IMAGES given (use in \"sprite\" or \"animations\" frames, grouped by name: idle*, walk*…): " + string.Join(", ", _aiImages.Select(Path.GetFileName)));
            if (_aiSounds.Count > 0) sb.AppendLine("SOUNDS given (SoundEmitter texts.asset = file name; nums onStart, loop, volume): " + string.Join(", ", _aiSounds.Select(Path.GetFileName)));
            return sb.ToString();
        }

        // one behavior in a line: "Patrol — Moves back and forth… nums speed=90 distance=200 axis=0(0 ↔|1 ↕); texts —"
        private static string BehaviorLine(BehaviorInfo info)
        {
            static string Opts(string[]? o) => o == null ? "" : "(" + string.Join("|", o.Select(x => x.Split('|')[0])) + ")";
            var desc = info.Desc.Split(new[] { ". " }, 2, StringSplitOptions.None)[0];
            if (desc.Length > 110) desc = desc[..110] + "…";
            var nums = string.Join(" ", info.Nums.Select(n => $"{n.Key}={n.Default:0.##}{Opts(n.Options)}"));
            var texts = string.Join(" ", info.Texts.Where(t => t.Pick != PickKind.Special).Select(t => $"{t.Key}={(t.Default.Length > 0 ? t.Default : "\"\"")}{Opts(t.Options)}"));
            return $"  {info.Kind} — {desc}." + (nums.Length > 0 ? " nums " + nums : "") + (texts.Length > 0 ? "; texts " + texts : "");
        }

        // the top of the widest solid near the bottom (where things stand), if there is one
        private double? GroundTop() => _scene.Layers.SelectMany(l => l.Objects)
            .Where(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Solid) && o.Width >= _scene.Width * 0.4 && o.Y > _scene.Height * 0.4)
            .OrderByDescending(o => o.Width).Select(o => (double?)o.Y).FirstOrDefault();

    }
}
