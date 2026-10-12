using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EasyEngine
{
    // Test hooks for the minimap (FullTest.Minimap.cs / App.SelfTest.Minimap.cs) and the underground darkness (FullTest.Cover.cs).
    public partial class PlayWindow
    {
        private Entity? TMini(string name) => _entities.FirstOrDefault(e => e.MiniCanvas != null && (name == "" || e.Data.Name == name));

        /// <summary>Is the big map open (the first minimap when no name is given)?</summary>
        public bool TMinimapMax(string name = "") => TMini(name)?.MiniMax ?? false;
        public List<(string name, string color)> TMinimapMarkers(string name = "") => ParseMapMarkers(TMini(name)?.Beh(BehaviorKind.Minimap)?.Text("markers", "") ?? "");

        /// <summary>Draws every minimap once (what Render does each live frame) — works headless.</summary>
        public void TRenderMinimap() { foreach (var e in _entities.Where(e => !e.Dead && e.MiniCanvas != null)) UpdateMinimap(e, e.Beh(BehaviorKind.Minimap)!); }

        /// <summary>Visible dots on the minimap with this colour ("#RRGGBB"; null = all of them).</summary>
        public int TMinimapDots(string? hex = null, string name = "")
        {
            var en = TMini(name);
            if (en?.MiniDots == null) return 0;
            Color? want = hex == null ? null : ParseColor(hex, Colors.Transparent);
            return en.MiniDots.Count(d => d.Visibility == Visibility.Visible && (want == null || (d.Fill as SolidColorBrush)?.Color == want));
        }

        /// <summary>Where the minimap frame sits on screen, and the size of the map inside it.</summary>
        public Rect TMinimapRect(string name = "")
        {
            var en = TMini(name);
            if (en?.MiniBox == null) return Rect.Empty;
            return new Rect(System.Windows.Controls.Canvas.GetLeft(en.MiniBox), System.Windows.Controls.Canvas.GetTop(en.MiniBox), en.MiniCanvas!.Width, en.MiniCanvas.Height);
        }
        public Size TMinimapShownSize(string name = "") => TMini(name)?.MiniBox is { } b ? new Size(b.ActualWidth, b.ActualHeight) : Size.Empty;
        public bool TMinimapTitleVisible(string name = "") => TMini(name)?.MiniTitle?.Visibility == Visibility.Visible;
        public bool TMinimapGround(string name = "") => TMini(name)?.MiniGround is { Visibility: Visibility.Visible } g && g.Points.Count > 60;
        public bool TUiHas(string key) => _uiRects.ContainsKey(key);
        public Rect TUiRect(string key) => _uiRects.TryGetValue(key, out var r) ? r : Rect.Empty;

        /// <summary>Pretend an endless terrain is loaded (the terrain work sets these for real).</summary>
        public void TFakeEndless(System.Func<double, double?> surfaceAt) { _endlessX = true; _terrainSurfaceAt = surfaceAt; }
    }
}
