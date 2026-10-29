using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>The editor's selection outline, drawn over a scene object instead of as its border: a border would take
    /// room inside the object and shrink its picture a little while selected.</summary>
    public sealed class SelectionAdorner : Adorner
    {
        private readonly Pen _pen;

        public SelectionAdorner(UIElement el, Brush brush) : base(el)
        {
            _pen = new Pen(brush, 2.5);
            _pen.Freeze();
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var s = AdornedElement.RenderSize;
            dc.DrawRectangle(null, _pen, new Rect(-1.25, -1.25, s.Width + 2.5, s.Height + 2.5)); // just outside the object
        }

        /// <summary>Gives <paramref name="el"/> the outline (on) or takes it away (off).</summary>
        public static void Set(UIElement el, bool on, Brush brush)
        {
            if (AdornerLayer.GetAdornerLayer(el) is not AdornerLayer layer) return;
            var have = layer.GetAdorners(el);
            bool has = have != null && System.Array.Exists(have, a => a is SelectionAdorner);
            if (on && !has) layer.Add(new SelectionAdorner(el, brush));
            else if (!on && has) foreach (var a in have!) if (a is SelectionAdorner) layer.Remove(a);
        }

        public static bool Has(UIElement el) =>
            AdornerLayer.GetAdornerLayer(el)?.GetAdorners(el) is Adorner[] a && System.Array.Exists(a, x => x is SelectionAdorner);
    }
}
