using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // Z (order inside a layer): LoadScene creates each layer's objects in Z order (ZOrder.InDrawOrder), so the World /
    // Hud / Sky children follow it and layers keep their own order. Copies spawned during play still go on top of the
    // scene (as they always did) and keep their template's Z: among the spawned copies on top, a higher Z is in front.
    public partial class PlayWindow
    {
        private bool _zLoading;                                         // LoadScene is creating the scene's own objects
        private readonly Dictionary<UIElement, int> _spawnedZ = new();  // visual of a copy spawned during play → its Z

        partial void ZEntityAdded(Entity en)
        {
            if (_zLoading) return;
            if (VisualTreeHelper.GetParent(en.Visual) is not Panel panel) return;
            int z = en.Data.Z, at = panel.Children.Count - 1;
            if (at < 0 || panel.Children[at] != en.Visual) { _spawnedZ[en.Visual] = z; return; }
            // walk back over the spawned copies on top that have a higher Z, and slip in under them
            int ins = at;
            for (int i = at - 1; i >= 0; i--)
            {
                if (panel.Children[i] is UIElement c && _spawnedZ.TryGetValue(c, out var cz) && cz > z) ins = i;
                else break;
            }
            if (ins != at) { panel.Children.RemoveAt(at); panel.Children.Insert(ins, en.Visual); }
            _spawnedZ[en.Visual] = z;
        }

        partial void ZEntityRemoved(Entity en) => _spawnedZ.Remove(en.Visual);

        partial void ZSceneReset() => _spawnedZ.Clear();

        // ---- test hooks ----
        internal int TZOf(string name) { var e = TE(name); return e == null ? int.MinValue : e.Data.Z; }
        /// <summary>Index of each live entity's visual (by object id) in its parent panel — the drawing order.</summary>
        internal int TVisualIndexById(int id)
        {
            foreach (var e in _entities)
                if (!e.Dead && e.Data.Id == id && VisualTreeHelper.GetParent(e.Visual) is Panel p) return p.Children.IndexOf(e.Visual);
            return -1;
        }
        internal List<int> TSpawnedIndexes(string name)
        {
            var list = new List<int>();
            foreach (var e in _entities)
                if (!e.Dead && e.Data.Name == name && VisualTreeHelper.GetParent(e.Visual) is Panel p) list.Add(p.Children.IndexOf(e.Visual));
            return list;
        }
        internal void TSpawn(string name, double x, double y) => SpawnAt(name, x, y);
    }
}
