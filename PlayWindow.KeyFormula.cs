using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace EasyEngine
{
    // "key:E" in a formula: 1 while that key is held, else 0 — so "Only while" (texts.cond), a rule's "only run if" and a
    // checklist line can all say "while E is held" (key:E >= 1). Read every frame, so it never allocates: each spelling is
    // turned into a Key once and kept (a project has a handful of them).
    public partial class PlayWindow
    {
        internal const string KeyPrefix = "key:";
        private static readonly Dictionary<string, Key> KeyFormulaCache = new(StringComparer.OrdinalIgnoreCase);

        private bool KeyFormulaHeld(string name)
        {
            if (!KeyFormulaCache.TryGetValue(name, out var k))
                KeyFormulaCache[name] = k = KeyByName(name.Substring(KeyPrefix.Length)) ?? Key.None;
            return k != Key.None && _keys.Contains(k);
        }

        /// <summary>A key as a person (or an AI) names it: "E", "space", "shift", "ctrl", "1", "up", "LeftShift"…; null if it
        /// isn't a key.</summary>
        internal static Key? KeyByName(string? s)
        {
            s = s?.Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Length == 1 && char.IsDigit(s[0])) return Key.D0 + (s[0] - '0');
            switch (s.ToLowerInvariant())
            {
                case "shift": return Key.LeftShift;
                case "ctrl": case "control": return Key.LeftCtrl;
                case "alt": return Key.LeftAlt;
                case "esc": return Key.Escape;
                case "return": return Key.Enter;
                case "spacebar": return Key.Space;
            }
            if (char.IsDigit(s[0]) || s[0] == '-') return null; // "5" would read as the enum's 5th value
            return Enum.TryParse<Key>(s, ignoreCase: true, out var k) && k != Key.None ? k : null;
        }

        // test hook: one name as a formula reads it (allocation checks call this in a loop)
        public double TResolve(string name, string? obj = null) => ResolveName(name, obj == null ? null : TE(obj));
    }
}
