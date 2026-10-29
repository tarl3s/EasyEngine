using System.Linq;
using System.Windows.Controls;

namespace EasyEngine
{
    // Moving objects to another layer: the inspector's "Layer" picker and the right-click menu's "Move to layer".
    // Every selected object goes (locked ones and locked layers stay put); one undo step.
    public partial class MainWindow
    {
        /// <summary>Moves the selected objects to <paramref name="target"/>; returns how many moved.</summary>
        internal int MoveSelectionToLayer(Layer target)
        {
            if (target.Locked) { Status(L.F("Layer \"{0}\" is locked — unlock it to add objects", target.Name)); return 0; }
            var moving = _selection.Where(o => !IsLockedObj(o) && SceneTools.LayerOf(_scene, o) is Layer from && from != target).ToList();
            if (moving.Count == 0) return 0;
            PushUndo();
            foreach (var o in moving) { SceneTools.LayerOf(_scene, o)!.Objects.Remove(o); target.Objects.Add(o); }
            var keep = _selection.ToList();
            RenderScene(); RefreshObjects();
            _selection.Clear(); _selection.AddRange(keep); SetPrimary(_selected);
            Dirty();
            Status(L.F("Moved {0} object(s) to \"{1}\"", moving.Count, target.Name));
            return moving.Count;
        }

        // the inspector's "Layer" row: which layer the object is in, pick another to move it (with the rest of the selection)
        private void LayerPickRow(GameObjectData obj)
        {
            if (_scene.Layers.Count < 2 || SceneTools.LayerOf(_scene, obj) is not Layer cur) return;
            var opts = _scene.Layers.Select((l, i) => (i.ToString(), l.Name)).ToList();
            var g = RowGrid(L.T("Layer"));
            var cb = Dropdown.Make(opts, _scene.Layers.IndexOf(cur).ToString(), v =>
            {
                if (int.TryParse(v, out int i) && i >= 0 && i < _scene.Layers.Count) MoveSelectionToLayer(_scene.Layers[i]);
            }, owner: this);
            L.SetSkip(cb, true); // the person's layer names
            cb.Height = 28; cb.IsEnabled = !IsLockedObj(obj);
            Grid.SetColumn(cb, 1); g.Children.Add(cb);
        }

        // the right-click menu's "Move to layer ▸"
        private MenuItem MoveToLayerMenu()
        {
            var mi = new MenuItem { Header = L.T("Move to layer"), IsEnabled = _selection.Count > 0 && _scene.Layers.Count > 1 };
            foreach (var l in _scene.Layers)
            {
                var item = new MenuItem { Header = l.Name, IsEnabled = !l.Locked };
                L.SetSkip(item, true);
                item.Click += (s, e) => MoveSelectionToLayer(l);
                mi.Items.Add(item);
            }
            return mi;
        }
    }
}
