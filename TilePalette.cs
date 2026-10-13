using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EasyEngine
{
    /// <summary>
    /// One tile of a Tilemap's palette: its colour, whether it collides, and optionally a picture (<see cref="Image"/>)
    /// or an animation (<see cref="Frames"/> played at <see cref="Fps"/>, every copy of the tile in sync). The colour
    /// stays the fallback when a picture is missing and the colour of the bits a broken tile throws.
    /// </summary>
    public sealed class TileKind
    {
        public string Color = "#888888";
        public bool Solid;
        public string? Image;                       // project-relative PNG (a still picture)
        public List<string> Frames = new();         // project-relative PNGs (an animated tile; wins over Image)
        public double Fps = 8;

        public TileKind() { }
        public TileKind(string color, bool solid) { Color = color; Solid = solid; }

        public bool HasPicture => Frames.Count > 0 || !string.IsNullOrEmpty(Image);
        public bool Animated => Frames.Count > 1;
        /// <summary>The picture the editor shows (an animated tile's first frame), or null for a plain colour.</summary>
        public string? FirstPicture => Frames.Count > 0 ? Frames[0] : string.IsNullOrEmpty(Image) ? null : Image;

        /// <summary>Back to a plain colour.</summary>
        public void ClearPicture() { Image = null; Frames.Clear(); }

        /// <summary>A still picture (drawn or chosen): replaces any animation.</summary>
        public void SetPicture(string relative) { Frames.Clear(); Image = relative; }

        /// <summary>The frames the animator returned: none = plain colour, one = a still picture, more = animated.</summary>
        public void SetFrames(IReadOnlyList<string> frames, double fps)
        {
            Fps = Math.Clamp(fps, 1, 60);
            if (frames.Count == 0) { ClearPicture(); return; }
            if (frames.Count == 1) { SetPicture(frames[0]); return; }
            Image = null; Frames = new List<string>(frames);
        }

        public TileKind Clone() => new() { Color = Color, Solid = Solid, Image = Image, Frames = new List<string>(Frames), Fps = Fps };

        /// <summary>A short description for the inspector (no internal formats).</summary>
        public string Describe() =>
            Animated ? L.F("{0} frames · {1} per second", Frames.Count, Fps.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture))
            : HasPicture ? Path.GetFileName(FirstPicture!) : L.T("Plain colour");
    }

    /// <summary>
    /// The Tilemap behavior's "palette" text: a JSON list of tiles. A colour-only tile is saved exactly as before
    /// (<c>{"color":"#5BA862","solid":true}</c>); a pictured tile adds <c>"image"</c>, an animated one <c>"frames"</c> and
    /// <c>"fps"</c>. Reading is tolerant (comments, trailing commas, quoted numbers, unknown keys), and an unreadable
    /// palette keeps the tiles read so far. Shared by the editor and the runtime.
    /// </summary>
    public static class TilePalette
    {
        public static List<TileKind> Parse(string? json)
        {
            var pal = new List<TileKind>();
            if (string.IsNullOrWhiteSpace(json)) return pal;
            try
            {
                using var doc = JsonDocument.Parse(json, LooseJson.Options);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return pal;
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var k = new TileKind
                    {
                        Color = e.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String ? (c.GetString() ?? "#888888") : "#888888",
                        Solid = e.TryGetProperty("solid", out var s) && (s.ValueKind == JsonValueKind.True || (LooseJson.TryNumber(s, out var sv) && sv >= 0.5)
                                                                          || (s.ValueKind == JsonValueKind.String && string.Equals(s.GetString(), "true", StringComparison.OrdinalIgnoreCase)))
                    };
                    if (e.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(im.GetString()))
                        k.Image = im.GetString();
                    if (e.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
                        foreach (var f in fr.EnumerateArray())
                            if (f.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(f.GetString())) k.Frames.Add(f.GetString()!);
                    if (e.TryGetProperty("fps", out var fp) && LooseJson.TryNumber(fp, out var fps)) k.Fps = Math.Clamp(fps, 1, 60);
                    pal.Add(k);
                }
            }
            catch { }
            return pal;
        }

        public static string Serialize(IEnumerable<TileKind> palette)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartArray();
                foreach (var k in palette)
                {
                    w.WriteStartObject();
                    w.WriteString("color", k.Color);
                    w.WriteBoolean("solid", k.Solid);
                    if (!string.IsNullOrEmpty(k.Image)) w.WriteString("image", k.Image);
                    if (k.Frames.Count > 0)
                    {
                        w.WriteStartArray("frames");
                        foreach (var f in k.Frames) w.WriteStringValue(f);
                        w.WriteEndArray();
                        w.WriteNumber("fps", k.Fps);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
