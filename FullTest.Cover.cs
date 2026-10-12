using System;
using System.Linq;

namespace EasyEngine;

// M3 — Day/Night "Darker under solid blocks": the more solid rows above the player, the darker (caves, underground),
// smoothly, with the sun/moon hidden while covered. Off = exactly the old light.
public partial class App
{
    partial void FeatureTestsCover(FullTestKit t)
    {
        // a Player at (1000,1000) under `rows` stacked 32 px blocks, a Day/Night controller (by day, full light) and a Sun
        PlayWindow Cave(int rows, bool coverDark = true, bool oneWay = false, params (string key, double val)[] nums)
        {
            return t.Sim("[]", PhysicsMode.None, (p, s) =>
            {
                var l = s.Layers.First(z => z.Kind == LayerKind.Base);
                l.Objects.Add(new GameObjectData { Id = 400, Name = "Player", Type = "Object", X = 1000, Y = 1000, Width = 32, Height = 48, Color = "#00AAFF", Physics = new PhysicsData { Mode = PhysicsMode.None } });
                l.Objects.Add(new GameObjectData { Id = 401, Name = "Sun", Type = "Object", X = 0, Y = 0, Width = 60, Height = 60, Color = "#FFD24D", Physics = new PhysicsData { Mode = PhysicsMode.None } });
                for (int r = 0; r < rows; r++)
                {
                    var block = new GameObjectData { Id = 410 + r, Name = "Dirt", Type = "Object", X = 960, Y = 1000 - 40 - 32 * (r + 1), Width = 120, Height = 32, Color = "#7D5A37", Physics = new PhysicsData { Mode = PhysicsMode.Static } };
                    if (oneWay) block.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Solid, Nums = { ["oneWay"] = 1 } });
                    l.Objects.Add(block);
                }
                var dn = new BehaviorData { Kind = BehaviorKind.DayNight, Texts = { ["sun"] = "Sun", ["coverWho"] = "Player" } };
                foreach (var (k, v) in new (string, double)[] { ("dayLength", 100), ("nightLength", 100), ("transition", 0), ("dayLight", 1), ("nightLight", 0.25),
                                                                ("coverDark", coverDark ? 1 : 0), ("coverDepth", 6), ("coverMax", 0.9) }.Concat(nums))
                    dn.Nums[k] = v;
                l.Objects.Add(new GameObjectData { Id = 480, Name = "DN", Type = "Object", Width = 10, Height = 10, Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { dn } });
            });
        }
        bool Near(double a, double b, double tol = 0.02) => Math.Abs(a - b) <= tol;

        t.Test("cover: open sky → no change (full daylight, cover 0, not underground)", () =>
        {
            var p = Cave(0); p.TSteps(3);
            return Near(p.TDarkness, 0) && Near(p.TVar("cover"), 0) && p.TVar("underground") == 0;
        });
        t.Test("cover: 3 rows above (of 6 until darkest) → half covered, darker", () =>
        {
            var p = Cave(3); p.TSteps(3);
            return Near(p.TVar("cover"), 0.5) && Near(p.TDarkness, 0.45) && p.TVar("underground") == 0;
        });
        t.Test("cover: 4 rows → underground = 1", () => { var p = Cave(4); p.TSteps(3); return p.TVar("underground") == 1 && p.TVar("cover") > 0.6; });
        t.Test("cover: 8 rows (deeper than 6) → saturates at 'how dark underground'", () =>
        {
            var p = Cave(8); p.TSteps(3);
            return Near(p.TVar("cover"), 1) && Near(p.TDarkness, 0.9);
        });
        t.Test("cover: 'how dark underground' sets the floor of the light", () =>
        {
            var p = Cave(8, nums: ("coverMax", 0.5)); p.TSteps(3);
            return Near(p.TDarkness, 0.5);
        });
        t.Test("cover: at night underground is darker still (multiplies the night light)", () =>
        {
            var p = Cave(8, nums: ("startAtNight", 1)); p.TSteps(3);
            return Near(p.TDarkness, 1 - 0.25 * 0.1);
        });
        t.Test("cover: off → exactly the old light, even under 8 rows (and no cover variable)", () =>
        {
            var off = Cave(8, coverDark: false); off.TSteps(3);
            var open = Cave(0, coverDark: false); open.TSteps(3);
            return off.TDarkness == open.TDarkness && Near(off.TDarkness, 0) && off.TVar("cover") == 0 && off.TVisible("Sun");
        });
        t.Test("cover: jump-through (one-way) platforms don't count as a roof", () =>
        {
            var p = Cave(8, oneWay: true); p.TSteps(3);
            return Near(p.TVar("cover"), 0) && Near(p.TDarkness, 0);
        });
        t.Test("cover: Top-down mode ignores it (no sky to cover)", () =>
        {
            var p = Cave(8, nums: ("mode", 1)); p.TSteps(3);
            return Near(p.TDarkness, 0);
        });
        t.Test("cover: the sun (and its light hole) is hidden underground, shown under the open sky", () =>
        {
            var cave = Cave(8); cave.TSteps(3); cave.TUpdateLighting();
            var open = Cave(0); open.TSteps(3); open.TUpdateLighting();
            return !cave.TVisible("Sun") && cave.TLightHoles == 0 && open.TVisible("Sun") && open.TLightHoles == 1;
        });
        t.Test("cover: walking out from under the roof brings the light back smoothly", () =>
        {
            var p = Cave(8); p.TSteps(3);
            double dark = p.TDarkness;
            p.TPlace("Player", 2000, 1000);
            p.TSteps(12);                      // ~0.2 s: sampled, easing (not a jump)
            double mid = p.TDarkness;
            p.TSteps(150);                     // ~2.4 s later: back in daylight
            return Near(dark, 0.9) && mid < dark - 0.05 && mid > 0.3 && Near(p.TDarkness, 0, 0.03) && p.TVisible("Sun") && p.TVar("underground") == 0;
        });
        t.Test("cover catalog: Platformer-only fields, depth/darkness/whose shown only when it's on", () =>
        {
            var info = BehaviorCatalog.Info(BehaviorKind.DayNight);
            var bh = info.CreateData();
            bool Shown(string key)
            {
                var ctx = new ParamCtx(bh, new GameObjectData(), new Scene());
                var n = info.Nums.FirstOrDefault(x => x.Key == key); var tx = info.Texts.FirstOrDefault(x => x.Key == key);
                return n != null ? n.ShowIf?.Invoke(ctx) ?? true : tx != null && (tx.ShowIf?.Invoke(ctx) ?? true);
            }
            bool offOk = Shown("coverDark") && !Shown("coverDepth") && !Shown("coverMax") && !Shown("coverWho");
            bh.Nums["coverDark"] = 1;
            bool onOk = Shown("coverDepth") && Shown("coverMax") && Shown("coverWho");
            bh.Nums["mode"] = 1;
            bool tdOk = !Shown("coverDark") && !Shown("coverDepth") && !Shown("coverWho");
            var who = info.Texts.First(x => x.Key == "coverWho");
            return offOk && onOk && tdOk && info.CreateData().Nums["coverDark"] == 0 && info.CreateData().Nums["coverDepth"] == 6
                   && Math.Abs(info.CreateData().Nums["coverMax"] - 0.9) < 1e-9 && who.Pick == PickKind.Object && who.Default == "Player"
                   && info.Nums.First(x => x.Key == "coverMax").Label.EndsWith("(0-1)") && info.Nums.First(x => x.Key == "coverDark").Label.EndsWith("(0/1)");
        });
    }
}
