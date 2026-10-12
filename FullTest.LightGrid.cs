using System;
using System.Linq;

namespace EasyEngine;

// Day/Night "Lighting style: Blocks" (light per block) and "True black darkness".
public partial class App
{
    partial void FeatureTestsLightGrid(FullTestKit t)
    {
        // At night (light 0.2): a torch at (600,300), radius 200, and a solid wall at x 660..692 between it and (750,300).
        // `lights` torches stack on the same spot, each with `intensity`.
        PlayWindow Night(double style = 1, int lights = 1, double intensity = 1, params (string key, double val)[] nums)
        {
            var p = t.Sim("[]", PhysicsMode.None, (pr, s) =>
            {
                var l = s.Layers.First(z => z.Kind == LayerKind.Base);
                for (int i = 0; i < lights; i++)
                    l.Objects.Add(new GameObjectData { Id = 500 + i, Name = "Torch" + i, Type = "Object", X = 592, Y = 292, Width = 16, Height = 16, Color = "#FFA040",
                        Physics = new PhysicsData { Mode = PhysicsMode.None },
                        Behaviors = { new BehaviorData { Kind = BehaviorKind.LightSource, Nums = { ["radius"] = 200, ["intensity"] = intensity }, Texts = { ["color"] = "#FFA040" } } } });
                l.Objects.Add(new GameObjectData { Id = 520, Name = "Wall", Type = "Object", X = 660, Y = 150, Width = 32, Height = 300, Color = "#777777",
                    Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
                var dn = new BehaviorData { Kind = BehaviorKind.DayNight };
                foreach (var (k, v) in new (string, double)[] { ("dayLength", 100), ("nightLength", 100), ("transition", 0), ("nightLight", 0.2), ("startAtNight", 1),
                                                                ("lightStyle", style) }.Concat(nums))
                    dn.Nums[k] = v;
                l.Objects.Add(new GameObjectData { Id = 530, Name = "DN", Type = "Object", Width = 10, Height = 10, Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { dn } });
            });
            p.TSteps(3); p.TUpdateLighting();
            return p;
        }
        byte A(PlayWindow p, double x, double y) => p.TLightGridAt(x, y)?.a ?? 0;

        t.Test("lightgrid: Smooth (the default) keeps the soft darkness and builds no grid", () =>
        {
            var p = Night(style: 0);
            return !p.TLightGridOn && p.TSmoothDarknessShown && p.TLightGridAt(600, 300) == null;
        });
        t.Test("lightgrid: Blocks hides the soft darkness; the torch's block is fully lit (no tint), far away is as dark as the night", () =>
        {
            var p = Night(nums: ("lightTint", 0));
            return p.TLightGridOn && !p.TSmoothDarknessShown && A(p, 600, 300) == 0 && Math.Abs(A(p, 300, 520) - 204) <= 2; // 1 - 0.2 = 0.8 dark
        });
        t.Test("lightgrid: a solid wall stops the light (behind it is darker than open air at the same distance)", () =>
        {
            var p = Night();
            return A(p, 750, 300) > A(p, 450, 300) + 60 && A(p, 750, 300) >= 200;
        });
        t.Test("lightgrid: with 'Solid blocks stop the light' off, both sides get the same light", () =>
        {
            var p = Night(nums: ("lightBlocked", 0));
            return Math.Abs(A(p, 750, 300) - A(p, 450, 300)) <= 12;
        });
        t.Test("lightgrid: 'Light colours what it touches' tints near an orange torch (red > blue); off = no tint", () =>
        {
            var on = Night(nums: ("trueBlack", 1)).TLightGridAt(600, 300)!.Value;
            var off = Night(nums: new[] { ("trueBlack", 1.0), ("lightTint", 0.0) }).TLightGridAt(600, 300)!.Value;
            return on.r > on.b + 20 && off.r == off.g && off.g == off.b;
        });
        t.Test("lightgrid: 'Every light adds up' — two half-strength torches light more than one; off = the same as one", () =>
        {
            byte one = A(Night(intensity: 0.4), 520, 300), two = A(Night(lights: 2, intensity: 0.4), 520, 300);
            byte twoMax = A(Night(lights: 2, intensity: 0.4, nums: ("lightsAdd", 0)), 520, 300);
            return two < one - 20 && Math.Abs(twoMax - one) <= 2;
        });
        t.Test("lightgrid: 'Light block size' 32 → one value per 32 px block", () =>
        {
            var p = Night(nums: ("lightCell", 32));
            return p.TLightGridAt(420, 300) == p.TLightGridAt(446, 300) && p.TLightGridAt(446, 300) != p.TLightGridAt(450, 300);
        });
        t.Test("true black: the night is pure black (not the night tint); off keeps the tint", () =>
        {
            return Night(style: 0, nums: ("trueBlack", 1)).TDarkColor == "#FF000000" && Night(style: 0).TDarkColor == "#FF0A1030";
        });
    }
}
