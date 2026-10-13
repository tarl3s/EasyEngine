using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EasyEngine
{
    // "level": the AI says WHAT a level has — {"level":{"platforms":3,"coins":5,"enemies":2,"door":"Level2"}} — and the
    // engine lays it out: ground, platforms a jump apart, coins on them, patrolling enemies, a door at the end (or, for
    // "style":"topdown", a walled room). Placing many objects well is what small models are worst at; this way every
    // model builds a playable level, and anything special can still come in the same reply's "create"/"edit".
    public partial class MainWindow
    {
        private void ApplyLevel(JsonElement root, string userPrompt, Action ensureUndo, List<string> changes)
        {
            if (!root.TryGetProperty("level", out var lv) || lv.ValueKind != JsonValueKind.Object) return;
            // A request that lists what it wants ("2 platforms and 3 coins"): its numbers win, and what a small model copies from
            // the hint but nobody asked for ("spikes":1, "door":"Level2") is left out. A vague "build a level" keeps the model's.
            if (LevelFromPrompt(userPrompt, _project.Scenes.Select(s => s.Name)) is not { } want || LooseJson.Node(lv) is not System.Text.Json.Nodes.JsonObject spec)
            { ApplyLevelSpec(lv, ensureUndo, changes); return; }
            var p = userPrompt.ToLowerInvariant();
            foreach (var (key, words) in LevelPieceWords)
                if (KeyIn(spec, key) is string real && !System.Text.RegularExpressions.Regex.IsMatch(p, words)) spec.Remove(real);
            foreach (var (k, v) in want) { if (KeyIn(spec, k) is string real) spec.Remove(real); spec[k] = JsonSerializer.SerializeToNode(v); }
            if (KeyIn(spec, "platforms") is string pl && KeyIn(spec, "stairs") is string st && spec[pl]?.ToJsonString() == spec[st]?.ToJsonString())
                spec.Remove(pl); // "a staircase of 5 platforms" is the stairs themselves
            using var doc = JsonDocument.Parse(spec.ToJsonString());
            ApplyLevelSpec(doc.RootElement, ensureUndo, changes);
        }
        private static string? KeyIn(System.Text.Json.Nodes.JsonObject o, string key) => o.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        private static readonly (string key, string words)[] LevelPieceWords =
        {
            ("spikes", @"spike|espinho|pincho|trap|armadilha|hazard|lava|danger"),
            ("enemies", @"enem|inimig|monst|slime|boss|chefe|bad guy"),
            ("door", @"door|porta|puerta|exit|sa[ií]da|next|level ?\d|fase ?\d|scene|cena|goal"),
            ("checkpoint", @"checkpoint|bandeira|flag|respawn"),
            ("stairs", @"stair|escada|escalera|steps|degrau"),
        };

        private void ApplyLevelSpec(JsonElement lv, Action ensureUndo, List<string> changes)
        {
            int N(string key, int max) { foreach (var p in lv.EnumerateObject()) if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && LooseJson.TryNumber(p.Value, out var v)) return Math.Clamp((int)Math.Round(v), 0, max); return 0; }
            string S(string key) { foreach (var p in lv.EnumerateObject()) if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString() ?? ""; return ""; }
            bool B(string key) { foreach (var p in lv.EnumerateObject()) if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) return LooseJson.IsTrue(p.Value); return false; }

            int platforms = N("platforms", 30), coins = N("coins", 60), enemies = N("enemies", 20), spikes = N("spikes", 20), stairs = N("stairs", 20);
            bool topdown = S("style").Contains("top", StringComparison.OrdinalIgnoreCase) || B("walls") || B("room");
            bool chasers = S("enemyKind").Contains("chase", StringComparison.OrdinalIgnoreCase) || S("enemies").Contains("chase", StringComparison.OrdinalIgnoreCase);
            string door = S("door"); if (door.Length == 0 && B("door")) door = _project.Scenes.Select(s => s.Name).FirstOrDefault(n => n != _scene.Name) ?? "";
            bool checkpoint = B("checkpoint");
            if (platforms + coins + enemies + spikes + stairs == 0 && door.Length == 0 && !topdown) return;

            ensureUndo();
            var layer = _scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.Base) ?? _activeLayer ?? _scene.Layers.First();
            var made = new List<string>();
            GameObjectData Add(string name, double x, double y, double w, double h, string color, PhysicsMode phys, params BehaviorKind[] kinds)
            {
                var o = new GameObjectData { Id = _project.NextId, Name = UniqueName(name), Type = name, X = Math.Round(x), Y = Math.Round(y), Width = w, Height = h, Color = color, Physics = new PhysicsData { Mode = phys } };
                foreach (var k in kinds) o.Behaviors.Add(BehaviorCatalog.Info(k).CreateData());
                layer.Objects.Add(o);
                _aiSpecObjects.Add(o.Name);
                return o;
            }
            BehaviorData Beh(GameObjectData o, BehaviorKind k) => o.Behaviors.First(b => b.Kind == k);

            // a long level gets a wider scene (≈260 px per platform)
            double W = Math.Max(_scene.Width, topdown ? _scene.Width : 400 + platforms * 260 + stairs * 130), H = _scene.Height;
            if (W > _scene.Width) _scene.Width = W;

            var solids = _scene.Layers.SelectMany(l => l.Objects).Where(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Solid)).ToList();
            var spots = new List<(double x, double top, double w)>(); // where coins and enemies can stand

            if (topdown)
            {
                const double t = 32;
                Add("Wall", 0, 0, W, t, "#4A4A58", PhysicsMode.Static, BehaviorKind.Solid);
                Add("Wall", 0, H - t, W, t, "#4A4A58", PhysicsMode.Static, BehaviorKind.Solid);
                Add("Wall", 0, t, t, H - 2 * t, "#4A4A58", PhysicsMode.Static, BehaviorKind.Solid);
                Add("Wall", W - t, t, t, H - 2 * t, "#4A4A58", PhysicsMode.Static, BehaviorKind.Solid);
                made.Add(L.T("walls all around"));
                var rng = new Random(platforms * 31 + coins * 7 + enemies);
                for (int i = 0; i < coins; i++) { var c = Add("Coin", 80 + rng.NextDouble() * (W - 180), 80 + rng.NextDouble() * (H - 180), 24, 24, "#FFD700", PhysicsMode.None, BehaviorKind.Collectible); Beh(c, BehaviorKind.Collectible).Texts["var"] = "score"; }
                for (int i = 0; i < enemies; i++)
                {
                    var e = Add("Enemy", W * (0.45 + 0.45 * i / Math.Max(1, enemies)), H * (0.25 + 0.5 * (i % 2)), 40, 40, "#E5484B", PhysicsMode.None, BehaviorKind.PathChase, BehaviorKind.Health, BehaviorKind.Damager);
                    Beh(e, BehaviorKind.Health).Nums["respawn"] = 0;
                }
                if (door.Length > 0) { var d = Add("Door", W - 32 - 48, H / 2 - 48, 48, 96, "#8B5A2B", PhysicsMode.None, BehaviorKind.GoToScene); Beh(d, BehaviorKind.GoToScene).Texts["scene"] = door; }
            }
            else
            {
                // the ground: the widest solid near the bottom, or a new one
                var ground = solids.Where(o => o.Width >= W * 0.5 && o.Y > H * 0.5).OrderByDescending(o => o.Width).FirstOrDefault();
                if (ground == null) { ground = Add("Ground", 0, H - 80, W, 80, "#3A3A46", PhysicsMode.Static, BehaviorKind.Solid); made.Add(L.T("ground")); }
                else if (ground.X + ground.Width < W) ground.Width = W - ground.X; // reach the end of a wider scene
                double top = ground.Y;
                spots.Add((360, top, W - 560));

                // one-way (jump up through them, nothing gets stuck under them), a jump apart: the jump climbs ~100 px
                GameObjectData Ledge(string name, double x, double y, double w)
                {
                    var p = Add(name, x, y, w, 20, "#6B5B45", PhysicsMode.Static, BehaviorKind.Solid);
                    Beh(p, BehaviorKind.Solid).Nums["oneWay"] = 1;
                    spots.Add((p.X, p.Y, p.Width));
                    return p;
                }
                for (int i = 0; i < platforms; i++) Ledge("Platform", 220 + i * 260, top - 80 - (i % 2) * 70, 140); // 80 px up, then 70 more
                int steps = Math.Min(stairs, (int)((top - 60) / 70)); // a staircase never leaves the top of the scene
                double stepsX = platforms > 0 ? 220 + platforms * 260 + 60 : 180; // after the platforms, never on them
                for (int i = 0; i < steps; i++) Ledge("Step", stepsX + i * 130, top - 70 * (i + 1), 110);

                // coins: over the platforms first, then in rows along the ground
                int placed = 0, slots = Math.Max(1, (int)((W - 400) / 48));
                foreach (var s in spots.Skip(1)) { if (placed >= coins) break; Coin(s.x + s.w / 2 - 12, s.top - 40); placed++; }
                for (int i = 0; placed < coins; i++, placed++) Coin(320 + i % slots * 48, top - 60 - i / slots * 40);
                void Coin(double x, double y) { var c = Add("Coin", x, y, 24, 24, "#FFD700", PhysicsMode.None, BehaviorKind.Collectible); Beh(c, BehaviorKind.Collectible).Texts["var"] = "score"; }

                for (int i = 0; i < enemies; i++)
                {
                    double x = 420 + (i + 0.5) * (W - 700) / Math.Max(1, enemies);
                    var e = Add("Enemy", x, top - 48, 40, 48, "#E5484B", PhysicsMode.Dynamic, chasers ? BehaviorKind.Chase : BehaviorKind.Patrol, BehaviorKind.Health, BehaviorKind.Damager);
                    Beh(e, BehaviorKind.Health).Nums["respawn"] = 0; Beh(e, BehaviorKind.Health).Nums["max"] = 1;
                    if (!chasers) Beh(e, BehaviorKind.Patrol).Nums["distance"] = 140;
                }
                for (int i = 0; i < spikes; i++) Add("Spike", 520 + (i + 0.5) * (W - 800) / Math.Max(1, spikes), top - 20, 32, 20, "#C0C0C8", PhysicsMode.None, BehaviorKind.Damager); // None: a Static one would be a wall nobody can touch
                if (checkpoint) Add("Checkpoint", W / 2, top - 64, 24, 64, "#3BC97A", PhysicsMode.None, BehaviorKind.Checkpoint);
                if (door.Length > 0) { var d = Add("Door", W - 120, top - 96, 56, 96, "#8B5A2B", PhysicsMode.None, BehaviorKind.GoToScene); Beh(d, BehaviorKind.GoToScene).Texts["scene"] = door; }
            }

            // coins count somewhere you can see
            if (coins > 0 && !_scene.Layers.SelectMany(l => l.Objects).Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Label && b.Text("text", "").Contains("{score}"))))
            {
                var ui = _scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.UI || l.ScreenSpace);
                if (ui != null)
                {
                    var lbl = new GameObjectData { Id = _project.NextId, Name = UniqueName("ScoreText"), Type = "Label", X = 20, Y = 16, Width = 220, Height = 32, Color = "#00000000" };
                    var lb = BehaviorCatalog.Info(BehaviorKind.Label).CreateData(); lb.Texts["text"] = "Score: {score}"; lb.Texts["align"] = "left";
                    lbl.Behaviors.Add(lb); ui.Objects.Add(lbl); _aiSpecObjects.Add(lbl.Name);
                }
            }

            var parts = new List<string>(made);
            if (platforms > 0) parts.Add(L.F("{0} platforms", platforms));
            if (stairs > 0) parts.Add(L.F("{0} steps", stairs));
            if (coins > 0) parts.Add(L.F("{0} coins", coins));
            if (enemies > 0) parts.Add(L.F("{0} enemies", enemies));
            if (spikes > 0) parts.Add(L.F("{0} spikes", spikes));
            if (door.Length > 0) parts.Add(L.F("a door to {0}", door));
            changes.Add(L.F("Built a level: {0}", string.Join(", ", parts)));
        }

        // ---- the level the REQUEST asks for, read from its words ("3 platforms, 5 coins and 2 enemies", "4 plataformas") ----

        private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8,
            ["nine"] = 9, ["ten"] = 10, ["um"] = 1, ["uma"] = 1, ["dois"] = 2, ["duas"] = 2, ["três"] = 3, ["tres"] = 3, ["quatro"] = 4, ["cinco"] = 5,
            ["seis"] = 6, ["sete"] = 7, ["oito"] = 8, ["nove"] = 9, ["dez"] = 10, ["un"] = 1, ["una"] = 1, ["dos"] = 2, ["cuatro"] = 4, ["siete"] = 7,
        };
        private static readonly string NumberAlternation = string.Join("|", NumberWords.Keys.OrderByDescending(k => k.Length));
        private static readonly (string key, string pattern)[] LevelNouns =
        {
            ("platforms", @"platforms?|plataformas?|ledges?"), ("stairs", @"steps?|stairs?|staircase|degraus?|escadas?|escalones?"),
            ("coins", @"coins?|moedas?|monedas?|gems?"), ("enemies", @"enem(?:y|ies)|inimigos?|enemigos?|monsters?|monstros?|slimes?"),
            ("spikes", @"spikes?|espinhos?|pinchos?|traps?|armadilhas?"),
        };

        /// <summary>The level a request describes ({"platforms":3,"coins":5,…}) — null when it doesn't describe one.</summary>
        internal static Dictionary<string, object>? LevelFromPrompt(string prompt, IEnumerable<string> sceneNames)
        {
            var p = (prompt ?? "").ToLowerInvariant();
            var spec = new Dictionary<string, object>();
            foreach (var (key, nouns) in LevelNouns)
            {
                var m = System.Text.RegularExpressions.Regex.Match(p, $@"\b(\d+|{NumberAlternation})\s+(?:[a-zà-ú]+\s+)?(?:{nouns})\b"); // "3 floating platforms", "um inimigo"
                if (!m.Success) continue;
                var w = m.Groups[1].Value;
                int n = int.TryParse(w, out var d) ? d : NumberWords.TryGetValue(w, out var nw) ? nw : 0;
                if (n > 0) spec[key] = Math.Min(n, key == "coins" ? 60 : 30);
            }
            // "a staircase of 5 platforms" is steps, not platforms
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"\b(stair|staircase|escada|escalera)") && spec.TryGetValue("platforms", out var pl))
            { spec["stairs"] = pl; spec.Remove("platforms"); }
            bool room = System.Text.RegularExpressions.Regex.IsMatch(p, @"\b(room|sala|habitación)\b") && System.Text.RegularExpressions.Regex.IsMatch(p, @"\b(walls?|paredes?|surrounded|cercad)");
            if (room) spec["style"] = "topdown";
            var door = System.Text.RegularExpressions.Regex.Match(p, @"\b(?:door|porta|puerta|exit|saída)\b(?:\s+\w+){0,3}?\s+(?:to|para|a|pra)\s+([\w-]+)");
            if (door.Success) { var target = sceneNames.FirstOrDefault(s => s.Equals(door.Groups[1].Value, StringComparison.OrdinalIgnoreCase)); if (target != null) spec["door"] = target; }
            bool levelWords = System.Text.RegularExpressions.Regex.IsMatch(p, @"\b(level|fase|stage|nível|nivel|map|mapa|room|sala|staircase|escada|in a row|em fila|em linha|fileira)\b");
            int things = spec.Keys.Count(k => k is "platforms" or "stairs" or "coins" or "enemies" or "spikes");
            return (things >= 2 || levelWords && (things >= 1 || room) || spec.ContainsKey("door")) ? spec : null; // a door to a scene that exists counts alone
        }

        /// <summary>Whether the reply itself already has at least what the request counted (then it is applied as it is).</summary>
        private static bool ReplyMeetsLevel(JsonElement root, Dictionary<string, object> want)
        {
            int Count(Func<string, string, bool> isIt)
            {
                int n = 0;
                foreach (var key in new[] { "create", "edit" })
                    if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var o in arr.EnumerateArray())
                        {
                            if (o.ValueKind != JsonValueKind.Object) continue;
                            var name = (o.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : "") ?? "";
                            var kinds = o.TryGetProperty("behaviors", out var bs) && bs.ValueKind == JsonValueKind.Array
                                ? string.Join(",", bs.EnumerateArray().Select(b => b.ValueKind == JsonValueKind.Object && b.TryGetProperty("kind", out var k) ? (ResolveKind(k.GetString())?.ToString() ?? "") : "")) : "";
                            if (isIt(name.ToLowerInvariant(), kinds)) n++;
                        }
                return n;
            }
            int Want(string k) => want.TryGetValue(k, out var v) ? Convert.ToInt32(v) : 0;
            bool Solid(string kinds) => kinds.Contains(nameof(BehaviorKind.Solid));
            return Count((n, k) => Solid(k) && !n.Contains("ground") && !n.Contains("chão") && !n.Contains("floor")) >= Want("platforms") + Want("stairs")
                && Count((n, k) => k.Contains(nameof(BehaviorKind.Collectible))) >= Want("coins")
                && Count((n, k) => k.Contains(nameof(BehaviorKind.Patrol)) || k.Contains(nameof(BehaviorKind.Chase)) || k.Contains("PathChase")) >= Want("enemies")
                && Count((n, k) => k.Contains(nameof(BehaviorKind.Damager)) && !k.Contains(nameof(BehaviorKind.Patrol)) && !k.Contains(nameof(BehaviorKind.Chase))) >= Want("spikes")
                && (!want.ContainsKey("style") || Count((n, k) => Solid(k)) >= 4)
                && (!want.ContainsKey("door") || Count((n, k) => k.Contains(nameof(BehaviorKind.GoToScene))) >= 1);
        }

        /// <summary>A piece of a level the engine will lay out itself (so the model's own attempt at it is left out).</summary>
        private static bool IsLevelPiece(JsonElement spec)
        {
            var name = ((spec.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : "") ?? "").ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"ground|floor|ch[aã]o|platform|plataforma|step|stair|escada|degrau|coin|moeda|enemy|inimigo|spike|espinho|wall|parede|door|porta|room|sala|level|fase"))
                return true;
            return spec.TryGetProperty("behaviors", out var bs) && bs.ValueKind == JsonValueKind.Array && bs.EnumerateArray().Any(b =>
                b.ValueKind == JsonValueKind.Object && b.TryGetProperty("kind", out var k) && ResolveKind(k.GetString()) is BehaviorKind.Solid or BehaviorKind.Collectible or BehaviorKind.Patrol or BehaviorKind.GoToScene);
        }
    }
}
