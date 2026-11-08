using System.Collections.Generic;
using System.Text.Json;
using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ BUILDERENGINE LINK
        // Objects built on BuilderEngine play here through the bridge (PlayWindow.BuilderBridge.cs): three coins with the
        // starter Coin mechanism (EasyEngine's Platformer hero collects them: the score is BuilderEngine's, shown by an
        // EasyEngine label), a slime with the starter Patrol, a sign with the project's own Sign mechanism (touching it says
        // hello in EasyEngine's dialogue box; its "camera shake" is BuilderEngine-only, logged once in the console) and a
        // starter Label (a BuilderEngine HUD text). Walk right, Enter closes the dialogue, Y confirms; R restarts.
        public const string BuilderLinkScene = "BuilderEngine Link";

        public const string SignMechanism =
            "mechanism Sign\n" +
            "    param message: text = \"Hello from BuilderEngine!\"\n" +
            "\n" +
            "    when touched by \"player\"\n" +
            "        if said: stop\n" +
            "        said = true\n" +
            "        camera shake 4 for 0.2\n" +
            "        say message\n";

        private static Scene BuilderLinkTest(Project p)
        {
            var scene = new Scene { Name = BuilderLinkScene, Width = 1400, Height = 600, BackgroundColor = "#202634", AmbientLight = 1 };
            scene.Variables["confirmLink"] = 0;
            scene.Variables["score"] = 0; // BuilderEngine's score (a global there) starts at 0 on every restart

            // the project's own mechanisms travel in the project's "Builder" block (BuilderEngine's contract)
            p.Extra ??= new();
            p.Extra[BuilderLink.Key] = JsonSerializer.SerializeToElement(new { linkVersion = 1, mechanisms = new Dictionary<string, string> { ["Sign"] = SignMechanism } });
            ProjectIO.SaveSound(p, SoundSynth.Tune(), "coin"); // the starter Coin plays "coin.wav"

            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            var ground = Box("Ground", 0, 500, 1400, 100, "#4A4F5E");
            ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));

            var hero = new GameObjectData { Name = "Hero", Type = "Player", X = 120, Y = 436, Width = 40, Height = 56, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            hero.Behaviors.Add(B(BehaviorKind.Platformer));
            baseL.Objects.Add(Id(hero));

            foreach (var (name, x) in new[] { ("Coin1", 360.0), ("Coin2", 460.0), ("Coin3", 560.0) })
                baseL.Objects.Add(Id(Built(Box(name, x, 462, 28, 28, "#F4C03B"), "Coin", new { value = 1 })));
            baseL.Objects.Add(Id(Built(Box("Slime", 900, 468, 44, 32, "#5BD16A"), "Patrol", new { speed = 60, distance = 100 })));
            baseL.Objects.Add(Id(Built(Box("Sign", 720, 440, 40, 60, "#B08A5A"), "Sign", new { message = "Hello from BuilderEngine!" })));
            ui.Objects.Add(Id(Built(Box("CoinText", 24, 200, 10, 10, "#00000000"), "Label", new { text = "BuilderEngine coins: {score}", size = 18, color = "#F4C03B" })));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmLink\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"" + BuilderLinkScene + "\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 680, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "BUILDERENGINE LINK   The coins, the slime, the sign and the coin counter are built on BuilderEngine.", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "A / D walk · Space jumps · walk right over the coins to the sign (Enter closes what it says)", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "The slime walks back and forth by itself · F1 console shows what BuilderEngine prints", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help4", 24, 130, "Coins counted, slime moving, sign talking? Y  ·  R restart", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Score", 24, 176, "Score (an EasyEngine label): {score}", "#FFFFFF", 16)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "BuilderEngine objects";
            cl.Texts["items"] = string.Join("\n",
                "Collect a BuilderEngine coin => score >= 1",
                "Collect all three => score >= 3",
                "The BuilderEngine slime patrols => Slime.x >= 940",
                "Coins, slime and sign work — press Y => confirmLink >= 1");
            cl.Nums["width"] = 360;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }

        /// <summary>The object built on BuilderEngine: one mechanism with its params, in its "Builder" block (as BuilderEngine saves it).</summary>
        internal static GameObjectData Built(GameObjectData o, string mechanism, object parameters)
        {
            o.Extra ??= new();
            o.Extra[BuilderLink.Key] = JsonSerializer.SerializeToElement(new { mechanisms = new[] { new { name = mechanism, @params = parameters } } });
            return o;
        }
    }
}
