using System.Linq;

namespace EasyEngine;

// Interact (press a key near it): the talk isn't queued again while it shows, every line of the message is the next
// thing said, it can freeze whoever is using it, and its dialogue box can have its own look and font.
public partial class App
{
    partial void FeatureTestsInteract(FullTestKit t)
    {
        PlayWindow Talk(string text, string look = "theme", string font = "", double freeze = 0)
        {
            var pw = PlayWindow.NewScriptSim("[]", out _, extra: (p, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                layer.Objects.Add(new GameObjectData { Id = 2, Name = "Floor", X = 0, Y = 300, Width = 1000, Height = 40, Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
                var player = new GameObjectData { Id = 3, Name = "Player", Type = "Player", X = 400, Y = 236, Width = 48, Height = 64, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
                player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
                layer.Objects.Add(player);
                var npc = new GameObjectData { Id = 4, Name = "NPC", X = 440, Y = 236, Width = 40, Height = 64 };
                var it = BehaviorCatalog.Info(BehaviorKind.Interact).CreateData();
                it.Texts["text"] = text; it.Texts["look"] = look; it.Texts["font"] = font; it.Nums["freeze"] = freeze;
                npc.Behaviors.Add(it);
                layer.Objects.Add(npc);
            });
            t.Sims.Add(pw);
            pw.TSteps(20);
            return pw;
        }
        void Press(PlayWindow pw, string key) { pw.TKey(key, true); pw.TSteps(3); pw.TKey(key, false); pw.TSteps(3); }

        t.Test("interact: pressing E again while the message shows doesn't queue the talk again; one Enter per line closes it", () =>
        {
            var pw = Talk("Hi!\nHow are you?");
            Press(pw, "E");
            bool open = pw.TDialog; int queued = pw.TDialogQueued;
            Press(pw, "E"); Press(pw, "E");
            bool same = pw.TDialogQueued == queued;
            pw.TAdvanceDialog(); pw.TAdvanceDialog(); pw.TAdvanceDialog(); // typewriter: reveal, next line, close
            if (pw.TDialog) pw.TAdvanceDialog();
            return open && queued == 1 && same && !pw.TDialog;
        });
        t.Test("interact: each line of the message (Enter) and the older | are the next thing said", () =>
        {
            var pw = Talk("One\r\nTwo|Three");
            Press(pw, "E");
            return pw.TDialog && pw.TDialogQueued == 2;
        });
        t.Test("interact: 'Freeze who is using it' keeps the player still until the talk ends", () =>
        {
            var pw = Talk("Wait here.", freeze: 1);
            Press(pw, "E");
            double x0 = pw.TX("Player");
            pw.TKey("Right", true); pw.TSteps(30);
            bool still = System.Math.Abs(pw.TX("Player") - x0) < 2;
            pw.TKey("Right", false);
            while (pw.TDialog) pw.TAdvanceDialog();
            pw.TKey("Right", true); pw.TSteps(30); pw.TKey("Right", false);
            return still && pw.TX("Player") > x0 + 20;
        });
        t.Test("interact: the dialogue box takes its look (parchment) and font, and the next plain message is back to the theme", () =>
        {
            var pw = Talk("Old map.", look: "parchment", font: "Consolas");
            Press(pw, "E");
            string bg = pw.TDialogBg, font = pw.TDialogFont;
            while (pw.TDialog) pw.TAdvanceDialog();
            var theme = new GameTheme();
            pw.TSay("plain", "");
            return bg.EndsWith("EAD9B0") && font == "Consolas" && pw.TDialogBg.EndsWith(theme.DialogBg.TrimStart('#').ToUpperInvariant());
        });
        t.Test("inspector: a sound with a very long file name is cut with … and its ✂ Edit and ✕ buttons stay inside the card", () =>
        {
            var project = ProjectIO.CreateNew("SoundRow", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EE_Full_SoundRow_" + System.Guid.NewGuid().ToString("N")[..6]));
            var ed = new MainWindow(project);
            var o = new GameObjectData { Name = "Radio" };
            var snd = BehaviorCatalog.Info(BehaviorKind.SoundEmitter).CreateData();
            snd.Texts["asset"] = @"Assets\sounds\Smells_Like_Teen_Spirit_2019_Remastered_Extended_Version_Final.wav";
            o.Behaviors.Add(snd);
            ed.TestInspect(o);
            const double width = 300;
            ed.InspectorRoot.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            ed.InspectorRoot.Arrange(new System.Windows.Rect(0, 0, width, ed.InspectorRoot.DesiredSize.Height));
            static System.Collections.Generic.IEnumerable<System.Windows.DependencyObject> All(System.Windows.DependencyObject d)
            { yield return d; foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(d).OfType<System.Windows.DependencyObject>()) foreach (var x in All(c)) yield return x; }
            var clear = All(ed.InspectorRoot).OfType<System.Windows.Controls.Button>().FirstOrDefault(b => b.Content as string == "✕" && b.ToolTip as string == L.T("No sound"));
            if (clear == null) return false;
            double right = clear.TranslatePoint(new System.Windows.Point(clear.ActualWidth, 0), ed.InspectorRoot).X;
            try { ed.Close(); } catch { }
            return clear.ActualWidth > 10 && right <= width + 0.5;
        });
        t.Test("blocks: 'Play an animation' shows it once and then idle again; 'over and over' keeps it; 'back to automatic' gives idle back", () =>
        {
            PlayWindow Sim(string mode) => PlayWindow.NewScriptSim("[{\"when\":\"keypress\",\"key\":\"Space\",\"do\":\"playanim\",\"anim\":\"dance\",\"mode\":\"" + mode + "\"},{\"when\":\"keypress\",\"key\":\"X\",\"do\":\"playanim\",\"mode\":\"auto\"}]", out _, extra: (p, s) =>
            {
                string Png(string n)
                {
                    var dir = System.IO.Path.Combine(p.FolderPath!, "Assets", "sprites"); System.IO.Directory.CreateDirectory(dir);
                    var bmp = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, n + ".png"))) enc.Save(fs);
                    return System.IO.Path.Combine("Assets", "sprites", n + ".png");
                }
                var a = s.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A");
                a.Animations.Add(new SpriteAnimation { Name = "idle", Frames = { Png("i0") }, Fps = 10 });
                a.Animations.Add(new SpriteAnimation { Name = "dance", Frames = { Png("d0"), Png("d1"), Png("d2") }, Fps = 10, Loop = true });
            });
            void Key(PlayWindow pw, string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(2); }
            var once = Sim("once"); t.Sims.Add(once);
            once.TSteps(3); once.TAnimate(0.05); string before = once.TAnimName("A");
            Key(once, "Space"); once.TAnimate(0.01); string playing = once.TAnimName("A");
            once.TAnimate(0.35); once.TAnimate(0.01); string after = once.TAnimName("A");
            var loop = Sim("loop"); t.Sims.Add(loop);
            loop.TSteps(3); Key(loop, "Space"); loop.TAnimate(0.01); loop.TAnimate(1.0); loop.TAnimate(0.01); string looping = loop.TAnimName("A");
            Key(loop, "X"); loop.TAnimate(0.01); string auto = loop.TAnimName("A");
            return before == "idle" && playing == "dance" && after == "idle" && looping == "dance" && auto == "idle";
        });
    }
}
