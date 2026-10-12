using System.Linq;
using static EasyEngine.DemoKit;

namespace EasyEngine
{
    /// <summary>
    /// The Mining demo's loop, seen from above: walk in 8 directions, pick up coins, buy a pickaxe and an axe,
    /// chop trees (wood) and break rocks (stone + a coin), craft a sword at the bench from wood, and survive the
    /// night, when slimes come out of the forest and the quarry (they melt at sunrise). No ground breaking.
    /// </summary>
    public static class TopDownGame
    {
        public const string ProjectName = "Top-Down Demo";

        public static string Build(string parentFolder)
        {
            var project = ProjectIO.CreateNew(ProjectName, parentFolder);
            project.Scenes.Clear();
            project.StartScene = "Island";
            project.Variables["coins"] = 0;

            // the grass is the scene colour; water all around keeps the player on the island
            var scene = new Scene { Name = "Island", Width = 2400, Height = 1600, BackgroundColor = "#5E9E4A" };
            var bg = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            // ---- ground decoration (not solid): dirt paths + a darker meadow ----
            bg.Objects.Add(Id(Rounded(Box("Meadow", 140, 140, 760, 520, "#6AAE55"), 40)));
            bg.Objects.Add(Id(Rounded(Box("PathEast", 380, 470, 1500, 46, "#B89A6A"), 20)));
            bg.Objects.Add(Id(Rounded(Box("PathSouth", 380, 470, 46, 640, "#B89A6A"), 20)));
            bg.Objects.Add(Id(Rounded(Box("QuarryFloor", 150, 960, 800, 520, "#8C8A7E"), 60)));

            // ---- water: the island's edge + a pond ----
            const double edge = 70;
            foreach (var (n, x, y, w, h) in new[] { ("WaterN", 0.0, 0.0, 2400.0, edge), ("WaterS", 0, 1600 - edge, 2400, edge),
                                                     ("WaterW", 0, 0, edge, 1600), ("WaterE", 2400 - edge, 0, edge, 1600) })
                baseL.Objects.Add(Id(Water(n, x, y, w, h)));
            baseL.Objects.Add(Id(Rounded(Water("Pond", 1120, 1020, 340, 220), 90)));
            baseL.Objects.Add(Id(Rounded(Water("Stream", 1000, 560, 30, 340), 12))); // a tree across it can't be chopped: the water is in the way

            // ---- trees (axe) and rocks (pickaxe): a few near the start, the rest scattered once ----
            var snd = GameAssets.SaveToolSounds(project);
            var treeSprite = GameAssets.TreeSprite(project);
            var rockSprite = GameAssets.RockSprite(project);
            baseL.Objects.Add(Id(Tree("Tree1", 560, 600, treeSprite, snd)));
            baseL.Objects.Add(Id(Tree("Tree2", 700, 660, treeSprite, snd)));
            baseL.Objects.Add(Id(Tree("Tree3", 1040, 660, treeSprite, snd)));
            baseL.Objects.Add(Id(Rock("Rock1", 520, 820, rockSprite, snd)));
            baseL.Objects.Add(Id(Mound("Mound1", 350, 760)));
            baseL.Objects.Add(Id(Gen("DigSpots", 160, 540, 820, 380, "Mound", 5, 40, "digspots")));
            baseL.Objects.Add(Id(Gen("Forest", 1250, 120, 1050, 760, "Tree", 16, 30, "forest")));
            baseL.Objects.Add(Id(Gen("Quarry", 190, 1000, 720, 440, "Rock", 9, 40, "quarry")));
            baseL.Objects.Add(Id(Gen("CoinField", 160, 160, 2000, 780, "Coin", 16, 20, "coins")));

            // ---- shops + crafting bench, by the start ----
            baseL.Objects.Add(Id(Shop("PickShop", "#B5651D", 470, 290, "coins", 5, "pickaxe", "E")));
            baseL.Objects.Add(Id(Label("PickLbl", 430, 262, "Pickaxe — E (5 coins)", "#FFF2C0", 13)));
            baseL.Objects.Add(Id(Shop("AxeShop", "#9AA0A0", 640, 290, "coins", 8, "axe", "E")));
            baseL.Objects.Add(Id(Label("AxeLbl", 610, 262, "Axe — E (8 coins)", "#FFF2C0", 13)));
            baseL.Objects.Add(Id(Shop("Bench", "#8A5A2B", 810, 290, "item:wood", 8, "sword", "C")));
            baseL.Objects.Add(Id(Label("BenchLbl", 770, 262, "Bench — C (8 wood → sword)", "#AEE1FF", 13)));
            baseL.Objects.Add(Id(Shop("ShovelShop", "#6B7B8C", 980, 290, "coins", 3, "shovel", "E")));
            baseL.Objects.Add(Id(Label("ShovelLbl", 950, 262, "Shovel — E (3 coins)", "#FFF2C0", 13)));

            // ---- night: slimes come out of the forest and the quarry ----
            foreach (var (n, x, y) in new[] { ("ForestDen", 1500.0, 420.0), ("QuarryDen", 560.0, 1220.0) })
            {
                var den = Box(n, x, y, 20, 20, "#00000000");
                den.Behaviors.Add(Script("[{\"when\":\"timer\",\"every\":7,\"do\":\"spawn\",\"object\":\"Slime\",\"cond\":\"isNight >= 1\"}]"));
                baseL.Objects.Add(Id(den));
            }

            // ---- player (added after the trees/rocks so it and the held item draw on top) ----
            baseL.Objects.Add(Id(Player(300, 400, snd.swing, snd.denied)));

            // ---- day / night (Top-down: no sun or moon, the ground covers the sky) + N / M to jump ----
            var dn = Box("DayNight", 0, 0, 10, 10, "#00000000");
            var dnB = B(BehaviorKind.DayNight);
            dnB.Nums["mode"] = 1; dnB.Nums["dayLength"] = 50; dnB.Nums["nightLength"] = 35; dnB.Nums["transition"] = 5;
            dnB.Nums["dayLight"] = 1; dnB.Nums["nightLight"] = 0.2;
            dn.Behaviors.Add(dnB);
            dn.Behaviors.Add(Script("[{\"when\":\"keypress\",\"key\":\"N\",\"do\":\"settime\",\"time\":\"night\"}," +
                                    "{\"when\":\"keypress\",\"key\":\"M\",\"do\":\"settime\",\"time\":\"day\"}]"));
            baseL.Objects.Add(Id(dn));

            // ---- HUD ----
            var hpBar = Box("HpBar", 16, 16, 220, 22, "#00000000");
            var barB = B(BehaviorKind.Bar); barB.Texts["var"] = "hp"; barB.Nums["max"] = 6; hpBar.Behaviors.Add(barB);
            ui.Objects.Add(Id(hpBar));
            ui.Objects.Add(Id(Label("CoinsHUD", 16, 46, "Coins: {coins}", "#FFD86B", 16)));
            ui.Objects.Add(Id(Label("ResHUD", 16, 72, "Wood {item:wood}  ·  Stone {item:stone}", "#CFE8FF", 14)));
            ui.Objects.Add(Id(Label("Hint", 16, 98, "WASD move · Wheel/1-4 hotbar · Click = use the item (axe → trees, pickaxe → rocks, shovel → dirt mounds, sword → slimes) · E buy · C craft · N night · M morning · F1 console", "#E6ECF5", 13)));

            scene.Layers.Add(bg); scene.Layers.Add(baseL); scene.Layers.Add(ui);
            project.Scenes.Add(scene);

            // the shovel is a custom item, added exactly like the editor's "＋ New item" button does it
            var player = baseL.Objects.First(o => o.Name == "Player");
            ItemMaker.Add(project, scene, player, player.Behaviors.First(b => b.Kind == BehaviorKind.Hotbar), new ItemSpec("shovel", "shovel", "#A9B4C2", 1, false));

            // ---- prefabs (scattered by the generators / spawned at runtime) ----
            project.Prefabs.Add(new PrefabDef { Name = "Tree", Template = Tree("Tree", 0, 0, treeSprite, snd) });
            project.Prefabs.Add(new PrefabDef { Name = "Rock", Template = Rock("Rock", 0, 0, rockSprite, snd) });
            project.Prefabs.Add(new PrefabDef { Name = "Mound", Template = Mound("Mound", 0, 0) });
            project.Prefabs.Add(new PrefabDef { Name = "Coin", Template = Coin(PhysicsMode.None) });
            project.Prefabs.Add(new PrefabDef { Name = "Slime", Template = Slime() });

            ProjectIO.Save(project);
            return project.FilePath!;
        }

        // ---------------- factories ----------------
        private static GameObjectData Water(string name, double x, double y, double w, double h)
        {
            var o = Box(name, x, y, w, h, "#3F7FBF");
            o.Physics.Mode = PhysicsMode.Static;
            o.Behaviors.Add(B(BehaviorKind.Solid));
            return o;
        }

        private static GameObjectData Gen(string name, double x, double y, double w, double h, string prefab, int count, double minGap, string genId)
        {
            var o = Box(name, x, y, w, h, "#00000000");
            var g = B(BehaviorKind.Generator);
            g.Nums["count"] = count; g.Nums["minGap"] = minGap;
            g.Texts["prefab"] = prefab; g.Texts["mode"] = "once"; g.Texts["layout"] = "scatter"; g.Texts["id"] = genId;
            o.Behaviors.Add(g);
            return o;
        }

        private static GameObjectData Tree(string name, double x, double y, string sprite, (string swing, string chop, string fall, string denied) snd)
            => DemoKit.Tree(name, x, y, 72, 144, sprite, snd.chop, snd.fall);

        // a rock gives stone, and a coin pops out when it breaks
        private static GameObjectData Rock(string name, double x, double y, string sprite, (string swing, string chop, string fall, string denied) snd)
        {
            var r = Box(name, x, y, 64, 48, "#00000000");
            r.SpritePath = sprite;
            var h = B(BehaviorKind.Harvestable);
            h.Texts["tool"] = "pickaxe"; h.Texts["drop"] = "stone"; h.Nums["hits"] = 3; h.Nums["amount"] = 3; h.Nums["fall"] = 0;
            h.Texts["hitSound"] = snd.chop; h.Texts["fellSound"] = snd.fall;
            r.Behaviors.Add(h);
            r.Behaviors.Add(Script("[{\"when\":\"death\",\"do\":\"spawn\",\"object\":\"Coin\"}]"));
            return r;
        }

        // a dirt mound with treasure: only the shovel digs it (2 hits) → +3 coins
        private static GameObjectData Mound(string name, double x, double y)
        {
            var m = Rounded(Box(name, x, y, 46, 26, "#6B4A2E"), 13);
            m.BorderColor = "#4A311C"; m.BorderWidth = 2;
            var h = B(BehaviorKind.Harvestable);
            h.Texts["tool"] = "shovel"; h.Texts["drop"] = ""; h.Nums["hits"] = 2; h.Nums["amount"] = 0; h.Nums["fall"] = 0;
            m.Behaviors.Add(h);
            m.Behaviors.Add(Script("[{\"when\":\"death\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":3}," +
                                   "{\"when\":\"death\",\"do\":\"emit\",\"count\":14,\"color\":\"#FFD24D\"}]"));
            return m;
        }

        private static GameObjectData Player(double x, double y, string swingSound, string deniedSound)
        {
            var o = new GameObjectData { Name = "Player", Type = "Player", X = x, Y = y, Width = 34, Height = 46, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.None } };
            var move = B(BehaviorKind.TopDown); move.Nums["speed"] = 230; o.Behaviors.Add(move);
            o.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 6; hp.Nums["respawn"] = 1; hp.Texts["var"] = "hp"; o.Behaviors.Add(hp);
            var hot = B(BehaviorKind.Hotbar); hot.Texts["items"] = "pickaxe:#8D949E,axe:#B9C2CC,sword:#DDEBFF"; hot.Texts["player"] = "Player"; o.Behaviors.Add(hot);
            var tool = B(BehaviorKind.ToolUse); tool.Texts["itemDamage"] = "sword=2,axe=1,pickaxe=1"; tool.Nums["reach"] = 80;
            tool.Texts["swingSound"] = swingSound; tool.Texts["deniedSound"] = deniedSound;
            o.Behaviors.Add(tool);
            var torch = B(BehaviorKind.LightSource); torch.Nums["radius"] = 230; torch.Texts["color"] = "#FFE6A0"; o.Behaviors.Add(torch);
            return o;
        }

        // night monster: hops toward the player when it is close, melts at sunrise; drops a coin when killed
        private static GameObjectData Slime()
        {
            var o = new GameObjectData { Name = "Slime", Type = "Enemy", Width = 40, Height = 32, Color = "#8E54E9", CornerRadius = 14, Physics = new PhysicsData { Mode = PhysicsMode.None } };
            var move = B(BehaviorKind.TopDown); move.Nums["controls"] = 0; o.Behaviors.Add(move); // walls stop it; the script steers
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Player"; dmg.Nums["damage"] = 1; dmg.Nums["knockback"] = 300; o.Behaviors.Add(dmg);
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 2; hp.Nums["respawn"] = 0; o.Behaviors.Add(hp);
            const string near = "dist(Player.cx, Player.cy, self.cx, self.cy)";
            var cust = new BehaviorData { Kind = BehaviorKind.Custom };
            cust.Texts["script"] =
                $"[{{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"(Player.cx - self.cx) / max(1, {near}) * 80\",\"cond\":\"{near} < 360\"}}," +
                $"{{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vy\",\"expr\":\"(Player.cy - self.cy) / max(1, {near}) * 80\",\"cond\":\"{near} < 360\"}}," +
                "{\"when\":\"always\",\"do\":\"emit\",\"count\":14,\"color\":\"#B98CF0\",\"cond\":\"isDay >= 1 && dayness >= 0.99\"}," +
                "{\"when\":\"always\",\"do\":\"destroy\",\"cond\":\"isDay >= 1 && dayness >= 0.99\"}," +
                "{\"when\":\"death\",\"do\":\"spawn\",\"object\":\"Coin\"}," +
                "{\"when\":\"death\",\"do\":\"emit\",\"count\":16,\"color\":\"#8E54E9\"}]";
            o.Behaviors.Add(cust);
            return o;
        }

        private static GameObjectData Shop(string name, string color, double x, double y, string currency, double cost, string item, string key)
        {
            var o = Rounded(Box(name, x, y, 44, 44, color), 6);
            var sh = B(BehaviorKind.Shop);
            sh.Texts["currency"] = currency; sh.Texts["item"] = item; sh.Texts["key"] = key; sh.Texts["player"] = "Player";
            sh.Nums["cost"] = cost; sh.Nums["radius"] = 80; sh.Nums["once"] = 1;
            o.Behaviors.Add(sh);
            return o;
        }
    }
}
