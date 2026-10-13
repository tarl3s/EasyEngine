using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // Layers and objects in the game:
    // - a layer (or an object) turned off (Active = false) isn't created when the scene loads; it stays in the scene's
    //   data, so it can still be a spawn template and a "setactive" rule can bring it in later;
    // - a layer's Opacity multiplies every object's own opacity (an OpacityMask on each visual, so the many places that set
    //   Visual.Opacity — fades, blinking, hide/show — keep working untouched; tilemap cells get it in their own opacity);
    // - the rule action {"do":"setactive","target":"Bridge"|"self","layer":"Secret","on":"on"|"off"}: off removes the live
    //   object(s) (every object of that layer), on creates the ones that aren't there, in their layer's place in the draw order.
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public int LayerIndex = -1;           // the scene layer it was created from; -1 = a copy spawned during play
            public GameObjectData? Source;        // the scene object it was created from (null for spawned copies)
            public List<UIElement>? TileRects;    // a tilemap's painted cells (separate visuals), so turning it off removes them
        }

        private double _layerAlpha = 1;           // the opacity of the layer whose object is being created (BuildTilemap reads it)
        private static readonly Dictionary<byte, SolidColorBrush> LayerMasks = new();

        /// <summary>Creates a scene object of a layer (at load, or turned on by a rule): its layer's opacity, the Sky.</summary>
        private Entity CreateLayerObject(Layer layer, int layerIndex, GameObjectData source, GameObjectData data, bool screen, bool sky)
        {
            double alpha = Math.Clamp(layer.Opacity, 0, 1);
            _layerAlpha = alpha;
            Entity en;
            try { en = AddEntity(data, screen); }
            finally { _layerAlpha = 1; }
            if (sky) PutInSky(en);
            en.LayerIndex = layerIndex; en.Source = source;
            if (alpha < 1) en.Visual.OpacityMask = LayerMask(alpha);
            return en;
        }

        private static SolidColorBrush LayerMask(double alpha)
        {
            byte a = (byte)Math.Round(alpha * 255);
            if (LayerMasks.TryGetValue(a, out var b)) return b;
            b = new SolidColorBrush(Color.FromArgb(a, 0, 0, 0));
            b.Freeze();
            return LayerMasks[a] = b;
        }

        // {"do":"setactive"}: every field is read first, so a rule that names nothing usable simply does nothing
        private void SetActiveRule(Entity en, CustomRule r)
        {
            string layerName = r.Str("layer", "").Trim(), who = r.Str("target", "self").Trim();
            bool on = r.Num("on", 1) >= 0.5;
            if (layerName.Length > 0)
            {
                int li = _scene.Layers.FindIndex(l => l.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase));
                if (li < 0) { if (!r.WarnedUnknown) { r.WarnedUnknown = true; Warn("Script", en.Data.Name, $"setactive: there is no layer \"{layerName}\" in this scene"); } return; }
                if (!on) { foreach (var e in _entities) if (!e.Dead && e.LayerIndex == li) TurnOff(e); }
                else foreach (var obj in ZOrder.InDrawOrder(_scene.Layers[li].Objects)) if (obj.Active) TurnOn(li, obj);
                Info("Script", en.Data.Name, $"layer \"{_scene.Layers[li].Name}\" turned {(on ? "on" : "off")}");
                return;
            }
            if (who.Length == 0 || who.Equals("self", StringComparison.OrdinalIgnoreCase)) { if (!on) TurnOff(en); return; }
            if (!on) { foreach (var e in _entities) if (!e.Dead && e.Data.Name.Equals(who, StringComparison.OrdinalIgnoreCase)) TurnOff(e); return; }
            var (index, found) = SceneObjectNamed(who);
            if (found == null) { if (!r.WarnedUnknown) { r.WarnedUnknown = true; Warn("Script", en.Data.Name, $"setactive: there is no object \"{who}\" in this scene"); } return; }
            TurnOn(index, found);
        }

        // the scene object with this name (an exact match first) and its layer's index
        private (int layer, GameObjectData? obj) SceneObjectNamed(string name)
        {
            (int, GameObjectData?) loose = (-1, null);
            for (int li = 0; li < _scene.Layers.Count; li++)
                foreach (var o in _scene.Layers[li].Objects)
                {
                    if (o.Name == name) return (li, o);
                    if (loose.Item2 == null && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) loose = (li, o);
                }
            return loose;
        }

        private void TurnOff(Entity e)
        {
            e.Dead = true; // Cleanup takes its visual, light, collider… away at the end of the frame
            if (e.TileRects == null) return;
            foreach (var rect in e.TileRects) if (VisualTreeHelper.GetParent(rect) is Panel p) p.Children.Remove(rect);
            e.TileRects = null;
            _solids.RemoveAll(s => ReferenceEquals(s.Data, e.Data) && !ReferenceEquals(s, e)); // its tile colliders
        }

        /// <summary>Creates a scene object that isn't there (already live = nothing), in its layer's place in the draw order.</summary>
        private void TurnOn(int layerIndex, GameObjectData obj)
        {
            foreach (var e in _entities) if (!e.Dead && ReferenceEquals(e.Source, obj)) return;
            var layer = _scene.Layers[layerIndex];
            bool sky = layer.Kind == LayerKind.Sky, screen = layer.ScreenSpace || layer.Kind == LayerKind.UI || sky;
            _zLoading = true; // not a spawned copy: it goes back among its layer
            Entity en;
            try { en = CreateLayerObject(layer, layerIndex, obj, JsonClone(obj), screen, sky); }
            finally { _zLoading = false; }
            PlaceInLayerOrder(en);
            if ((_cameraTarget == null || _cameraTarget.Dead) && en.Has(BehaviorKind.FollowCamera)) _cameraTarget = en;
        }

        // a visual created late goes in front of its own layer but behind the later layers (and the copies spawned during play)
        private void PlaceInLayerOrder(Entity en)
        {
            if (VisualTreeHelper.GetParent(en.Visual) is not Panel panel) return;
            var owners = new Dictionary<UIElement, Entity>();
            foreach (var e in _entities) if (!ReferenceEquals(e, en)) owners[e.Visual] = e;
            for (int i = 0; i < panel.Children.Count; i++)
            {
                var c = panel.Children[i];
                if (ReferenceEquals(c, en.Visual) || !owners.TryGetValue(c, out var o)) continue;
                if (o.LayerIndex < 0 || o.LayerIndex > en.LayerIndex || (o.LayerIndex == en.LayerIndex && o.Data.Z > en.Data.Z))
                {
                    panel.Children.Remove(en.Visual);
                    panel.Children.Insert(Math.Min(i, panel.Children.Count), en.Visual);
                    return;
                }
            }
        }

        // ---- test hooks ----
        /// <summary>The layer opacity drawn on this object (its visual's OpacityMask alpha); 1 = none.</summary>
        internal double TLayerAlpha(string name) => _entities.Find(e => e.Data.Name == name)?.Visual.OpacityMask is SolidColorBrush b ? b.Color.A / 255.0 : 1;
        internal int TTileRects(string name) => _entities.Find(e => !e.Dead && e.Data.Name == name)?.TileRects?.Count ?? 0;
        internal double TTileOpacity(string name) => _entities.Find(e => !e.Dead && e.Data.Name == name)?.TileRects is { Count: > 0 } t ? t[0].Opacity : -1;
    }
}
