using System.Collections.Generic;
using System.Linq;

namespace EasyEngine
{
    // Procedural terrain (BehaviorKind.Terrain): seeded generation, chunks streamed around the camera, cells that
    // collide through SolidsNear (not the _solids list), mining, trees. (M7 — filled in by the terrain work.)
    public partial class PlayWindow
    {
        /// <summary>Terrain cells (as collider entities) overlapping the box — none until a Terrain exists.</summary>
        private IEnumerable<Entity> TerrainSolidsNear(double x, double y, double w, double h) => Enumerable.Empty<Entity>();
    }
}
