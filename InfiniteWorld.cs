namespace EasyEngine
{
    /// <summary>
    /// Builds the "Infinite World" project: an endless procedural terrain (hills, caves, ore, trees) you can dig into with
    /// the pickaxe, trees to chop, slimes that come out at night near the player (and drop coins), underground darkness,
    /// a minimap that follows you, and a HUD — all from engine behaviors.
    /// </summary>
    public static class InfiniteWorld
    {
        public const string ProjectName = "Infinite World";
        private const int TS = 32, Surface = 600, Depth = 60;

        public static string Build(string parentFolder)
        {
            var project = ProjectIO.CreateNew(ProjectName, parentFolder);
            project.Scenes.Clear();
            project.StartScene = "World";
            project.Variables["coins"] = 0;
            foreach (var tool in new[] { "pickaxe", "axe", "sword" }) project.Variables["item:" + tool] = 1; // every tool owned from the start

            var scene = new Scene { Name = "World", Width = 4000, Height = Surface + Depth * TS + 200, BackgroundColor = "#8EC5F0" };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            // ---- the world itself: one Terrain Generator (a new world every game, endless both ways) ----
            var land = Box("Land", 0, 0, 64, 64, "#00000000");
            var tb = B(BehaviorKind.Terrain);
            tb.Nums["seed"] = 0; tb.Nums["tileSize"] = TS; tb.Nums["surface"] = Surface; tb.Nums["hills"] = 2; tb.Nums["depth"] = Depth;
            tb.Nums["endless"] = 1; tb.Nums["caves"] = 1; tb.Nums["ores"] = 1; tb.Nums["trees"] = 1; tb.Nums["treeDensity"] = 0.15; tb.Nums["mineable"] = 1;
            tb.Texts["requires"] = "holding:pickaxe"; tb.Texts["tree"] = "Tree"; tb.Texts["startOn"] = "Player";
            land.Behaviors.Add(tb);
            baseL.Objects.Add(Id(land));

            var snd = GameAssets.SaveToolSounds(project);
            baseL.Objects.Add(Id(Player(400, 100, snd)));

            // ---- day / night: slimes at night, darker the deeper you dig ----
            var dn = Box("DayNight", 0, 0, 10, 10, "#00000000");
            var dnB = B(BehaviorKind.DayNight);
            dnB.Nums["mode"] = 0; dnB.Nums["dayLength"] = 90; dnB.Nums["nightLength"] = 60; dnB.Nums["transition"] = 5;
            dnB.Nums["dayLight"] = 1; dnB.Nums["nightLight"] = 0.22; dnB.Nums["arcHeight"] = 170;
            dnB.Nums["coverDark"] = 1; dnB.Nums["coverDepth"] = 6; dnB.Nums["coverMax"] = 0.92; dnB.Texts["coverWho"] = "Player"; // underground darkness
            dn.Behaviors.Add(dnB);
            baseL.Objects.Add(Id(dn));

            // ---- HUD (screen space, under the play window's top bar) ----
            var hpBar = Box("HpBar", 16, 64, 220, 22, "#00000000");
            var barB = B(BehaviorKind.Bar); barB.Texts["var"] = "hp"; barB.Nums["max"] = 6; hpBar.Behaviors.Add(barB);
            ui.Objects.Add(Id(hpBar));
            ui.Objects.Add(Id(Label("CoinsHUD", 16, 94, "Coins: {coins}", "#FFD86B", 16)));
            ui.Objects.Add(Id(Label("ResHUD", 16, 120, "Wood {item:wood}  ·  Dirt {item:dirt}  ·  Stone {item:stone}  ·  Ore {item:ore}", "#CFE8FF", 14)));
            ui.Objects.Add(Id(Label("Hint", 16, 146, "A/D move · Space jump · 1-3 tools · click to use · M big map · F1 console", "#E6ECF5", 13)));
            var map = Box("Minimap", 0, 0, 220, 120, "#00000000");
            var mm = B(BehaviorKind.Minimap);
            mm.Nums["size"] = 220; mm.Texts["corner"] = "tr"; mm.Texts["player"] = "Player";
            mm.Texts["markers"] = "Slime:#9B59F0,Coin:#FFD24D,Tree:#3C8C3C";
            mm.Nums["canMax"] = 1; mm.Texts["maxKey"] = "M"; // M opens the big map
            map.Behaviors.Add(mm);
            ui.Objects.Add(Id(map));

            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            SkySetup.Ensure(scene); // Sky layer at the bottom with the Sun and Moon of the cycle
            project.Scenes.Add(scene);

            // ---- prefabs: planted by the terrain / spawned at night / dropped ----
            project.Prefabs.Add(new PrefabDef { Name = "Tree", Template = Tree(GameAssets.TreeSprite(project), snd) });
            project.Prefabs.Add(new PrefabDef { Name = "Slime", Template = Slime() });
            project.Prefabs.Add(new PrefabDef { Name = "Coin", Template = Coin() });

            ProjectIO.Save(project);
            return project.FilePath!;
        }

        // ---------------- factories ----------------
        private static BehaviorData B(BehaviorKind k) => BehaviorCatalog.Info(k).CreateData();

        private static BehaviorData Script(string json) { var c = new BehaviorData { Kind = BehaviorKind.Custom }; c.Texts["script"] = json; return c; }

        private static GameObjectData Box(string name, double x, double y, double w, double h, string color)
            => new() { Name = name, Type = "Object", X = x, Y = y, Width = w, Height = h, Color = color, Physics = new PhysicsData { Mode = PhysicsMode.None } };

        private static GameObjectData Label(string name, double x, double y, string text, string color, double size)
        {
            var o = Box(name, x, y, 900, size + 10, "#00000000");
            var lb = B(BehaviorKind.Label); lb.Texts["text"] = text; lb.Texts["color"] = color; lb.Texts["align"] = "left"; lb.Nums["fontSize"] = size;
            o.Behaviors.Add(lb);
            return o;
        }

        private static GameObjectData Player(double x, double y, (string swing, string chop, string fall, string denied) snd)
        {
            var o = new GameObjectData { Name = "Player", Type = "Player", X = x, Y = y, Width = 28, Height = 46, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 240; plat.Nums["jump"] = 640; o.Behaviors.Add(plat);
            o.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 6; hp.Nums["respawn"] = 1; hp.Texts["var"] = "hp"; o.Behaviors.Add(hp);
            var hot = B(BehaviorKind.Hotbar); hot.Texts["items"] = "pickaxe:#8D949E,axe:#B9C2CC,sword:#DDEBFF"; hot.Texts["player"] = "Player"; o.Behaviors.Add(hot);
            // click to use what's in hand: the pickaxe digs blocks, the axe fells trees, the sword hits slimes hardest
            var tool = B(BehaviorKind.ToolUse); tool.Texts["itemDamage"] = "sword=2,axe=1,pickaxe=1"; tool.Nums["reach"] = 100;
            tool.Texts["swingSound"] = snd.swing; tool.Texts["deniedSound"] = snd.denied;
            o.Behaviors.Add(tool);
            var torch = B(BehaviorKind.LightSource); torch.Nums["radius"] = 260; torch.Texts["color"] = "#FFE6A0"; o.Behaviors.Add(torch);
            // at night a slime comes out near the player every 8 s, at most 4 at a time ('slimes' is recounted every frame:
            // reset here, then each slime adds itself — the timer reads last frame's count)
            o.Behaviors.Add(Script("[{\"when\":\"timer\",\"every\":8,\"do\":\"spawn\",\"object\":\"Slime\",\"at\":\"near\",\"cond\":\"isNight >= 1 && slimes < 4\"}," +
                                   "{\"when\":\"always\",\"do\":\"setvar\",\"var\":\"slimes\",\"to\":0}]"));
            return o;
        }

        private static GameObjectData Tree(string sprite, (string swing, string chop, string fall, string denied) snd)
        {
            var t = Box("Tree", 0, 0, 96, 192, "#00000000");
            t.SpritePath = sprite;
            var h = B(BehaviorKind.Harvestable);
            h.Texts["tool"] = "axe"; h.Texts["drop"] = "wood"; h.Nums["hits"] = 4; h.Nums["amount"] = 5;
            h.Texts["hitSound"] = snd.chop; h.Texts["fellSound"] = snd.fall;
            t.Behaviors.Add(h);
            return t;
        }

        private static GameObjectData Slime()
        {
            var o = new GameObjectData { Name = "Slime", Type = "Enemy", Width = 40, Height = 30, Color = "#9B59F0", CornerRadius = 12, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["controls"] = 0; o.Behaviors.Add(plat);
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Player"; dmg.Nums["damage"] = 1; dmg.Nums["knockback"] = 300; o.Behaviors.Add(dmg);
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 2; hp.Nums["respawn"] = 0; o.Behaviors.Add(hp);
            var off = B(BehaviorKind.DestroyOffscreen); off.Nums["margin"] = 900; o.Behaviors.Add(off); // left far behind → gone
            // count itself, hop toward the player (over the hills), melt at sunrise, drop a coin when killed
            o.Behaviors.Add(Script("[{\"when\":\"always\",\"do\":\"addvar\",\"var\":\"slimes\",\"amount\":1}," +
                                   "{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"sign(Player.cx - self.cx) * 80\"}," +
                                   "{\"when\":\"timer\",\"every\":1.2,\"do\":\"jump\",\"force\":560}," +
                                   "{\"when\":\"timer\",\"every\":1,\"do\":\"destroy\",\"cond\":\"isDay >= 1\"}," +
                                   "{\"when\":\"death\",\"do\":\"spawn\",\"object\":\"Coin\"}," +
                                   "{\"when\":\"death\",\"do\":\"emit\",\"count\":16,\"color\":\"#9B59F0\"}]"));
            return o;
        }

        private static GameObjectData Coin()
        {
            // Dynamic so a dropped coin falls to the ground where the player can reach it
            var o = new GameObjectData { Name = "Coin", Type = "Object", Width = 22, Height = 22, Color = "#FFD24D", CornerRadius = 11, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            o.Behaviors.Add(Script("[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":1}," +
                                   "{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"emit\",\"count\":8,\"color\":\"#FFD24D\"}," +
                                   "{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"destroy\"}]"));
            return o;
        }
    }
}
