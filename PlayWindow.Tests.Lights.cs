using System.Linq;

namespace EasyEngine
{
    // Test hooks for light shapes and effects (PlayWindow.LightShapes.cs).
    public partial class PlayWindow
    {
        /// <summary>How many lights cut their hole with a shaped picture (beam, area… or an effect) instead of the round one.</summary>
        public int TShapedLights => _lightMasks.Count(m => m.mask == null);
        /// <summary>How much the shaped lights light a world point (0 = dark, 1 = fully lit), read from their mask pictures.</summary>
        public double TShapedLightAt(double wx, double wy) => ShapedLightAt(wx, wy);
        /// <summary>The darkness really drawn at a screen point (0-1): the darkness and its chain of light holes, rendered off-screen.</summary>
        public double TRenderedDarknessAt(double sx, double sy) => RenderedDarknessAt(sx, sy);
        public double TLightAim(string name) => TE(name)?.Look?.Angle ?? double.NaN;
        public double TLightScale(string name) => TE(name)?.Look?.Scale ?? double.NaN;
        public double TLightLevel(string name) => TE(name)?.Look?.Level ?? double.NaN;
        public string TLightShape(string name) => TE(name)?.Look?.Shape ?? "";
        public bool TLightPlain(string name) => TE(name)?.Look?.Plain ?? false;
        public string TLightVisualType(string name) => TE(name)?.LightVisual?.GetType().Name ?? "";
    }
}
