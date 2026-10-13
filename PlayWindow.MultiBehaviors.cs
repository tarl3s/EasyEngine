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
                st.Player = PlaySound(snd, en.Data.Name);
                st.Was = true;
            }
        }

        // it is on this frame: one that was off starts playing
        private void SoundOn(Entity en, BehaviorData snd, int i)
        {
            var st = St(en, i);
            if (!st.Rested) return;
            st.Rested = false;
            st.Player = PlaySound(snd, en.Data.Name, own: true);
            st.Was = true;
        }

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
    }
}
