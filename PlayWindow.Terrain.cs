using System;
using System.Collections.Generic;
using System.Linq;

namespace EasyEngine
{
    // Procedural terrain (BehaviorKind.Terrain): seeded generation, chunks streamed around the camera, cells that
    // collide through SolidsNear (not the _solids list), mining, trees. (M7 — filled in by the terrain work.)
    public partial class PlayWindow
    {
        /// <summary>True while an endless terrain is loaded: the world has no left/right edge (camera X unclamped,
        /// the minimap follows the player instead of showing the whole scene).</summary>
        private bool _endlessX;

        /// <summary>World Y of the ground surface at a world X, while a terrain is loaded (null = no terrain there).
        /// Used by the minimap (ground line) and by "spawn near".</summary>
        private Func<double, double?>? _terrainSurfaceAt;

        /// <summary>Terrain cells (as collider entities) overlapping the box — none until a Terrain exists.</summary>
        private IEnumerable<Entity> TerrainSolidsNear(double x, double y, double w, double h) => Enumerable.Empty<Entity>();
    }
}
