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

        /// <summary>A number, or a numeric string ("260", "40%") — small models often quote numbers.</summary>
        public static bool TryNumber(JsonElement e, out double value)
        {
            if (e.ValueKind == JsonValueKind.Number) { value = e.GetDouble(); return true; }
            if (e.ValueKind == JsonValueKind.String &&
                double.TryParse((e.GetString() ?? "").Replace("%", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            value = 0;
            return false;
        }
    }
}
