using System.Globalization;
using System.Text.Json;

namespace EasyEngine
{
    /// <summary>
    /// Reading JSON written by people and AI models: comments and trailing commas are fine, the JSON can be wrapped in
    /// prose or ``` fences, and numbers may come quoted ("260", "40%"). Shared by the AI builders, the script/step
    /// parsers and the block editors.
    /// </summary>
    public static class LooseJson
    {
        public static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>The JSON inside a reply: first '{' to last '}' — or, with <paramref name="allowArray"/>, a bare
        /// [...] when it comes first (null if there is none).</summary>
        public static string? Extract(string reply, bool allowArray = false)
        {
            int a = reply.IndexOf('{'), b = reply.LastIndexOf('}');
            if (allowArray)
            {
                int sa = reply.IndexOf('['), sb = reply.LastIndexOf(']');
                if (sa >= 0 && sb > sa && (a < 0 || sa < a)) return reply.Substring(sa, sb - sa + 1);
            }
            return a >= 0 && b > a ? reply.Substring(a, b - a + 1) : null;
        }

        /// <summary>A number, or a numeric string ("260") — small models often quote numbers. A "40%" string is
        /// accepted only with <paramref name="percent"/> (where the value itself is a percentage).</summary>
        public static bool TryNumber(JsonElement e, out double value, bool percent = false)
        {
            if (e.ValueKind == JsonValueKind.Number) { value = e.GetDouble(); return true; }
            if (e.ValueKind == JsonValueKind.String) return TryParse(e.GetString() ?? "", out value, percent);
            value = 0;
            return false;
        }

        /// <summary>Number text: never with thousands separators; a lone ',' (no '.') is the decimal point ("0,5" = 0.5).
        /// A trailing '%' is allowed only with <paramref name="percent"/> and is kept as-is ("40%" = 40).</summary>
        public static bool TryParse(string s, out double value, bool percent = false)
        {
            s = s.Trim();
            if (percent && s.EndsWith('%')) s = s[..^1].TrimEnd();
            if (s.Contains(',') && !s.Contains('.')) s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }
    }
}
