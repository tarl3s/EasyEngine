using System;

namespace EasyEngine
{
    /// <summary>
    /// The math behind a Sound heard "only nearby" and placed "left / right in the headphones" (PlayWindow.Sound.cs).
    /// Pure functions, so the tests check them without any audio: <see cref="Falloff"/> is how loud it is at a distance,
    /// <see cref="Balance"/> which ear it is in.
    /// </summary>
    public static class SpatialSound
    {
        /// <summary>Up to this share of the radius the sound is at full volume.</summary>
        public const double FullShare = 0.25;

        /// <summary>How loud (0..1) a sound is at <paramref name="distance"/> px when it is heard up to <paramref name="radius"/> px:
        /// full until a quarter of the radius, then a smooth curve (smoothstep) down to 0 at the edge; silent beyond it.</summary>
        public static double Falloff(double distance, double radius)
        {
            if (!(radius > 0) || double.IsNaN(distance)) return 1;
            double full = radius * FullShare;
            if (distance <= full) return 1;
            if (distance >= radius) return 0;
            double t = (distance - full) / (radius - full); // 0 at the end of the full zone .. 1 at the edge
            return 1 - t * t * (3 - 2 * t);
        }

        /// <summary>Which ear (−1 left … 0 both … +1 right): the sound's horizontal offset from the listener
        /// (<paramref name="dx"/> = sound x − listener x) over <paramref name="scale"/> px, clamped.</summary>
        public static double Balance(double dx, double scale)
        {
            if (!(scale > 0) || double.IsNaN(dx)) return 0;
            return Math.Clamp(dx / scale, -1, 1);
        }

        /// <summary>The loudness factor and the balance of a sound <paramref name="dx"/>,<paramref name="dy"/> px from its listener.
        /// <paramref name="radius"/> ≤ 0 = heard everywhere (factor 1); the balance scales by the radius when there is one,
        /// else by <paramref name="halfScreen"/>; <paramref name="pan"/> off = always centred.</summary>
        public static (double volume, double balance) Mix(double dx, double dy, double radius, bool pan, double halfScreen)
        {
            double vol = radius > 0 ? Falloff(Math.Sqrt(dx * dx + dy * dy), radius) : 1;
            double bal = pan ? Balance(dx, radius > 0 ? radius : halfScreen) : 0;
            return (vol, bal);
        }
    }
}
