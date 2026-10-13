using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace EasyEngine
{
    // Several behaviors of one kind on one object (BehaviorInfo.Repeatable): "＋ Add behavior…" offers a repeatable kind
    // again (the others only while the object doesn't have them), every card is its own (Remove takes that card only), and
    // the cards of a repeated kind are numbered in their header — "💬 Interact (press key) 1", "… 2".
    public partial class MainWindow
    {
        // " 2" after the card's name when the object has more than one behavior of that kind (its place among them)
        private static string CardNumber(GameObjectData obj, BehaviorData bh)
        {
            int n = 0, mine = 0;
            foreach (var b in obj.Behaviors)
                if (b.Kind == bh.Kind) { n++; if (ReferenceEquals(b, bh)) mine = n; }
            return n > 1 && mine > 0 ? " " + mine : "";
        }

        // ---- test hooks ----
        private ComboBox? AddBehaviorCombo() => InspectorRoot.Children.OfType<ComboBox>().LastOrDefault();

        /// <summary>The kinds "＋ Add behavior…" offers for the inspected object.</summary>
        internal List<BehaviorKind> TestAddableKinds() =>
            AddBehaviorCombo()?.Items.OfType<ComboBoxItem>().Select(i => i.Tag).OfType<BehaviorKind>().ToList() ?? new List<BehaviorKind>();

        /// <summary>Picks a kind in "＋ Add behavior…" as a click would; false when it isn't offered.</summary>
        internal bool TestAddBehavior(BehaviorKind kind)
        {
            var combo = AddBehaviorCombo();
            var item = combo?.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is BehaviorKind k && k == kind);
            if (combo == null || item == null) return false;
            combo.SelectedItem = item;
            return true;
        }

        // the behavior cards' headers (title, ?, Remove), in order
        private List<Grid> CardHeaders() => InspectorRoot.Children.OfType<Border>().Select(b => b.Child as StackPanel).OfType<StackPanel>()
            .Select(p => p.Children.OfType<Grid>().FirstOrDefault()).OfType<Grid>()
            .Where(g => g.Children.OfType<Button>().Any(b => b.Content as string == L.T("Remove"))).ToList();

        /// <summary>The behavior cards' titles, in order.</summary>
        internal List<string> TestCardTitles() => CardHeaders().Select(g => g.Children.OfType<TextBlock>().FirstOrDefault()?.Text).OfType<string>().ToList();

        /// <summary>Clicks Remove on the n-th behavior card (0 = first).</summary>
        internal bool TestRemoveCard(int n)
        {
            var cards = CardHeaders();
            if (n >= cards.Count) return false;
            cards[n].Children.OfType<Button>().First(b => b.Content as string == L.T("Remove")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return true;
        }
    }
}
