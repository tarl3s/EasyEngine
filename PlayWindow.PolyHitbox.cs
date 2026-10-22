using System;

namespace EasyEngine
{
    // A polygon hitbox (PhysicsData.Shape, drawn in the ⬠ Hitbox shape editor). On a solid that stands still it is the
    // real ground and walls: things land on its outline, walk up its slopes and stop at its steep sides (ResolveX/Y ask
    // here). On anything else it is what touches and gets touched (Overlap / Touching) — a moving object still moves as
    // its box. The shape follows the box (it is stored as fractions) and is mirrored with the picture.
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public System.Collections.Generic.List<PolyPoint>? Shape; // null = the box
            public double[] PX = Array.Empty<double>(), PY = Array.Empty<double>(); // its corners in the world, this frame
            public bool ShapeFlips;                                   // mirrored with the picture (platformer characters, bosses)

            /// <summary>Its corners placed on where it is now (false = it has no shape).</summary>
            public bool PlaceShape()
            {
                if (Shape == null) return false;
                PolyHitbox.Place(Shape, X, Y, W, H, ShapeFlips && Facing < 0, PX, PY);
                return true;
            }
        }

        private void SetUpShape(Entity en)
        {
            var s = en.Data.Physics.Shape;
            if (!PolyHitbox.Valid(s)) return;
            en.Shape = s;
            en.PX = new double[s!.Count]; en.PY = new double[s.Count];
            en.ShapeFlips = en.Boss != null || en.Has(BehaviorKind.Platformer);
        }

        // two things overlapping / touching, by their shapes when they have one (the boxes are checked first: cheap)
        private static bool ShapesMeet(Entity a, Entity b, double grow)
        {
            bool pa = a.PlaceShape(), pb = b.PlaceShape();
            if (pa && pb) return PolyHitbox.OverlapsPoly(a.PX, a.PY, a.PX.Length, b.PX, b.PY, b.PX.Length);
            return pa ? PolyHitbox.OverlapsBox(a.PX, a.PY, a.PX.Length, b.X, b.Y, b.W, b.H, grow)
                      : PolyHitbox.OverlapsBox(b.PX, b.PY, b.PX.Length, a.X, a.Y, a.W, a.H, grow);
        }

        // ---- solid polygons (ResolveX / ResolveY / SolidUnder / SolidBeside)

        /// <summary>Moving sideways into a polygon solid: walk up a slope low enough to step on, otherwise stop at its side.</summary>
        private void PolyResolveX(Entity en, Entity s, double dt)
        {
            s.PlaceShape();
            int n = s.PX.Length;
            if (!PolyHitbox.OverlapsBox(s.PX, s.PY, n, en.X, en.Y, en.W, en.H)) return;
            double feet = en.Y + en.H;
            if (!en.FrameTopDown && en.VY >= 0)
            {
                double step = Math.Abs(en.VX) * dt * 1.8 + 3; // ≈ a 60° slope at walking speed
                double top = PolyHitbox.TopIn(s.PX, s.PY, n, en.X, en.X + en.W, feet - step);
                if (top <= feet && !PolyHitbox.OverlapsBox(s.PX, s.PY, n, en.X, top - en.H, en.W, en.H)) { en.Y = top - en.H; return; } // up the slope
            }
            if (en.VX != 0) en.BlockedVX = en.VX;
            double y0 = en.Y, y1 = feet;
            if (en.VX > 0)
            {
                double wall = PolyHitbox.LeftIn(s.PX, s.PY, n, y0, y1, en.X + en.W - en.VX * dt - 1);
                if (!double.IsInfinity(wall)) en.X = Math.Min(en.X, wall - en.W);
            }
            else if (en.VX < 0)
            {
                double wall = PolyHitbox.RightIn(s.PX, s.PY, n, y0, y1, en.X - en.VX * dt + 1);
                if (!double.IsInfinity(wall)) en.X = Math.Max(en.X, wall);
            }
            double b = en.Data.Physics.Bounciness;
            en.VX = (b > 0.01 && Math.Abs(en.VX) > 120) ? -en.VX * b : 0;
        }

        /// <summary>Moving up or down into a polygon solid: land on its outline under the feet, or bump the head on it.</summary>
        private void PolyResolveY(Entity en, Entity s, double dt)
        {
            s.PlaceShape();
            int n = s.PX.Length;
            if (!PolyHitbox.OverlapsBox(s.PX, s.PY, n, en.X, en.Y, en.W, en.H)) return;
            double x0 = en.X, x1 = en.X + en.W, b = en.Data.Physics.Bounciness;
            if (en.VY >= 0)
            {
                double prevFeet = en.Y + en.H - en.VY * dt;
                double top = PolyHitbox.TopIn(s.PX, s.PY, n, x0, x1, prevFeet - (s.OneWay ? 6 : 3));
                if (double.IsInfinity(top) || top > en.Y + en.H) return;
                en.Y = top - en.H;
                if (b > 0.01 && en.VY > 120) en.VY = -en.VY * b;
                else { en.Grounded = true; en.VY = 0; }
            }
            else if (!s.OneWay)
            {
                double prevHead = en.Y - en.VY * dt;
                double ceil = PolyHitbox.BottomIn(s.PX, s.PY, n, x0, x1, prevHead + 3);
                if (double.IsInfinity(ceil) || ceil < en.Y) return;
                en.Y = ceil;
                en.VY = (b > 0.01 && -en.VY > 120) ? -en.VY * b : 0;
            }
        }

        /// <summary>Walking down a slope: it was on the ground last frame and the outline is just under its feet → stay on it
        /// (instead of falling in little hops).</summary>
        private void PolySnapDown(Entity en, double dt)
        {
            double reach = Math.Abs(en.VX) * dt * 1.8 + 4, feet = en.Y + en.H;
            foreach (var s in SolidsNear(en.X, feet, en.W, reach))
            {
                if (s == en || s.Dead || s.SolidDisabled || !s.PlaceShape()) continue;
                double top = PolyHitbox.TopIn(s.PX, s.PY, s.PX.Length, en.X, en.X + en.W, feet - 0.5);
                if (top - feet <= reach) { en.Y = top - en.H; en.Grounded = true; en.VY = 0; return; }
            }
        }

        // ---- test hooks (FullTest.PolyHitbox.cs)
        public bool THasShape(string name) => TE(name)?.Shape != null;
    }
}
