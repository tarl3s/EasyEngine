using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // "Built on BuilderEngine" (BuilderLink.cs): an object with BuilderEngine mechanisms is BuilderEngine's to give behaviour.
    // The inspector shows only the fields both engines share (name, layer, lock, position, size, colour, picture) and one
    // read-only card listing its mechanisms, with "Open in BuilderEngine" when BuilderEngine is installed. The AI never
    // gives it behaviors (MainWindow.AiApply.cs) and the ＋ behavior picker skips it. When the game plays, its mechanisms
    // run for real (PlayWindow.BuilderBridge.cs).
    public partial class MainWindow
    {
        /// <summary>Builds the inspector of a Built-on-BuilderEngine object; false for any other object.</summary>
        private bool BuilderInspector(GameObjectData obj)
        {
            if (!BuilderLink.IsBuilt(obj)) return false;
            Section(L.T("OBJECT"));
            LockRow(obj);
            TextRow(L.T("Name"), obj.Name, v => { obj.Name = v; RefreshObjects(); Dirty(); });
            LayerPickRow(obj);

            Section(L.T("TRANSFORM"));
            NumRow(L.T("X"), obj.X, v => { obj.X = v; ReposSelected(); Dirty(); });
            NumRow(L.T("Y"), obj.Y, v => { obj.Y = v; ReposSelected(); Dirty(); });
            NumRow(L.T("Width"), obj.Width, v => SetSize(obj, v, null));
            NumRow(L.T("Height"), obj.Height, v => SetSize(obj, null, v));

            Section(L.T("APPEARANCE"));
            ColorRow(L.T("Color"), obj.Color, v => { obj.Color = v; RenderScene(); Dirty(); });
            SpriteRow(obj);

            Section(L.T("BEHAVIORS"));
            BuilderCard(obj);
            ApplyInspectorLock(obj);
            return true;
        }

        /// <summary>The one card a Built-on-BuilderEngine object has instead of behaviors: read-only.</summary>
        private void BuilderCard(GameObjectData obj)
        {
            var panel = new StackPanel { Tag = "builder-card" };
            panel.Children.Add(new TextBlock { Text = "🧩  " + L.T("Built on BuilderEngine"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(new TextBlock
            {
                Text = L.T("Its behaviour comes from BuilderEngine mechanisms. It plays here too; to change what it does, open it in BuilderEngine."),
                TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 6)
            });
            foreach (var m in BuilderLink.Mechanisms(obj))
                panel.Children.Add(L.Keep(new TextBlock { Text = "•  " + m.Name, FontSize = 12, Margin = new Thickness(4, 0, 0, 2) })); // mechanism names are the person's
            if (BuilderLink.FindBuilderExe() != null)
            {
                var open = new Button { Content = L.T("Open in BuilderEngine"), Style = (Style)FindResource("CardButton"), Height = 28, Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                open.Click += (s, e) =>
                {
                    try { SaveProjectFile(); } catch (Exception ex) { Status(L.F("Error saving: {0}", ex.Message)); return; }
                    if (!BuilderLink.OpenInBuilder(_project)) Status(L.T("BuilderEngine wasn't found on this computer."));
                    else if (!_linkOn) SetLiveLink(true); // both engines have it open now: edits go back and forth
                };
                panel.Children.Add(open);
            }
            InspectorRoot.Children.Add(new Border
            {
                Background = (SolidColorBrush)FindResource("Bg2"),
                BorderBrush = (SolidColorBrush)FindResource("Border"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = panel
            });
        }

        // the objects the AI wanted to change but are BuilderEngine's (listed in its change summary)
        private readonly System.Collections.Generic.List<string> _aiSkippedBuilder = new();

        private void AddBuilderNotes(System.Collections.Generic.List<string> changes)
        {
            foreach (var name in _aiSkippedBuilder)
                changes.Add(L.F("Skipped \"{0}\": it's built on BuilderEngine (change what it does there)", name));
        }

        // ---- test hooks (FullTest.BuilderLink.cs) ----
        internal bool TestHasBuilderCard() => InspectorRoot.Children.OfType<Border>().Any(b => b.Child is FrameworkElement { Tag: "builder-card" });
        internal bool TestHasAddBehavior() => InspectorRoot.Children.OfType<FrameworkElement>().Any(e => e.Tag as string == "add-behavior");
        internal int TestBehaviorCards() => InspectorRoot.Children.OfType<Border>().Count(b => b.Child is StackPanel sp && sp.Tag == null && sp.Children.Count > 0 && sp.Children[0] is Grid);
        internal GameObjectData? TestSelected => _selected;
    }
}
