using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EasyEngine
{
    /// <summary>
    /// The Terrain Generator's extra blocks (its "blocks" text, a JSON list): each one has a name, a colour, WHERE it grows
    /// (sky, surface, underground, cave edges, or an area that replaces the ground), an optional box in blocks — Y counted
    /// from the ground level (up = positive, down = negative), X counted from the generator — HOW MUCH, whether it is solid,
    /// a platform or see-through, how many hits it takes and what it drops. Blocks are tried in list order; the first that
    /// matches a spot wins. The grass/dirt/stone/ore of the generator stay as they are.
    /// </summary>
    public sealed class TerrainBlockSpec
    {
        public string Name = "Block";
        public string Color = "#C8A060";
        public string Where = "underground";   // sky | surface | underground | caves | area
        public int YFrom = -10, YTo = -40;      // blocks from the ground level, up = +
        public bool XOn;                        // only between XFrom and XTo (columns from the generator)
        public int XFrom = 0, XTo = 50;
        public string Amount = "normal";        // rare | normal | lots | all
        public string Collide = "solid";        // solid | platform | none
        public int Hits = 2;
        public string Drop = "";

        public static readonly string[] Places = { "sky", "surface", "underground", "caves", "area" };
        public static readonly string[] Amounts = { "rare", "normal", "lots", "all" };
        public static readonly string[] Collisions = { "solid", "platform", "none" };

        /// <summary>A new block for <paramref name="where"/>, with sensible heights and collision for that place.</summary>
        public static TerrainBlockSpec For(string where, string name, string color)
        {
            var s = new TerrainBlockSpec { Name = name, Color = color };
            s.SetPlace(where);
            return s;
        }

        /// <summary>Moves the block to another place, with that place's usual heights (and clouds as platforms).</summary>
        public void SetPlace(string where)
        {
            Where = Places.Contains(where) ? where : "underground";
            (YFrom, YTo, Collide, Hits) = Where switch
            {
                "sky" => (4, 14, "platform", 1),
                "surface" => (60, -60, "solid", 1),
                "caves" => (-5, -60, "solid", 3),
                "area" => (-1, -12, "solid", 2),
                _ => (-15, -60, "solid", 3),
            };
            if (Where == "area") { XOn = true; XFrom = 20; XTo = 50; }
        }

        /// <summary>The Y box as (lowest, highest), whichever order it was typed in.</summary>
        public (int lo, int hi) YRange => (Math.Min(YFrom, YTo), Math.Max(YFrom, YTo));
        public (int lo, int hi) XRange => (Math.Min(XFrom, XTo), Math.Max(XFrom, XTo));

        /// <summary>Chance (0-1) for scattered places; "all" fills every spot that fits.</summary>
        public double Chance => Amount switch { "rare" => 0.08, "lots" => 0.45, "all" => 1.0, _ => 0.2 };

        // ---------------------------------------------------------------- saving (tolerant, like the AI's JSON)
        public static List<TerrainBlockSpec> Parse(string? text)
        {
            var list = new List<TerrainBlockSpec>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            try
            {
                using var doc = JsonDocument.Parse(text, LooseJson.Options);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var s = new TerrainBlockSpec();
                    string Str(string k, string d) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? d : d;
                    int Int(string k, int d) => e.TryGetProperty(k, out var v) && LooseJson.TryNumber(v, out var n) ? (int)Math.Round(n) : d;
                    bool Bool(string k, bool d) => e.TryGetProperty(k, out var v) ? v.ValueKind == JsonValueKind.True || (LooseJson.TryNumber(v, out var n) && n >= 0.5) : d;
                    s.Name = Str("name", s.Name).Trim();
                    s.Color = Str("color", s.Color);
                    var where = Str("where", "underground").ToLowerInvariant();
                    s.Where = Places.Contains(where) ? where : "underground";
                    s.YFrom = Int("yFrom", s.YFrom); s.YTo = Int("yTo", s.YTo);
                    s.XOn = Bool("xOn", false); s.XFrom = Int("xFrom", s.XFrom); s.XTo = Int("xTo", s.XTo);
                    var amount = Str("amount", "normal").ToLowerInvariant();
                    s.Amount = Amounts.Contains(amount) ? amount : "normal";
                    var collide = Str("collide", "solid").ToLowerInvariant();
                    s.Collide = Collisions.Contains(collide) ? collide : "solid";
                    s.Hits = Math.Clamp(Int("hits", 2), 1, 10);
                    s.Drop = Str("drop", "").Trim();
                    if (s.Name.Length > 0) list.Add(s);
                }
            }
            catch (JsonException) { }
            return list;
        }

        public static string Serialize(IEnumerable<TerrainBlockSpec> list) => JsonSerializer.Serialize(list.Select(s => new Dictionary<string, object>
        {
            ["name"] = s.Name, ["color"] = s.Color, ["where"] = s.Where, ["yFrom"] = s.YFrom, ["yTo"] = s.YTo,
            ["xOn"] = s.XOn, ["xFrom"] = s.XFrom, ["xTo"] = s.XTo, ["amount"] = s.Amount, ["collide"] = s.Collide,
            ["hits"] = s.Hits, ["drop"] = s.Drop,
        }));
    }
}
