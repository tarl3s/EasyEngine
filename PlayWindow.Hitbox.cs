using System;
using System.Collections.Generic;
using System.Windows;

namespace EasyEngine
{
    // "Hitbox: fit to the drawing": the object's X/Y/W/H at runtime are the part of its box where the picture has
    // visible pixels, so it collides, overlaps, gets hit and picks things up with the drawing, not its transparent
    // margins. Everything that moves, respawns, spawns or follows it already works on X/Y/W/H; only the picture is
    // drawn at the full box, shifted back by the margins (mirrored when the picture is flipped to face left, so the
    // drawing stays over the hitbox).
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public bool Fit;                          // the hitbox is fitted to the drawing
            public double FitL, FitT, FitR, FitB;     // the transparent margins, as fractions of the picture
        }

        // relative image path → visible bounds (fractions); computed once per image, from the bitmap Load already decoded
        private readonly Dictionary<string, Rect?> _opaqueCache = new(StringComparer.OrdinalIgnoreCase);

        private Rect? OpaqueOf(string relative)
        {
            if (_opaqueCache.TryGetValue(relative, out var r)) return r;
            var bmp = Load(relative);
            r = bmp == null ? null : HitboxFit.Opaque(bmp);
            _opaqueCache[relative] = r;
            return r;
        }

        private void FitHitboxToDrawing(Entity en)
        {
            if (PolyHitbox.Valid(en.Data.Physics.Shape)) return; // a drawn polygon is the hitbox
            var obj = en.Data;
            if (HitboxFit.Fraction(obj, OpaqueOf) is not Rect f) return;
            var box = HitboxFit.Apply(f, obj.X, obj.Y, obj.Width, obj.Height);
            en.Fit = true;
            en.FitL = f.X; en.FitT = f.Y; en.FitR = Math.Max(0, 1 - f.Right); en.FitB = Math.Max(0, 1 - f.Bottom);
            en.X = en.StartX = box.X; en.Y = en.StartY = box.Y; en.W = box.Width; en.H = box.Height;
            LogThrottled("hitbox:" + obj.Name, 10, LogLevel.Info, "Hitbox", obj.Name, "Fitted to the drawing",
                         $"{en.W:0}×{en.H:0} inside its {obj.Width:0}×{obj.Height:0} box (margins L{f.X * obj.Width:0} T{f.Y * obj.Height:0} R{en.FitR * obj.Width:0} B{en.FitB * obj.Height:0})");
        }

        // The picture is mirrored (facing left) for platformer characters and bosses — see Render / RenderBosses.
        private bool PictureFlipped(Entity en) => en.Facing < 0 && (en.Boss != null || en.Has(BehaviorKind.Platformer));

        /// <summary>Where the picture's top-left goes: the box's corner, i.e. the hitbox minus the drawing's margins
        /// (the left and right margins swap when the picture is mirrored).</summary>
        private (double x, double y) PictureAt(Entity en)
        {
            if (!en.Fit) return (en.X, en.Y);
            double pw = en.Visual.Width, ph = en.Visual.Height; // the picture as drawn (a tween of width/height resizes both)
            return (en.X - pw * (PictureFlipped(en) ? en.FitR : en.FitL), en.Y - ph * en.FitT);
        }

        // the picture's size for the current hitbox size (a tween of "width"/"height" resizes the hitbox)
        private static double PictureW(Entity en) => en.Fit ? en.W / Math.Max(0.01, 1 - en.FitL - en.FitR) : en.W;
        private static double PictureH(Entity en) => en.Fit ? en.H / Math.Max(0.01, 1 - en.FitT - en.FitB) : en.H;

        // ---- test hooks (FullTest.Hitbox.cs, E2ETests.Hitbox.cs)
        public Rect THitbox(string name) { var e = TE(name); return e == null ? Rect.Empty : new Rect(e.X, e.Y, e.W, e.H); }
        public Point TPictureAt(string name) { var e = TE(name); if (e == null) return new Point(double.NaN, double.NaN); var (x, y) = PictureAt(e); return new Point(x, y); }
        public bool TFitted(string name) => TE(name)?.Fit ?? false;
    }
}
