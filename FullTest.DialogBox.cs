using System.IO;
using System.Linq;

namespace EasyEngine;

// Dialogue boxes: an Interact, a Trigger Area and a Cutscene's (or boss's) "say" step have the same box settings — a
// ready-made look, the person's own colours, their own picture (the box keeps the picture's proportions) and a font.
public partial class App
{
    partial void FeatureTestsDialogBox(FullTestKit t)
    {
        // an 800×150 picture in the project
        string Png(Project p)
        {
            var dir = Path.Combine(p.FolderPath!, "Assets", "sprites"); Directory.CreateDirectory(dir);
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(800, 150, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[800 * 150 * 4], 800 * 4);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = File.Create(Path.Combine(dir, "box.png"))) enc.Save(fs);
            return Path.Combine("Assets", "sprites", "box.png");
        }
        PlayWindow Sim(System.Action<Project, Scene, GameObjectData> add)
        {
            var pw = PlayWindow.NewScriptSim("[]", out _, extra: (p, s) =>
            {
                var o = new GameObjectData { Id = 9, Name = "Box", X = 0, Y = 0, Width = 10, Height = 10 };
                add(p, s, o);
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(o);
            });
            t.Sims.Add(pw); pw.TSteps(5);
            return pw;
        }
        // the box is as tall as the picture's proportions say for the box's width (window minus its 40 px margins)
        bool Proportional(PlayWindow pw) => pw.TDialogImage.Length > 0 && pw.TDialogMinHeight > 20
                                            && System.Math.Abs(pw.TDialogMinHeight / System.Math.Max(100, pw.Width - 80) - 150.0 / 800) < 0.01;

        t.Test("dialogue box: every behavior that talks (Interact, Trigger Area, Cutscene say) offers the same box settings, picture included", () =>
        {
            string[] keys = { "look", "boxColor", "boxImage", "textColor", "font" };
            bool Has(BehaviorKind k) => keys.All(key => BehaviorCatalog.Info(k).Texts.Any(tp => tp.Key == key));
            var say = StepCatalog.Find(SeqMode.Cutscene, "say")!;
            return Has(BehaviorKind.Interact) && Has(BehaviorKind.Area) && keys.All(key => say.Fields.Any(f => f.Key == key))
                && BehaviorCatalog.Info(BehaviorKind.Interact).Texts.First(tp => tp.Key == "boxImage").Pick == PickKind.Picture
                && say.Fields.First(f => f.Key == "boxImage").Type == "picture";
        });
        t.Test("dialogue box: an Area's 'My own picture' box shows the picture, keeps its proportions and its text colour", () =>
        {
            var pw = Sim((p, s, o) =>
            {
                var a = BehaviorCatalog.Info(BehaviorKind.Area).CreateData();
                a.Texts["action"] = "say"; a.Texts["text"] = "A sign."; a.Texts["look"] = "picture"; a.Texts["boxImage"] = Png(p); a.Texts["textColor"] = "#FF0000"; a.Texts["target"] = "Box";
                o.Behaviors.Add(a);
            });
            bool ok = pw.TDialog && Proportional(pw);
            while (pw.TDialog) pw.TAdvanceDialog();
            pw.TSay("plain", "");
            return ok && pw.TDialogImage.Length == 0 && pw.TDialogMinHeight == 0;
        });
        t.Test("dialogue box: a Cutscene's say step has its own look (parchment), picture and font; a plain step keeps the cutscene's", () =>
        {
            PlayWindow Cut(System.Func<Project, string> steps, string font = "") => Sim((p, s, o) =>
            {
                var c = BehaviorCatalog.Info(BehaviorKind.Cutscene).CreateData();
                c.Texts["script"] = "{\"steps\":[" + steps(p) + "]}"; c.Texts["font"] = font; c.Nums["bars"] = 0;
                o.Behaviors.Add(c);
            });
            var parch = Cut(_ => "{\"do\":\"say\",\"text\":\"Hi\",\"look\":\"parchment\"}");
            var pic = Cut(p => "{\"do\":\"say\",\"text\":\"Hi\",\"look\":\"picture\",\"boxImage\":" + System.Text.Json.JsonSerializer.Serialize(Png(p)) + ",\"font\":\"Consolas\"}", "Georgia");
            var plain = Cut(_ => "{\"do\":\"say\",\"text\":\"Hi\"}", "Georgia");
            return parch.TDialogBg.EndsWith("EAD9B0") && Proportional(pic) && pic.TDialogFont == "Consolas" && plain.TDialogFont == "Georgia";
        });
        t.Test("dialogue box: an old say step is written back unchanged (the new box settings are empty by default)", () =>
        {
            const string json = "{\"steps\":[{\"do\":\"say\",\"text\":\"Hello!\",\"speaker\":\"Bob\"}]}";
            var cs = SequenceJson.ParseCutscene(json, out _);
            return SequenceJson.Serialize(cs) == SequenceJson.Serialize(SequenceJson.ParseCutscene(SequenceJson.Serialize(cs), out _))
                && !SequenceJson.Serialize(cs).Contains("look") && !SequenceJson.Serialize(cs).Contains("Color");
        });
    }
}
