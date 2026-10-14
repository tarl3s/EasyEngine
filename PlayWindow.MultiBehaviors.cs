using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    // Several behaviors of one kind on one object (BehaviorInfo.Repeatable: two Interacts, a Sound for music and one for a
    // jingle, two guns…). Each behavior keeps its own state here, by its place in the object's list, so they never share a
    // timer, a key edge or an "only once".
    // One key press uses at most ONE Interact (and buys at most ONE Shop item) per object: the first in the list that is on
    // and fires. That is what makes an on/off toggle with a single key — Interact A "only while radioOn < 1" sets radioOn
    // to 1, Interact B "only while radioOn >= 1" sets it to 0; pressing E runs A, and B (now on) waits for the next press.
    // A Sound with "Only while" plays when its formula becomes true and stops when it turns false (or a block turns it off).
    public partial class PlayWindow
    {
        private sealed class BehState
        {
            public bool Was;            // edge memory: key down / inside / (Sound) playing, the last time it ran
            public bool Fired;          // its "only once" latch (Interact, Area, Shop bought) / Particles: the start burst is done
            public bool Rested;         // Sound: off — it plays (again) when it comes back on
            public double Timer;        // Spawner / Shooter / Particles clock
            public int Count;           // Spawner: how many it made
            public MediaPlayer? Player; // Sound: its own player, so it can be stopped
            public double Vol, Pan;     // Sound: its volume and its left/right balance now (heard only nearby / by its position)
            public Entity? Listener;    // Sound: who hears it (its "Heard by" object), looked up again when it is gone
            public string? ChannelKey;  // Sound: the variable of its channel volume ("vol.music"), made once
        }

        private partial class Entity
        {
            public BehState?[]? States;                               // per behavior, by index in Data.Behaviors (made on first use)
            public Key InteractLatch = Key.None, ShopLatch = Key.None; // the key a press already used on this object, while held
        }

        // the state of the i-th behavior of this object (allocated once, the first time it runs)
        private static BehState St(Entity en, int i)
        {
            var a = en.States;
            if (a == null || a.Length <= i)
            {
                var grown = new BehState?[Math.Max(i + 1, en.Data.Behaviors.Count)];
                if (a != null) Array.Copy(a, grown, a.Length);
                en.States = a = grown;
            }
            return a[i] ??= new BehState();
        }

        // a used press is free again once its key is let go
        private void ReleaseLatches(Entity en)
        {
            if (en.InteractLatch != Key.None && !_keys.Contains(en.InteractLatch)) en.InteractLatch = Key.None;
            if (en.ShopLatch != Key.None && !_keys.Contains(en.ShopLatch)) en.ShopLatch = Key.None;
        }

        // ---- Sound: on start, or while its "Only while" is true ----
        private static bool HasCond(BehaviorData bh) => bh.Texts.TryGetValue("cond", out var c) && !string.IsNullOrWhiteSpace(c);

        // the scene starts: every Sound plays on start (an "only while" one waits for its formula instead)
        private void StartSounds(Entity en)
        {
            var list = en.Data.Behaviors;
            for (int i = 0; i < list.Count; i++)
            {
                var snd = list[i];
                if (snd.Kind != BehaviorKind.SoundEmitter) continue;
                var st = St(en, i);
                if (HasCond(snd)) { st.Rested = true; continue; }
                if (snd.Num("onStart", 1) < 0.5) continue;
                PlaceSound(en, snd, st, snap: true);
                st.Player = PlaySound(snd, st.Vol, st.Pan, en.Data.Name);
                st.Was = true;
            }
        }

        // it is on this frame: one that was off starts playing; one that plays follows where it is (if it is placed)
        private void SoundOn(Entity en, BehaviorData snd, int i)
        {
            var st = St(en, i);
            if (!st.Rested)
            {
                if (st.Was && IsSpatial(snd)) FollowSound(en, snd, st);
                return;
            }
            st.Rested = false;
            PlaceSound(en, snd, st, snap: true);
            st.Player = PlaySound(snd, st.Vol, st.Pan, en.Data.Name, own: true);
            st.Was = true;
        }

        // ---- Sound heard only nearby ("range", "radius") and left / right in the headphones ("pan") — SpatialSound.cs ----
        // Off (the default, and every older project): it plays at its volume, centred, as it always did.
        private static bool IsSpatial(BehaviorData snd) => snd.Num("range", 0) >= 0.5 || snd.Num("pan", 0) >= 0.5;
        private const double SoundEase = 0.2; // per frame: a placed sound glides to its new volume / balance (no clicks)

        // its volume and balance for where it is now; snap = set at once (it starts now), else glide toward them
        private void PlaceSound(Entity en, BehaviorData snd, BehState st, bool snap)
        {
            double volume = snd.Num("volume", 0.8);
            if (!IsSpatial(snd)) { st.Vol = volume; st.Pan = 0; return; }
            ListenerAt(snd, st, out double lx, out double ly);
            double sx = en.ScreenSpace ? en.CX - _camX : en.CX, sy = en.ScreenSpace ? en.CY - _camY : en.CY; // a UI object is where it shows
            double radius = snd.Num("range", 0) >= 0.5 ? Math.Max(1, snd.Num("radius", 600)) : 0;
            var (near, bal) = SpatialSound.Mix(sx - lx, sy - ly, radius, snd.Num("pan", 0) >= 0.5, ViewWidth() / 2);
            double vol = volume * near;
            if (snap) { st.Vol = vol; st.Pan = bal; }
            else { st.Vol += (vol - st.Vol) * SoundEase; st.Pan += (bal - st.Pan) * SoundEase; }
        }

        // every frame while a placed sound plays: its own player follows (a one-shot from the pool was set once when it started)
        private void FollowSound(Entity en, BehaviorData snd, BehState st)
        {
            PlaceSound(en, snd, st, snap: false);
            var mp = st.Player;
            if (mp == null) return;
            double vol = Math.Clamp(st.Vol * Mixing(st.ChannelKey ??= ChannelKey(snd.Text("channel", "sfx"))), 0, 1);
            double bal = Math.Clamp(st.Pan, -1, 1);
            try
            {
                if (Math.Abs(mp.Volume - vol) > 0.002) mp.Volume = vol;
                if (Math.Abs(mp.Balance - bal) > 0.002) mp.Balance = bal;
            }
            catch { }
        }

        // who hears it: its "Heard by" object (the player by default), else the middle of the screen
        private void ListenerAt(BehaviorData snd, BehState st, out double x, out double y)
        {
            var l = st.Listener;
            if (l == null || l.Dead)
            {
                l = st.Listener = null;
                if (st.Count-- <= 0)
                {
                    string name = snd.Text("listener", "Player").Trim();
                    l = st.Listener = name.Length == 0 ? null : FindNamed(name) ?? (name.Equals("Player", StringComparison.OrdinalIgnoreCase) ? FirstPlayer() : null);
                    if (l == null) st.Count = 30; // not there (yet): look again in half a second
                }
            }
            if (l != null) { x = l.ScreenSpace ? l.CX - _camX : l.CX; y = l.ScreenSpace ? l.CY - _camY : l.CY; return; }
            x = -_camX + ViewWidth() / 2;
            y = -_camY + (Root.ActualHeight > 10 ? Root.ActualHeight : _project.ScreenHeight) / 2;
        }

        private Entity? FirstPlayer()
        {
            foreach (var e in _entities)
                if (!e.Dead && string.Equals(e.Data.Type, "Player", StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        private double ViewWidth() => Root.ActualWidth > 10 ? Root.ActualWidth : _project.ScreenWidth;

        // it is off this frame ("only while" false, or a block turned it off): stop it
        private void SoundRest(Entity en, BehaviorData snd, int i)
        {
            var st = St(en, i);
            if (st.Rested) return;
            st.Rested = true;
            if (!st.Was) return;
            st.Was = false;
            if (st.Player != null)
            {
                try { st.Player.Stop(); st.Player.Close(); } catch { }
                _players.Remove(st.Player);
                st.Player = null;
            }
            Info("Sound", en.Data.Name, $"■ {Path.GetFileName(snd.Text("asset", ""))} stopped");
        }

        // ---- test hooks ----
        /// <summary>Whether the n-th Sound (0 = first) of that object is playing now.</summary>
        public bool TSoundPlaying(string name, int nth = 0)
        {
            var en = TE(name);
            if (en == null) return false;
            for (int i = 0, k = 0; i < en.Data.Behaviors.Count; i++)
                if (en.Data.Behaviors[i].Kind == BehaviorKind.SoundEmitter && k++ == nth) return St(en, i).Was;
            return false;
        }

        /// <summary>The volume (the Sound's volume × how near it is; channel and master not included) and the left/right
        /// balance (−1 … +1) the n-th Sound of that object plays at now — computed in tests too, where nothing is heard.</summary>
        /// <summary>The width of the game screen a Sound without a radius scales its left/right by (twice).</summary>
        public double TViewWidth() => ViewWidth();

        public (double volume, double balance) TSoundMix(string name, int nth = 0)
        {
            var en = TE(name);
            if (en == null) return (double.NaN, double.NaN);
            for (int i = 0, k = 0; i < en.Data.Behaviors.Count; i++)
                if (en.Data.Behaviors[i].Kind == BehaviorKind.SoundEmitter && k++ == nth) { var st = St(en, i); return (st.Vol, st.Pan); }
            return (double.NaN, double.NaN);
        }
    }
}
