namespace EasyEngine
{
    // "Stay Inside": after the physics step, an object with this behavior is kept inside the scene (a Terrain world's
    // edges count; an endless one has no left/right edge) or inside what the camera shows. At an edge it stops, bounces
    // back, or comes out the other side. Sides: all, left/right only, top/bottom only, or all but the bottom (pits).
    public partial class PlayWindow
    {
        partial void StayInsideLogic()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var en = _entities[i];
                if (en.Dead) continue;
                var bh = en.Beh(BehaviorKind.StayInside);
                if (bh == null) continue;

                double x0, y0, x1, y1; bool horizontal = true;
                if (bh.Text("area", "scene") == "screen")
                {
                    x0 = -_camX; y0 = -_camY;
                    x1 = x0 + (Root.Width > 0 ? Root.Width : _scene.Width); y1 = y0 + (Root.Height > 0 ? Root.Height : _scene.Height);
                }
                else
                {
                    double wx0 = 0, ww = _scene.Width, wh = _scene.Height;
                    CameraClampOverride(ref horizontal, ref wx0, ref ww, ref wh); // a Terrain world is bigger; an endless one frees X
                    x0 = wx0; y0 = 0; x1 = ww; y1 = wh;
                }

                string sides = bh.Text("sides", "all"), edge = bh.Text("edge", "stop");
                bool lr = horizontal && sides is "all" or "leftright" or "notbottom";
                bool top = sides is "all" or "topbottom" or "notbottom";
                bool bottom = sides is "all" or "topbottom";

                if (lr) KeepIn(en, ref en.X, ref en.VX, en.W, x0, x1, edge, true, true);
                if (top || bottom) KeepIn(en, ref en.Y, ref en.VY, en.H, y0, y1, edge, top, bottom);
            }
        }

        // One axis: pos/size against [lo, hi]; checkLo / checkHi say which of the two edges count.
        private static void KeepIn(Entity en, ref double pos, ref double vel, double size, double lo, double hi, string edge, bool checkLo, bool checkHi)
        {
            if (edge == "wrap")
            {
                if (checkHi && pos > hi) pos = lo - size;          // fully past one side → just outside the other, sliding in
                else if (checkLo && pos + size < lo) pos = hi;
                return;
            }
            bool bounce = edge == "bounce";
            if (checkLo && pos < lo) { pos = lo; if (vel < 0) vel = bounce ? -vel : 0; }
            else if (checkHi && pos + size > hi) { pos = hi - size; if (vel > 0) vel = bounce ? -vel : 0; }
        }
    }
}
