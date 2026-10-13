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
        private void ApplyLevel(JsonElement root, Action ensureUndo, List<string> changes)
        {
            if (!root.TryGetProperty("level", out var lv) || lv.ValueKind != JsonValueKind.Object) return;
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
            double W = Math.Max(_scene.Width, topdown ? _scene.Width : 300 + Math.Max(platforms, stairs) * 260), H = _scene.Height;
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
                if (door.Length > 0) { var d = Add("Door", W - 32 - 48, H / 2 - 48, 48, 96, "#8B5A2B", PhysicsMode.Static, BehaviorKind.GoToScene); Beh(d, BehaviorKind.GoToScene).Texts["scene"] = door; }
            }
            else
            {
                // the ground: the widest solid near the bottom, or a new one
                var ground = solids.Where(o => o.Width >= W * 0.5 && o.Y > H * 0.5).OrderByDescending(o => o.Width).FirstOrDefault();
                if (ground == null) { ground = Add("Ground", 0, H - 80, W, 80, "#3A3A46", PhysicsMode.Static, BehaviorKind.Solid); made.Add(L.T("ground")); }
                else if (ground.X + ground.Width < W) ground.Width = W - ground.X; // reach the end of a wider scene
                double top = ground.Y;
                spots.Add((360, top, W - 560));

                for (int i = 0; i < platforms; i++) // a jump apart (a jump climbs ~100 px): 80 px up, then 70 more, left to right
                {
                    double x = 260 + i * (W - 520) / Math.Max(1, platforms - (platforms > 1 ? 1 : 0)) - 70, y = top - 80 - (i % 2) * 70;
                    var p = Add("Platform", x, y, 140, 20, "#6B5B45", PhysicsMode.Static, BehaviorKind.Solid);
                    spots.Add((p.X, p.Y, p.Width));
                }
                for (int i = 0; i < stairs; i++)
                {
                    var p = Add("Step", 180 + i * 120, top - 70 * (i + 1), 110, 20, "#6B5B45", PhysicsMode.Static, BehaviorKind.Solid);
                    spots.Add((p.X, p.Y, p.Width));
                }

                // coins: over the platforms first, then in a row along the ground
                int placed = 0;
                foreach (var s in spots.Skip(1)) { if (placed >= coins) break; Coin(s.x + s.w / 2 - 12, s.top - 40); placed++; }
                for (int i = 0; placed < coins; i++, placed++) Coin(320 + i * 48 % Math.Max(48, W - 400), top - 60);
                void Coin(double x, double y) { var c = Add("Coin", x, y, 24, 24, "#FFD700", PhysicsMode.None, BehaviorKind.Collectible); Beh(c, BehaviorKind.Collectible).Texts["var"] = "score"; }

                for (int i = 0; i < enemies; i++)
                {
                    double x = 420 + (i + 0.5) * (W - 700) / Math.Max(1, enemies);
                    var e = Add("Enemy", x, top - 48, 40, 48, "#E5484B", PhysicsMode.Dynamic, chasers ? BehaviorKind.Chase : BehaviorKind.Patrol, BehaviorKind.Health, BehaviorKind.Damager);
                    Beh(e, BehaviorKind.Health).Nums["respawn"] = 0; Beh(e, BehaviorKind.Health).Nums["max"] = 1;
                    if (!chasers) Beh(e, BehaviorKind.Patrol).Nums["distance"] = 140;
                }
                for (int i = 0; i < spikes; i++) Add("Spike", 520 + (i + 0.5) * (W - 800) / Math.Max(1, spikes), top - 20, 32, 20, "#C0C0C8", PhysicsMode.Static, BehaviorKind.Damager);
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
    }
}
