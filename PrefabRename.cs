using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyEngine
{
    /// <summary>
    /// Renaming a prefab: the game finds prefabs by name (spawners, "spawn" rules, Boss/Cutscene steps, the terrain's
    /// tree, minimap markers…), so every reference in the project's behavior texts follows the new name — a whole value,
    /// an item of a comma list ("Bat,Slime"), a "Name:#color" entry, or a quoted name inside a script ("Slime").
    /// Objects already placed keep their own names.
    /// </summary>
    public static class PrefabRename
    {
        /// <summary>Why <paramref name="newName"/> can't be used (null = fine).</summary>
        public static string? Problem(Project p, PrefabDef pf, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return L.T("The name can't be empty.");
            if (newName.Contains(',') || newName.Contains(':') || newName.Contains('"')) return L.T("A name can't contain , : or \".");
            if (p.Prefabs.Any(o => o != pf && o.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                return L.T("A prefab with that name already exists.");
            return null;
        }

        /// <summary>Renames <paramref name="pf"/> and its references; returns how many texts changed.</summary>
        public static int Apply(Project p, PrefabDef pf, string newName)
        {
            newName = newName.Trim();
            string old = pf.Name;
            pf.Name = newName;
            if (old.Equals(newName, StringComparison.Ordinal)) return 0;
            var quoted = new Regex("\"" + Regex.Escape(old) + "\"", RegexOptions.IgnoreCase);
            int changed = 0;
            IEnumerable<GameObjectData> objects = p.Scenes.SelectMany(s => s.Layers).SelectMany(l => l.Objects).Concat(p.Prefabs.Select(o => o.Template));
            foreach (var o in objects) if (o.Prefab == old) o.Prefab = newName; // its copies stay linked
            foreach (var bh in objects.SelectMany(o => o.Behaviors))
                foreach (var key in bh.Texts.Keys.ToList())
                {
                    var v = bh.Texts[key] ?? "";
                    var nv = Replace(v, old, newName, quoted);
                    if (nv != v) { bh.Texts[key] = nv; changed++; }
                }
            return changed;
        }

        private static string Replace(string v, string old, string neu, Regex quoted)
        {
            if (v.Equals(old, StringComparison.OrdinalIgnoreCase)) return neu;
            if (v.Contains('"')) return quoted.Replace(v, "\"" + neu.Replace("$", "$$") + "\""); // scripts / JSON
            if (!v.Contains(',') && !v.Contains(':')) return v;
            // comma lists, each item maybe "Name:#color"
            var parts = v.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                var item = parts[i];
                int colon = item.IndexOf(':');
                var name = (colon < 0 ? item : item[..colon]).Trim();
                if (name.Equals(old, StringComparison.OrdinalIgnoreCase))
                    parts[i] = item.Replace(name, neu);
            }
            return string.Join(",", parts);
        }
    }
}
