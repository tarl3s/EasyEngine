using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EasyEngine;

// Light shapes (circle, beam, half circle, area, bar) and effects (flicker, pulse, blink) — PlayWindow.LightShapes.cs.
public partial class App
{
    partial void FeatureTestsLights(FullTestKit t)
    {
        static BehaviorData Light(string shape = "", string direction = "", string effect = "", double radius = 300, double spread = 40,
                                  params (string key, double val)[] nums)
        {
            var b = new BehaviorData { Kind = BehaviorKind.LightSource, Nums = { ["radius"] = radius, ["intensity"] = 1 }, Texts = { ["color"] = "#FFA040" } };
            if (shape.Length > 0) { b.Texts["shape"] = shape; b.Nums["spread"] = spread; }
            if (direction.Length > 0) b.Texts["direction"] = direction;
            if (effect.Length > 0) b.Texts["effect"] = effect;
            foreach (var (k, v) in nums) b.Nums[k] = v;
            return b;
        }
        static GameObjectData Obj(string name, double cx, double cy, double w, double h, params BehaviorData[] behaviors)
        {
            var o = new GameObjectData { Id = 600 + name.Length * 7 + (int)cx % 97, Name = name, Type = "Object", X = cx - w / 2, Y = cy - h / 2, Width = w, Height = h,
                                         Color = "#FFE7A0", Physics = new PhysicsData { Mode = PhysicsMode.None } };
            o.Behaviors.AddRange(behaviors);
            return o;
        }
        // a dark scene (ambient light 0.1) with these objects; the lighting worked out once
        PlayWindow Dark(params GameObjectData[] objs)
        {
            var p = t.Sim("[]", PhysicsMode.None, (pr, s) =>
            {
                s.AmbientLight = 0.1;
                s.Layers.First(z => z.Kind == LayerKind.Base).Objects.AddRange(objs);
            });
            p.TSteps(2); p.TUpdateLighting();
            return p;
        }
        // a dark scene with one light "L" centred at (600, 300)
        PlayWindow One(BehaviorData light, double w = 16, double h = 16) => Dark(Obj("L", 600, 300, w, h, light));
        bool Lit(PlayWindow p, double x, double y) => p.TShapedLightAt(x, y) > 0.8;
        bool Unlit(PlayWindow p, double x, double y) => p.TShapedLightAt(x, y) < 0.05;

        // ---------------- old projects and the default look ----------------
        t.Test("lights: an old project's light (no shape / effect keys) and a new default Light are the original round light", () =>
        {
            var p = Dark(Obj("Old", 400, 300, 16, 16, Light()), Obj("New", 700, 300, 16, 16, BehaviorCatalog.Info(BehaviorKind.LightSource).CreateData()));
            return p.TLightHoles == 2 && p.TShapedLights == 0 && p.TLightPlain("Old") && p.TLightPlain("New")
                   && p.TLightVisualType("Old") == "Ellipse" && p.TLightVisualType("New") == "Ellipse";
        });
        t.Test("lights: the circle profile at the default soft edge is the original one (clear core to 68%, soft edge to 100%)", () =>
            Math.Abs(LightShapes.Dark(0.5, 0.9, 0.5) - 0.01) < 1e-9 && Math.Abs(LightShapes.Dark(0.68, 0.9, 0.5) - 0.01) < 1e-9
            && Math.Abs(LightShapes.Dark(0.85, 0.9, 0.5) - 0.55) < 1e-9 && LightShapes.Dark(1, 0.9, 0.5) == 1
            && Math.Abs(LightShapes.Glow(0) - 0.45) < 1e-9 && Math.Abs(LightShapes.Glow(0.6) - 0.34) < 1e-9 && LightShapes.Glow(1) == 0);
        t.Test("lights: words for shapes, directions and effects (flashlight → beam, window → area, cursor → mouse, candle → flicker; unknown → circle)", () =>
            LightShapes.Shape("Flashlight") == "beam" && LightShapes.Shape("cone") == "beam" && LightShapes.Shape("window") == "area"
            && LightShapes.Shape("neon tube") == "bar" && LightShapes.Shape("half-circle") == "half" && LightShapes.Shape("") == "circle" && LightShapes.Shape("blob") == "circle"
            && LightShapes.Direction("cursor") == "mouse" && LightShapes.Direction("Up") == "up" && LightShapes.Direction("") == "facing"
            && LightShapes.Effect("candle") == "flicker" && LightShapes.Effect("alarm") == "blink" && LightShapes.Effect("pulsing") == "pulse" && LightShapes.Effect("") == "none");

        // ---------------- smooth lighting ----------------
        t.Test("lights: a beam (pointing right) lights in front of it, not behind it nor outside its cone; a little around its holder", () =>
        {
            var p = One(Light("beam", "right"));
            return p.TShapedLights == 1 && p.TLightVisualType("L") == "Image" && Lit(p, 750, 300) && Lit(p, 780, 320)
                   && Unlit(p, 450, 300) && Unlit(p, 750, 450) && Unlit(p, 600, 150) && p.TShapedLightAt(612, 300) > 0.5 && Unlit(p, 950, 300);
        });
        t.Test("lights: a wider cone lights further to the side (spread 90 vs 20)", () =>
        {
            var wide = One(Light("beam", "right", spread: 90)); var narrow = One(Light("beam", "right", spread: 20));
            return Lit(wide, 750, 400) && Unlit(narrow, 750, 400) && Lit(narrow, 750, 300);
        });
        t.Test("lights: drawn for real — the beam's hole shows in the darkness on screen (in front clear, behind dark)", () =>
        {
            var p = One(Light("beam", "right"));
            double front = p.TRenderedDarknessAt(760, 300), behind = p.TRenderedDarknessAt(440, 300), side = p.TRenderedDarknessAt(760, 460);
            var down = One(Light("beam", "down")); // turned: the picture rotates with the light
            return front < 0.15 && behind > 0.8 && side > 0.8 && p.TRenderedDarknessAt(950, 300) > 0.8
                   && down.TRenderedDarknessAt(600, 450) < 0.15 && down.TRenderedDarknessAt(600, 150) > 0.8 && down.TRenderedDarknessAt(760, 300) > 0.8;
        });
        t.Test("lights: drawn for real — an old round light's hole is unchanged (clear near it, dark far away)", () =>
        {
            var p = One(Light(radius: 200));
            return p.TRenderedDarknessAt(650, 300) < 0.1 && p.TRenderedDarknessAt(900, 300) > 0.8;
        });
        t.Test("lights: direction 'facing' follows the player — the flashlight flips when it walks left, and back", () =>
        {
            var pl = Obj("P", 600, 300, 30, 50, Light("beam", "facing"), new BehaviorData { Kind = BehaviorKind.Platformer, Nums = { ["speed"] = 200 } });
            pl.Type = "Player";
            var p = Dark(pl);
            bool right = Math.Abs(p.TLightAim("P")) < 1e-6;
            p.TKey("A", true); p.TSteps(5); p.TKey("A", false); p.TStep(); p.TUpdateLighting();
            double cx = p.TX("P") + 15, cy = p.TY("P") + 25;
            bool left = Math.Abs(p.TLightAim("P") - 180) < 1e-6 && Lit(p, cx - 150, cy) && Unlit(p, cx + 150, cy);
            p.TKey("D", true); p.TSteps(5); p.TKey("D", false); p.TStep(); p.TUpdateLighting();
            return right && left && Math.Abs(p.TLightAim("P")) < 1e-6;
        });
        t.Test("lights: a top-down character's flashlight points where it walks (down, diagonal) and stays there when it stops", () =>
        {
            var pl = Obj("P", 600, 300, 30, 30, Light("beam", "facing"), new BehaviorData { Kind = BehaviorKind.TopDown, Nums = { ["speed"] = 200 } });
            var p = Dark(pl);
            p.TKey("S", true); p.TSteps(3); p.TKey("S", false); p.TSteps(3); p.TUpdateLighting();
            bool down = Math.Abs(p.TLightAim("P") - 90) < 1e-6;
            p.TKey("W", true); p.TKey("D", true); p.TSteps(3); p.TKey("W", false); p.TKey("D", false); p.TSteps(3); p.TUpdateLighting();
            return down && Math.Abs(p.TLightAim("P") + 45) < 1e-6;
        });
        t.Test("lights: direction 'mouse' aims at the pointer; fixed directions point their way (up = -90°)", () =>
        {
            var p = Dark(Obj("M", 600, 300, 16, 16, Light("beam", "mouse")), Obj("U", 200, 300, 16, 16, Light("beam", "up")));
            p.TMouseAt(600, 600); p.TStep(); p.TUpdateLighting();
            return Math.Abs(p.TLightAim("M") - 90) < 1e-6 && Lit(p, 600, 450) && Math.Abs(p.TLightAim("U") + 90) < 1e-6 && Lit(p, 200, 150);
        });
        t.Test("lights: a half circle (wall lamp pointing down) lights below it, not above", () =>
        {
            var p = One(Light("half", "down", radius: 200));
            return Lit(p, 600, 400) && Lit(p, 500, 330) && Unlit(p, 600, 200);
        });
        t.Test("lights: an area light lights its whole rectangle and a little past its edges (radius 60)", () =>
        {
            var p = One(Light("area", radius: 60), 200, 100); // the rectangle 500..700 × 250..350
            return Lit(p, 506, 256) && Lit(p, 694, 344) && Lit(p, 600, 300) && Lit(p, 720, 300) && Unlit(p, 790, 300) && Unlit(p, 600, 440);
        });
        t.Test("lights: a bar glows along its length (a wide tube: along x; a tall one: along y)", () =>
        {
            var wide = One(Light("bar", radius: 80), 300, 10);
            var tall = One(Light("bar", radius: 80), 10, 300);
            return Lit(wide, 745, 300) && Lit(wide, 600, 340) && Unlit(wide, 600, 400) && Unlit(wide, 850, 300)
                   && Lit(tall, 600, 445) && Unlit(tall, 700, 300);
        });

        // ---------------- effects ----------------
        (double sMin, double sMax, double lMin, double lMax, HashSet<double> levels) Sample(string effect, double speed = 1)
        {
            var p = One(Light(effect: effect, radius: 200, nums: ("speed", speed)));
            double sMin = 9, sMax = 0, lMin = 9, lMax = 0; var levels = new HashSet<double>();
            for (int i = 0; i < 240; i++)
            {
                p.TStep(); p.TUpdateLighting();
                double s = p.TLightScale("L"), l = p.TLightLevel("L");
                sMin = Math.Min(sMin, s); sMax = Math.Max(sMax, s); lMin = Math.Min(lMin, l); lMax = Math.Max(lMax, l); levels.Add(l);
            }
            return (sMin, sMax, lMin, lMax, levels);
        }
        t.Test("lights: flicker changes size and brightness a little, smoothly, and stays in range", () =>
        {
            var f = Sample("flicker");
            return f.sMin >= 0.94 && f.sMax <= 1.06 && f.sMax - f.sMin > 0.03 && f.lMin >= 0.75 && f.lMax <= 1 && f.lMax - f.lMin > 0.08;
        });
        t.Test("lights: pulse slowly grows and shrinks (about ±12%)", () =>
        {
            var f = Sample("pulse");
            return f.sMin >= 0.87 && f.sMax <= 1.13 && f.sMax - f.sMin > 0.15 && f.lMin >= 0.83;
        });
        t.Test("lights: blink is only on or off (1 or 0), both happen; the light with an effect uses the shaped hole", () =>
        {
            var f = Sample("blink", 2);
            var p = One(Light(effect: "blink"));
            return f.levels.SetEquals(new[] { 0.0, 1.0 }) && p.TShapedLights == 1 && !p.TLightPlain("L");
        });
        t.Test("lights: a blinking light that is off cuts no hole; on, it does (also as drawn)", () =>
        {
            var p = One(Light(effect: "blink", radius: 200));
            bool sawOff = false, sawOn = false;
            for (int i = 0; i < 80; i++)
            {
                p.TStep(); p.TUpdateLighting();
                if (p.TLightLevel("L") == 0) { if (!sawOff) sawOff = p.TShapedLightAt(600, 300) == 0 && p.TRenderedDarknessAt(600, 300) > 0.8; }
                else if (!sawOn) sawOn = p.TShapedLightAt(600, 300) > 0.9 && p.TRenderedDarknessAt(600, 300) < 0.15;
            }
            return sawOff && sawOn;
        });

        // ---------------- block lighting ----------------
        // at night (light 0.2), Blocks lighting, the light "L" at (600, 300), a solid wall at x 660..692 when wall = true
        PlayWindow Blocks(BehaviorData light, bool wall = false, double w = 16, double h = 16)
        {
            light.Nums["tint"] = 0;
            var p = t.Sim("[]", PhysicsMode.None, (pr, s) =>
            {
                var l = s.Layers.First(z => z.Kind == LayerKind.Base);
                l.Objects.Add(Obj("L", 600, 300, w, h, light));
                if (wall) l.Objects.Add(new GameObjectData { Id = 520, Name = "Wall", Type = "Object", X = 660, Y = 150, Width = 32, Height = 300, Color = "#777777",
                    Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
                var dn = new BehaviorData { Kind = BehaviorKind.DayNight };
                foreach (var (k, v) in new (string, double)[] { ("dayLength", 100), ("nightLength", 100), ("transition", 0), ("nightLight", 0.2), ("startAtNight", 1), ("lightStyle", 1) })
                    dn.Nums[k] = v;
                l.Objects.Add(new GameObjectData { Id = 530, Name = "DN", Type = "Object", Width = 10, Height = 10, Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { dn } });
            });
            p.TSteps(3); p.TUpdateLighting();
            return p;
        }
        byte A(PlayWindow p, double x, double y) => p.TLightGridAt(x, y)?.a ?? 0;
        t.Test("lights (blocks): a beam lights the blocks in front of it, not behind it nor beside its cone", () =>
        {
            var p = Blocks(Light("beam", "right", radius: 260));
            return A(p, 760, 300) < 40 && A(p, 450, 300) >= 200 && A(p, 760, 460) >= 200 && A(p, 605, 300) < 60;
        });
        t.Test("lights (blocks): a wall stops a beam (behind the wall dark; without blocks stopping it, lit)", () =>
        {
            var walled = Blocks(Light("beam", "right", radius: 220), wall: true);
            var through = Blocks(Light("beam", "right", radius: 220, nums: ("stopByBlocks", 0)), wall: true);
            return A(walled, 740, 300) >= 180 && A(through, 740, 300) < 60;
        });
        t.Test("lights (blocks): an area lights every block of its rectangle; far below stays as dark as the night", () =>
        {
            var p = Blocks(Light("area", radius: 40), w: 200, h: 100);
            return A(p, 508, 258) < 20 && A(p, 692, 342) < 20 && A(p, 600, 480) >= 200;
        });
        t.Test("lights (blocks): a blinking light lights its blocks only while on", () =>
        {
            var p = Blocks(Light(effect: "blink", radius: 200));
            bool sawOff = false, sawOn = false;
            for (int i = 0; i < 80; i++)
            {
                p.TStep(); p.TUpdateLighting();
                if (p.TLightLevel("L") == 0) sawOff |= A(p, 600, 300) >= 200; else sawOn |= A(p, 600, 300) < 20;
            }
            return sawOff && sawOn;
        });

        // ---------------- editor card + AI ----------------
        t.Test("light card: every Light field is in the card's order; beam options only for beam/half; effect speed only with an effect", () =>
        {
            var info = BehaviorCatalog.Info(BehaviorKind.LightSource);
            var keys = info.Nums.Select(n => n.Key).Concat(info.Texts.Select(x => x.Key)).ToList();
            var light = info.CreateData(); var obj = new GameObjectData { Name = "Lamp" }; obj.Behaviors.Add(light);
            var scene = new Scene { Name = "S", Layers = { new Layer { Name = "Base", Objects = { obj } } } };
            bool Shown(string key) => (info.Nums.FirstOrDefault(n => n.Key == key) is NumParam np ? np.ShowIf : info.Texts.First(x => x.Key == key).ShowIf) is not { } f || f(new ParamCtx(light, obj, scene));
            bool circle = !Shown("direction") && !Shown("spread") && !Shown("speed") && Shown("shape") && Shown("softness") && Shown("effect");
            light.Texts["shape"] = "beam"; light.Texts["effect"] = "flicker";
            bool beam = Shown("direction") && Shown("spread") && Shown("speed");
            light.Texts["shape"] = "half";
            bool half = Shown("direction") && !Shown("spread");
            return keys.All(EasyEngine.MainWindow.LightCardOrder.Contains) && EasyEngine.MainWindow.LightCardOrder.All(keys.Contains)
                   && light.Num("softness", -1) == 0.5 && circle && beam && half;
        });
        t.Test("AI fix: a Light written loosely — type flashlight, direction forward, effect candle, angle 60 — becomes shape beam, facing, flicker, spread 60", () =>
        {
            var item = JsonNode.Parse("{\"kind\":\"LightSource\",\"texts\":{\"type\":\"flashlight\",\"direction\":\"forward\",\"effect\":\"candle\"},\"nums\":{\"angle\":60}}")!.AsObject();
            AiSpecFix.Behavior(BehaviorCatalog.Info(BehaviorKind.LightSource), item, new List<string>());
            var texts = item["texts"]!.AsObject(); var nums = item["nums"]!.AsObject();
            return (string?)texts["shape"] == "beam" && (string?)texts["direction"] == "facing" && (string?)texts["effect"] == "flicker" && (double?)nums["spread"] == 60
                   && texts["type"] == null && nums["angle"] == null;
        });

        // ---------------- the "Light Shapes Test" scene, played step by step ----------------
        foreach (var (step, ok, lines) in RunLightShapesE2E())
            t.Test("e2e light shapes: " + step + (ok ? "" : "  ⟶ " + string.Join(" | ", lines.TakeLast(4))), () => ok);
    }
}
