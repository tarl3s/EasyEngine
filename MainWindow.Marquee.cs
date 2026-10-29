using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyEngine
{
    // Drag on an empty part of the scene: a selection box (like the Windows desktop) picks every object it touches.
    // Ctrl keeps what was already selected. Tilemaps and objects that can't be picked (a locked layer) are left out.
    public partial class MainWindow
    {
        private Point? _marqueeStart;
        private Rectangle? _marquee;
        private bool _marqueeAdd;

        private void StartMarquee(MouseButtonEventArgs e)
        {
            _marqueeAdd = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!_marqueeAdd) Select(null);
            _marqueeStart = e.GetPosition(SceneCanvas);
            SceneCanvas.CaptureMouse();
        }

        // true while a box is being dragged
        private bool MarqueeMove(Point p)
        {
            if (_marqueeStart is not Point s) return false;
            if (_marquee == null)
            {
                if (Math.Abs(p.X - s.X) < 4 && Math.Abs(p.Y - s.Y) < 4) return true; // a click, not a drag (yet)
                var accent = ((SolidColorBrush)FindResource("Accent")).Color;
                _marquee = new Rectangle { Stroke = new SolidColorBrush(accent), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
                                           Fill = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)), IsHitTestVisible = false };
                Panel.SetZIndex(_marquee, int.MaxValue);
                SceneCanvas.Children.Add(_marquee);
            }
            var r = new Rect(s, p);
            Canvas.SetLeft(_marquee, r.X); Canvas.SetTop(_marquee, r.Y); _marquee.Width = r.Width; _marquee.Height = r.Height;
            return true;
        }

        // true when it ended a box (or a plain click on empty space)
        private bool EndMarquee(Point p)
        {
            if (_marqueeStart is not Point s) return false;
            _marqueeStart = null;
            SceneCanvas.ReleaseMouseCapture();
            if (_marquee == null) return true;
            SceneCanvas.Children.Remove(_marquee); _marquee = null;
            SelectInBox(new Rect(s, p), _marqueeAdd);
            return true;
        }

        /// <summary>Selects every pickable object touching <paramref name="box"/> (scene coordinates); returns how many.</summary>
        internal int SelectInBox(Rect box, bool add = false)
        {
            var hits = _visuals.Where(kv => kv.Value.IsHitTestVisible && kv.Value.Visibility == Visibility.Visible
                                            && !kv.Key.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap)
                                            && box.IntersectsWith(new Rect(kv.Key.X, kv.Key.Y, Math.Max(1, kv.Key.Width), Math.Max(1, kv.Key.Height))))
                               .Select(kv => kv.Key).ToList();
            if (!add) _selection.Clear();
            foreach (var o in hits) if (!_selection.Contains(o)) _selection.Add(o);
            SetPrimary(_selection.LastOrDefault());
            if (hits.Count > 0) Status(L.F("{0} objects selected", _selection.Count));
            return hits.Count;
        }
        internal int TestSelectedCount => _selection.Count;
        internal System.Collections.Generic.IReadOnlyList<GameObjectData> SelectedObjects => _selection;
    }
}
