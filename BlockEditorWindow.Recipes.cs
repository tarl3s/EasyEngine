using System.Windows;
using System.Windows.Controls;

namespace EasyEngine
{
    // "📚 Recipes": ready-made blocks for the things people ask for most (the same shapes the AI's recipes teach),
    // added at the end of the list to be tweaked like any other block. Saved exactly like hand-made blocks.
    public partial class BlockEditorWindow
    {
        internal static (string label, string json)[] Recipes() => new[]
        {
            (L.T("Fade out after 5 seconds"), "[{\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":2}]"),
            (L.T("Blink forever"), "[{\"when\":\"start\",\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":0.5,\"loop\":1,\"yoyo\":1}]"),
            (L.T("Pulse (grow and shrink)"), "[{\"when\":\"start\",\"do\":\"tween\",\"prop\":\"scale\",\"to\":1.2,\"duration\":0.6,\"ease\":\"easeinout\",\"loop\":1,\"yoyo\":1}]"),
            (L.T("Follow the mouse"), "[{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"x\",\"expr\":\"mousex - self.w/2\"},{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"y\",\"expr\":\"mousey - self.h/2\"}]"),
            (L.T("Move in a circle"), "[{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"vx\",\"expr\":\"-sin(time*2) * 200\"},{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"vy\",\"expr\":\"cos(time*2) * 200\"}]"),
            (L.T("Run away from the player"), "[{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"vx\",\"expr\":\"sign(self.cx - Player.cx) * 160\"}]"),
            (L.T("Walk toward the player"), "[{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"vx\",\"expr\":\"sign(Player.cx - self.cx) * 120\"}]"),
            (L.T("Jump every 2 seconds"), "[{\"when\":\"timer\",\"every\":2,\"do\":\"jump\",\"force\":600}]"),
            (L.T("Spin while R is held"), "[{\"when\":\"key\",\"key\":\"R\",\"do\":\"rotate\",\"speed\":180}]"),
            (L.T("Click it: +1 score"), "[{\"when\":\"click\",\"do\":\"addvar\",\"var\":\"score\",\"amount\":1}]"),
            (L.T("Disappear when the Player touches it"), "[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"destroy\"}]"),
            (L.T("Explode when it dies"), "[{\"when\":\"death\",\"do\":\"emit\",\"count\":30,\"speed\":220,\"color\":\"#FF8A3D\"}]"),
            (L.T("Countdown from 60 (show it with a Label \"{timer}\")"), "[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"timer\",\"to\":60},{\"when\":\"timer\",\"every\":1,\"cond\":\"timer > 0\",\"do\":\"addvar\",\"var\":\"timer\",\"amount\":-1}]"),
            (L.T("Say something when the scene starts"), "[{\"when\":\"start\",\"do\":\"say\",\"text\":\"Hello!\"}]"),
        };

        private Button RecipesButton()
        {
            var btn = new Button { Content = L.T("📚  Recipes"), Height = 34, Margin = new Thickness(8, 8, 0, 0), Padding = new Thickness(14, 0, 14, 0), Style = Theme.Style("GhostButton"),
                                   ToolTip = L.T("Ready-made blocks for common things — add one, then change it as you like") };
            var menu = new ContextMenu { PlacementTarget = btn };
            foreach (var (label, json) in Recipes())
            {
                var item = new MenuItem { Header = label };
                item.Click += (s, e) => AddRules(json);
                menu.Items.Add(item);
            }
            btn.Click += (s, e) => menu.IsOpen = true;
            return btn;
        }
    }
}
