using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyEngine
{
    // PHYSICS → "Hitbox: fit to the drawing" (collide with the picture's visible pixels, HitboxFit.cs) and
    // "✂ Trim box to drawing" (shrink the object's own box to the drawing once, cropping its images to match).
    // In the scene, an object with the option on shows its hitbox as a dashed outline over the picture.
    public partial class MainWindow
    {
        private void HitboxRows(GameObjectData obj)
        {
            bool hasImage = obj.SpritePath != null || obj.Animations.Count > 0;
            bool tilemap = obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap);
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            var chk = new CheckBox
            {
                Content = L.T("Hitbox: fit to the drawing"), IsChecked = obj.Physics.FitHitbox, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)FindResource("Text"), Margin = new Thickness(0, 0, 10, 6),
                ToolTip = L.T("Collide with the visible pixels of the picture, not its transparent margins. The picture stays the same size.")
            };
            chk.Checked += (s, e) => { PushUndo(); obj.Physics.FitHitbox = true; RenderScene(); Dirty(); BuildInspector(obj); };
            chk.Unchecked += (s, e) => { PushUndo(); obj.Physics.FitHitbox = false; RenderScene(); Dirty(); BuildInspector(obj); };
            row.Children.Add(chk);
            if (hasImage && !obj.TileImage && !tilemap)
            {
                var trim = new Button
                {
                    Content = L.T("✂ Trim box to drawing"), Height = 28, FontSize = 11, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 6),
                    Style = (Style)FindResource("CardButton"),
                    ToolTip = L.T("Shrinks the object's own box to its drawing once (the images are cropped to match). Undo with Ctrl+Z.")
                };
                trim.Click += (s, e) => Status(TrimBoxToDrawing(obj));
                row.Children.Add(trim);
            }
            InspectorRoot.Children.Add(row);

            // ⬠ the hitbox as a polygon (HitboxShapeWindow): ramps, rocks, round things
            bool shaped = PolyHitbox.Valid(obj.Physics.Shape);
            var shapeRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            var edit = new Button
            {
                Content = shaped ? L.F("⬠ Hitbox shape ({0} corners)…", obj.Physics.Shape!.Count) : L.T("⬠ Draw a hitbox shape…"),
                Height = 28, FontSize = 11, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6), Style = (Style)FindResource("CardButton"),
                ToolTip = L.T("Draw the hitbox as a shape with corners. On a solid that stands still it is the real ground and walls (slopes can be walked up); on anything else it is what touches.")
            };
            edit.Click += (s, e) => EditHitboxShape(obj);
            shapeRow.Children.Add(edit);
            if (shaped)
            {
                var off = new Button { Content = L.T("✕ Use the box"), Height = 28, FontSize = 11, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 6), Style = (Style)FindResource("GhostButton"), ToolTip = L.T("Remove the shape: the whole box collides again") };
                off.Click += (s, e) => SetHitboxShape(obj, null);
                shapeRow.Children.Add(off);
            }
            InspectorRoot.Children.Add(shapeRow);

            string? hint = null;
            if (shaped)
                hint = obj.Physics.FitHitbox ? L.T("The shape you drew is the hitbox (it replaces \"fit to the drawing\").")
                     : obj.Physics.Mode == PhysicsMode.Static || obj.Behaviors.Any(b => b.Kind == BehaviorKind.Solid) ? L.T("The shape is the ground and the walls: things land on it and walk up its slopes.")
                     : L.T("The shape is what touches and gets touched; it still moves as its box.");
            else if (obj.Physics.FitHitbox)
            {
                if (!hasImage) hint = L.T("Give it an image first: the hitbox follows the picture's visible pixels.");
                else if (obj.TileImage) hint = L.T("A repeating (tiled) image isn't trimmed: the whole box collides.");
                else if (tilemap) hint = L.T("A tilemap collides with its own tiles.");
                else hint = HitboxFit.Fraction(_project, obj) == null
                    ? L.T("The drawing already fills the box.")
                    : L.T("The dashed outline in the scene is what collides.");
            }
            if (hint != null)
                InspectorRoot.Children.Add(new TextBlock { Text = hint, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        }

        private void EditHitboxShape(GameObjectData obj)
        {
            if (HitboxShapeWindow.Edit(this, _project, obj, out var shape)) SetHitboxShape(obj, shape);
        }

        private void SetHitboxShape(GameObjectData obj, List<PolyPoint>? shape)
        {
            PushUndo();
            obj.Physics.Shape = PolyHitbox.Valid(shape) ? shape : null;
            RenderScene(); BuildInspector(obj); Dirty();
            Status(obj.Physics.Shape == null ? L.T("The whole box is the hitbox again.") : L.F("Hitbox shape: {0} corners.", obj.Physics.Shape.Count));
        }

        /// <summary>A polygon hitbox drawn over the object in the scene (dashed accent), redrawn when the box is resized.</summary>
        private void AddShapeOutline(GameObjectData obj, Border border)
        {
            if (border.Child != null || !PolyHitbox.Valid(obj.Physics.Shape)) return;
            var shape = obj.Physics.Shape!;
            var poly = new Polygon { Stroke = (SolidColorBrush)FindResource("Accent"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
            var c = new Canvas { IsHitTestVisible = false, Tag = "shape" };
            c.Children.Add(poly);
            void Fit() => poly.Points = new PointCollection(shape.Select(p => new Point(p.X * c.ActualWidth, p.Y * c.ActualHeight)));
            c.SizeChanged += (s, e) => Fit();
            border.Child = c;
        }

        /// <summary>The hitbox drawn over the picture in the scene: a dashed accent outline placed by star-sized rows and
        /// columns, so it follows the box while it is resized.</summary>
        private void AddHitboxOutline(GameObjectData obj, Border border)
        {
            if (PolyHitbox.Valid(obj.Physics.Shape)) return; // the drawn shape is the hitbox (AddShapeOutline)
            if (border.Child != null || HitboxFit.Fraction(_project, obj) is not Rect f) return;
            double r = Math.Max(0, 1 - f.Right), b = Math.Max(0, 1 - f.Bottom);
            var g = new Grid { IsHitTestVisible = false, Tag = "hitbox" };
            foreach (var w in new[] { f.X, f.Width, r }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
            foreach (var h in new[] { f.Y, f.Height, b }) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(h, GridUnitType.Star) });
            var rect = new Rectangle { Stroke = (SolidColorBrush)FindResource("Accent"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, SnapsToDevicePixels = true };
            Grid.SetColumn(rect, 1); Grid.SetRow(rect, 1);
            g.Children.Add(rect);
            border.Child = g;
        }

        /// <summary>Shrinks the object's box to the visible pixels of all its images (static image + every animation) and
        /// crops those images to match, so the picture stays exactly where it was. One undo step. Returns a status line.</summary>
        internal string TrimBoxToDrawing(GameObjectData obj)
        {
            if (obj.TileImage) return L.T("A repeating (tiled) image can't be trimmed.");
            var images = HitboxFit.AllImages(obj).ToList();
            if (images.Count == 0) return L.T("This object has no image to trim to.");
            var u = HitboxFit.Union(images, rel => HitboxFit.OpaqueFile(ProjectIO.AbsoluteAsset(_project, rel)));
            if (u == null) return L.T("The image is fully transparent.");
            if (HitboxFit.IsWhole(u.Value)) return L.T("The box already fits the drawing.");

            PushUndo();
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in images)
            {
                var abs = ProjectIO.AbsoluteAsset(_project, rel);
                var bmp = abs == null ? null : HitboxFit.LoadBitmap(abs);
                if (bmp == null) continue;
                try { renamed[rel] = ProjectIO.SaveSprite(_project, HitboxFit.Crop(bmp, u.Value), System.IO.Path.GetFileNameWithoutExtension(rel) + "_trim"); }
                catch { }
            }
            string Map(string rel) => renamed.TryGetValue(rel, out var n) ? n : rel;
            if (obj.SpritePath != null) obj.SpritePath = Map(obj.SpritePath);
            foreach (var a in obj.Animations)
                for (int i = 0; i < a.Frames.Count; i++) a.Frames[i] = Map(a.Frames[i]);

            var box = HitboxFit.Apply(u.Value, obj.X, obj.Y, obj.Width, obj.Height);
            double oldW = obj.Width, oldH = obj.Height;
            obj.X = Math.Round(box.X, 2); obj.Y = Math.Round(box.Y, 2);
            obj.Width = Math.Max(2, Math.Round(box.Width, 2)); obj.Height = Math.Max(2, Math.Round(box.Height, 2));
            RenderScene(); BuildInspector(obj); Dirty();
            return L.F("Box trimmed to the drawing: {0}×{1} → {2}×{3}.", Math.Round(oldW), Math.Round(oldH), Math.Round(obj.Width), Math.Round(obj.Height));
        }

        // ---- test hooks (FullTest.Hitbox.cs)
        internal string TestTrim(GameObjectData o) => TrimBoxToDrawing(o);
        internal string TestBuildPrompt(string request = "") => BuildProjectPrompt(request);
        internal bool TestHasHitboxOutline(GameObjectData o) => _visuals.TryGetValue(o, out var el) && el is Border b && b.Child is Grid g && (g.Tag as string) == "hitbox";
        internal IEnumerable<FrameworkElement> TestInspectorItems() => InspectorRoot.Children.OfType<FrameworkElement>();
        internal void TestSetShape(GameObjectData o, List<PolyPoint>? s) => SetHitboxShape(o, s);
        internal bool TestHasShapeOutline(GameObjectData o) => _visuals.TryGetValue(o, out var el) && el is Border b && b.Child is Canvas c && (c.Tag as string) == "shape";
    }
}
