using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // A Sound behavior playing an FMOD Studio event (GameAudio.cs): the event is picked from the project's banks (its
    // "fmod" folder), and each of the event's parameters gets its own slider (or a list for a few whole steps) — saved as
    // "fmodParams" (name=value;…), which the person never sees.
    public partial class MainWindow
    {
        /// <summary>Export: put FMOD in the game? Asked only when FMOD is installed here; No is the default (FMOD's licence is
        /// the person's to follow — free for small games, see fmod.com/licensing).</summary>
        private bool AskExportFmod()
        {
            if (GameAudio.ExportableDlls().Count == 0) return false;
            bool uses = GameAudio.ProjectUsesFmod(_project);
            var text = L.T("Include FMOD in the exported game?") + "\n\n" +
                       (uses ? L.T("This game uses FMOD Studio events: without FMOD they stay silent.") : L.T("With FMOD the game's sounds play through FMOD; without it they play with the classic Windows players.")) + "\n\n" +
                       L.T("FMOD isn't part of EasyEngine and has its own licence (free for small games — fmod.com/licensing). Include it only if you follow that licence.");
            return MessageBox.Show(this, text, L.T("Include FMOD (you must follow FMOD's licence)"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        partial void FmodEventRow(GameObjectData obj, BehaviorData bh, BehaviorInfo info, TextParam tp, string label, string cur, Panel panel)
        {
            var options = GameAudio.EventOptions(_project.FolderPath, out var why);
            PickRow(label, panel, options, cur, true, null, v => { bh.Texts[tp.Key] = v; Dirty(); RefreshAfterChoiceAlways(obj); }, names: true);
            if (options.Count == 0) EmptyHint(panel, why);
            if (options.Count == 0 || cur.Length == 0)
                EmptyHint(panel, L.F("Build your FMOD Studio project's banks into this project's \"{0}\" folder (Settings → FMOD Studio).", "fmod"));
        }

        partial void FmodParamsEditor(GameObjectData obj, BehaviorData bh, TextParam tp, Panel panel)
        {
            var ev = GameAudio.FindEvent(_project.FolderPath, bh.Text("event", ""));
            if (ev == null) return; // no event picked (or not in the banks): nothing to set
            if (ev.Parameters.Count == 0) { EmptyHint(panel, L.T("This event has no parameters to set.")); return; }
            panel.Children.Add(new TextBlock { Text = L.T(tp.Label), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
            var values = GameAudio.ParseParams(bh.Text(tp.Key, ""));
            void Save(string name, double v)
            {
                values.RemoveAll(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                values.Add((name, v));
                bh.Texts[tp.Key] = GameAudio.FormatParams(values);
                Dirty();
            }
            foreach (var p in ev.Parameters)
            {
                var name = p.Name;
                double cur = values.FirstOrDefault(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { name: not null } found ? found.value : p.Default;
                string label = p.Global ? L.F("{0} (global)", name) : name;
                double min = Math.Min(p.Min, p.Max), max = Math.Max(p.Min, p.Max);
                if (p.Discrete && max - min <= 12) // a few whole steps (a surface, a weapon, an intensity 0–3): picked from a list
                {
                    var opts = Enumerable.Range((int)Math.Ceiling(min), (int)(Math.Floor(max) - Math.Ceiling(min)) + 1)
                        .Select(i => (i.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture))).ToList();
                    PickRow(label, panel, opts, Math.Round(cur).ToString(CultureInfo.InvariantCulture), false, null,
                        v => Save(name, double.Parse(v, CultureInfo.InvariantCulture)), names: true);
                }
                else
                {
                    if (!(max > min)) max = min + 1;
                    SliderRow(label, Math.Clamp(cur, min, max), min, max, v => Save(name, Math.Round(v, max - min > 20 ? 0 : 2)), panel);
                }
                // the parameter's name is the person's own (from FMOD Studio): never translated
                if (panel.Children[^1] is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock t) L.SetSkip(t, true);
            }
            EmptyHint(panel, L.T("The event starts with these values; a rule can change them while it plays (Set an FMOD parameter)."));
        }
    }
}
