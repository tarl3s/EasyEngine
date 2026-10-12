using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // Terrain Generator "More blocks": one card per block — colour + name, where it grows, the height box (blocks from the
    // ground level, up = +), an optional column box (biomes), how much, solid / platform / see-through, hits and drop —
    // plus "＋ Add a block" with ready-made starts (cloud, deeper ore, sand area…). Saved as the "blocks" JSON list
    // (TerrainBlocks.cs); the person never sees that text.
    public partial class MainWindow
    {
        partial void TerrainBlocksEditor(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            var blocks = TerrainBlockSpec.Parse(bh.Text("blocks", ""));
            void Save() { bh.Texts["blocks"] = TerrainBlockSpec.Serialize(blocks); Dirty(); }

            panel.Children.Add(new TextBlock { Text = L.T("More blocks"), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 8, 0, 4) });
            if (blocks.Count == 0) EmptyHint(panel, L.T("Only grass, dirt, stone and ore — add clouds, a deeper ore, a sand biome…"));

            var places = new List<(string, string)> { ("sky", L.T("In the sky")), ("surface", L.T("On the surface (instead of grass)")),
                ("underground", L.T("Underground (veins)")), ("caves", L.T("Around caves")), ("area", L.T("An area (replaces the ground)")) };
            var amounts = new List<(string, string)> { ("rare", L.T("Rare")), ("normal", L.T("Normal")), ("lots", L.T("A lot")), ("all", L.T("All of it")) };
            var collisions = new List<(string, string)> { ("solid", L.T("Solid")), ("platform", L.T("Platform (stand on it, jump up through it)")), ("none", L.T("See-through (decoration)")) };
            var hits = new List<(string, string)> { ("1", L.T("1 hit")), ("2", L.F("{0} hits", 2)), ("3", L.F("{0} hits", 3)), ("5", L.F("{0} hits", 5)), ("10", L.F("{0} hits", 10)) };

            for (int idx = 0; idx < blocks.Count; idx++)
            {
                var s = blocks[idx];
                var card = new StackPanel();

                // colour · name · ✕
                var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                foreach (var w in new[] { 32.0, -1, 30 }) head.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
                var swatch = SwatchButton(s.Color, "#C8A060", 4, L.T("Colour of this block"), hex => { s.Color = hex; Save(); });
                var name = L.Keep(new TextBox { Text = s.Name, Height = 28, Margin = new Thickness(0, 0, 6, 0), ToolTip = L.T("Name of this block") });
                name.LostFocus += (o, e) => { var n = name.Text.Trim(); if (n.Length > 0 && n != s.Name) { PushUndo(); s.Name = n; Save(); } };
                var del = DeleteButton(obj, L.T("Remove this block"), () => { blocks.Remove(s); Save(); });
                Grid.SetColumn(swatch, 0); Grid.SetColumn(name, 1); Grid.SetColumn(del, 2);
                head.Children.Add(swatch); head.Children.Add(name); head.Children.Add(del);
                card.Children.Add(head);

                PickRow(L.T("Where it grows"), card, places, s.Where, false, null, v => { s.SetPlace(v); Save(); RefreshAfterChoiceAlways(obj); });
                NumRow(L.T("From height"), s.YFrom, v => { s.YFrom = (int)Math.Round(v); Save(); }, card);
                NumRow(L.T("To height"), s.YTo, v => { s.YTo = (int)Math.Round(v); Save(); }, card);
                card.Children.Add(new TextBlock
                {
                    Text = L.T("Heights in blocks from the ground level: up (sky) is +, down is −."), TextWrapping = TextWrapping.Wrap,
                    FontSize = 10.5, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 6)
                });
                CheckRow(L.T("Only between columns"), s.XOn, on => { s.XOn = on; Save(); RefreshAfterChoiceAlways(obj); }, card);
                if (s.XOn)
                {
                    NumRow(L.T("From column"), s.XFrom, v => { s.XFrom = (int)Math.Round(v); Save(); }, card);
                    NumRow(L.T("To column"), s.XTo, v => { s.XTo = (int)Math.Round(v); Save(); }, card);
                    card.Children.Add(new TextBlock
                    {
                        Text = L.T("Columns from the generator: right is +, left is −."), TextWrapping = TextWrapping.Wrap,
                        FontSize = 10.5, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 6)
                    });
                }
                PickRow(L.T("How much"), card, amounts, s.Amount, false, null, v => { s.Amount = v; Save(); });
                PickRow(L.T("Collision"), card, collisions, s.Collide, false, null, v => { s.Collide = v; Save(); });
                PickRow(L.T("Breaks after"), card, hits, s.Hits.ToString(), false, null, v => { s.Hits = int.Parse(v); Save(); });
                PickRow(L.T("Drops"), card, ParamSources.Items(_project).Select(i => (i, ToolArt.Title(i))).ToList(), s.Drop, true,
                        L.T("New item name (e.g. wood, key, shovel):"), v => { s.Drop = v; Save(); }, names: true);

                panel.Children.Add(new Border
                {
                    Background = (SolidColorBrush)FindResource("Bg3"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 6), Child = card
                });
            }

            // "＋ Add a block": ready-made starts the person then adjusts
            var add = new Button { Content = L.T("＋ Add a block"), Height = 28, Margin = new Thickness(0, 2, 0, 4), Style = (Style)FindResource("CardButton") };
            add.Click += (o, e) =>
            {
                var menu = new ContextMenu { PlacementTarget = add };
                void Option(string label, Func<TerrainBlockSpec> make)
                {
                    var item = new MenuItem { Header = label };
                    item.Click += (_, _) => { PushUndo(); blocks.Add(make()); Save(); RefreshAfterChoiceAlways(obj); };
                    menu.Items.Add(item);
                }
                Option(L.T("☁ Clouds in the sky (platforms)"), () => TerrainBlockSpec.For("sky", L.T("Cloud"), "#F2F5FA"));
                Option(L.T("💎 A deeper ore"), () => { var b = TerrainBlockSpec.For("underground", L.T("Crystal"), "#5FD4E8"); b.YFrom = -30; b.YTo = -80; b.Amount = "rare"; b.Hits = 5; b.Drop = "crystal"; return b; });
                Option(L.T("🏜 A sand biome (an area of columns)"), () => { var b = TerrainBlockSpec.For("area", L.T("Sand"), "#E3CF8A"); b.YFrom = 20; b.YTo = -8; b.Amount = "all"; b.Hits = 1; b.Drop = "sand"; return b; });
                Option(L.T("🍄 Moss around caves"), () => TerrainBlockSpec.For("caves", L.T("Moss"), "#4E8B4A"));
                Option(L.T("🌼 Flowers on the surface"), () => { var b = TerrainBlockSpec.For("surface", L.T("Flower grass"), "#9ACD5A"); b.Amount = "normal"; return b; });
                Option(L.T("▫ An empty block (underground)"), () => TerrainBlockSpec.For("underground", L.F("Block {0}", blocks.Count + 1), "#B08C6A"));
                menu.IsOpen = true;
            };
            panel.Children.Add(add);
        }
    }
}
