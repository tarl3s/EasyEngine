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
                var first = sa >= 0 ? reply.AsSpan(sa + 1).TrimStart() : default;
                bool listOfObjects = first.Length > 0 && first[0] is '{' or ']' or '"'; // not "[Updated] {…}"
                if (sa >= 0 && sb > sa && (a < 0 || sa < a) && listOfObjects) return reply.Substring(sa, sb - sa + 1);
            }
            if (a < 0) return null;
            var whole = b > a ? reply.Substring(a, b - a + 1) : null; // none when it was cut off before its first "}"
            if (whole != null && Parses(whole)) return whole;
            // not JSON as it stands: the first object alone (a reply with a stray "}" or a second object after it),
            // or the object with the brackets it forgot to close
            return Balanced(reply, a) is string one && Parses(one) ? one : Truncated(reply, a) ?? whole;
        }

        // an answer that stopped in the middle (a small model running out of room): everything up to the last value that
        // was finished, with its brackets closed — what was cut off is lost, the rest is kept
        private static string? Truncated(string reply, int start)
        {
            int tries = 0;
            for (int cut = reply.Length - 1; cut > start && tries < 300; cut--)
            {
                if (reply[cut] is not ('}' or ']' or '"' or 'e' or 'l' or '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9')) continue;
                tries++;
                if (Balanced(reply[..(cut + 1)], start) is string s && Parses(s)) return s;
            }
            return null;
        }

        private static bool Parses(string s)
        {
            try { using var d = JsonDocument.Parse(s, Options); return true; }
            catch (System.Exception) { return false; }
        }

        // from reply[start] ('{'): up to where its brackets close; brackets still open at the end are closed
        private static string? Balanced(string reply, int start)
        {
            var open = new System.Collections.Generic.Stack<char>();
            bool str = false, esc = false;
            for (int i = start; i < reply.Length; i++)
            {
                char c = reply[i];
                if (str) { if (esc) esc = false; else if (c == '\\') esc = true; else if (c == '"') str = false; continue; }
                if (c == '"') str = true;
                else if (c is '{' or '[') open.Push(c == '{' ? '}' : ']');
                else if (c is '}' or ']')
                {
                    if (open.Count == 0 || open.Peek() != c) return null;
                    open.Pop();
                    if (open.Count == 0) return reply.Substring(start, i - start + 1);
                }
            }
            if (str || open.Count == 0) return null;
            var sb = new System.Text.StringBuilder(reply.Substring(start).TrimEnd().TrimEnd(','));
            while (open.Count > 0) sb.Append(open.Pop());
            return sb.ToString();
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

        /// <summary>An element as an editable node, read as leniently as the rest (comments, trailing commas); null when
        /// it can't be (e.g. a key written twice).</summary>
        public static System.Text.Json.Nodes.JsonNode? Node(JsonElement e)
        {
            try { return System.Text.Json.Nodes.JsonNode.Parse(e.GetRawText(), null, Options); }
            catch (System.Exception) { return null; }
        }

        /// <summary>A yes: true, 1, "true", "yes" or "1" (models write flags every way).</summary>
        public static bool IsTrue(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => e.GetDouble() >= 0.5,
            JsonValueKind.String => (e.GetString() ?? "").Trim().ToLowerInvariant() is "true" or "yes" or "1",
            _ => false
        };

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
