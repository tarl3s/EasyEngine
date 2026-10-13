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
    // Applying the AI's 🔨 Build reply to the project: objects created/edited (behaviors, rules, looks, physics,
    // pixel art, animations), variables, layers, and the guesses that keep a small model's answer on target.
    public partial class MainWindow
    {
        // Applies a behavior/property spec (JsonElement object) to an existing object. Caller handles undo.
        private bool ApplyBehaviorsToObject(GameObjectData target, JsonElement spec, string userPrompt)
        {
            if (target.Locked) { if (!_aiSkippedLocked.Contains(target.Name)) _aiSkippedLocked.Add(target.Name); return false; } // locked: never edited by the AI
            var result = target.Behaviors.Select(JsonClone).ToList();
            bool changed = false;
            var impliedRules = new List<string>(); // rules the behaviors stand for (a Custom written as a behavior, a door "on death")
            double? behaviorBounce = null, behaviorGravity = null; // physics the model wrote inside a behavior
            AiAddedKinds.Clear(); AiKeyWritten.Clear();

            if (spec.TryGetProperty("behaviors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var raw in arr.EnumerateArray())
                {
                    if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("kind", out var kindEl)) continue;
                    var kindOpt = ResolveKind(kindEl.ValueKind == JsonValueKind.String ? kindEl.GetString() : null);
                    if (kindOpt is not BehaviorKind kind || kind == BehaviorKind.Tilemap) continue;
                    if (kind == BehaviorKind.Custom) { AiSpecFix.CustomAsBehavior(raw, impliedRules); continue; }
                    if (LooseJson.Node(raw) is not System.Text.Json.Nodes.JsonObject node) continue; // unreadable: skipped, never a crash
                    AiSpecFix.Behavior(BehaviorCatalog.Info(kind), node, impliedRules);
                    if (node.ContainsKey("drop")) continue;
                    if (node["_bounciness"] is System.Text.Json.Nodes.JsonNode nb && LooseJson.TryParse(nb.ToString(), out var nbv)) behaviorBounce = nbv;
                    if ((node["_gravity"] ?? node["_gravityscale"]) is System.Text.Json.Nodes.JsonNode ng && LooseJson.TryParse(ng.ToString(), out var ngv)) behaviorGravity = ngv;
                    if (!target.Behaviors.Any(b => b.Kind == kind)) AiAddedKinds.Add(kind); // new on this object (habits are only stripped from those)
                    if ((node["texts"] as System.Text.Json.Nodes.JsonObject)?["key"] != null) AiKeyWritten.Add(kind);
                    using var fixedDoc = JsonDocument.Parse(node.ToJsonString());
                    var item = fixedDoc.RootElement.Clone();

                    var existing = result.FirstOrDefault(x => x.Kind == kind);
                    var data = existing ?? BehaviorCatalog.Info(kind).CreateData();
                    if (item.TryGetProperty("nums", out var nums) && nums.ValueKind == JsonValueKind.Object)
                        foreach (var p in nums.EnumerateObject())
                            if (LooseJson.TryNumber(p.Value, out var nv)) data.Nums[p.Name] = nv;
                    if (item.TryGetProperty("texts", out var texts) && texts.ValueKind == JsonValueKind.Object)
                        foreach (var p in texts.EnumerateObject())
                            if (p.Value.ValueKind == JsonValueKind.String) data.Texts[p.Name] = p.Value.GetString() ?? "";
                            else if (p.Value.ValueKind == JsonValueKind.Number) data.Texts[p.Name] = p.Value.GetRawText(); // tolerate quoted-less values
                    if (existing == null) result.Add(data);
                    changed = true;
                }
            }

            if (spec.TryGetProperty("remove", out var rem) && rem.ValueKind == JsonValueKind.Array)
                foreach (var r in rem.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String && ResolveKind(r.GetString()) is BehaviorKind rk)
                        if (result.RemoveAll(x => x.Kind == rk) > 0) changed = true;

            // the rules: "custom" (also "rules"/"script", a single rule object, or a JSON string of them) plus the implied ones, cleaned up
            foreach (var key in new[] { "custom", "rules", "script" })
                if (spec.TryGetProperty(key, out var c)) { AiSpecFix.CollectRules(c, impliedRules); break; }
            if (impliedRules.Count > 0)
            {
                using var rulesDoc = JsonDocument.Parse(AiSpecFix.Rules(impliedRules, _aiNotes));
                var cust = rulesDoc.RootElement;
                if (cust.GetArrayLength() > 0)
                {
                    var existing = result.FirstOrDefault(x => x.Kind == BehaviorKind.Custom);
                    var data = existing ?? new BehaviorData { Kind = BehaviorKind.Custom };
                    bool replace = spec.TryGetProperty("replaceCustom", out var rc) && LooseJson.IsTrue(rc);
                    data.Texts["script"] = existing == null || replace ? cust.GetRawText() : MergeRules(existing.Text("script", "[]"), cust);
                    if (existing == null) result.Add(data);
                    changed = true;
                }
            }

            if (ApplyBasics(target, spec)) changed = true;
            if (ApplyAnimations(target, spec)) changed = true;

            // resolve sound filenames (SoundEmitter asset) to project-relative paths
            foreach (var b in result.Where(b => b.Kind == BehaviorKind.SoundEmitter))
                if (b.Texts.TryGetValue("asset", out var a) && !string.IsNullOrWhiteSpace(a) && !a.Replace('\\', '/').Contains("Assets/"))
                { var rel = ResolveSound(a); if (rel != null) b.Texts["asset"] = rel; }

            // object variables
            if (spec.TryGetProperty("variables", out var ov) && ov.ValueKind == JsonValueKind.Object)
                foreach (var p in ov.EnumerateObject())
                    if (VarScope.Of(target).SetFromJson(p.Name, p.Value, out _)) changed = true; // number, true/false (Yes/No) or words (Text)
            if (spec.TryGetProperty("removeVariables", out var orv) && orv.ValueKind == JsonValueKind.Array)
                foreach (var el in orv.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && VarScope.Of(target).Remove(el.GetString() ?? "")) changed = true;

            // SAFEGUARD: Platformer/TopDown read the PLAYER's keyboard. On anything that isn't the player,
            // turn OFF the default controls so it doesn't mirror the player (it keeps the movement abilities;
            // move it with custom keys or a script).
            bool looksLikePlayer = NameLooksPlayer(target.Name) || result.Any(b => b.Kind == BehaviorKind.FollowCamera);
            if (!looksLikePlayer)
                foreach (var b in result.Where(b => b.Kind == BehaviorKind.Platformer || b.Kind == BehaviorKind.TopDown))
                    if (b.Num("controls", 1) >= 0.5) { b.Nums["controls"] = 0; changed = true; }

            bool customJumps = result.Any(b => b.Kind == BehaviorKind.Custom && b.Text("script", "").ToLowerInvariant().Contains("\"jump\""));

            // infer physics from resulting behaviors (don't trust the model's physics field)
            bool HasK(BehaviorKind k) => result.Any(x => x.Kind == k);
            bool hasMover = HasK(BehaviorKind.Platformer) || HasK(BehaviorKind.TopDown) || HasK(BehaviorKind.Patrol) ||
                            HasK(BehaviorKind.AdvancedPatrol) || HasK(BehaviorKind.Chase) || HasK(BehaviorKind.Projectile) || HasK(BehaviorKind.Shooter);
            var low = userPrompt.ToLowerInvariant();
            bool gravityIntent = new[] { "gravity", "gravid", "fall", "falling", "cair", "queda" }.Any(low.Contains);
            PhysicsMode desired = target.Physics.Mode;
            var (askedMode, bounce, grav) = SpecPhysics(spec);
            bounce ??= behaviorBounce; grav ??= behaviorGravity;
            if (StripCopiedHabits(target, result, userPrompt)) changed = true;
            if (bounce is double bv) { target.Physics.Bounciness = Math.Clamp(bv, 0, 1); changed = true; }
            if (grav is double gv) { target.Physics.GravityScale = Math.Clamp(gv, -5, 5); changed = true; }
            if (HasK(BehaviorKind.Platformer) || gravityIntent || customJumps) desired = PhysicsMode.Dynamic;
            else if (bounce > 0 && (askedMode == PhysicsMode.Dynamic || askedMode == null && !result.Any(b => StaysPut.Contains(b.Kind)))) // a bouncy ball falls and bounces; a bouncy platform stays put
            {
                desired = PhysicsMode.Dynamic;
                if (result.RemoveAll(b => b.Kind == BehaviorKind.Solid) > 0) changed = true;
            }
            else if (HasK(BehaviorKind.Solid) && !hasMover) desired = PhysicsMode.Static;
            // the model's own "physics" is trusted only where it can't pin a HUD/door/pickup to gravity by mistake
            else if ((askedMode == PhysicsMode.Dynamic || bounce > 0) && !result.Any(b => StaysPut.Contains(b.Kind))) desired = PhysicsMode.Dynamic;
            if (desired != target.Physics.Mode) { target.Physics.Mode = desired; changed = true; }

            target.Behaviors = result;
            return changed;
        }

        // x/y/width/height/color
        private bool ApplyBasics(GameObjectData obj, JsonElement spec)
        {
            bool changed = false;
            if (spec.TryGetProperty("x", out var x) && LooseJson.TryNumber(x, out var xv)) { obj.X = xv; changed = true; }
            if (spec.TryGetProperty("y", out var y) && LooseJson.TryNumber(y, out var yv)) { obj.Y = yv; changed = true; }
            if (spec.TryGetProperty("width", out var w) && LooseJson.TryNumber(w, out var wv)) { obj.Width = Math.Max(2, wv); changed = true; }
            if (spec.TryGetProperty("height", out var h) && LooseJson.TryNumber(h, out var hv)) { obj.Height = Math.Max(2, hv); changed = true; }
            if (spec.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String) { obj.Color = c.GetString() ?? obj.Color; changed = true; }
            if (spec.TryGetProperty("sprite", out var sp) && sp.ValueKind == JsonValueKind.String)
            {
                var rel = ResolveImage(sp.GetString() ?? "");
                if (rel != null) { obj.SpritePath = rel; changed = true; }
            }
            if (spec.TryGetProperty("pixelArt", out var pa) && pa.ValueKind == JsonValueKind.Object)
            {
                var rel = RenderPixelArt(pa, obj.Name + "_sprite");
                if (rel != null) { obj.SpritePath = rel; changed = true; }
            }
            // appearance / aesthetics
            if (spec.TryGetProperty("opacity", out var op) && LooseJson.TryNumber(op, out var opv)) { obj.Opacity = Math.Clamp(opv, 0, 1); changed = true; }
            if (spec.TryGetProperty("borderColor", out var bc) && bc.ValueKind == JsonValueKind.String) { obj.BorderColor = bc.GetString(); changed = true; }
            if (spec.TryGetProperty("borderWidth", out var bw) && LooseJson.TryNumber(bw, out var bwv)) { obj.BorderWidth = Math.Max(0, bwv); changed = true; }
            if (spec.TryGetProperty("cornerRadius", out var cr) && LooseJson.TryNumber(cr, out var crv)) { obj.CornerRadius = Math.Max(0, crv); changed = true; }
            if (spec.TryGetProperty("font", out var ff) && ff.ValueKind == JsonValueKind.String) { obj.FontFamily = ff.GetString(); changed = true; }
            if (spec.TryGetProperty("bold", out var bd) && AsBool(bd, out var bdv)) { obj.Bold = bdv; changed = true; }
            if (spec.TryGetProperty("fitHitbox", out var fh) && AsBool(fh, out var fhv)) { obj.Physics.FitHitbox = fhv; changed = true; } // collide with the drawing (HitboxFit.cs)
            if (spec.TryGetProperty("group", out var gr) && gr.ValueKind == JsonValueKind.String) { obj.Group = gr.GetString() ?? ""; changed = true; }
            if (spec.TryGetProperty("z", out var zz) && LooseJson.TryNumber(zz, out var zv)) { obj.Z = (int)Math.Round(zv); changed = true; } // order inside its layer
            return changed;
        }

        private bool ApplyAnimations(GameObjectData obj, JsonElement spec)
        {
            if (!spec.TryGetProperty("animations", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
            bool changed = false;
            foreach (var a in arr.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var anim = new SpriteAnimation { Name = a.TryGetProperty("name", out var nm) ? (nm.GetString() ?? "anim") : "anim" };
                if (a.TryGetProperty("fps", out var fps) && LooseJson.TryNumber(fps, out var fpsv)) anim.Fps = Math.Clamp(fpsv, 1, 60);
                if (a.TryGetProperty("loop", out var lp) && AsBool(lp, out var lpv)) anim.Loop = lpv;
                if (a.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
                    foreach (var f in fr.EnumerateArray())
                    {
                        if (f.ValueKind == JsonValueKind.String) { var rel = ResolveImage(f.GetString() ?? ""); if (rel != null) anim.Frames.Add(rel); }
                        else if (f.ValueKind == JsonValueKind.Object) { var rel = RenderPixelArt(f, obj.Name + "_" + anim.Name + "_" + anim.Frames.Count); if (rel != null) anim.Frames.Add(rel); }
                    }
                if (anim.Frames.Count == 0) continue;
                obj.Animations.RemoveAll(x => x.Name.Equals(anim.Name, StringComparison.OrdinalIgnoreCase));
                obj.Animations.Add(anim);
                obj.DefaultAnimation ??= anim.Name;
                changed = true;
            }
            return changed;
        }

        // ---- AI-drawn sprites: pixel art described as a palette + rows of characters → PNG ----
        private static bool ParseColorBgra(string s, out (byte b, byte g, byte r, byte a) c)
        {
            c = (0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return true; // transparent
            var t = s.Trim().ToLowerInvariant();
            if (t is "." or "none" or "transparent" or "clear" or "empty") return true;
            try { var col = (Color)ColorConverter.ConvertFromString(s.Trim()); c = (col.B, col.G, col.R, 255); return true; }
            catch { return false; }
        }

        private string? RenderPixelArt(JsonElement art, string baseName)
        {
            if (string.IsNullOrEmpty(_project.FolderPath)) return null; // need a saved project to write the PNG
            JsonElement rowsEl;
            if (!(art.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array) &&
                !(art.TryGetProperty("pixels", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)) return null;

            var rows = new List<string>();
            foreach (var r in rowsEl.EnumerateArray()) if (r.ValueKind == JsonValueKind.String) rows.Add(r.GetString() ?? "");
            if (rows.Count == 0) return null;

            var pal = new Dictionary<char, (byte b, byte g, byte r, byte a)>();
            if (art.TryGetProperty("palette", out var pj) && pj.ValueKind == JsonValueKind.Object)
                foreach (var p in pj.EnumerateObject())
                {
                    if (p.Name.Length == 0) continue;
                    var val = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : "";
                    if (ParseColorBgra(val, out var col)) pal[p.Name[0]] = col;
                }

            int h = Math.Min(rows.Count, 128);
            int w = Math.Min(128, rows.Max(r => r.Length));
            if (w < 1 || h < 1) return null;
            var data = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                var row = rows[y];
                for (int x = 0; x < w && x < row.Length; x++)
                {
                    char ch = row[x];
                    if (ch is '.' or ' ' or '0') continue; // transparent background
                    if (!pal.TryGetValue(ch, out var col) || col.a == 0) continue;
                    int i = (y * w + x) * 4;
                    data[i] = col.b; data[i + 1] = col.g; data[i + 2] = col.r; data[i + 3] = 255;
                }
            }
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, data, w * 4);
            try { return ProjectIO.SaveSprite(_project, bmp, baseName); } catch { return null; }
        }

        // Maps the model's loose kind strings to real BehaviorKinds (synonyms) — key to small-model reliability.
        private static BehaviorKind? ResolveKind(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (Enum.TryParse<BehaviorKind>(s, true, out var k)) return k == BehaviorKind.Melee ? BehaviorKind.ToolUse : k; // Melee is legacy
            var t = s.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            return t switch
            {
                "walk" or "run" or "move" or "movement" or "jump" or "platform" or "platforming" or "sidescroller" or "sidescroll" or "jumping" or "move+jump" => BehaviorKind.Platformer,
                "topdown" or "eightway" or "8way" or "wasd" or "topdownmovement" => BehaviorKind.TopDown,
                "attack" or "sword" or "hit" or "nail" or "slash" or "swing" or "swordattack" or "meleeattack" or "melee" => BehaviorKind.ToolUse,
                "heal" or "hp" or "life" or "lives" or "healthbar" or "hitpoints" => BehaviorKind.Health,
                "hurt" or "damage" or "spike" or "spikes" or "hazard" or "contactdamage" or "trap" or "dmg" => BehaviorKind.Damager,
                "shoot" or "fire" or "gun" or "ranged" or "blaster" or "shooting" => BehaviorKind.Shooter,
                "bullet" or "projectilemove" => BehaviorKind.Projectile,
                "follow" or "camera" or "followcam" or "camerafollow" => BehaviorKind.FollowCamera,
                "pace" or "backandforth" => BehaviorKind.Patrol,
                "advancedpatrol" or "advanced patrol" or "pathpatrol" or "smartpatrol" or "patrolpath" or "drawnpath" => BehaviorKind.AdvancedPatrol,
                "pursue" or "follower" or "chaseplayer" => BehaviorKind.Chase,
                "rotate" or "rotation" or "spinning" => BehaviorKind.Spin,
                "generator" or "emitter" => BehaviorKind.Spawner,
                "audio" or "sfx" or "music" or "sound" => BehaviorKind.SoundEmitter,
                "glow" or "lamp" or "lantern" or "lighting" => BehaviorKind.LightSource,
                "tool" or "useitem" or "swingtool" or "usetool" or "axeuse" or "chop" or "weapon" => BehaviorKind.ToolUse,
                "tree" or "choppable" or "mineable" or "resource" or "harvest" or "ore" => BehaviorKind.Harvestable,
                "objectives" or "tasks" or "testchecklist" or "checks" => BehaviorKind.Checklist,
                "scrollbackground" => BehaviorKind.Parallax,
                "collect" or "coin" or "pickup" or "gem" or "collect ible" => BehaviorKind.Collectible,
                "door" or "teleport" or "goto" or "scenechange" or "portal" or "nextlevel" => BehaviorKind.GoToScene,
                "ground" or "wall" or "block" or "collider" or "floor" => BehaviorKind.Solid,
                "text" or "hud" or "score" or "scoretext" or "healthtext" => BehaviorKind.Label,
                "respawnpoint" or "flag" or "save" => BehaviorKind.Checkpoint,
                "offscreen" or "despawn" => BehaviorKind.DestroyOffscreen,
                "pushblock" or "pushableblock" or "crate" or "movableblock" or "sokoban" => BehaviorKind.Pushable,
                "lever" or "toggle" or "switchlever" => BehaviorKind.Switch,
                "gate" or "lockeddoor" or "lockedgate" or "barrier" or "keydoor" or "abilitygate" => BehaviorKind.Gate,
                "npc" or "sign" or "talk" or "dialogue" or "dialog" or "read" or "interactable" => BehaviorKind.Interact,
                "ability" or "unlock" or "keypickup" or "upgrade" or "powerup" or "abilitypickup" or "keyitem" => BehaviorKind.AbilityPickup,
                "zone" or "trigger" or "triggerzone" or "region" or "triggerarea" => BehaviorKind.Area,
                "procedural" or "procgen" or "proceduralgeneration" or "randommap" or "spawnarea" or "mapgenerator" => BehaviorKind.Generator,
                "pathfind" or "pathfinding" or "smartchase" or "navigate" or "chasearound" or "seek" => BehaviorKind.PathChase,
                "spring" or "bouncepad" or "trampoline" or "jumppad" or "bouncer" or "launchpad" => BehaviorKind.Spring,
                "minimap" or "radar" or "worldmap" => BehaviorKind.Minimap,
                "particle" or "particles" or "emitter" or "explosion" or "sparkle" or "smoke" or "fx" => BehaviorKind.Particles,
                "button" or "uibutton" or "menubutton" or "btn" => BehaviorKind.UIButton,
                "bar" or "healthbar" or "progressbar" or "progress" or "hpbar" or "manabar" => BehaviorKind.Bar,
                _ => null
            };
        }

        // the name IS a name for the player ("Hero", "Jogador") — not just contains one ("PlayerHealthBar" is a new thing)
        private static bool NameIsPlayer(string name) => name.Trim().ToLowerInvariant() is "player" or "the player" or "hero" or "jogador" or "personagem" or "character" or "protagonist" or "protagonista" or "pc" or "player1" or "player 1";

        private static bool NameLooksPlayer(string name)
        {
            var n = name.ToLowerInvariant();
            return new[] { "player", "hero", "protagon", "jogador", "personagem", "character", "pc" }.Any(n.Contains);
        }

        private static bool IsNewIntent(string p)
        {
            p = p.ToLowerInvariant();
            return new[] { "another", "a new", "new ", "second", "third", "spawn", "extra", "outro", "outra", "mais um", "mais uma", "novo ", "nova " }.Any(p.Contains);
        }

        private static bool IsModifyIntent(string p)
        {
            p = p.ToLowerInvariant();
            return new[] { "add", "give", "set ", "change", "increase", "more", "the player", "player's", "adicion", "aumenta", "muda", "coloca", "deixa", "bota", "poe", "põe", "vida do", "mais vida" }.Any(p.Contains);
        }

        private GameObjectData? ResolveTarget(string? name, string userPrompt)
        {
            var all = _scene.Layers.SelectMany(l => l.Objects).ToList();
            if (!string.IsNullOrWhiteSpace(name))
            {
                var exact = all.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                if (NameIsPlayer(name)) { var p = FindPlayer(); if (p != null) return p; }
                // a shortened name ("Enemy" for "Enemy 2") — but never a longer one ("PlayerHealthBar" isn't the Player): that's new
                return all.FirstOrDefault(o => o.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var low = userPrompt.ToLowerInvariant();
            if (low.Contains("player") || low.Contains("jogador") || low.Contains("character") || low.Contains("personagem")) return FindPlayer();
            return null;
        }

        private GameObjectData? FindPlayer()
        {
            var all = _scene.Layers.SelectMany(l => l.Objects).ToList();
            return all.FirstOrDefault(o => o.Name.Equals("Player", StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Platformer))
                ?? all.FirstOrDefault(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.TopDown))
                ?? all.FirstOrDefault(o => o.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // behaviors that turn an object into a platform/coin/enemy/door/etc. — almost never belong on the Player
        private static readonly BehaviorKind[] NonPlayerKinds =
        {
            BehaviorKind.Solid, BehaviorKind.Collectible, BehaviorKind.Patrol, BehaviorKind.AdvancedPatrol, BehaviorKind.Chase,
            BehaviorKind.GoToScene, BehaviorKind.Parallax, BehaviorKind.DestroyOffscreen, BehaviorKind.Projectile,
            BehaviorKind.Pushable, BehaviorKind.Switch, BehaviorKind.Gate, BehaviorKind.Interact,
            BehaviorKind.AbilityPickup, BehaviorKind.Area, BehaviorKind.Generator,
            BehaviorKind.PathChase, BehaviorKind.Spring, BehaviorKind.Minimap,
            BehaviorKind.Particles, BehaviorKind.UIButton, BehaviorKind.Bar
        };

        private static readonly string[] ThingNouns =
        {
            "coin","platform","enemy","boss","npc","door","spike","wall","obstacle","bullet","item","powerup",
            "crystal","orb","gem","rock","block","trap","portal","chest","monster","slime","ghost","turret","hazard","pickup","collectible",
            "ball","box","crate","flag","checkpoint","sign","button","trampoline","lever","star","ladder","cloud","tree","label","bar",
            "moeda","plataforma","inimigo","chefe","porta","espinho","parede","bola","caixa","bandeira","chave","placa","botão","botao","estrela","pedra","bloco","armadilha","baú","bau","monstro"
        };

        private static readonly string[] AbilityWords =
        { "health","hp","heart","life","lives","speed","faster","dash","double jump","wall jump","attack","sword","melee","shoot","gun","fire","glow","gravity","jump" };

        private static string? FirstThingNoun(string prompt)
        {
            var p = prompt.ToLowerInvariant();
            foreach (var n in ThingNouns)
                if (System.Text.RegularExpressions.Regex.IsMatch(p, $@"\b{n}s?\b")) return char.ToUpper(n[0]) + n.Substring(1);
            return null;
        }
        private static bool MentionsThing(string prompt) => FirstThingNoun(prompt) != null;
        private static bool HasAbility(string prompt) { var p = prompt.ToLowerInvariant(); return AbilityWords.Any(p.Contains); }

        private bool SpecHasNonPlayerBehavior(JsonElement spec)
        {
            if (!spec.TryGetProperty("behaviors", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
            foreach (var it in arr.EnumerateArray())
                if (it.ValueKind == JsonValueKind.Object && it.TryGetProperty("kind", out var k) && ResolveKind(k.GetString()) is BehaviorKind bk && NonPlayerKinds.Contains(bk))
                    return true;
            return false;
        }

        private string? ResolveImage(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;
            var f = Path.GetFileName(filename);
            var hit = _aiImages.FirstOrDefault(r => string.Equals(Path.GetFileName(r), f, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
            try
            {
                var dir = Path.Combine(_project.FolderPath!, "Assets", "sprites");
                if (Directory.Exists(dir))
                {
                    var match = Directory.GetFiles(dir).FirstOrDefault(p => string.Equals(Path.GetFileName(p), f, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return Path.Combine("Assets", "sprites", Path.GetFileName(match));
                }
            }
            catch { }
            return null;
        }


        // Accepts a JSON bool OR "true"/"false"/"1"/"0" string.
        private static bool AsBool(JsonElement e, out bool val)
        {
            if (e.ValueKind == JsonValueKind.True) { val = true; return true; }
            if (e.ValueKind == JsonValueKind.False) { val = false; return true; }
            if (e.ValueKind == JsonValueKind.String) { var s = (e.GetString() ?? "").Trim().ToLowerInvariant(); if (s is "true" or "1" or "yes") { val = true; return true; } if (s is "false" or "0" or "no") { val = false; return true; } }
            if (e.ValueKind == JsonValueKind.Number) { val = e.GetDouble() != 0; return true; }
            val = false; return false;
        }

        private void ApplyGlobalSceneVars(JsonElement root, Action ensureUndo, List<string> changes)
        {
            var parts = new List<string>();
            void SetInto(JsonElement obj, VarScope scope, string label) // a number, true/false (a Yes/No) or words (a Text)
            {
                foreach (var p in obj.EnumerateObject())
                    if (scope.SetFromJson(p.Name, p.Value, out var shown)) parts.Add($"{label} {shown}");
            }
            void RemoveFrom(JsonElement arr, VarScope scope, string label)
            {
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && scope.Remove(e.GetString() ?? "")) parts.Add($"{label} -{e.GetString()}");
            }

            if (root.TryGetProperty("variables", out var v) && v.ValueKind == JsonValueKind.Object)
            {
                if (v.TryGetProperty("global", out var g) && g.ValueKind == JsonValueKind.Object) SetInto(g, VarScope.Of(_project), "global");
                if (v.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Object) SetInto(s, VarScope.Of(_scene), "scene");
            }
            if (root.TryGetProperty("removeVariables", out var rv) && rv.ValueKind == JsonValueKind.Object)
            {
                if (rv.TryGetProperty("global", out var g) && g.ValueKind == JsonValueKind.Array) RemoveFrom(g, VarScope.Of(_project), "global");
                if (rv.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Array) RemoveFrom(s, VarScope.Of(_scene), "scene");
            }

            if (parts.Count > 0) { ensureUndo(); changes.Add(L.F("Variables — {0}", string.Join("; ", parts))); }

            // in-game THEME (fonts + dialogue box look)
            if (root.TryGetProperty("theme", out var th) && th.ValueKind == JsonValueKind.Object)
            {
                ensureUndo();
                var t = _project.Theme ??= new GameTheme();
                string S(string k, string d) => th.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? d) : d;
                double N(string k, double d) => th.TryGetProperty(k, out var e) && LooseJson.TryNumber(e, out var nv) ? nv : d;
                t.FontFamily = S("font", t.FontFamily);
                t.DialogBg = S("dialogBg", t.DialogBg);
                t.DialogBorder = S("dialogBorder", t.DialogBorder);
                t.DialogText = S("dialogText", t.DialogText);
                t.DialogSpeaker = S("dialogSpeaker", t.DialogSpeaker);
                t.DialogEffect = S("dialogEffect", t.DialogEffect);
                t.DialogFontSize = N("dialogFontSize", t.DialogFontSize);
                t.DialogCorner = N("dialogCorner", t.DialogCorner);
                t.DialogSpeed = N("dialogSpeed", t.DialogSpeed);
                changes.Add(L.T("Theme updated"));
            }
        }

        private void ApplyLayerArray(JsonElement lyArr, Action ensureUndo, List<string> changes)
        {
            var acts = new List<Action>(); var lch = new List<string>();
            foreach (var item in lyArr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var nm)) continue;
                var layer = _scene.Layers.FirstOrDefault(l => l.Name.Equals(nm.GetString(), StringComparison.OrdinalIgnoreCase));
                if (layer == null) continue;
                var lyr = layer;
                if (item.TryGetProperty("screenSpace", out var ss) && (ss.ValueKind == JsonValueKind.True || ss.ValueKind == JsonValueKind.False))
                { bool v = ss.GetBoolean(); acts.Add(() => lyr.ScreenSpace = v); lch.Add($"{lyr.Name}:{(v ? "fixed" : "scroll")}"); }
                if (item.TryGetProperty("visible", out var vv) && (vv.ValueKind == JsonValueKind.True || vv.ValueKind == JsonValueKind.False))
                { bool v = vv.GetBoolean(); acts.Add(() => lyr.Visible = v); lch.Add($"{lyr.Name}:{(v ? "visible" : "hidden")}"); }
            }
            if (acts.Count > 0) { ensureUndo(); foreach (var a in acts) a(); changes.Add(L.F("Layers — {0}", string.Join("; ", lch))); }
        }

        private GameObjectData? BuildObjectFromSpec(JsonElement spec, string userPrompt, string? nameOverride = null)
        {
            var obj = new GameObjectData { Color = "#9A9AA0", Width = 48, Height = 48 };
            obj.Name = nameOverride ?? (spec.TryGetProperty("name", out var nm) ? (nm.GetString() ?? "Object") : "Object");
            obj.Type = obj.Name;

            ApplyBehaviorsToObject(obj, spec, userPrompt);

            // "asPrefab": a thing to spawn later (doesn't appear in the scene until spawned)
            if (spec.TryGetProperty("asPrefab", out var ap) && ap.ValueKind == JsonValueKind.True)
            {
                obj.Id = 0; obj.X = 0; obj.Y = 0;
                _project.Prefabs.RemoveAll(p => p.Name.Equals(obj.Name, StringComparison.OrdinalIgnoreCase));
                _project.Prefabs.Add(new PrefabDef { Name = obj.Name, Icon = "▢", Template = obj });
                return obj;
            }

            var layerName = spec.TryGetProperty("layer", out var ly) ? ly.GetString() : null;
            var layer = _scene.Layers.FirstOrDefault(l => l.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                        ?? _activeLayer ?? _scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.Base) ?? _scene.Layers.FirstOrDefault();
            if (layer == null) return null;

            if (!spec.TryGetProperty("x", out _)) obj.X = _scene.Width / 2 - obj.Width / 2;
            if (!spec.TryGetProperty("y", out _)) obj.Y = _scene.Height / 2 - obj.Height / 2;

            obj.Id = _project.NextId;
            obj.Name = UniqueName(obj.Name);
            layer.Objects.Add(obj);
            return obj;
        }

        private bool ApplyProjectSpec(string reply, string userPrompt, out string summary)
        {
            summary = "";
            _aiSpecObjects.Clear(); _aiSkippedLocked.Clear();
            _aiNotes.Clear();
            var json = LooseJson.Extract(reply);
            if (json == null) return false;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json, LooseJson.Options); } catch { return false; }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                var changes = new List<string>();
                bool pushed = false;
                void EnsureUndo() { if (!pushed) { PushUndo(); pushed = true; } }
                ApplyDeletes(root, EnsureUndo, changes);

                bool hasCreate = root.TryGetProperty("create", out var cr) && cr.ValueKind == JsonValueKind.Array;
                bool hasEdit = root.TryGetProperty("edit", out var ed) && ed.ValueKind == JsonValueKind.Array;

                // SCOPED to selection: when the user selected objects, apply the change ONLY to them (no creation).
                if (_selection.Count > 0)
                {
                    var specs = new List<(string? name, JsonElement spec)>();
                    if (hasEdit) foreach (var s in ed.EnumerateArray()) if (s.ValueKind == JsonValueKind.Object) specs.Add((s.TryGetProperty("name", out var n) ? n.GetString() : null, s));
                    if (hasCreate) foreach (var s in cr.EnumerateArray()) if (s.ValueKind == JsonValueKind.Object) specs.Add((s.TryGetProperty("name", out var n) ? n.GetString() : null, s));
                    if (specs.Count == 0 && (root.TryGetProperty("behaviors", out _) || root.TryGetProperty("custom", out _))) specs.Add((null, root));

                    JsonElement? SpecFor(GameObjectData o)
                    {
                        foreach (var (nm, sp) in specs) if (nm != null && o.Name.Equals(nm, StringComparison.OrdinalIgnoreCase)) return sp;
                        if (specs.Count == 1) return specs[0].spec;
                        foreach (var (nm, sp) in specs) if (nm == null) return sp;
                        return null;
                    }

                    foreach (var obj in _selection.ToList())
                    {
                        var sp = SpecFor(obj);
                        if (sp is not JsonElement spec) continue;
                        EnsureUndo();
                        if (ApplyBehaviorsToObject(obj, spec, userPrompt)) changes.Add(EditedLine(obj));
                    }

                    if (root.TryGetProperty("layers", out var lyScoped) && lyScoped.ValueKind == JsonValueKind.Array)
                        ApplyLayerArray(lyScoped, EnsureUndo, changes);
                    ApplyGlobalSceneVars(root, EnsureUndo, changes);
                    ApplyLevel(root, EnsureUndo, changes); // a level is built whatever is selected

                    AddLockedNotes(changes);
                    if (changes.Count == 0) return false;
                    summary = SummaryOf(changes);
                    return true;
                }

                if (hasCreate)
                    foreach (var spec in cr.EnumerateArray())
                    {
                        if (spec.ValueKind != JsonValueKind.Object) continue;
                        var name = spec.TryGetProperty("name", out var cn) ? cn.GetString() : null;

                        bool asPrefab = spec.TryGetProperty("asPrefab", out var apf) && apf.ValueKind == JsonValueKind.True;

                        // REDIRECT B: "add a dash/melee/health…" (an ability for the player, no new thing named) that the
                        // model wrongly turned into a new object → apply it to the player instead.
                        if (!asPrefab && HasAbility(userPrompt) && !MentionsThing(userPrompt) && !SpecHasNonPlayerBehavior(spec))
                        {
                            var player = FindPlayer();
                            if (player != null && !(name ?? "").Equals(player.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                EnsureUndo();
                                if (ApplyBehaviorsToObject(player, spec, userPrompt)) changes.Add(EditedLine(player));
                                continue;
                            }
                        }

                        // don't duplicate: unless it's a prefab or the user clearly wants a NEW/another one, edit the existing object
                        GameObjectData? existing = null;
                        if (!asPrefab && !IsNewIntent(userPrompt))
                        {
                            existing = _scene.Layers.SelectMany(l => l.Objects).FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                            // a "create" named like the player (Hero, Jogador…) is the player; any other new name is a NEW thing,
                            // even when the request mentions the player ("an enemy that chases the player")
                            if (existing == null && IsModifyIntent(userPrompt) && NameIsPlayer(name ?? "")) existing = FindPlayer();
                        }

                        EnsureUndo();
                        if (existing != null)
                        {
                            if (ApplyBehaviorsToObject(existing, spec, userPrompt)) changes.Add(EditedLine(existing));
                        }
                        else
                        {
                            var obj = BuildObjectFromSpec(spec, userPrompt);
                            if (obj != null) changes.Add(asPrefab ? PrefabLine(obj) : CreatedLine(obj));
                        }
                    }

                if (hasEdit)
                    foreach (var spec in ed.EnumerateArray())
                    {
                        if (spec.ValueKind != JsonValueKind.Object) continue;
                        var nm = spec.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var obj = ResolveTarget(nm, userPrompt) ?? _selected;
                        if (obj == null)
                        {
                            // an "edit" of something that isn't there yet (a ScoreText the model forgot to create): make it
                            if (!string.IsNullOrWhiteSpace(nm) && (spec.TryGetProperty("behaviors", out _) || spec.TryGetProperty("custom", out _)))
                            { EnsureUndo(); var made = BuildObjectFromSpec(spec, userPrompt); if (made != null) changes.Add(CreatedLine(made)); }
                            continue;
                        }

                        // REDIRECT A: the model tried to put a platform/coin/enemy/door behavior ON the player.
                        // That's almost always wrong → make a NEW object with those behaviors instead.
                        if (ReferenceEquals(obj, FindPlayer()) && SpecHasNonPlayerBehavior(spec))
                        {
                            EnsureUndo();
                            var created = BuildObjectFromSpec(spec, userPrompt, FirstThingNoun(userPrompt) ?? "Object");
                            if (created != null) changes.Add(CreatedLine(created));
                            continue;
                        }

                        EnsureUndo();
                        if (ApplyBehaviorsToObject(obj, spec, userPrompt)) changes.Add(EditedLine(obj));
                    }

                // simple/back-compat: top-level behaviors/custom and no create/edit → selected, player, or a new object
                if (!hasCreate && !hasEdit && (root.TryGetProperty("behaviors", out _) || root.TryGetProperty("custom", out _)))
                {
                    if (_selection.Count == 0 && SpecHasNonPlayerBehavior(root))
                    {
                        // platform/coin/enemy/door behavior with no target → it's a NEW object, not the player
                        EnsureUndo();
                        var created = BuildObjectFromSpec(root, userPrompt, FirstThingNoun(userPrompt) ?? "Object");
                        if (created != null) changes.Add(CreatedLine(created));
                    }
                    else
                    {
                        var obj = _selected ?? FindPlayer();
                        if (obj != null) { EnsureUndo(); if (ApplyBehaviorsToObject(obj, root, userPrompt)) changes.Add(EditedLine(obj)); }
                    }
                }

                if (root.TryGetProperty("layers", out var lyArr) && lyArr.ValueKind == JsonValueKind.Array)
                    ApplyLayerArray(lyArr, EnsureUndo, changes);
                ApplyGlobalSceneVars(root, EnsureUndo, changes);
                ApplyLevel(root, EnsureUndo, changes); // "level": the engine lays out a whole level (MainWindow.AiLevel.cs)

                if (changes.Count > 0)
                {
                    var probe = JsonClone(_scene); // only touch (and undo-snapshot) the scene if the sky really changes
                    if (SkySetup.Ensure(probe) != null) { EnsureUndo(); var sky = SkySetup.Ensure(_scene); if (sky != null) changes.Add(sky); }
                }

                AddLockedNotes(changes); // locked objects the AI wanted to change (MainWindow.LockZ.cs)
                if (changes.Count == 0) return false;
                summary = SummaryOf(changes);
                return true;
            }
        }

        // the lines of the AI's change summary; each also records the object it touched (for Ctrl+K, "show the last AI change")
        private readonly List<string> _aiSpecObjects = new();
        private string EditedLine(GameObjectData o) { _aiSpecObjects.Add(o.Name); return L.F("Edited \"{0}\"", o.Name); }
        private string PrefabLine(GameObjectData o) { _aiSpecObjects.Add(o.Name); return L.F("Prefab \"{0}\"", o.Name); }
        private string CreatedLine(GameObjectData o)
        {
            _aiSpecObjects.Add(o.Name);
            return L.F("Created \"{0}\" [{1}]", o.Name, string.Join(",", o.Behaviors.Select(b => b.Kind)));
        }
    }
}
