using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        public const string LivesBlocksScene = "Lives & Blocks Test";

        // ============================================================ LIVES & BLOCKS TEST
        // The Lives behavior and the blocks made for what beginners kept failing to build: the hero has 3 lives (hearts at
        // the top right); the Lava costs one and sends him back to the flag (Checkpoint); three coins (Coin, Coin_2, Coin_3)
        // and "When none of an object are left"; a Shopkeeper with "When a key is pressed near an object" (E, 3 coins → a
        // Sword); a spike and a Heart that heals him ("Heal" with a target) and disappears; a J combo with "When a key hasn't
        // been pressed for a while" and "When a variable becomes…". Losing every life starts the scene again.
        private static Scene LivesBlocksTest()
        {
            var scene = new Scene { Name = LivesBlocksScene, Width = 1800, Height = 600, BackgroundColor = "#25324A", AmbientLight = 1 };
            foreach (var v in new[] { "coins", "allCoins", "item:Sword", "healed", "combo", "comboDone", "lostOne" }) scene.Variables[v] = 0;
            scene.Variables["lives"] = 3; // R (and a game over) start again with 3
            scene.Texts["msg"] = "";

            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            var ground = Box("Ground", 0, 500, 1800, 100, "#3A4A3A"); ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));

            var hero = new GameObjectData { Name = "Hero", Type = "Player", X = 80, Y = 436, Width = 40, Height = 64, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            hero.Behaviors.Add(B(BehaviorKind.Platformer));
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 3; hp.Nums["respawn"] = 1; hero.Behaviors.Add(hp);
            var lives = B(BehaviorKind.Lives); lives.Nums["lives"] = 3; lives.Texts["hazard"] = "Lava"; lives.Texts["back"] = "checkpoint"; lives.Texts["scene"] = "";
            hero.Behaviors.Add(lives);
            hero.Behaviors.Add(B(BehaviorKind.FollowCamera));
            baseL.Objects.Add(Id(hero));

            foreach (var (name, x) in new[] { ("Coin", 260.0), ("Coin_2", 330.0), ("Coin_3", 400.0) })
            {
                var c = Box(name, x, 450, 24, 24, "#FFD24D"); c.CornerRadius = 12;
                var col = B(BehaviorKind.Collectible); col.Texts["collector"] = "Hero"; col.Texts["var"] = "coins"; col.Nums["points"] = 1;
                c.Behaviors.Add(col);
                baseL.Objects.Add(Id(c));
            }
            var flag = Box("Flag", 560, 436, 16, 64, "#4DE1A0");
            var cp = B(BehaviorKind.Checkpoint); cp.Texts["target"] = "Hero"; flag.Behaviors.Add(cp);
            baseL.Objects.Add(Id(flag));
            baseL.Objects.Add(Id(Box("Lava", 700, 488, 140, 14, "#FF6A1A")));

            var shop = Box("Shopkeeper", 960, 436, 40, 64, "#8E6CFF");
            shop.Behaviors.Add(Script(
                "[{\"when\":\"keynear\",\"key\":\"E\",\"name\":\"Hero\",\"radius\":120,\"do\":\"addvar\",\"var\":\"coins\",\"amount\":-3,\"cond\":\"coins >= 3\",\"block\":1}," +
                "{\"when\":\"keynear\",\"key\":\"E\",\"name\":\"Hero\",\"radius\":120,\"do\":\"additem\",\"item\":\"Sword\",\"amount\":1,\"cond\":\"coins >= 3\",\"block\":1}," +
                "{\"when\":\"keynear\",\"key\":\"E\",\"name\":\"Hero\",\"radius\":120,\"do\":\"settext\",\"var\":\"msg\",\"to\":\"Here is your Sword!\",\"cond\":\"coins >= 3\",\"block\":1}]"));
            baseL.Objects.Add(Id(shop));
            baseL.Objects.Add(Id(DemoKit.Label("ShopSign", 900, 400, "E: 3 coins → Sword", "#FFFFFF", 13, 200, 8)));

            var spike = Box("Spike", 1120, 484, 40, 16, "#C8C8D0");
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Hero"; dmg.Nums["damage"] = 1; dmg.Nums["knockback"] = 200; spike.Behaviors.Add(dmg);
            baseL.Objects.Add(Id(spike));
            var heart = Box("Heart", 1260, 440, 28, 28, "#FF4D6D"); heart.CornerRadius = 8;
            heart.Behaviors.Add(Script(
                "[{\"when\":\"touch\",\"name\":\"Hero\",\"do\":\"heal\",\"amount\":1,\"target\":\"Hero\",\"block\":1},{\"when\":\"touch\",\"name\":\"Hero\",\"do\":\"setvar\",\"var\":\"healed\",\"to\":1,\"block\":1}," +
                "{\"when\":\"touch\",\"name\":\"Hero\",\"do\":\"destroy\",\"block\":1}]"));
            baseL.Objects.Add(Id(heart));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"allgone\",\"name\":\"Coin\",\"do\":\"setvar\",\"var\":\"allCoins\",\"to\":1,\"block\":1},{\"when\":\"allgone\",\"name\":\"Coin\",\"do\":\"settext\",\"var\":\"msg\",\"to\":\"All the coins!\",\"block\":1}," +
                "{\"when\":\"becomes\",\"var\":\"lives\",\"op\":\"<=\",\"value\":2,\"do\":\"setvar\",\"var\":\"lostOne\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"J\",\"cooldown\":0.1,\"do\":\"addvar\",\"var\":\"combo\",\"amount\":1}," +
                "{\"when\":\"keyidle\",\"key\":\"J\",\"seconds\":1,\"do\":\"setvar\",\"var\":\"combo\",\"to\":0}," +
                "{\"when\":\"becomes\",\"var\":\"combo\",\"op\":\">=\",\"value\":3,\"do\":\"setvar\",\"var\":\"comboDone\",\"to\":1,\"block\":2},{\"when\":\"becomes\",\"var\":\"combo\",\"op\":\">=\",\"value\":3,\"do\":\"settext\",\"var\":\"msg\",\"to\":\"Combo!\",\"block\":2}," +
                "{\"when\":\"becomes\",\"var\":\"combo\",\"op\":\">=\",\"value\":3,\"do\":\"setvar\",\"var\":\"combo\",\"to\":0,\"block\":2}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"" + LivesBlocksScene + "\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 250, 560, 130, "#B0101018"); panel.CornerRadius = 8; // under the checklist (top left)
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(DemoKit.Label("Help1", 24, 256, "LIVES & BLOCKS   3 lives (hearts, top right) · lava: one less, back to the flag", "#FFFFFF", 13, 540, 8)));
            ui.Objects.Add(Id(DemoKit.Label("Help2", 24, 278, "Collect the 3 coins · E next to the Shopkeeper with 3 coins = a Sword", "#FFFFFF", 13, 540, 8)));
            ui.Objects.Add(Id(DemoKit.Label("Help3", 24, 298, "The spike hurts, the Heart heals · J J J quickly = Combo! (1 s pause: 0)", "#FFFFFF", 13, 540, 8)));
            ui.Objects.Add(Id(DemoKit.Label("Help4", 24, 318, "Lose every life: the scene starts again  ·  F1 console  ·  R restart", "#FFFFFF", 13, 540, 8)));
            ui.Objects.Add(Id(DemoKit.Label("Status", 24, 342, "Coins {coins} · Sword {item:Sword} · Health {health} · Combo {combo}   {msg}", "#FFE9A8", 14, 540, 8)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Lives & new blocks";
            cl.Texts["items"] = string.Join("\n",
                "Collect the 3 coins: \"All the coins!\" => allCoins >= 1",
                "E next to the Shopkeeper with 3 coins: a Sword => item:Sword >= 1",
                "Touch the flag, then the lava: a heart lost, back at the flag => lostOne >= 1",
                "The spike hurts, the Heart heals and disappears => healed >= 1",
                "J J J quickly: Combo! => comboDone >= 1");
            cl.Nums["width"] = 400; cl.Texts["corner"] = "tl"; // (the hearts are at the top right)
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }
    }
}
