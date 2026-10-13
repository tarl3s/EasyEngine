using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EasyEngine
{
    /// <summary>
    /// Scene and layer operations shared by the editor and the tests (no UI here): duplicate a scene or a layer (deep
    /// copies whose objects get new unique ids), move a layer in the draw order, and the layer an object lives in.
    /// Rule references (a door's GoToScene, a "goscene" rule) are copied as they are — never rewritten.
    /// </summary>
    public static class SceneTools
    {
        private static T Clone<T>(T v) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v))!;

        /// <summary>"Level 1 copy", then "Level 1 copy 2", "Level 1 copy 3"… — the first name no scene has.</summary>
        public static string UniqueSceneName(Project p, string baseName)
        {
            string first = baseName + " copy";
            if (!p.Scenes.Any(s => s.Name == first)) return first;
            for (int i = 2; ; i++)
                if (!p.Scenes.Any(s => s.Name == $"{first} {i}")) return $"{first} {i}";
        }

        /// <summary>A deep copy of the scene, placed right after it, named uniquely, every object with a new id.</summary>
        public static Scene DuplicateScene(Project p, Scene original)
        {
            var copy = Clone(original);
            copy.Name = UniqueSceneName(p, original.Name);
            foreach (var o in copy.Layers.SelectMany(l => l.Objects)) o.Id = 0; // not counted by NextId below
            int at = p.Scenes.IndexOf(original);
            p.Scenes.Insert(at < 0 ? p.Scenes.Count : at + 1, copy);
            foreach (var o in copy.Layers.SelectMany(l => l.Objects)) o.Id = p.NextId;
            return copy;
        }

        /// <summary>A copy of the layer right after it (in front of it): "Name copy", its objects with new ids and names
        /// that don't clash in the scene. A copied Base layer becomes Custom (a scene keeps one Base layer).</summary>
        public static Layer DuplicateLayer(Project p, Scene scene, Layer original)
        {
            var copy = Clone(original);
            copy.Name = UniqueLayerName(scene, original.Name + " copy");
            if (copy.Kind == LayerKind.Base) copy.Kind = LayerKind.Custom;
            copy.Locked = false; // the copy is there to be worked on
            var names = new HashSet<string>(scene.Layers.SelectMany(l => l.Objects).Select(o => o.Name));
            foreach (var o in copy.Objects)
            {
                o.Id = p.NextId;
                string n = o.Name + " copy";
                for (int i = 2; names.Contains(n); i++) n = $"{o.Name} copy {i}";
                o.Name = n; names.Add(n);
            }
            int at = scene.Layers.IndexOf(original);
            scene.Layers.Insert(at < 0 ? scene.Layers.Count : at + 1, copy);
            return copy;
        }

        public static string UniqueLayerName(Scene scene, string name)
        {
            if (!scene.Layers.Any(l => l.Name == name)) return name;
            for (int i = 2; ; i++)
                if (!scene.Layers.Any(l => l.Name == $"{name} {i}")) return $"{name} {i}";
        }

        /// <summary>Moves a layer in the draw order: +1 = one step toward the front (later in the list), −1 toward the
        /// back. False when it is already at that end.</summary>
        public static bool MoveLayer(Scene scene, Layer layer, int delta)
        {
            int i = scene.Layers.IndexOf(layer), j = i + Math.Sign(delta);
            if (i < 0 || j < 0 || j >= scene.Layers.Count) return false;
            scene.Layers.RemoveAt(i);
            scene.Layers.Insert(j, layer);
            return true;
        }

        public static Layer? LayerOf(Scene scene, GameObjectData obj) => scene.Layers.FirstOrDefault(l => l.Objects.Contains(obj));

        /// <summary>Locked for the editor's canvas and the AI: the object itself, or the layer it is in.</summary>
        public static bool IsLocked(Scene scene, GameObjectData obj) => obj.Locked || LayerOf(scene, obj)?.Locked == true;
    }
}
