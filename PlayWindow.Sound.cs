using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace EasyEngine
{
    // Sound playback for play mode. One-shot effects come from a small preloaded MediaPlayer pool per
    // file (a fresh MediaPlayer takes 100–250 ms to open, which would make a hit sound late). Looping
    // sounds (music) get their own player. In headless tests nothing is played — every sound is only
    // recorded and logged, so E2E tests can assert which sounds fired and in what order.
    public partial class PlayWindow
    {
        private bool _headless;
        private readonly Dictionary<string, List<MediaPlayer>> _soundPool = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<MediaPlayer> _busy = new();
        private readonly List<string> _soundsPlayed = new();
        private const int MaxPerSound = 4;

        /// <summary>Play a project sound (a relative path like Assets\sounds\chop.wav). A looping one, or one asked for with
        /// <paramref name="own"/>, gets its own player, returned so it can be stopped (null when nothing plays live).</summary>
        private MediaPlayer? PlaySoundFile(string rel, double volume = 0.8, string channel = "sfx", bool loop = false, string source = "", bool own = false)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            string name = Path.GetFileName(rel);
            var abs = ProjectIO.AbsoluteAsset(_project, rel);
            if (abs == null) { LogThrottled("snd-missing:" + rel, 2, LogLevel.Warn, "Sound", source, $"Sound file not found: {rel}"); return null; }

            // mixing: sound volume × channel volume × master volume (missing vars count as 1)
            double chVol = _vars.TryGetValue("vol." + channel.ToLowerInvariant(), out var cv) ? cv : 1;
            double master = _vars.TryGetValue("vol.master", out var mv) ? mv : 1;
            double vol = Math.Clamp(volume * chVol * master, 0, 1);
            _soundsPlayed.Add(name);

            if (_headless) { Info("Sound", source, $"♪ {name} (muted in test)", $"volume {vol:0.##} · channel {channel}"); return null; }
            Info("Sound", source, $"♪ {name}", $"volume {vol:0.##} · channel {channel}");

            try
            {
                if (loop || own)
                {
                    var music = NewPlayer(abs, pooled: false);
                    music.Volume = vol;
                    if (loop) music.MediaEnded += (s, e) => { music.Position = TimeSpan.Zero; music.Play(); };
                    music.Play();
                    _players.Add(music);
                    return music;
                }
                var mp = GetPooledPlayer(abs);
                mp.Volume = vol;
                mp.Position = TimeSpan.Zero;
                mp.Play();
            }
            catch (Exception ex) { Error("Sound", source, $"Couldn't play {name}", ex.Message); }
            return null;
        }

        // An idle player for this file, a new one (up to MaxPerSound), or — all busy — restart the oldest.
        private MediaPlayer GetPooledPlayer(string abs)
        {
            if (!_soundPool.TryGetValue(abs, out var pool)) { pool = new List<MediaPlayer>(); _soundPool[abs] = pool; }
            foreach (var p in pool) if (!_busy.Contains(p)) { _busy.Add(p); return p; }
            if (pool.Count < MaxPerSound) { var p = NewPlayer(abs, pooled: true); pool.Add(p); _busy.Add(p); return p; }
            var oldest = pool[0]; pool.RemoveAt(0); pool.Add(oldest);
            return oldest;
        }

        private MediaPlayer NewPlayer(string abs, bool pooled)
        {
            var mp = new MediaPlayer();
            if (pooled) mp.MediaEnded += (s, e) => _busy.Remove(mp);
            mp.MediaFailed += (s, e) =>
            {
                _busy.Remove(mp);
                Error("Sound", "", $"Couldn't play {Path.GetFileName(abs)}", e.ErrorException?.Message ?? "MediaPlayer failed (is Windows Media Player available?)");
            };
            mp.Open(new Uri(abs));
            return mp;
        }

        // Open one player for every sound the scene references, so the first hit plays without delay.
        private void PreloadSounds()
        {
            if (_headless) return;
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var en in _entities)
            {
                foreach (var b in en.Data.Behaviors)
                {
                    foreach (var kv in b.Texts)
                        if ((kv.Key.EndsWith("Sound", StringComparison.OrdinalIgnoreCase) || (b.Kind == BehaviorKind.SoundEmitter && kv.Key == "asset")) && !string.IsNullOrWhiteSpace(kv.Value))
                            refs.Add(kv.Value);
                }
                if (en.Custom != null)
                    foreach (var r in en.Custom)
                        if (r.Do == "playsound" && r.T.TryGetValue("sound", out var s) && !string.IsNullOrWhiteSpace(s)) refs.Add(s);
            }
            foreach (var rel in refs)
            {
                var abs = ProjectIO.AbsoluteAsset(_project, rel);
                if (abs == null || _soundPool.ContainsKey(abs)) continue;
                try { _soundPool[abs] = new List<MediaPlayer> { NewPlayer(abs, pooled: true) }; } catch { }
            }
        }

        private void StopAllSounds()
        {
            foreach (var mp in _players) { try { mp.Stop(); mp.Close(); } catch { } }
            _players.Clear();
            foreach (var pool in _soundPool.Values) foreach (var mp in pool) { try { mp.Stop(); mp.Close(); } catch { } }
            _soundPool.Clear();
            _busy.Clear();
        }
    }
}
