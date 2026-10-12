using System.Collections.Generic;
using System.Linq;
using static EasyEngine.DemoKit;

namespace EasyEngine
{
    /// <summary>
    /// Builds a Mining demo entirely from engine features: procedural coins (once),
    /// breakable ground/stone/trees mined with the right tool, a hotbar (mouse wheel) that shows
    /// the held tool in hand, shops + a crafting bench, a health bar, enemies that drop coins,
    /// a day/night cycle with a sun & moon, particles and light.
    /// </summary>
    public static class MiningGame
    {
        private const int TS = 40;

        public static string Build(string parentFolder)
        {
            var project = ProjectIO.CreateNew("Mining Demo", parentFolder);
            project.Scenes.Clear();
            project.StartScene = "World";
            project.Variables["coins"] = 0;

            var scene = new Scene { Name = "World", Width = 2000, Height = 1200, BackgroundColor = "#6FA8DC" };
            var bg = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            int cols = (int)scene.Width / TS;        // 50
            double surfaceY = 720;                   // top of the dirt

            // ---- terrain: dirt then stone, both breakable, mined with the pickaxe ----
            int dirtRows = 4, stoneRows = ((int)scene.Height - (int)surfaceY) / TS - dirtRows;
            baseL.Objects.Add(Id(BreakTiles("Dirt", 0, surfaceY, cols, dirtRows, "#7A5230", "holding:pickaxe", "dirt", 1)));
            baseL.Objects.Add(Id(BreakTiles("Stone", 0, surfaceY + dirtRows * TS, cols, stoneRows, "#6B6B74", "holding:pickaxe", "stone", 2)));
            // a grass strip on top (decoration, not solid)
            baseL.Objects.Add(Id(Box("Grass", 0, surfaceY - 6, scene.Width, 8, "#5BA85C")));

            // ---- trees: hold the axe, stand next to one and click — 4 hits fell it (+5 wood) ----
            var snd = GameAssets.SaveToolSounds(project);
            var treeSprite = GameAssets.TreeSprite(project);
            int treeNo = 1;
            foreach (double tx in new[] { 520.0, 1020.0, 1520.0 })
            {
                var t = Box("Tree" + treeNo++, tx, surfaceY - 192, 96, 192, "#00000000");
                t.SpritePath = treeSprite;
                var h = B(BehaviorKind.Harvestable);
                h.Texts["tool"] = "axe"; h.Texts["drop"] = "wood"; h.Nums["hits"] = 4; h.Nums["amount"] = 5;
                h.Texts["hitSound"] = snd.chop; h.Texts["fellSound"] = snd.fall;
                t.Behaviors.Add(h);
                baseL.Objects.Add(Id(t));
            }

            // ---- player (after the trees, so the player and the held tool draw on top) ----
            baseL.Objects.Add(Id(Player(160, surfaceY - 120, snd.swing, snd.denied)));

            // ---- shops + crafting bench ----
            baseL.Objects.Add(Id(Shop("PickShop", "#B5651D", 300, surfaceY - 60, "coins", 5, "pickaxe", "E")));
            baseL.Objects.Add(Id(Label("PickLbl", 250, surfaceY - 96, "Pickaxe — E (5 coins)", "#FFF2C0", 13)));
            baseL.Objects.Add(Id(Shop("AxeShop", "#9AA0A0", 820, surfaceY - 60, "coins", 8, "axe", "E")));
            baseL.Objects.Add(Id(Label("AxeLbl", 770, surfaceY - 96, "Axe — E (8 coins)", "#FFF2C0", 13)));
            baseL.Objects.Add(Id(Shop("Bench", "#8A5A2B", 1300, surfaceY - 56, "item:wood", 8, "sword", "C")));
            baseL.Objects.Add(Id(Label("BenchLbl", 1232, surfaceY - 92, "Bench — C (8 wood → sword)", "#AEE1FF", 13)));

            // ---- coins scattered once (procedural) ----
            var gen = Box("CoinField", 220, 300, 1500, 360, "#00000000");
            var genB = B(BehaviorKind.Generator);
            genB.Nums["count"] = 14; genB.Texts["prefab"] = "Coin"; genB.Texts["mode"] = "once"; genB.Texts["layout"] = "scatter"; genB.Texts["id"] = "coinfield";
            gen.Behaviors.Add(genB);
            baseL.Objects.Add(Id(gen));

            // ---- enemies: two at the start + a night spawner ----
            baseL.Objects.Add(Id(Slime(700, surfaceY - 44)));
            baseL.Objects.Add(Id(Slime(1400, surfaceY - 44)));
            var spawner = Box("NightSpawner", 1000, surfaceY - 60, 20, 20, "#00000000");
            spawner.Behaviors.Add(Script("[{\"when\":\"timer\",\"every\":6,\"do\":\"spawn\",\"object\":\"Slime\",\"cond\":\"isNight >= 1\"}]"));
            baseL.Objects.Add(Id(spawner));

            // ---- day / night controller + sun & moon (UI = screen space) ----
            var dn = Box("DayNight", 0, 0, 10, 10, "#00000000");
            var dnB = B(BehaviorKind.DayNight);
            dnB.Nums["mode"] = 0; dnB.Nums["dayLength"] = 40; dnB.Nums["nightLength"] = 28; dnB.Nums["transition"] = 5;
            dnB.Nums["dayLight"] = 1; dnB.Nums["nightLight"] = 0.22; dnB.Nums["arcHeight"] = 170;
            dnB.Texts["sun"] = "Sun"; dnB.Texts["moon"] = "Moon";
            dn.Behaviors.Add(dnB);
            baseL.Objects.Add(Id(dn));

            // ---- HUD (screen space) ----
            var hpBar = Box("HpBar", 16, 16, 220, 22, "#00000000");
            var barB = B(BehaviorKind.Bar); barB.Texts["var"] = "hp"; barB.Nums["max"] = 6; hpBar.Behaviors.Add(barB);
            ui.Objects.Add(Id(hpBar));
            ui.Objects.Add(Id(Label("CoinsHUD", 16, 46, "Coins: {coins}", "#FFD86B", 16)));
            ui.Objects.Add(Id(Label("ResHUD", 16, 72, "Wood {item:wood}  ·  Dirt {item:dirt}  ·  Stone {item:stone}", "#CFE8FF", 14)));
            ui.Objects.Add(Id(Label("Hint", 16, 98, "A/D move · Space jump · Wheel/1-3 hotbar · Pickaxe: hold Left mouse on dirt · Axe: stand by a tree + click · Sword: click near a slime · E buy · C craft · F1 console", "#AEB6C6", 13)));

            scene.Layers.Add(bg); scene.Layers.Add(baseL); scene.Layers.Add(ui);
            SkySetup.Ensure(scene); // Sky layer at the bottom with the Sun and Moon of the cycle
            project.Scenes.Add(scene);

            // ---- prefabs spawned at runtime ----
            // Dynamic so scattered/dropped coins fall to the ground where the player can reach them
            project.Prefabs.Add(new PrefabDef { Name = "Coin", Template = Coin(PhysicsMode.Dynamic) });
            project.Prefabs.Add(new PrefabDef { Name = "Slime", Template = Slime(0, 0) });

            ProjectIO.Save(project);
            return project.FilePath!;
        }

        // ---------------- factories ----------------
        private static GameObjectData BreakTiles(string name, double x, double y, int cols, int rows, string color, string requires, string drop, int hits)
        {
            var o = Box(name, x, y, cols * TS, rows * TS, "#00000000");
            var bh = new BehaviorData { Kind = BehaviorKind.Tilemap };
            bh.Texts["palette"] = "[{\"color\":\"" + color + "\",\"solid\":true}]";
            bh.Texts["data"] = "{\"cols\":" + cols + ",\"rows\":" + rows + ",\"cells\":[" + string.Join(",", Enumerable.Repeat("1", cols * rows)) + "]}";
            bh.Nums["tileSize"] = TS; bh.Nums["breakable"] = 1; bh.Nums["breakHits"] = hits;
            bh.Nums["byMelee"] = 0; bh.Nums["byBullet"] = 0; bh.Nums["byDamage"] = 0; // only the aim+click mining breaks these
            bh.Texts["requires"] = requires; bh.Texts["drop"] = drop;
            o.Behaviors.Add(bh);
            return o;
        }

        private static GameObjectData Player(double x, double y, string swingSound, string deniedSound)
        {
            var o = new GameObjectData { Name = "Player", Type = "Player", X = x, Y = y, Width = 34, Height = 50, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 240; plat.Nums["jump"] = 620; o.Behaviors.Add(plat);
            o.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 6; hp.Nums["respawn"] = 1; hp.Texts["var"] = "hp"; o.Behaviors.Add(hp);
            var hot = B(BehaviorKind.Hotbar); hot.Texts["items"] = "pickaxe:#8D949E,axe:#B9C2CC,sword:#DDEBFF"; hot.Texts["player"] = "Player"; o.Behaviors.Add(hot);
            // every item is used by clicking: the sword hits slimes hardest, the axe fells trees, the pickaxe digs
            var tool = B(BehaviorKind.ToolUse); tool.Texts["itemDamage"] = "sword=2,axe=1,pickaxe=1"; tool.Nums["reach"] = 80;
            tool.Texts["swingSound"] = swingSound; tool.Texts["deniedSound"] = deniedSound;
            o.Behaviors.Add(tool);
            var torch = B(BehaviorKind.LightSource); torch.Nums["radius"] = 240; torch.Texts["color"] = "#FFE6A0"; o.Behaviors.Add(torch);
            // mine/chop the tile under the cursor while the left mouse button is held, within reach
            // (the held tool gates WHAT breaks via each tilemap's 'requires' flag)
            o.Behaviors.Add(Script("[{\"when\":\"always\",\"do\":\"breaktile\",\"where\":\"atmouse\",\"cond\":\"mousedown >= 1 && dist(mousex, mousey, self.cx, self.cy) < 170\"}]"));
            return o;
        }

        private static GameObjectData Slime(double x, double y)
        {
            var o = new GameObjectData { Name = "Slime", Type = "Enemy", X = x, Y = y, Width = 40, Height = 30, Color = "#8E54E9", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["controls"] = 0; o.Behaviors.Add(plat);
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Player"; dmg.Nums["damage"] = 1; dmg.Nums["knockback"] = 300; o.Behaviors.Add(dmg);
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 2; hp.Nums["respawn"] = 0; o.Behaviors.Add(hp);
            // walk toward the player (gravity from Platformer keeps it on the ground); drop a coin on death
            o.Behaviors.Add(Script("[{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"sign(Player.cx - self.cx) * 70\"}," +
                                   "{\"when\":\"death\",\"do\":\"spawn\",\"object\":\"Coin\"}," +
                                   "{\"when\":\"death\",\"do\":\"emit\",\"count\":16,\"color\":\"#8E54E9\"}]"));
            return o;
        }

        private static GameObjectData Shop(string name, string color, double x, double y, string currency, double cost, string item, string key)
        {
            var o = new GameObjectData { Name = name, Type = "Object", X = x, Y = y, Width = 44, Height = 56, Color = color, Physics = new PhysicsData { Mode = PhysicsMode.None } };
            o.CornerRadius = 6;
            var sh = B(BehaviorKind.Shop);
            sh.Texts["currency"] = currency; sh.Texts["item"] = item; sh.Texts["key"] = key; sh.Texts["player"] = "Player";
            sh.Nums["cost"] = cost; sh.Nums["radius"] = 80; sh.Nums["once"] = 1;
            o.Behaviors.Add(sh);
            return o;
        }
    }
}
