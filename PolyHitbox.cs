using System;
using System.Collections.Generic;

namespace EasyEngine
{
    // A hitbox drawn as a polygon (PhysicsData.Shape, corners as fractions of the box). The geometry the game asks, on
    // world-space corners (xs/ys, n of them): does a box overlap it, and where is its boundary inside a band — the surface
    // under the feet, the ceiling over the head, the wall in front. Allocation-free, never throws; concave shapes work.
    public static class PolyHitbox
    {
        public static bool Valid(List<PolyPoint>? s) => s != null && s.Count >= 3;

        /// <summary>Does the box (x, y, w, h) share some area with the polygon? (grow &lt; 0 shrinks the box: resting
        /// against an edge isn't overlapping; grow &gt; 0 counts a box just beside it — "touching".)</summary>
        public static bool OverlapsBox(double[] xs, double[] ys, int n, double x, double y, double w, double h, double grow = -0.01)
        {
            double x0 = x - grow, y0 = y - grow, x1 = x + w + grow, y1 = y + h + grow;
            if (x1 <= x0 || y1 <= y0) return false;
            for (int i = 0; i < n; i++)
                if (xs[i] > x0 && xs[i] < x1 && ys[i] > y0 && ys[i] < y1) return true;          // a corner inside the box
            if (Inside(xs, ys, n, (x0 + x1) / 2, (y0 + y1) / 2) || Inside(xs, ys, n, x0, y0)) return true; // the box inside it
            for (int i = 0, j = n - 1; i < n; j = i++)
                if (SegmentCrossesBox(xs[j], ys[j], xs[i], ys[i], x0, y0, x1, y1)) return true; // an edge through the box
            return false;
        }

        /// <summary>Do two polygons overlap? (an edge of one crosses the other, or one is inside the other)</summary>
        public static bool OverlapsPoly(double[] ax, double[] ay, int an, double[] bx, double[] by, int bn)
        {
            if (Inside(bx, by, bn, ax[0], ay[0]) || Inside(ax, ay, an, bx[0], by[0])) return true;
            for (int i = 0, j = an - 1; i < an; j = i++)
                for (int k = 0, l = bn - 1; k < bn; l = k++)
                    if (SegmentsCross(ax[j], ay[j], ax[i], ay[i], bx[l], by[l], bx[k], by[k])) return true;
            return false;
        }

        /// <summary>Point in polygon (even-odd rule).</summary>
        public static bool Inside(double[] xs, double[] ys, int n, double px, double py)
        {
            bool c = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if ((ys[i] > py) != (ys[j] > py) && px < (xs[j] - xs[i]) * (py - ys[i]) / (ys[j] - ys[i]) + xs[i]) c = !c;
            return c;
        }

        /// <summary>The highest point of the polygon's outline between x0 and x1 that is at or below <paramref name="fromY"/>
        /// (the ground under the feet); +∞ when there is none.</summary>
        public static double TopIn(double[] xs, double[] ys, int n, double x0, double x1, double fromY) => Scan(xs, ys, n, x0, x1, fromY, true, true);

        /// <summary>The lowest point of the outline between x0 and x1 at or above <paramref name="fromY"/> (the ceiling
        /// over the head); −∞ when there is none.</summary>
        public static double BottomIn(double[] xs, double[] ys, int n, double x0, double x1, double fromY) => Scan(xs, ys, n, x0, x1, fromY, true, false);

        /// <summary>The leftmost point of the outline between y0 and y1 at or right of <paramref name="fromX"/> (the wall in
        /// front when moving right); +∞ when there is none.</summary>
        public static double LeftIn(double[] xs, double[] ys, int n, double y0, double y1, double fromX) => Scan(xs, ys, n, y0, y1, fromX, false, true);

        /// <summary>The rightmost point of the outline between y0 and y1 at or left of <paramref name="fromX"/>; −∞ when none.</summary>
        public static double RightIn(double[] xs, double[] ys, int n, double y0, double y1, double fromX) => Scan(xs, ys, n, y0, y1, fromX, false, false);

        // the outline points inside a band (the corners in it and where edges cross its two sides): vertical = a band of x
        // values, answering a y (otherwise a band of y values, answering an x); the nearest one past "from" in the direction
        // asked (up = the smallest value >= from, otherwise the largest <= from)
        private static double Scan(double[] xs, double[] ys, int n, double a0, double a1, double from, bool vertical, bool up)
        {
            double[] A = vertical ? xs : ys, B = vertical ? ys : xs;
            double best = up ? double.PositiveInfinity : double.NegativeInfinity;
            void Take(double v) { if (up ? (v >= from && v < best) : (v <= from && v > best)) best = v; }
            for (int i = 0; i < n; i++) if (A[i] >= a0 && A[i] <= a1) Take(B[i]);
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double ai = A[i], aj = A[j];
                if (ai == aj) continue;
                double lo = Math.Min(ai, aj), hi = Math.Max(ai, aj);
                if (a0 >= lo && a0 <= hi) Take(B[j] + (B[i] - B[j]) * (a0 - aj) / (ai - aj));
                if (a1 >= lo && a1 <= hi) Take(B[j] + (B[i] - B[j]) * (a1 - aj) / (ai - aj));
            }
            return best;
        }

        // Liang–Barsky: does the segment pass through the open box?
        private static bool SegmentCrossesBox(double ax, double ay, double bx, double by, double x0, double y0, double x1, double y1)
        {
            double t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
            bool Clip(double p, double q)
            {
                if (p == 0) return q > 0;
                double r = q / p;
                if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }
            return Clip(-dx, ax - x0) && Clip(dx, x1 - ax) && Clip(-dy, ay - y0) && Clip(dy, y1 - ay) && t1 - t0 > 1e-9;
        }

        private static bool SegmentsCross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            static double Cross(double ox, double oy, double px, double py, double qx, double qy) => (px - ox) * (qy - oy) - (py - oy) * (qx - ox);
            double d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by), d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>A shape's corners placed on a box (mirrored left-right when <paramref name="flip"/>).</summary>
        public static void Place(List<PolyPoint> shape, double x, double y, double w, double h, bool flip, double[] xs, double[] ys)
        {
            for (int i = 0; i < shape.Count; i++)
            {
                xs[i] = x + (flip ? 1 - shape[i].X : shape[i].X) * w;
                ys[i] = y + shape[i].Y * h;
            }
        }
    }
}
