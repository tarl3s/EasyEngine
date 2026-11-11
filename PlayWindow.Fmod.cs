using System;
using System.Collections.Generic;
using BE = BuilderEngine.Script;

namespace EasyEngine
{
    // Optional FMOD audio in a game (GameAudio.cs has the setting and the shared FMOD Core):
    //  - sound files: with FMOD, PlaySoundFile plays them as FMOD channels (PlayWindow.Sound.cs); every channel is kept here
    //    so a scene change / the game closing / a reversed "play a sound" stops them.
    //  - FMOD Studio events (the project's "fmod" folder): one BE.FmodEventHost per game, made the first time something asks
    //    for an event — a Sound behavior set to "An FMOD Studio event", the "fmodevent" / "fmodparam" rule actions, the
    //    cutscene step "Play an FMOD event" and BuilderEngine objects (World.FmodControl).
    // Headless (tests) nothing is played: every request is recorded in TFmod and logged, like sounds.
    public partial class PlayWindow
    {
        private BE.FmodCore? _fmod;                                         // the shared FMOD Core when this game's sound files play through FMOD (null = classic)
        private readonly List<(string abs, BE.FmodChannel ch)> _fmodChannels = new(); // what FMOD plays for this game (file sounds)
        private BE.FmodEventHost? _fmodHost;                                // FMOD Studio events (made on the first request)
        private bool _fmodHostTried;
        private readonly List<string> _fmodLog = new();                     // every FMOD Studio request, in order (tests read it)
        private readonly Dictionary<CustomRule, double> _fmodParamLast = new(); // a rule's last parameter value (sent again only when it changes)
        private Action<string, bool>? _fmodSink;

        /// <summary>The FMOD Studio requests so far ("▶ event:/Music/Forest", "■ event:/…", "◆ event:/… RPM=1500", "◆ global Rain=1").</summary>
        public IReadOnlyList<string> TFmod => _fmodLog;
        /// <summary>Whether this game plays its sound files through FMOD (false in tests and with classic sound).</summary>
        public bool TFmodCore => _fmod != null;

        // ---- the game starts / runs / stops (PlayWindow.xaml.cs: Start, OnFrame, Stop) ----
        private void AudioStart()
        {
            if (_headless) return; // tests never play audio
            _fmodSink = (m, warn) => { if (warn) Warn("FMOD", "", m); else Info("FMOD", "", m); };
            GameAudio.Attach(_fmodSink);
            _fmod = GameAudio.Core;
            if (_fmod != null) Info("Sound", "", $"Sound files play through FMOD {_fmod.VersionText}");
            else Info("Sound", "", "Sound files play with the classic Windows players", GameAudio.Why);
        }

        private void AudioFrame()
        {
            var core = _fmod;
            if (core != null)
            {
                core.Update();
                if (_frameNo % 15 == 0) // forget the channels that ended (a quarter of a second is soon enough)
                    for (int i = _fmodChannels.Count - 1; i >= 0; i--)
                        if (!core.IsPlaying(_fmodChannels[i].ch)) _fmodChannels.RemoveAt(i);
            }
            var host = _fmodHost;
            if (host != null)
            {
                var p = FirstPlayer(); // 3D events are heard from the player (else the middle of the screen)
                if (p != null) host.SetListener(p.CX, p.CY);
                else host.SetListener(-_camX + ViewWidth() / 2, -_camY + (Root.ActualHeight > 10 ? Root.ActualHeight : _project.ScreenHeight) / 2);
                host.Update();
            }
        }

        private void AudioStop()
        {
            if (_fmodHost != null)
            {
                if (GameAudio.LiveHost == _fmodHost) GameAudio.LiveHost = null;
                try { _fmodHost.Dispose(); } catch { }
                _fmodHost = null;
            }
            if (_fmodSink != null) GameAudio.Detach(_fmodSink);
        }

        // ---- sound files through FMOD (PlayWindow.Sound.cs) ----
        private void StopFmodChannels()
        {
            var core = _fmod;
            if (core != null) foreach (var (_, ch) in _fmodChannels) core.Stop(ch);
            _fmodChannels.Clear();
            _fmodParamLast.Clear();
        }

        private void StopFmodFile(string abs)
        {
            var core = _fmod;
            if (core == null) return;
            for (int i = _fmodChannels.Count - 1; i >= 0; i--)
                if (string.Equals(_fmodChannels[i].abs, abs, StringComparison.OrdinalIgnoreCase)) { core.Stop(_fmodChannels[i].ch); _fmodChannels.RemoveAt(i); }
        }

        // ---- FMOD Studio events ----
        private BE.FmodEventHost? FmodEvents()
        {
            if (_headless) return null;
            if (_fmodHostTried) return _fmodHost;
            _fmodHostTried = true;
            if (string.IsNullOrEmpty(_project.FolderPath)) { Warn("FMOD", "", "FMOD Studio events need a saved project (its \"fmod\" folder holds the banks)"); return null; }
            if (_fmodSink == null) { _fmodSink = (m, warn) => { if (warn) Warn("FMOD", "", m); else Info("FMOD", "", m); }; GameAudio.Attach(_fmodSink); }
            _fmodHost = new BE.FmodEventHost(_project.FolderPath);
            if (_fmodHost.Studio == null) Warn("FMOD", "", "FMOD Studio events can't play here", _fmodHost.Status);
            else Info("FMOD", "", "FMOD Studio: " + _fmodHost.Status, GameAudio.BankFolder(_project.FolderPath));
            GameAudio.LiveHost = _fmodHost;
            return _fmodHost;
        }

        private void FmodRecord(string what, string source)
        {
            if (_fmodLog.Count < 4000) _fmodLog.Add(what);
            Info("Sound", source, _headless ? what + " (muted in test)" : what);
        }

        // A Sound behavior set to "An FMOD Studio event" (source = fmod)
        private static bool IsEventSound(BehaviorData snd) => string.Equals(snd.Text("source", "file"), "fmod", StringComparison.OrdinalIgnoreCase);

        // its volume now: a 3D event is placed (FMOD makes it quieter far away and puts it left or right), a 2D one gets "heard
        // only nearby" from the Sound's own math; both × the channel and master volume
        private double EventVolume(BehaviorData snd, BehState st)
            => Math.Clamp((st.Ev3D ? snd.Num("volume", 0.8) : st.Vol) * Mixing(st.ChannelKey ??= ChannelKey(snd.Text("channel", "sfx"))), 0, 4);

        private void StartSoundEvent(Entity en, BehaviorData snd, BehState st)
        {
            var raw = snd.Text("event", "");
            if (string.IsNullOrWhiteSpace(raw)) { LogThrottled("fmod-noevent:" + en.Data.Name, 5, LogLevel.Warn, "Sound", en.Data.Name, "This Sound plays an FMOD Studio event, but none is picked"); return; }
            string path = st.EvPath = BE.FmodStudio.NormalizePath(raw);
            FmodRecord("▶ " + path, en.Data.Name);
            var ps = GameAudio.ParseParams(snd.Text("fmodParams", ""));
            if (_headless) { foreach (var (n, v) in ps) _fmodLog.Add($"◆ {path} {n}={v.ToString(System.Globalization.CultureInfo.InvariantCulture)}"); return; }
            var studio = FmodEvents()?.Studio;
            if (studio == null) return;
            st.Ev3D = studio.FindEvent(path)?.Is3D == true;
            st.EvVol = EventVolume(snd, st);
            var ev = studio.Play(path, st.EvVol, keep: false); // FMOD frees it when it ends; the handle still changes / stops it until then
            st.Ev = ev;
            if (ev.IsNone) return;
            foreach (var (n, v) in ps) studio.SetParameter(ev, n, v);
            if (st.Ev3D) studio.SetPosition(ev, en.CX, en.CY);
        }

        // every frame while it plays: its volume (heard nearby, channel volume) and, for a 3D event, where it is
        private void FollowSoundEvent(Entity en, BehaviorData snd, BehState st)
        {
            if (st.Ev.IsNone) return;
            var studio = _fmodHost?.Studio;
            if (studio == null) return;
            double vol = EventVolume(snd, st);
            if (Math.Abs(vol - st.EvVol) > 0.002) { studio.SetVolume(st.Ev, vol); st.EvVol = vol; }
            if (st.Ev3D) studio.SetPosition(st.Ev, en.CX, en.CY);
        }

        private void StopSoundEvent(Entity en, BehState st)
        {
            if (st.EvPath is { Length: > 0 } path) FmodRecord("■ " + path, en.Data.Name);
            if (!st.Ev.IsNone) _fmodHost?.Studio?.Stop(st.Ev, fade: true); // fades out as designed in FMOD Studio
            st.Ev = default;
        }

        // ---- rule actions (DoCustom): "fmodevent" play / stop, "fmodparam" set a parameter ----
        private void DoFmodRule(Entity en, CustomRule r)
        {
            string raw = r.Str("event").Trim();
            if (r.Do == "fmodevent")
            {
                string act = r.Str("action", "play"), name = r.Str("name").Trim();
                double vol = r.Num("volume", 1);
                bool stop = act.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase);
                string path = raw.Length == 0 ? "" : BE.FmodStudio.NormalizePath(raw);
                if (!stop && path.Length == 0) { LogThrottled("fmod-noevent-rule:" + en.Data.Name, 5, LogLevel.Warn, "Script", en.Data.Name, "Play an FMOD event: no event is picked"); return; }
                FmodRecord(stop ? "■ " + (name.Length > 0 ? name : path.Length > 0 ? path : "every event") : "▶ " + path + (name.Length > 0 ? " as " + name : ""), en.Data.Name);
                var host = FmodEvents();
                if (host == null) return;
                double master = _vars.TryGetValue("vol.master", out var mv) ? mv : 1;
                host.Handle(new BE.FmodRequest(stop ? BE.FmodAction.Stop : BE.FmodAction.Play, path, name, Volume: Math.Clamp(vol * master, 0, 4), X: en.CX, Y: en.CY));
                return;
            }

            // fmodparam: of the event playing with that path (or name) — also every Sound playing it — or, without an event, a global parameter
            string param = r.Str("param").Trim();
            double value = r.HasValue("expr") ? r.Num("expr", 0) : r.Num("value", 0);
            if (param.Length == 0) { LogThrottled("fmod-noparam:" + en.Data.Name, 5, LogLevel.Warn, "Script", en.Data.Name, "Set an FMOD parameter: no parameter is picked"); return; }
            if (_fmodParamLast.TryGetValue(r, out var last) && Math.Abs(last - value) < 1e-9) return; // the same value again: nothing to tell FMOD
            _fmodParamLast[r] = value;
            string target = raw.Length == 0 ? "global" : raw;
            if (_fmodLog.Count < 4000) _fmodLog.Add($"◆ {target} {param}={value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
            LogThrottled("fmod-param:" + target + param, 0.5, LogLevel.Info, "Sound", en.Data.Name, $"◆ {target}: {param} = {value:0.###}" + (_headless ? " (muted in test)" : ""));
            var host2 = FmodEvents();
            var studio = host2?.Studio;
            if (host2 == null || studio == null) return;
            if (raw.Length == 0) { studio.SetGlobalParameter(param, value); return; }
            string key = host2.IsPlaying(raw) ? raw : BE.FmodStudio.NormalizePath(raw);
            if (host2.IsPlaying(key)) host2.Handle(new BE.FmodRequest(BE.FmodAction.SetParameter, Name: key, Parameter: param, Value: value));
            foreach (var e in _entities) // Sounds playing that event
            {
                var states = e.States;
                if (states == null) continue;
                foreach (var st in states)
                    if (st != null && !st.Ev.IsNone && GameAudio.SameEvent(st.EvPath, raw)) studio.SetParameter(st.Ev, param, value);
            }
        }

        // ---- BuilderEngine objects asking for FMOD Studio events (World.FmodControl, PlayWindow.BuilderBridge.cs) ----
        private void BuilderFmod(BE.FmodRequest r)
        {
            string what = r.Action switch
            {
                BE.FmodAction.Play => "▶ " + BE.FmodStudio.NormalizePath(r.Event) + (r.Name.Length > 0 ? " as " + r.Name : ""),
                BE.FmodAction.Stop => "■ " + (r.Name.Length > 0 ? r.Name : r.Event.Length > 0 ? BE.FmodStudio.NormalizePath(r.Event) : "every event"),
                BE.FmodAction.SetParameter or BE.FmodAction.SetGlobal => $"◆ {(r.Action == BE.FmodAction.SetGlobal ? "global" : r.Name.Length > 0 ? r.Name : r.Event)} {r.Parameter}={r.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
                _ => "",
            };
            if (what.Length > 0) FmodRecord(what, "BuilderEngine");
            FmodEvents()?.Handle(r);
        }

        /// <summary>Test hook: a request as a BuilderEngine object would send it (World.FmodControl).</summary>
        public void TBuilderFmod(BE.FmodRequest r) => BuilderFmod(r);
    }
}
