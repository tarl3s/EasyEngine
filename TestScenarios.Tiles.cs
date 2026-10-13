using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ TILES TEST
        // Tilemap tiles with pictures: a brick tile drawn as a picture (solid, breakable — K breaks the one in front and
        // gives a brick), an animated water tile (3 frames, every water cell in sync) and a plain-colour grass tile (old
        // colour-only tiles still work). Walk right, break through the brick wall, Y confirms the look; R restarts.
        public const int TilesTS = 40;

        private static Scene TilesTest(Project p)
        {
            var scene = new Scene { Name = "Tiles Test", Width = 1200, Height = 600, BackgroundColor = "#1B2230", AmbientLight = 1 };
            scene.Variables["confirmLook"] = 0;

            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            baseL.Objects.Add(Id(TilesLevel(p)));

            var hero = new GameObjectData { Name = "Hero", Type = "Player", X = 200, Y = 444, Width = 30, Height = 36, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            hero.Behaviors.Add(B(BehaviorKind.Platformer));
            hero.Behaviors.Add(Script("[{\"when\":\"keypress\",\"key\":\"K\",\"do\":\"breaktile\",\"where\":\"front\"}]"));
            baseL.Objects.Add(Id(hero));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmLook\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Tiles Test\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 640, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "TILES   Bricks are a drawn picture, the water is an animated tile, the grass a plain colour.", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "A / D walk · Space jumps · K breaks the brick in front of you (it gives a brick)", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "Break through the brick wall on the right and walk past it", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help4", 24, 130, "Bricks drawn + water moving (all together)? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Tiles with pictures";
            cl.Texts["items"] = string.Join("\n",
                "Break a pictured brick (K, facing the wall) => item:brick >= 1",
                "Walk through the gap => Hero.x >= 1010",
                "Bricks have their picture and the water moves — press Y => confirmLook >= 1");
            cl.Nums["width"] = 340;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }

        // 30×15 cells of 40 px: brick floor (rows 12-14) with a grass strip on the left, a water pool on the floor in the
        // middle, and a 3-brick wall at column 24.
        private static GameObjectData TilesLevel(Project p)
        {
            const int cols = 30, rows = 15;
            var brick = new TileKind("#9A4B32", true); brick.SetPicture(TileBrickSprite(p));
            var water = new TileKind("#2E6FD8", false); water.SetFrames(TileWaterFrames(p), 4);
            var grass = new TileKind("#5BA862", true);
            var cells = new int[cols * rows];
            for (int r = 12; r < rows; r++) for (int c = 0; c < cols; c++) cells[r * cols + c] = 1;
            for (int c = 0; c < 4; c++) cells[12 * cols + c] = 3;
            for (int c = 12; c < 17; c++) cells[11 * cols + c] = 2;
            for (int r = 9; r < 12; r++) cells[r * cols + 24] = 1;
            for (int r = 0; r < 12; r++) cells[r * cols + cols - 1] = 1; // the end of the level (don't fall off)

            var bh = B(BehaviorKind.Tilemap);
            bh.Texts["palette"] = TilePalette.Serialize(new[] { brick, water, grass });
            bh.Texts["data"] = System.Text.Json.JsonSerializer.Serialize(new { cols, rows, cells });
            bh.Nums["tileSize"] = TilesTS; bh.Nums["breakable"] = 1; bh.Texts["drop"] = "brick";
            return new GameObjectData { Name = "Level", Type = "Object", X = 0, Y = 0, Width = cols * TilesTS, Height = rows * TilesTS, Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } };
        }

        /// <summary>A 40×40 brick tile picture saved in the project (also used by the tests and the selftest).</summary>
        internal static string TileBrickSprite(Project p) => ProjectIO.SaveSprite(p, GameAssets.Draw(TilesTS, TilesTS, (dc, w, h) =>
        {
            dc.DrawRectangle(GameAssets.Br("#D9C3A5"), null, new Rect(0, 0, w, h));                  // mortar
            var b = GameAssets.Br("#9A4B32");
            dc.DrawRectangle(b, null, new Rect(1, 1, w - 2, h / 2 - 2));                               // top row
            dc.DrawRectangle(b, null, new Rect(1, h / 2 + 1, w / 2 - 2, h / 2 - 2));                   // bottom row, two halves
            dc.DrawRectangle(b, null, new Rect(w / 2 + 1, h / 2 + 1, w / 2 - 2, h / 2 - 2));
        }), "tile_brick");

        /// <summary>Three 40×40 water frames (the wave moves right), saved in the project.</summary>
        internal static List<string> TileWaterFrames(Project p)
        {
            var frames = new List<string>();
            for (int f = 0; f < 3; f++)
            {
                int shift = f * TilesTS / 3;
                frames.Add(ProjectIO.SaveSprite(p, GameAssets.Draw(TilesTS, TilesTS, (dc, w, h) =>
                {
                    dc.DrawRectangle(GameAssets.Br("#2E6FD8"), null, new Rect(0, 0, w, h));
                    var crest = GameAssets.Br("#9CD3FF");
                    for (int k = -1; k < 2; k++) dc.DrawEllipse(crest, null, new Point(shift + k * w / 2 + w / 4, 8), w / 6, 3);
                    dc.DrawRectangle(GameAssets.Br("#2458B0"), null, new Rect(0, h * 0.6, w, h * 0.4));
                }), "tile_water" + (f + 1)));
            }
            return frames;
        }
    }
}
