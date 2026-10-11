using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    // Hotbar (what's in hand), ToolUse (click anywhere to swing it), Harvestable (trees/rocks that take
    // N hits from the right tool and then fall), plus the tool drawn in the player's hand.
    public partial class PlayWindow
    {
        // what the hotbar currently puts in the player's hand (refreshed every logic tick)
        private string _heldItem = "";      // owned + selected item, or "" if the selected slot isn't owned
        private string _heldSlotName = "";  // the selected slot's item name, owned or not
        private string _heldColor = "#CCCCCC";
        private bool _heldOwned;
        private int _heldSlot;
        private Entity? _hotbarOwner, _hotbarPlayer;

        // ================================================================== HOTBAR
        private static List<(string name, string color)> ParseHotbarItems(string spec)
        {
            var list = new List<(string, string)>();
            foreach (var part in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var bits = part.Split(':', 2);
                string name = bits[0].Trim();
                if (name.Length == 0) continue;
                list.Add((name, bits.Length > 1 ? bits[1].Trim() : "#CCCCCC"));
            }
            return list;
        }

        // Slot rectangles in screen space — shared by drawing, click-to-select and the "click on HUD" test.
        private List<Rect> HotbarSlotRects(BehaviorData bh, int count)
        {
            double size = Math.Max(24, bh.Num("size", 46)), gap = 6;
            double W = Root.ActualWidth > 10 ? Root.ActualWidth : 960;
            double H = Root.ActualHeight > 10 ? Root.ActualHeight : 540;
            double x0 = (W - (count * size + (count - 1) * gap)) / 2, y0 = H - size - 16;
            var rects = new List<Rect>(count);
            for (int i = 0; i < count; i++) rects.Add(new Rect(x0 + i * (size + gap), y0, size, size));
            return rects;
        }

        private void UpdateHotbar(Entity en, BehaviorData bh, double dt)
        {
            var items = ParseHotbarItems(bh.Text("items", ""));
            if (items.Count == 0) return;
            var rects = HotbarSlotRects(bh, items.Count);
            for (int i = 0; i < items.Count; i++) _uiRects[$"hotbar slot {i + 1} ({items[i].name})"] = rects[i];

            int sel = (int)Math.Round(en.Locals.TryGetValue("__slot", out var sv) ? sv : bh.Num("slot", 0));
            if (_wheelDelta < 0) sel++;
            else if (_wheelDelta > 0) sel--;
            for (int i = 0; i < items.Count && i < 9; i++)
                if (_keys.Contains(Key.D1 + i) || _keys.Contains(Key.NumPad1 + i)) sel = i;
            if (_mouseClicked) for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(_mouseScreen)) sel = i; // click a slot to pick it
            sel = ((sel % items.Count) + items.Count) % items.Count;
            en.Locals["__slot"] = sel;

            // "holding:<name>" = 1 only for the selected, owned slot (cleared first: duplicate names are fine)
            var (name, color) = items[sel];
            bool owned = Var("item:" + name) >= 1;
            foreach (var it in items) _vars["holding:" + it.name] = 0;
            if (owned) _vars["holding:" + name] = 1;
            _vars["heldSlot"] = sel;

            _heldItem = owned ? name : ""; _heldSlotName = name; _heldOwned = owned; _heldSlot = sel; _heldColor = color;
            _hotbarOwner = en;
            _hotbarPlayer = _entities.FirstOrDefault(z => !z.Dead && z.Data.Name.Equals(bh.Text("player", "Player"), StringComparison.OrdinalIgnoreCase));

            // console: every change of selection (or of ownership of the selected slot)
            var sig = $"{sel}|{owned}";
            if (sig != en.HotbarSig)
            {
                string who = _hotbarPlayer?.Data.Name ?? en.Data.Name;
                if (owned) Info("Hotbar", who, $"In hand: {ToolArt.Title(name)} (slot {sel + 1})");
                else Warn("Hotbar", who, $"Slot {sel + 1} '{name}' selected but NOT owned (item:{name} = {Var("item:" + name):0})",
                          "Nothing is in hand until you get it (buy, craft or pick it up), or pick another slot.");
                en.HotbarSig = sig;
            }

            // redraw only when something visible changed
            var renderSig = $"{sel}|{string.Join(",", items.Select(it => Var("item:" + it.name).ToString("0")))}|{rects[0].X:0},{rects[0].Y:0}";
            if (en.HotbarCanvas == null) { en.HotbarCanvas = new Canvas { IsHitTestVisible = false }; Hud.Children.Add(en.HotbarCanvas); }
            if (renderSig != en.HotbarRenderSig) { RenderHotbar(en.HotbarCanvas, items, sel, rects); en.HotbarRenderSig = renderSig; }
        }

        private void RenderHotbar(Canvas canvas, List<(string name, string color)> items, int sel, List<Rect> rects)
        {
            canvas.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var r = rects[i];
                bool owned = Var("item:" + items[i].name) >= 1;
                var slot = new Border
                {
                    Width = r.Width, Height = r.Height, CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(owned ? (byte)215 : (byte)95, 24, 24, 30)),
                    BorderBrush = i == sel ? Brushes.White : new SolidColorBrush(Color.FromArgb(160, 90, 90, 100)),
                    BorderThickness = new Thickness(i == sel ? 3 : 1.5)
                };
                Canvas.SetLeft(slot, r.X); Canvas.SetTop(slot, r.Y); canvas.Children.Add(slot);

                var icon = ToolArt.Make(items[i].name, items[i].color, r.Width * 0.7, out _);
                icon.Opacity = owned ? 1 : 0.28;
                Canvas.SetLeft(icon, r.X + r.Width * 0.15); Canvas.SetTop(icon, r.Y + r.Height * 0.15); canvas.Children.Add(icon);

                double count = Var("item:" + items[i].name);
                if (count > 1)
                {
                    var cnt = new TextBlock { Text = ((long)count).ToString(), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(cnt, r.X + r.Width - 16); Canvas.SetTop(cnt, r.Y + r.Height - 16); canvas.Children.Add(cnt);
                }
                var key = new TextBlock { Text = (i + 1).ToString(), Foreground = new SolidColorBrush(Color.FromArgb(180, 220, 220, 220)), FontSize = 9 };
                Canvas.SetLeft(key, r.X + 3); Canvas.SetTop(key, r.Y + 2); canvas.Children.Add(key);
            }

            // the selected item's name above the bar — clear evidence of what is in hand
            var selName = items[sel].name;
            bool selOwned = Var("item:" + selName) >= 1;
            var caption = new TextBlock
            {
                Text = selOwned ? ToolArt.Title(selName) : $"{ToolArt.Title(selName)} — not owned",
                Foreground = selOwned ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF4, 0xC0, 0x4E)),
                FontSize = 14, FontWeight = FontWeights.SemiBold
            };
            caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = rects[0].X, right = rects[^1].Right;
            Canvas.SetLeft(caption, (left + right) / 2 - caption.DesiredSize.Width / 2);
            Canvas.SetTop(caption, rects[0].Y - 24);
            canvas.Children.Add(caption);
        }

        // Draws the held tool at the player's hand — in Render, after physics, so it never lags a frame.
        private void RenderHeldTool()
        {
            var owner = _hotbarOwner; var player = _hotbarPlayer;
            if (owner == null || owner.Dead) return;
            if (player == null || player.Dead || _heldItem.Length == 0)
            {
                if (owner.HeldVisual != null) owner.HeldVisual.Visibility = Visibility.Collapsed;
                return;
            }
            if (owner.HeldVisual == null || owner.HeldName != _heldItem)
            {
                if (owner.HeldVisual != null) { World.Children.Remove(owner.HeldVisual); Hud.Children.Remove(owner.HeldVisual); }
                owner.HeldVisual = ToolArt.Make(_heldItem, _heldColor, 30, out owner.HeldGrip);
                owner.HeldName = _heldItem;
                (player.ScreenSpace ? Hud : World).Children.Add(owner.HeldVisual); // added after the player → drawn on top
            }
            owner.HeldVisual.Visibility = Visibility.Visible;

            // angle: resting 35° (raised, forward); a swing sweeps from -70° (behind the head) to +110° (down in front)
            double angle = 35;
            if (player.SwingT > 0 && player.SwingDur > 0)
            {
                double u = 1 - player.SwingT / player.SwingDur;
                angle = -70 + 180 * (1 - Math.Pow(1 - u, 3)); // ease-out
            }
            double hx = player.Facing >= 0 ? player.X + player.W - 6 : player.X + 6;
            double hy = player.Y + player.H * 0.55;
            var g = owner.HeldGrip;
            Canvas.SetLeft(owner.HeldVisual, hx - g.X); Canvas.SetTop(owner.HeldVisual, hy - g.Y);
            var tg = new TransformGroup();
            tg.Children.Add(new RotateTransform(angle, g.X, g.Y));
            tg.Children.Add(new ScaleTransform(player.Facing >= 0 ? 1 : -1, 1, g.X, g.Y)); // mirror around the hand when facing left
            owner.HeldVisual.RenderTransform = tg;
        }

        // ================================================================== TOOL USE
        private void UpdateToolUse(Entity en, BehaviorData bh, double dt)
        {
            if (en.ToolCd > 0) en.ToolCd -= dt;
            if (en.SwingT > 0) en.SwingT = Math.Max(0, en.SwingT - dt);
            if (en.ImpactPending) { en.ImpactIn -= dt; if (en.ImpactIn <= 0) { en.ImpactPending = false; ResolveImpact(en, bh); } }

            if (!_mouseClicked || _dialogOpen) return;
            if (UiHitAt(_mouseScreen) != null) return; // clicks on the HUD (hotbar, checklist, top bar) don't swing

            // what's in hand?
            string tool;
            if (_hotbarOwner != null && !_hotbarOwner.Dead)
            {
                if (!_heldOwned)
                {
                    Warn("Tool", en.Data.Name, $"Can't swing: slot {_heldSlot + 1} '{_heldSlotName}' is not owned (item:{_heldSlotName} = {Var("item:" + _heldSlotName):0})",
                         "Get the item first, or scroll / press a number to pick a slot you own.");
                    PlaySoundFile(bh.Text("deniedSound", ""), 0.6, "sfx", false, en.Data.Name);
                    return;
                }
                tool = _heldItem;
            }
            else tool = bh.Text("tool", "");
            if (tool.Length == 0)
            {
                Warn("Tool", en.Data.Name, "Can't swing: nothing in hand", "Add a Hotbar with the tool, or set Tool Use's 'tool' parameter.");
                return;
            }
            var allowed = bh.Text("tools", "");
            if (allowed.Length > 0 && !allowed.Split(',').Any(t => t.Trim().Equals(tool, StringComparison.OrdinalIgnoreCase)))
                return; // e.g. the sword attacks with Melee (J), not with clicks
            if (en.ToolCd > 0) { LogThrottled("tool-cd:" + en.Data.Name, 1, LogLevel.Info, "Tool", en.Data.Name, "Swing ignored — still recovering from the last one"); return; }

            // swing!
            double swingTime = Math.Max(0.08, bh.Num("swingTime", 0.25));
            en.ToolCd = Math.Max(0.05, bh.Num("cooldown", 0.35));
            en.SwingDur = swingTime; en.SwingT = swingTime;
            en.Facing = _mouseWorld.X >= en.CX ? 1 : -1;
            en.ImpactPending = true; en.ImpactIn = swingTime * 0.5; en.ImpactTool = tool; en.ImpactAt = _mouseWorld;
            _vars["stat:swings"] = Var("stat:swings") + 1;
            PlaySoundFile(bh.Text("swingSound", ""), 0.7, "sfx", false, en.Data.Name);
            Info("Tool", en.Data.Name, $"{ToolArt.Title(tool)} swing", $"aimed at ({_mouseWorld.X:0}, {_mouseWorld.Y:0}) · facing {(en.Facing > 0 ? "right" : "left")}");
        }

        // Half-way through the swing: find what the tool lands on.
        private void ResolveImpact(Entity user, BehaviorData bh)
        {
            double reach = bh.Num("reach", 80);
            string tool = user.ImpactTool;
            Entity? best = null, wrongTool = null, nearestOk = null;
            double bestScore = double.MaxValue, nearestOkGap = double.MaxValue;
            foreach (var t in _entities)
            {
                if (t.Dead || t.Felled || t == user) continue;
                var hb = t.Beh(BehaviorKind.Harvestable); if (hb == null) continue;
                double gap = RectGap(user, t);
                var need = hb.Text("tool", "");
                bool accepts = string.IsNullOrWhiteSpace(need) || need.Equals(tool, StringComparison.OrdinalIgnoreCase);
                if (accepts && gap < nearestOkGap) { nearestOkGap = gap; nearestOk = t; }
                if (gap > reach) continue;
                bool under = user.ImpactAt.X >= t.X && user.ImpactAt.X <= t.X + t.W && user.ImpactAt.Y >= t.Y && user.ImpactAt.Y <= t.Y + t.H;
                // wrong tool: only refuse when you actually aimed at it (mining dirt next to a tree must not complain)
                if (!accepts) { if (under) wrongTool ??= t; continue; }
                // prefer the one under the cursor, then the one on the clicked side, then the nearest
                bool sameSide = Math.Sign(t.CX - user.CX) == Math.Sign(user.ImpactAt.X - user.CX) || Math.Abs(t.CX - user.CX) < 1;
                double score = gap + (under ? -10000 : 0) + (sameSide ? 0 : 1000);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (best != null) { HitHarvestable(best, user, tool); return; }
            if (wrongTool != null)
            {
                var need = wrongTool.Beh(BehaviorKind.Harvestable)!.Text("tool", "");
                _vars["stat:denied"] = Var("stat:denied") + 1;
                PlaySoundFile(bh.Text("deniedSound", ""), 0.6, "sfx", false, user.Data.Name);
                Warn("Tool", user.Data.Name, $"{wrongTool.Data.Name} needs {A(need)} — you're holding {A(tool)}");
                return;
            }
            _vars["stat:missed"] = Var("stat:missed") + 1;
            if (nearestOk != null)
                Info("Tool", user.Data.Name, $"Nothing in reach — nearest {nearestOk.Data.Name} is {nearestOkGap:0}px away (reach {reach:0})", "Walk closer, then click.");
        }

        private static string A(string word) => (word.Length > 0 && "aeiouAEIOU".IndexOf(word[0]) >= 0 ? "an " : "a ") + word;

        // Distance between two boxes (0 when they touch or overlap).
        private static double RectGap(Entity a, Entity b)
        {
            double dx = Math.Max(0, Math.Max(a.X - (b.X + b.W), b.X - (a.X + a.W)));
            double dy = Math.Max(0, Math.Max(a.Y - (b.Y + b.H), b.Y - (a.Y + a.H)));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ================================================================== HARVESTABLE
        private void HitHarvestable(Entity t, Entity user, string tool)
        {
            var hb = t.Beh(BehaviorKind.Harvestable)!;
            int total = Math.Max(1, (int)Math.Round(hb.Num("hits", 4)));
            if (t.HarvestLeft <= 0) t.HarvestLeft = total;
            t.HarvestLeft--;
            int done = total - t.HarvestLeft;
            _vars["stat:hits"] = Var("stat:hits") + 1;
            if (hb.Num("shake", 1) >= 0.5) t.ShakeT = 0.25;

            var drop = hb.Text("drop", "");
            string chips = drop.Equals("stone", StringComparison.OrdinalIgnoreCase) ? "#9A9AA6" : drop.Equals("wood", StringComparison.OrdinalIgnoreCase) ? "#C8A06A" : "#D0D0D0";
            double cx = user.CX < t.CX ? t.X : t.X + t.W;
            EmitParticles(cx, t.Y + t.H * 0.7, 8, 170, 120, user.CX < t.CX ? 200 : -20, 0.45, 4, 500, chips);
            PlaySoundFile(hb.Text("hitSound", ""), 0.85, "sfx", false, t.Data.Name);

            if (t.HarvestLeft > 0) { Info("Harvest", t.Data.Name, $"Hit {done}/{total} with the {tool}"); return; }

            // felled
            t.Felled = true;
            double amount = Math.Max(0, hb.Num("amount", 5));
            if (drop.Length > 0 && amount > 0) _vars["item:" + drop] = Var("item:" + drop) + amount;
            _vars["stat:felled"] = Var("stat:felled") + 1;
            PlaySoundFile(hb.Text("fellSound", ""), 0.9, "sfx", false, t.Data.Name);
            Info("Harvest", t.Data.Name, drop.Length > 0 ? $"FELLED → +{amount:0} {drop}" : "FELLED", $"took {total} hits with the {tool}");
            if (drop.Length > 0) FloatText(t.CX, t.Y + 10, $"+{amount:0} {drop}");
            EmitParticles(t.CX, t.Y + t.H * 0.3, 22, 220, 360, 0, 0.9, 7, 300, drop.Equals("wood", StringComparison.OrdinalIgnoreCase) ? "#3C8C3C" : chips);
            RunDeathRules(t, 0);
            if (hb.Num("fall", 1) >= 0.5) { t.FallT = 0.6; t.FallDir = t.CX >= user.CX ? 1 : -1; }
            else t.Dead = true;
        }

        private void UpdateHarvestable(Entity en, BehaviorData bh, double dt)
        {
            if (en.ShakeT > 0) en.ShakeT = Math.Max(0, en.ShakeT - dt);
            if (en.Felled && en.FallT > 0) { en.FallT -= dt; if (en.FallT <= 0) en.Dead = true; }
        }

        // A short floating text ("+5 wood") built on the particle system.
        private void FloatText(double x, double y, string text, string colorHex = "#FFF2B0")
        {
            var tb = new TextBlock { Text = text, Foreground = Brush(colorHex), FontWeight = FontWeights.Bold, FontSize = 18 };
            tb.Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 1, BlurRadius = 3, Opacity = 0.8 };
            var p = new Particle { X = x - 28, Y = y - 12, VX = 0, VY = -48, Life = 1.3, MaxLife = 1.3, Grav = 0, Size = 0 };
            p.V = new Border { Child = tb, IsHitTestVisible = false };
            World.Children.Add(p.V);
            _particles.Add(p);
        }
    }
}
