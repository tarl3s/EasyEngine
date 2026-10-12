namespace EasyEngine
{
    /// <summary>Object and behavior factories shared by the demo games and the test scenarios (art: <see cref="GameAssets"/>).</summary>
    internal static class DemoKit
    {
        /// <summary>A behavior with its catalog defaults.</summary>
        public static BehaviorData B(BehaviorKind k) => BehaviorCatalog.Info(k).CreateData();

        /// <summary>A Custom behavior running <paramref name="json"/> (block rules).</summary>
        public static BehaviorData Script(string json) => new() { Kind = BehaviorKind.Custom, Texts = { ["script"] = json } };

        /// <summary>A plain coloured rectangle with no physics.</summary>
        public static GameObjectData Box(string name, double x, double y, double w, double h, string color)
            => new() { Name = name, Type = "Object", X = x, Y = y, Width = w, Height = h, Color = color, Physics = new PhysicsData { Mode = PhysicsMode.None } };

        public static GameObjectData Rounded(GameObjectData o, double r) { o.CornerRadius = r; return o; }

        /// <summary>Left-aligned text starting at (x, y), in a transparent box <paramref name="width"/> wide and size + <paramref name="pad"/> tall.</summary>
        public static GameObjectData Label(string name, double x, double y, string text, string color, double size, double width = 900, double pad = 10)
        {
            var o = Box(name, x, y, width, size + pad, "#00000000");
            var lb = B(BehaviorKind.Label); lb.Texts["text"] = text; lb.Texts["color"] = color; lb.Texts["align"] = "left"; lb.Nums["fontSize"] = size;
            o.Behaviors.Add(lb);
            return o;
        }

        /// <summary>A tree drawn by <paramref name="sprite"/>: the axe fells it in 4 hits for 5 wood.</summary>
        public static GameObjectData Tree(string name, double x, double y, double w, double h, string sprite, string hitSound, string fellSound)
        {
            var t = Box(name, x, y, w, h, "#00000000");
            t.SpritePath = sprite;
            var hv = B(BehaviorKind.Harvestable);
            hv.Texts["tool"] = "axe"; hv.Texts["drop"] = "wood"; hv.Nums["hits"] = 4; hv.Nums["amount"] = 5;
            hv.Texts["hitSound"] = hitSound; hv.Texts["fellSound"] = fellSound;
            t.Behaviors.Add(hv);
            return t;
        }

        /// <summary>The coin prefab: touching it gives 1 coin, with a sparkle.</summary>
        public static GameObjectData Coin(PhysicsMode physics)
        {
            var o = new GameObjectData { Name = "Coin", Type = "Object", Width = 22, Height = 22, Color = "#FFD24D", CornerRadius = 11, Physics = new PhysicsData { Mode = physics } };
            o.Behaviors.Add(Script("[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":1}," +
                                   "{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"emit\",\"count\":8,\"color\":\"#FFD24D\"}," +
                                   "{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"destroy\"}]"));
            return o;
        }
    }
}
