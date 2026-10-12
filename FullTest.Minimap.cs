using System;
using System.Linq;

namespace EasyEngine;

// M2 — the minimap: coloured markers (by object name, prefab copies or group), the big map on a key, the top-left corner
// below the play bar, and the window around the player in an endless world.
public partial class App
{
    partial void FeatureTestsMinimap(FullTestKit t)
    {
        const string Purple = "#9B59F0", Green = "#5BC77A", Gray = "#B4B4BE", Cyan = "#00E5FF";

        // a scene with a Player, two Slimes, a Coin, a Bat in the group "Bats", a Rock nobody marked, and the minimap
        Action<Project, Scene> World(string markers, Action<BehaviorData>? tweak = null) => (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            int id = 300;
            void Obj(string name, double x, double y, string group = "") =>
                l.Objects.Add(new GameObjectData { Id = id++, Name = name, Group = group, Type = "Object", X = x, Y = y, Width = 20, Height = 20, Color = "#888888", Physics = new PhysicsData { Mode = PhysicsMode.None } });
            Obj("Player", 1000, 1000); Obj("Slime", 400, 400); Obj("Slime", 600, 900); Obj("Coin", 2000, 500);
            Obj("Bat1", 3000, 300, "Bats"); Obj("Rock", 1500, 1500);
            var bh = BehaviorCatalog.Info(BehaviorKind.Minimap).CreateData();
            bh.Texts["markers"] = markers;
            tweak?.Invoke(bh);
            l.Objects.Add(new GameObjectData { Id = id++, Name = "Map", Type = "Object", X = 0, Y = 0, Width = 40, Height = 40, Color = "#333333", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
        };
        PlayWindow Map(string markers, Action<BehaviorData>? tweak = null, Action<Project, Scene>? more = null)
        {
            var w = World(markers, tweak);
            var p = t.Sim("[]", PhysicsMode.None, (pr, sc) => { w(pr, sc); more?.Invoke(pr, sc); });
            p.TStep(); p.TRenderMinimap();
            return p;
        }

        t.Test("minimap: markers parse as Name:#colour, and old entries without a colour stay (gray)", () =>
        {
            var m = PlayWindow.ParseMapMarkers("Slime:#9B59F0, Coin ; Bats:#5BC77A,");
            return m.Count == 3 && m[0] == ("Slime", "#9B59F0") && m[1] == ("Coin", "") && m[2] == ("Bats", "#5BC77A");
        });
        t.Test("minimap: each marker draws its things in its own colour (name or group), the player on top in cyan", () =>
        {
            var p = Map($"Slime:{Purple},Coin,Bats:{Green}");
            return p.TMinimapDots(Purple) == 2 && p.TMinimapDots(Gray) == 1 && p.TMinimapDots(Green) == 1 && p.TMinimapDots(Cyan) == 1
                   && p.TMinimapDots() == 5; // the Rock (not marked) has no dot
        });
        t.Test("minimap: a group entry shows every object of that group; a name shows that object (case doesn't matter)", () =>
        {
            var p = Map($"bats:{Green},ROCK:{Purple}");
            return p.TMinimapDots(Green) == 1 && p.TMinimapDots(Purple) == 1 && p.TMinimapDots() == 3;
        });
        t.Test("minimap: old projects' \"Slime,Coin\" still work (gray dots)", () =>
        {
            var p = Map("Slime,Coin");
            return p.TMinimapMarkers().All(m => m.color == "") && p.TMinimapDots(Gray) == 3 && p.TMinimapDots(Cyan) == 1;
        });
        t.Test("minimap: the player's colour can be chosen", () =>
        {
            var p = Map("", bh => bh.Texts["playerColor"] = "#FF0000");
            return p.TMinimapDots("#FF0000") == 1 && p.TMinimapDots(Cyan) == 0;
        });
        t.Test("minimap: copies of a prefab spawned while playing are shown (they keep the prefab's name)", () =>
        {
            var p = Map($"Enemy:{Purple}", more: AddEnemyPrefab);
            int before = p.TMinimapDots(Purple);
            var s = t.Sim("[{\"when\":\"start\",\"do\":\"spawn\",\"object\":\"Enemy\"}]", PhysicsMode.None, (pr, sc) => { World($"Enemy:{Purple}")(pr, sc); AddEnemyPrefab(pr, sc); });
            s.TSteps(2); s.TRenderMinimap();
            return before == 0 && s.TAlive("Enemy") >= 1 && s.TMinimapDots(Purple) == s.TAlive("Enemy");
        });
        t.Test("minimap: dots are reused frame after frame (no growing pile of shapes)", () =>
        {
            var p = Map($"Slime:{Purple}");
            for (int i = 0; i < 5; i++) p.TRenderMinimap();
            return p.TMinimapDots() == 3;
        });

        t.Test("minimap: the key opens the big map on the press, holding it does nothing, pressing again closes it", () =>
        {
            var p = Map("Slime");
            bool closed0 = !p.TMinimapMax();
            p.TKey("M", true); p.TStep(); bool open = p.TMinimapMax();
            p.TSteps(5); bool stillOpen = p.TMinimapMax();          // held down: no flicker
            p.TKey("M", false); p.TStep(); bool afterUp = p.TMinimapMax();
            p.TKey("M", true); p.TStep(); bool closed = !p.TMinimapMax();
            p.TKey("M", false); p.TStep();
            return closed0 && open && stillOpen && afterUp && closed && p.TLogHas("Big map opened") && p.TLogHas("Big map closed");
        });
        t.Test("minimap: the big map is centred, ~70% of the screen, titled, and counts as UI while open (clicks don't swing tools)", () =>
        {
            var p = Map("Slime");
            var small = p.TMinimapRect();
            p.TKey("M", true); p.TStep(); p.TKey("M", false); p.TStep(); p.TRenderMinimap();
            var big = p.TMinimapRect(); var ui = p.TUiRect("the map");
            bool openOk = p.TUiHas("the map") && p.TMinimapTitleVisible() && big.Height > 540 * 0.6 && big.Height <= 540 * 0.7
                          && Math.Abs(ui.X + ui.Width / 2 - 480) < 2 && Math.Abs(ui.Y + ui.Height / 2 - 270) < 2 && p.TIsUi(480, 270)
                          && Math.Abs(big.Width / big.Height - small.Width / small.Height) < 0.01;
            p.TKey("M", true); p.TStep(); p.TKey("M", false); p.TStep(); p.TRenderMinimap();
            return openOk && !p.TUiHas("the map") && !p.TMinimapTitleVisible() && Math.Abs(p.TMinimapRect().Width - small.Width) < 0.01 && !p.TIsUi(480, 270);
        });
        t.Test("minimap: 'Can be maximized' off → the key does nothing; another key can be picked", () =>
        {
            var off = Map("", bh => bh.Nums["canMax"] = 0);
            off.TKey("M", true); off.TStep();
            var tab = Map("", bh => bh.Texts["maxKey"] = "Tab");
            tab.TKey("M", true); tab.TStep(); bool mIgnored = !tab.TMinimapMax();
            tab.TKey("Tab", true); tab.TStep();
            return !off.TMinimapMax() && mIgnored && tab.TMinimapMax();
        });
        t.Test("minimap: a project saved before the key option existed opens the big map with M (as its title says)", () =>
        {
            var old = Map("", bh => bh.Texts.Remove("maxKey"));
            old.TKey("M", true); old.TStep();
            return old.TMinimapMax();
        });
        t.Test("minimap: top-left starts below the play bar (it used to hide under Stop / Console)", () =>
        {
            var tl = Map("", bh => bh.Texts["corner"] = "tl");
            var tr = Map("");
            return tl.TMinimapRect().Y >= 50 && tl.TMinimapRect().X <= 12 && tr.TMinimapRect().Y <= 12;
        });
        t.Test("minimap: an endless world shows the area around the player, with the ground drawn", () =>
        {
            var p = Map($"Slime:{Purple}", more: (pr, sc) =>
                sc.Layers.First(z => z.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 390, Name = "Slime", Type = "Object", X = 1500, Y = 1000, Width = 20, Height = 20, Physics = new PhysicsData { Mode = PhysicsMode.None } }));
            p.TFakeEndless(x => 1100 + Math.Sin(x / 300) * 40);
            p.TRenderMinimap();
            // the window is 3000 px wide around the player (x≈1000): the Slime at 1500 is in, the ones at 400/600 are too,
            // but move the player far away and only the player stays on the map
            bool near = p.TMinimapGround() && p.TMinimapDots(Purple) == 3 && p.TMinimapDots(Cyan) == 1;
            p.TPlace("Player", 20000, 1000); p.TRenderMinimap();
            return near && p.TMinimapDots(Purple) == 0 && p.TMinimapDots(Cyan) == 1 && p.TMinimapGround();
        });
        t.Test("minimap: a finite world shows the whole scene and no ground line", () =>
        {
            var p = Map("Coin");
            return !p.TMinimapGround() && p.TMinimapDots(Gray) == 1 && Math.Abs(p.TMinimapRect().Width - 180) < 0.01;
        });

        t.Test("minimap catalog: 'Also show' is the marker editor, with the player colour, 'Can be maximized' and its key", () =>
        {
            var info = BehaviorCatalog.Info(BehaviorKind.Minimap);
            var markers = info.Texts.FirstOrDefault(x => x.Key == "markers");
            var key = info.Texts.FirstOrDefault(x => x.Key == "maxKey");
            var can = info.Nums.FirstOrDefault(x => x.Key == "canMax");
            var col = info.Texts.FirstOrDefault(x => x.Key == "playerColor");
            var bh = info.CreateData();
            bool shownOn = key?.ShowIf?.Invoke(new ParamCtx(bh, new GameObjectData(), new Scene())) == true;
            bh.Nums["canMax"] = 0;
            bool hiddenOff = key?.ShowIf?.Invoke(new ParamCtx(bh, new GameObjectData(), new Scene())) == false;
            return markers?.Pick == PickKind.MapMarkers && key?.Pick == PickKind.Key && key.Default == "M" && can?.Default == 1 && can.Label.EndsWith("(0/1)")
                   && col?.Pick == PickKind.Color && col.Default == "#00E5FF" && shownOn && hiddenOff;
        });
    }
}
