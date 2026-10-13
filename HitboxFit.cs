using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    /// <summary>
    /// "Hitbox: fit to the drawing" (PhysicsData.FitHitbox). An object's picture is stretched over its whole box, so the
    /// transparent margins of a sprite would collide too. This finds the rectangle of the picture's visible pixels — as
    /// fractions of the image (0..1), so it maps onto any box size — and the runtime collides with that part of the box
    /// while still drawing the picture over the whole box. Shared by the runtime, the editor (dashed outline, "Trim box
    /// to drawing") and the tests.
    /// </summary>
    public static class HitboxFit
    {
        /// <summary>A pixel counts as part of the drawing when its alpha is above this (0-255): soft anti-aliased fringes don't.</summary>
        public const byte AlphaThreshold = 16;

        /// <summary>The visible pixels' bounds as fractions of the image (0..1), or null when nothing is visible.
        /// Images without transparency (e.g. JPG) give the whole image.</summary>
        public static Rect? Opaque(BitmapSource bmp)
        {
            try
            {
                BitmapSource src = bmp.Format == PixelFormats.Bgra32 || bmp.Format == PixelFormats.Pbgra32
                    ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = src.PixelWidth, h = src.PixelHeight;
                if (w <= 0 || h <= 0) return null;
                var px = new byte[w * h * 4];
                src.CopyPixels(px, w * 4, 0);
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        if (px[row + x * 4 + 3] <= AlphaThreshold) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
                if (maxX < 0) return null;
                return new Rect((double)minX / w, (double)minY / h, (double)(maxX - minX + 1) / w, (double)(maxY - minY + 1) / h);
            }
            catch { return null; }
        }

        // editor-side cache: absolute path → (file time, bounds); decoded once per file version
        private static readonly Dictionary<string, (DateTime stamp, Rect? frac)> _fileCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Visible bounds of an image file (cached per path; re-read only when the file changes).</summary>
        public static Rect? OpaqueFile(string? absPath)
        {
            if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath)) return null;
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(absPath); } catch { return null; }
            lock (_fileCache)
                if (_fileCache.TryGetValue(absPath, out var c) && c.stamp == stamp) return c.frac;
            Rect? frac = null;
            var bmp = LoadBitmap(absPath);
            if (bmp != null) frac = Opaque(bmp);
            lock (_fileCache) _fileCache[absPath] = (stamp, frac);
            return frac;
        }

        /// <summary>Reads an image file fully into memory (the file isn't kept open).</summary>
        public static BitmapImage? LoadBitmap(string absPath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(absPath); bmp.EndInit(); bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>Is the option on and can it work for this object? Tiled (repeating) images and tilemaps keep the whole box.</summary>
        public static bool Applies(GameObjectData o) =>
            o.Physics.FitHitbox && !o.TileImage && (!string.IsNullOrEmpty(o.SpritePath) || o.Animations.Count > 0)
            && !o.Behaviors.Exists(b => b.Kind == BehaviorKind.Tilemap);

        /// <summary>The images the hitbox follows: every frame of the default animation (so the box doesn't jump between
        /// frames), else the static image — the same picture the game shows first.</summary>
        public static IEnumerable<string> FitImages(GameObjectData o)
        {
            var anim = o.ResolveDefaultAnimation();
            if (anim != null && anim.Frames.Count > 0) { foreach (var f in anim.Frames) yield return f; }
            else if (!string.IsNullOrEmpty(o.SpritePath)) yield return o.SpritePath!;
        }

        /// <summary>Every image the object can show (static image + all animations): what "Trim box to drawing" crops.</summary>
        public static IEnumerable<string> AllImages(GameObjectData o)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(o.SpritePath) && seen.Add(o.SpritePath!)) yield return o.SpritePath!;
            foreach (var a in o.Animations)
                foreach (var f in a.Frames)
                    if (!string.IsNullOrEmpty(f) && seen.Add(f)) yield return f;
        }

        /// <summary>Union of the visible bounds of these images (null when none has visible pixels).</summary>
        public static Rect? Union(IEnumerable<string> images, Func<string, Rect?> opaqueOf)
        {
            Rect? u = null;
            foreach (var rel in images)
            {
                var r = opaqueOf(rel);
                if (r == null) continue;
                u = u == null ? r : Rect.Union(u.Value, r.Value);
            }
            return u;
        }

        /// <summary>The fraction of the box the hitbox keeps, or null when the option is off / can't apply / changes nothing.</summary>
        public static Rect? Fraction(GameObjectData o, Func<string, Rect?> opaqueOf)
        {
            if (!Applies(o)) return null;
            var u = Union(FitImages(o), opaqueOf);
            return u == null || IsWhole(u.Value) ? null : u;
        }

        /// <summary>The editor's view: the fraction read from the project's image files.</summary>
        public static Rect? Fraction(Project p, GameObjectData o) => Fraction(o, rel => OpaqueFile(ProjectIO.AbsoluteAsset(p, rel)));

        public static bool IsWhole(Rect f) => f.X < 1e-6 && f.Y < 1e-6 && f.Right > 1 - 1e-6 && f.Bottom > 1 - 1e-6;

        /// <summary>A fraction of the box mapped onto a box (world coordinates); never thinner than 1 px.</summary>
        public static Rect Apply(Rect f, double x, double y, double w, double h) =>
            new(x + f.X * w, y + f.Y * h, Math.Max(1, f.Width * w), Math.Max(1, f.Height * h));

        /// <summary>The image's pixels inside a fraction of it (rounded outward, so nothing visible is cut).</summary>
        public static BitmapSource Crop(BitmapSource bmp, Rect f)
        {
            int pw = bmp.PixelWidth, ph = bmp.PixelHeight;
            int x0 = Math.Clamp((int)Math.Floor(f.X * pw + 1e-6), 0, pw - 1), y0 = Math.Clamp((int)Math.Floor(f.Y * ph + 1e-6), 0, ph - 1);
            int x1 = Math.Clamp((int)Math.Ceiling(f.Right * pw - 1e-6), x0 + 1, pw), y1 = Math.Clamp((int)Math.Ceiling(f.Bottom * ph - 1e-6), y0 + 1, ph);
            var c = new CroppedBitmap(bmp, new Int32Rect(x0, y0, x1 - x0, y1 - y0));
            c.Freeze();
            return c;
        }
    }
}
