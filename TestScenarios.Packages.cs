using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using static EasyEngine.DemoKit;

namespace EasyEngine
{
    /// <summary>Behavior packages used by the tests and the "Behavior Packages" scene (BehaviorPackages.cs): written as real
    /// .behavior files (BuilderEngine's format), read back only through BuilderEngine's BehaviorPackage.</summary>
    public static class TestPackages
    {
        public const string TwirlScript =
            "mechanism Twirl\n" +
            "    param speed: number = 90\n" +
            "    param direction: Choice \"clockwise\", \"counter\" = \"clockwise\"\n" +
            "\n" +
            "    when tick\n" +
            "        if direction == \"clockwise\"\n" +
            "            self.angle += speed * dt\n" +
            "        else\n" +
            "            self.angle -= speed * dt\n";

        public const string DriftScript =
            "mechanism Drift\n" +
            "    param speed: number = 60\n" +
            "\n" +
            "    when tick\n" +
            "        self.x += speed * dt\n";

        public const string BellScript =
            "mechanism Bell\n" +
            "    param press: Key = \"e\"\n" +
            "    param amount: number = 1\n" +
            "\n" +
            "    when tick\n" +
            "        down = key(press)\n" +
            "        if down and not was\n" +
            "            rings += amount\n" +
            "        was = down\n";

        public const string GadgetScript =
            "mechanism Gadget\n" +
            "    param power: number = 5\n" +
            "    param delay: seconds = 1.5\n" +
            "    param loud: bool = false\n" +
            "    param title: text = \"Hi\"\n" +
            "    param noise: Sound = \"\"\n" +
            "    param picture: Image = \"\"\n" +
            "    param tint: Color = \"#FF8800\"\n" +
            "    param trigger: Key = \"space\"\n" +
            "    param target: Object = \"Player\"\n" +
            "    param next_scene: Scene = \"\"\n" +
            "    param route: Path = []\n" +
            "    param pose: animation = \"\"\n" +
            "    param steps: List of number = [1, 2]\n" +
            "    param mode: Choice \"calm\", \"wild\" = \"calm\"\n" +
            "    param wild_level: number = 3\n" +
            "\n" +
            "    when start\n" +
            "        print title\n";

        private static JsonObject P(string name, string label, string type, JsonNode? def, string help = "", double? min = null, double? max = null, double? step = null, string? showIf = null)
        {
            var o = new JsonObject { ["name"] = name, ["label"] = label, ["type"] = type, ["default"] = def };
            if (help.Length > 0) o["help"] = help;
            if (min != null) o["min"] = min; if (max != null) o["max"] = max; if (step != null) o["step"] = step;
            if (showIf != null) o["showIf"] = showIf;
            return o;
        }

        public static string Json(string name, string icon, string category, string description, string script, JsonArray parameters,
                                  bool repeatable = false, string[]? keywords = null, string guide = "")
        {
            var o = new JsonObject
            {
                ["format"] = "builderengine.behavior", ["version"] = 1, ["name"] = name, ["icon"] = icon, ["category"] = category,
                ["description"] = description, ["author"] = "EasyEngine tests", ["license"] = "MIT", ["packageVersion"] = "1.0.0",
                ["params"] = parameters, ["script"] = script,
            };
            if (repeatable) o["repeatable"] = true;
            if (keywords != null || guide.Length > 0)
            {
                var ai = new JsonObject();
                if (keywords != null) ai["keywords"] = new JsonArray(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(keywords, k => (JsonNode?)k)));
                if (guide.Length > 0) ai["guide"] = guide;
                o["ai"] = ai;
            }
            return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        public static string Twirl(string description = "Turns the object round and round.") => Json("Twirl", "🌀", "Movement", description, TwirlScript,
            new JsonArray(P("speed", "Speed (degrees per second)", "number", 90, "Negative turns the other way.", -1080, 1080, 15),
                          P("direction", "Direction", "Choice \"clockwise\", \"counter\"", "clockwise")),
            keywords: new[] { "twirl", "rotate", "girar" }, guide: "Add Twirl to make a gem or a saw turn. Speed is degrees per second.");

        public static string Drift(double speed = 60, string description = "Slides the object sideways on its own, like a breeze.") => Json("Drift", "🍃", "Movement", description, DriftScript,
            new JsonArray(P("speed", "Speed (px per second)", "number", speed, "Negative slides left.", -600, 600, 10)),
            keywords: new[] { "drift", "breeze", "wind", "vento" }, guide: "Drift moves an object sideways by itself, on top of anything else that moves it.");

        public static string Bell() => Json("Bell", "🔔", "Puzzle", "Adds to the variable rings each time a key is pressed.", BellScript,
            new JsonArray(P("press", "Key", "Key", "e", "The key that rings it."),
                          P("amount", "Rings per press", "number", 1, "", 1, 10, 1)),
            repeatable: true, keywords: new[] { "bell", "ring", "sino" });

        public static string Gadget() => Json("Gadget", "🧪", "Tests", "A package with one setting of every type (the inspector's pickers).", GadgetScript,
            new JsonArray(P("power", "Power", "number", 5, "How strong it is.", 0, 10, 0.5),
                          P("delay", "Delay", "seconds", 1.5),
                          P("loud", "Loud", "bool", false),
                          P("title", "Title", "text", "Hi"),
                          P("noise", "Noise", "Sound", ""),
                          P("picture", "Picture", "Image", ""),
                          P("tint", "Tint", "Color", "#FF8800"),
                          P("trigger", "Trigger key", "Key", "space"),
                          P("target", "Target", "Object", "Player"),
                          P("next_scene", "Next scene", "Scene", ""),
                          P("route", "Route", "Path", new JsonArray()),
                          P("pose", "Pose", "animation", ""),
                          P("steps", "Steps", "List of number", new JsonArray(1, 2)),
                          P("mode", "Mode", "Choice \"calm\", \"wild\"", "calm"),
                          P("wild_level", "Wild level", "number", 3, "", 1, 9, 1, "mode == \"wild\"")));

        /// <summary>Writes these packages (file name → JSON) into a folder.</summary>
        public static void Write(string folder, params (string file, string json)[] packages)
        {
            Directory.CreateDirectory(folder);
            foreach (var (file, json) in packages) File.WriteAllText(Path.Combine(folder, file), json);
        }

        /// <summary>A package behavior with its settings, as the inspector stores it.</summary>
        public static BehaviorData Use(string package, Dictionary<string, double>? nums = null, Dictionary<string, string>? texts = null)
        {
            var b = new BehaviorData { Kind = BehaviorKind.Package };
            b.Texts[BehaviorPackages.NameKey] = package;
            if (nums != null) foreach (var kv in nums) b.Nums[kv.Key] = kv.Value;
            if (texts != null) foreach (var kv in texts) b.Texts[kv.Key] = kv.Value;
            return b;
        }
    }

    public static partial class TestScenarios
    {
        // ============================================================ BEHAVIOR PACKAGES
        // Behaviors from .behavior packages (BehaviorPackages.cs) playing through the BuilderEngine bridge: a gem turned by the
        // Twirl package, a cloud slid by Drift, the Platformer hero also pushed by a gentle Drift (both move it), and a Bell
        // package that counts E presses in the EasyEngine variable rings. The project's behaviors/ folder holds the files.
        public const string PackagesScene = "Behavior Packages";

        private static Scene PackagesTest(Project p)
        {
            if (p.FolderPath != null)
                TestPackages.Write(Path.Combine(p.FolderPath, "behaviors"),
                    ("Twirl.behavior", TestPackages.Twirl()), ("Drift.behavior", TestPackages.Drift()), ("Bell.behavior", TestPackages.Bell()));

            var scene = new Scene { Name = PackagesScene, Width = 1400, Height = 600, BackgroundColor = "#1F2A33", AmbientLight = 1 };
            scene.Variables["rings"] = 0;
            scene.Variables["confirmPkg"] = 0;
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            var ground = Box("Ground", 0, 500, 1400, 100, "#4A4F5E");
            ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));

            var hero = new GameObjectData { Name = "Hero", Type = "Player", X = 120, Y = 436, Width = 40, Height = 56, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            hero.Behaviors.Add(B(BehaviorKind.Platformer));
            hero.Behaviors.Add(TestPackages.Use("Drift", new() { ["speed"] = 30 })); // a breeze: the hero slides right even standing still
            baseL.Objects.Add(Id(hero));

            var gem = Box("Gem", 520, 400, 40, 40, "#4DC8E5");
            gem.Behaviors.Add(TestPackages.Use("Twirl", new() { ["speed"] = 180 }, new() { ["direction"] = "clockwise" }));
            baseL.Objects.Add(Id(gem));

            var cloud = Box("Cloud", 200, 220, 120, 40, "#E8EEF5"); cloud.CornerRadius = 18;
            cloud.Behaviors.Add(TestPackages.Use("Drift", new() { ["speed"] = 40 }));
            baseL.Objects.Add(Id(cloud));

            var bell = Box("Bell", 900, 420, 40, 48, "#F4C03B"); bell.CornerRadius = 10;
            bell.Behaviors.Add(TestPackages.Use("Bell", new() { ["amount"] = 1 }, new() { ["press"] = "E" }));
            baseL.Objects.Add(Id(bell));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmPkg\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"" + PackagesScene + "\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 700, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "BEHAVIOR PACKAGES   The gem, the cloud, the bell and the breeze on the hero are .behavior packages.", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "Twirl turns the gem · Drift slides the cloud and pushes the hero right · Bell counts E presses", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "A / D walk, Space jumps (the breeze adds to it) · F1 console shows the packages loaded", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help4", 24, 130, "Gem turning, cloud moving, E ringing? Y  ·  R restart", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Rings", 24, 176, "Rings (an EasyEngine variable set by the Bell package): {rings}", "#FFFFFF", 16)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Behavior packages";
            cl.Texts["items"] = string.Join("\n",
                "Twirl turns the gem => Gem.rot >= 90",
                "Drift slides the cloud => Cloud.x >= 260",
                "The breeze pushes the hero right => Hero.x >= 160",
                "Press E: the Bell rings => rings >= 1",
                "All four packages work — press Y => confirmPkg >= 1");
            cl.Nums["width"] = 380;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }
    }
}
