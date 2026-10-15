using System;
using System.Collections.Generic;
using System.IO;

namespace EasyEngine
{
    // "↔ Reverse" on a DO block ({"do":"show","reverse":true}): the action does its opposite — show hides, give an item takes
    // it, add 5 subtracts 5, a speed goes the other way, heal hurts, on turns off, play a sound stops it. The rule is turned
    // into its opposite once, when it is read, so the rest of the game never has to know.
    public partial class PlayWindow
    {
        internal static readonly HashSet<string> ReversibleActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "hide", "show", "additem", "removeitem", "listadd", "listremove", "addvar", "addlocal", "velocity", "move", "accelerate",
            "rotate", "heal", "boost", "controls", "ability", "behavior", "setactive", "playsound",
        };

        private static void ReverseRule(CustomRule r)
        {
            void Neg(string k, double def)
            {
                bool any = false;
                if (r.N.TryGetValue(k, out var v)) { r.N[k] = -v; any = true; }
                if (r.T.TryGetValue(k, out var s) && s.Trim().Length > 0) { r.T[k] = $"-({s})"; any = true; }
                if (!any) r.N[k] = -def;
            }
            void Flip(string k, string def)
            {
                string cur = r.T.TryGetValue(k, out var s) ? s : r.N.TryGetValue(k, out var n) ? (n >= 0.5 ? "on" : "off") : def;
                bool on = cur.Trim().ToLowerInvariant() is "on" or "true" or "yes" or "1";
                r.T[k] = on ? "off" : "on"; r.N[k] = on ? 0 : 1;
            }
            switch (r.Do)
            {
                case "hide": r.Do = "show"; break;
                case "show": r.Do = "hide"; break;
                case "additem": r.Do = "removeitem"; break;
                case "removeitem": r.Do = "additem"; break;
                case "listadd": r.Do = "listremove"; break;
                case "listremove": r.Do = "listadd"; break;
                case "addvar": case "addlocal":
                    if (r.T.TryGetValue("expr", out var ex) && ex.Trim().Length > 0) r.T["expr"] = $"-({ex})"; else Neg("amount", 1);
                    break;
                case "velocity": Neg("vx", 0); Neg("vy", 0); break;
                case "move": Neg("dx", 0); Neg("dy", 0); break;
                case "accelerate": Neg("ax", 0); Neg("ay", 0); break;
                case "rotate": Neg("speed", 90); break;
                case "heal": Neg("amount", 1); break; // reversed: it hurts
                case "boost":
                {
                    double f = r.N.TryGetValue("factor", out var fv) ? fv : 2;
                    r.T.Remove("factor"); r.N["factor"] = f == 0 ? 1 : 1 / f;
                    break;
                }
                case "controls": Flip("on", "off"); break;
                case "ability": Flip("on", "on"); break;
                case "behavior": Flip("on", "off"); break;
                case "setactive": Flip("on", "on"); break;
                case "playsound": r.Reverse = true; break; // stops that sound (DoCustom)
            }
        }

        // the sound a reversed "play a sound" stops (every copy of that file still playing)
        private void StopSoundFile(string rel, string source)
        {
            if (string.IsNullOrWhiteSpace(rel)) return;
            Info("Sound", source, $"■ {Path.GetFileName(rel)} stopped");
            var abs = ProjectIO.AbsoluteAsset(_project, rel);
            if (abs == null || !_soundPool.TryGetValue(abs, out var pool)) return;
            foreach (var mp in pool) { try { mp.Stop(); } catch { } _busy.Remove(mp); }
        }
    }
}
