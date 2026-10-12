using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    // Steps that run one after another (cutscenes and boss attacks share this runner): a step list, the index of the
    // next step and what the current step waits for (a moment of _gameTime, or a condition such as "the dialogue is
    // closed"). Every runner is ticked in StepLogic, so headless tests run exactly what live play runs. The engine
    // pieces the steps drive live here too: input lock, camera pan, screen shake, fade, cinema bars, big title,
    // moving objects. Their state is logic state; Render only draws it (SequencesRender).
    public partial class PlayWindow
    {
        private sealed class SeqRunner
        {
            public Entity Owner = null!;
            public SeqMode Mode;
            public string Label = "";               // for the console: the cutscene's object, "attack 'Charge'", "death"…
            public List<SeqStep> Steps = new();
            public int Index;                        // next step to run
            public double WaitUntil = -1;            // a _gameTime moment, or -1
            public Func<bool>? Until;                // checked every tick (it may also drive a motion); true = done
            public Action? OnStop;                   // undoes the current blocking step when it ends or is cut short
            public Action<SeqRunner>? OnFinish;
            public bool Finished;
            public Key? SkipKey; public bool SkipWas; // cutscenes: the key that skips to the end
            public bool BarsUsed, WentToScene;
        }

        private sealed class Mover { public SeqRunner? Owner; public Entity E = null!; public double FromX, FromY, ToX, ToY, Start, Dur; public bool Done; }
        private sealed class CamPan { public SeqRunner? Owner; public Entity? Target; public double PX, PY, FromX, FromY, Start, Dur; public bool Returning; }

        // per-object state of the Cutscene behavior
        private sealed class CutState { public bool Was; public SeqRunner? Runner; public bool Warned; }
        private partial class Entity { public CutState? Cut; }

        private readonly List<SeqRunner> _runners = new();
        private readonly List<Mover> _moves = new();
        private readonly HashSet<SeqRunner> _inputLocks = new();
        private HashSet<Key>? _heldKeys;                 // the real keys while a locked tick runs with an empty set
        private readonly HashSet<Key> _noKeys = new();
        private CamPan? _camPan;
        private double _shakeStrength, _shakeDur, _shakeUntil;
        private string _fadeColor = "#000000";
        private double _fadeFrom, _fadeTo, _fadeStart, _fadeDur;
        private bool _barsOn; private double _barsT;      // cinema bars: wanted + how far in (0..1)
        private string _titleText = ""; private double _titleStart, _titleDur;

        // ================================================================== hooks
        partial void SequencesBeforeLogic(double dt)
        {
            if (_heldKeys != null) { _keys = _heldKeys; _heldKeys = null; } // a tick that threw never handed them back

            // skip keys read the real keyboard (a locked cutscene can still be skipped)
            foreach (var r in _runners.ToList())
            {
                if (r.Finished || r.SkipKey is not Key k) continue;
                bool down = _keys.Contains(k);
                if (down && !r.SkipWas) SkipCutscene(r);
                r.SkipWas = down;
            }

            _barsT = Math.Clamp(_barsT + (_barsOn ? dt : -dt) / 0.4, 0, 1);
            if (_camPan is { Returning: true } && PanProgress() >= 1) _camPan = null;

            foreach (var r in _runners.ToList()) TickRunner(r);
            _runners.RemoveAll(r => r.Finished);

            if (_inputLocks.Count > 0) // locked: this tick sees no keys, clicks or wheel (Enter still advances dialogue)
            {
                _heldKeys = _keys; _noKeys.Clear(); _keys = _noKeys;
                _mouseClicked = _rmbClicked = false; _wheelDelta = 0;
            }
        }

        partial void SequencesAfterLogic(double dt)
        {
            if (_heldKeys != null) { _keys = _heldKeys; _heldKeys = null; }
            // objects being moved by a step: placed after physics, so gravity/collision can't drag them off the line
            foreach (var m in _moves)
            {
                double u = m.Dur <= 0 ? 1 : Math.Clamp((_gameTime - m.Start) / m.Dur, 0, 1);
                double k = EaseT("inout", u);
                m.E.X = m.FromX + (m.ToX - m.FromX) * k; m.E.Y = m.FromY + (m.ToY - m.FromY) * k;
                m.E.VX = 0; m.E.VY = 0;
                if (u >= 1) m.Done = true;
            }
            _moves.RemoveAll(m => m.Done);
        }

        partial void SequencesSceneReset()
        {
            foreach (var r in _runners) r.Finished = true;
            _runners.Clear(); _moves.Clear(); _inputLocks.Clear();
            if (_heldKeys != null) { _keys = _heldKeys; _heldKeys = null; }
            _camPan = null; _shakeUntil = 0; _titleText = ""; _barsOn = false; _barsT = 0;
            // a fade-out that led here clears on its own in the new scene (its cutscene may fade in itself)
            double fade = FadeOpacity();
            if (fade > 0) StartFade(_fadeColor, 0, 0.6, fade);
            BossSceneReset();
        }

        partial void SequencesEntityRemoved(Entity en)
        {
            foreach (var r in _runners.Where(r => r.Owner == en && r.Mode == SeqMode.Boss).ToList()) AbortRunner(r);
            _moves.RemoveAll(m => m.E == en);
        }

        partial void CameraTargetOverride(ref double tx, ref double ty, ref double smooth)
        {
            if (_camPan == null) return;
            double goalX = tx, goalY = ty; // returning: back to where the follow camera wants to be
            if (!_camPan.Returning)
            {
                var t = _camPan.Target;
                double cx = t != null && !t.Dead ? t.CX : _camPan.PX, cy = t != null && !t.Dead ? t.CY : _camPan.PY;
                goalX = -(cx - Root.ActualWidth / 2); goalY = -(cy - Root.ActualHeight / 2);
            }
            double k = EaseT("inout", PanProgress());
            tx = _camPan.FromX + (goalX - _camPan.FromX) * k;
            ty = _camPan.FromY + (goalY - _camPan.FromY) * k;
            smooth = 1;
        }

        partial void CameraShakeOffset(ref double ox, ref double oy)
        {
            if (_gameTime >= _shakeUntil || _shakeDur <= 0) return;
            double amp = _shakeStrength * Math.Clamp((_shakeUntil - _gameTime) / _shakeDur, 0, 1);
            ox = (_rng.NextDouble() * 2 - 1) * amp; oy = (_rng.NextDouble() * 2 - 1) * amp;
        }

        partial void SequencesRender(double dt)
        {
            FadeRect.Fill = Brush(_fadeColor);
            FadeRect.Opacity = FadeOpacity();
            double bar = Math.Max(0, Root.ActualHeight) * 0.11 * EaseT("inout", _barsT);
            BarTop.Height = bar; BarBottom.Height = bar;
            BigTitle.Text = _titleText;
            BigTitle.Opacity = TitleAlpha();
            RenderBosses();
        }

        // ================================================================== the Cutscene behavior
        partial void UpdateCutscene(Entity en, BehaviorData bh, double dt)
        {
            var st = en.Cut ??= new CutState();
            bool fire;
            switch (bh.Text("trigger", "start").ToLowerInvariant())
            {
                case "enter":
                {
                    var who = FindNamed(bh.Text("who", "Player"));
                    bool inside = who != null && who != en && Overlap(en, who);
                    fire = inside && !st.Was; st.Was = inside;
                    break;
                }
                case "var":
                {
                    var v = bh.Text("var", "");
                    bool on = v.Length > 0 && Var(v) >= 0.5;
                    fire = on && !st.Was; st.Was = on;
                    break;
                }
                case "key":
                {
                    bool down = _keys.Contains(ParseKey(bh.Text("key", "E")));
                    fire = down && !st.Was; st.Was = down;
                    break;
                }
                default: fire = !st.Was; st.Was = true; break; // the scene starts
            }
            if (!fire || st.Runner is { Finished: false }) return;

            string memory = "cut:" + en.Data.Name;
            bool once = bh.Num("once", 1) >= 0.5;
            if (once && Var(memory) >= 1) return;
            var notes = new List<string>();
            var script = SequenceJson.ParseCutscene(bh.Text("script", "{\"steps\":[]}"), out var err, notes);
            if (err != null || notes.Count > 0)
            {
                if (!st.Warned) Warn("Cutscene", en.Data.Name, err != null ? "Its steps can't be read — " + err : $"{notes.Count} step(s) ignored", string.Join("\n", notes));
                st.Warned = true;
                if (err != null) return;
            }
            if (once) _vars[memory] = 1;

            var skip = bh.Text("skipKey", "Escape").Trim();
            Key? skipKey = skip.Length > 0 && Enum.TryParse<Key>(skip, true, out var sk) ? sk : null;
            var r = new SeqRunner { Owner = en, Mode = SeqMode.Cutscene, Label = en.Data.Name, Steps = script.Steps, SkipKey = skipKey, SkipWas = skipKey is Key k0 && _keys.Contains(k0) };
            if (bh.Num("lock", 1) >= 0.5) _inputLocks.Add(r);
            if (bh.Num("bars", 1) >= 0.5) { _barsOn = true; r.BarsUsed = true; }
            r.OnFinish = _ => Info("Cutscene", en.Data.Name, "Finished");
            st.Runner = r;
            _runners.Add(r);
            Info("Cutscene", en.Data.Name, $"Started — {script.Steps.Count} steps", skipKey != null ? $"skip with {skipKey}" : "can't be skipped");
        }

        /// <summary>Esc closes the game window — unless a cutscene that Esc skips is playing.</summary>
        private bool CutsceneTakesEscape => _runners.Any(r => !r.Finished && r.SkipKey == Key.Escape);

        // ================================================================== the runner
        private SeqRunner StartRunner(Entity owner, SeqMode mode, string label, List<SeqStep> steps, Action<SeqRunner>? onFinish)
        {
            var r = new SeqRunner { Owner = owner, Mode = mode, Label = label, Steps = steps, OnFinish = onFinish };
            _runners.Add(r);
            return r;
        }

        private void TickRunner(SeqRunner r)
        {
            if (r.Mode == SeqMode.Boss && r.Owner.Dead) { AbortRunner(r); return; } // its boss is gone
            for (int guard = 0; guard < 200 && !r.Finished; guard++) // instant steps run back to back until one waits
            {
                if (r.Until != null) { if (!r.Until()) return; EndWait(r); }
                if (r.WaitUntil >= 0) { if (_gameTime < r.WaitUntil) return; r.WaitUntil = -1; }
                if (r.Index >= r.Steps.Count) { FinishRunner(r); return; }
                var step = r.Steps[r.Index++];
                LogStep(r, step);
                RunStep(r, step);
            }
        }

        private static void EndWait(SeqRunner r) { r.Until = null; var stop = r.OnStop; r.OnStop = null; stop?.Invoke(); }

        // The end of a runner: give back everything it took (keys, bars, camera, a fade it left on).
        private void FinishRunner(SeqRunner r)
        {
            if (r.Finished) return;
            EndWait(r);
            r.Finished = true;
            ReleaseRunner(r);
            if (r.Mode == SeqMode.Cutscene && !r.WentToScene && FadeOpacity() > 0 && _fadeTo > 0) StartFade(_fadeColor, 0, 0.5);
            r.OnFinish?.Invoke(r);
        }

        // Stop a runner where it is (a boss changing phase, dying, or removed) — no OnFinish.
        private void AbortRunner(SeqRunner r)
        {
            if (r.Finished) return;
            EndWait(r);
            r.Finished = true;
            _moves.RemoveAll(m => m.Owner == r);
            ReleaseRunner(r);
        }

        private void ReleaseRunner(SeqRunner r)
        {
            _inputLocks.Remove(r);
            if (r.BarsUsed) _barsOn = false;
            if (_camPan != null && _camPan.Owner == r && !_camPan.Returning) StartPan(null, null, 0, 0, 0.6, returning: true);
        }

        // Skip: jump to the end, applying what the remaining steps would have changed for good.
        private void SkipCutscene(SeqRunner r)
        {
            EndWait(r);
            foreach (var m in _moves.Where(m => m.Owner == r)) { m.E.X = m.ToX; m.E.Y = m.ToY; m.E.VX = m.E.VY = 0; }
            _moves.RemoveAll(m => m.Owner == r);
            for (; r.Index < r.Steps.Count; r.Index++)
            {
                var s = r.Steps[r.Index];
                switch (s.Do)
                {
                    case "setvar": case "addvar": case "settime": case "show": case "hide": case "destroy": case "spawn": case "goscene":
                        RunStep(r, s); break;
                    case "move":
                        if (SeqFind(r, s.Str("object")) is Entity e) { var (x, y) = MoveGoal(r, s, e, "to"); e.X = x; e.Y = y; e.VX = e.VY = 0; }
                        break;
                }
            }
            _dialog.Clear(); if (_dialogOpen) CloseDialog();
            _titleText = ""; _camPan = null;
            if (!r.WentToScene) { _fadeFrom = _fadeTo = 0; _fadeDur = 0; }
            Info("Cutscene", r.Label, "Skipped", "the rest of its changes were applied");
            FinishRunner(r);
        }

        private void LogStep(SeqRunner r, SeqStep s)
        {
            string line = $"{r.Label} · step {r.Index}/{r.Steps.Count}: {StepText(s)}";
            if (r.Mode == SeqMode.Cutscene) Info("Cutscene", r.Owner.Data.Name, line);
            else LogThrottled($"boss-step:{r.Owner.Data.Name}:{r.Label}:{r.Index}", 6, LogLevel.Info, "Boss", r.Owner.Data.Name, line); // attacks loop: rate-limited
        }

        private static string StepText(SeqStep s)
        {
            var parts = s.P.Where(kv => kv.Value.Length > 0).Select(kv => $"{kv.Key}={(kv.Value.Length > 28 ? kv.Value[..28] + "…" : kv.Value)}");
            return (s.Do + " " + string.Join(" ", parts)).Trim();
        }

        private void RunStep(SeqRunner r, SeqStep s)
        {
            var self = r.Owner;
            switch (s.Do)
            {
                case "wait": r.WaitUntil = _gameTime + Math.Max(0, s.Num("time", 1)); break;
                case "say":
                    ShowDialog(s.Str("text"), s.Str("speaker"));
                    r.Until = () => !_dialogOpen && _dialog.Count == 0;
                    break;
                case "title":
                    _titleText = Substitute(s.Str("text")); _titleStart = _gameTime; _titleDur = Math.Max(0.3, s.Num("time", 2));
                    if (s.On("wait", true)) r.WaitUntil = _gameTime + _titleDur;
                    break;
                case "fadeout":
                    StartFade(s.Str("color", "#000000"), 1, s.Num("time", 1));
                    r.WaitUntil = _gameTime + Math.Max(0, s.Num("time", 1));
                    break;
                case "fadein":
                    StartFade(_fadeColor, 0, s.Num("time", 1));
                    r.WaitUntil = _gameTime + Math.Max(0, s.Num("time", 1));
                    break;
                case "bars":
                    _barsOn = s.On("on", true);
                    if (_barsOn && r.Mode == SeqMode.Cutscene) r.BarsUsed = true;
                    break;
                case "camera":
                {
                    var t = SeqFind(r, s.Str("object"));
                    StartPan(r, t, s.Num("x"), s.Num("y"), s.Num("time", 1), returning: false);
                    r.WaitUntil = _gameTime + Math.Max(0, s.Num("time", 1));
                    break;
                }
                case "cameraback":
                    if (_camPan == null) break;
                    StartPan(null, null, 0, 0, s.Num("time", 1), returning: true);
                    r.WaitUntil = _gameTime + Math.Max(0, s.Num("time", 1));
                    break;
                case "shake": Shake(s.Num("strength", 8), s.Num("time", 0.5)); break;
                case "move":
                {
                    var e = SeqFind(r, s.Str("object", "player"));
                    if (e == null) { StepWarn(r, s, $"no object '{s.Str("object")}'"); break; }
                    var m = MoveTo(r, e, MoveGoal(r, s, e, "to"), s.Num("time", 1));
                    if (s.On("wait", true)) r.Until = () => m.Done;
                    break;
                }
                case "face":
                {
                    var e = SeqFind(r, s.Str("object", "self"));
                    if (e == null) break;
                    var toward = s.Str("toward", "player").ToLowerInvariant();
                    if (toward == "left") e.Facing = -1;
                    else if (toward == "right") e.Facing = 1;
                    else if (SeqFind(r, toward) is Entity t && Math.Abs(t.CX - e.CX) > 1) e.Facing = t.CX > e.CX ? 1 : -1;
                    break;
                }
                case "show":
                case "hide":
                {
                    var e = SeqFind(r, s.Str("object", "self"));
                    if (e == null) { StepWarn(r, s, $"no object '{s.Str("object")}'"); break; }
                    e.Visual.Visibility = s.Do == "show" ? Visibility.Visible : Visibility.Collapsed;
                    if (s.Do == "show" && e.Visual.Opacity < 0.05) e.Visual.Opacity = 1;
                    break;
                }
                case "lock": _inputLocks.Add(r); break;
                case "unlock": _inputLocks.Remove(r); break;
                // instant steps the Custom rules already know: the same code runs them
                case "setvar": DoCustom(self, SeqRule(s, "setvar", "var", "to"), 0); break;
                case "addvar": DoCustom(self, SeqRule(s, "addvar", "var", "amount"), 0); break;
                case "playsound": DoCustom(self, SeqRule(s, "playsound", "sound", "volume"), 0); break;
                case "settime": DoCustom(self, SeqRule(s, "settime", "time"), 0); break;
                case "goscene": r.WentToScene = true; DoCustom(self, SeqRule(s, "goscene", "scene"), 0); break;
                case "emit":
                    if (SeqFind(r, s.Str("at", "self")) is Entity at) DoCustom(at, SeqRule(s, "emit", "count", "color"), 0);
                    break;
                case "destroy":
                    if (SeqFind(r, s.Str("object", "self")) is Entity victim) DoCustom(victim, SeqRule(s, "destroy"), 0);
                    break;
                case "spawn":
                {
                    var name = s.Str("object");
                    if (name.Length == 0) break;
                    var where = s.Str("at", "self");
                    bool made;
                    if (where.Length == 0) made = SpawnAt(name, s.Num("x"), s.Num("y")) != null;
                    else
                    {
                        var at2 = SeqFind(r, where);
                        int before = _entities.Count;
                        if (at2 != null) DoCustom(at2, SeqRule(s, "spawn", "object"), 0);
                        made = _entities.Count > before;
                    }
                    if (!made) StepWarn(r, s, $"nothing named '{name}' to create (an object in this scene or a prefab)");
                    break;
                }
                default:
                    if (!RunBossStep(r, s)) StepWarn(r, s, $"unknown step '{s.Do}'");
                    break;
            }
        }

        private void StepWarn(SeqRunner r, SeqStep s, string why)
            => LogThrottled($"seq-warn:{r.Owner.Data.Name}:{s.Do}:{why}", 5, LogLevel.Warn, r.Mode == SeqMode.Boss ? "Boss" : "Cutscene", r.Owner.Data.Name, $"Step '{s.Do}' skipped — {why}");

        // A Custom rule made from a step, so DoCustom runs it (numbers → N, words → T; a non-number value → an expression).
        private static CustomRule SeqRule(SeqStep s, string action, params string[] keys)
        {
            var r = new CustomRule { Do = action };
            foreach (var k in keys)
            {
                var v = s.Str(k);
                if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) r.N[k] = d;
                else if (k is "to" or "amount") { if (v.Length > 0) r.T["expr"] = v; }
                else r.T[k] = v;
            }
            return r;
        }

        // "self" = the runner's object, "player" = the player (a boss: its target), else an object by name.
        private Entity? SeqFind(SeqRunner r, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;
            if (name.Equals("self", StringComparison.OrdinalIgnoreCase)) return r.Owner;
            if (name.Equals("player", StringComparison.OrdinalIgnoreCase))
                return (r.Mode == SeqMode.Boss ? BossTarget(r.Owner) : null) ?? FindPlayer();
            return FindNamed(name);
        }

        private Entity? FindNamed(string name)
            => _entities.FirstOrDefault(e => !e.Dead && e.Data.Name == name)
            ?? _entities.FirstOrDefault(e => !e.Dead && e.Data.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private Entity? FindPlayer()
            => FindNamed("Player")
            ?? (_cameraTarget is { Dead: false } ? _cameraTarget : null)
            ?? _entities.FirstOrDefault(e => !e.Dead && ((e.Beh(BehaviorKind.Platformer) ?? e.Beh(BehaviorKind.TopDown))?.Num("controls", 1) ?? 0) >= 0.5);

        // Where a move ends (top-left): centred on an object, or at the point x, y.
        private (double x, double y) MoveGoal(SeqRunner r, SeqStep s, Entity e, string toKey)
        {
            var to = SeqFind(r, s.Str(toKey));
            return to != null ? (to.CX - e.W / 2, to.CY - e.H / 2) : (s.Num("x"), s.Num("y"));
        }

        private Mover MoveTo(SeqRunner? owner, Entity e, (double x, double y) goal, double time)
        {
            _moves.RemoveAll(m => m.E == e); // a new move replaces the old one
            var m = new Mover { Owner = owner, E = e, FromX = e.X, FromY = e.Y, ToX = goal.x, ToY = goal.y, Start = _gameTime, Dur = Math.Max(0, time) };
            _moves.Add(m);
            return m;
        }

        private void StartPan(SeqRunner? owner, Entity? target, double x, double y, double time, bool returning)
            => _camPan = new CamPan { Owner = owner, Target = target, PX = x, PY = y, FromX = _camX, FromY = _camY, Start = _gameTime, Dur = Math.Max(0, time), Returning = returning };

        private double PanProgress() => _camPan == null ? 1 : _camPan.Dur <= 0 ? 1 : Math.Clamp((_gameTime - _camPan.Start) / _camPan.Dur, 0, 1);

        private void Shake(double strength, double time)
        {
            _shakeStrength = Math.Max(0, strength); _shakeDur = Math.Max(0.01, time); _shakeUntil = _gameTime + _shakeDur;
        }

        private void StartFade(string color, double to, double time, double? from = null)
        {
            _fadeFrom = from ?? FadeOpacity(); _fadeTo = Math.Clamp(to, 0, 1); _fadeColor = color;
            _fadeStart = _gameTime; _fadeDur = Math.Max(0, time);
        }

        private double FadeOpacity()
        {
            double u = _fadeDur <= 0 ? 1 : Math.Clamp((_gameTime - _fadeStart) / _fadeDur, 0, 1);
            return _fadeFrom + (_fadeTo - _fadeFrom) * u;
        }

        // A title fades in, holds, then fades out over its time.
        private double TitleAlpha()
        {
            if (_titleText.Length == 0) return 0;
            double e = _gameTime - _titleStart;
            if (e < 0 || e > _titleDur) return 0;
            double fadeIn = Math.Min(0.35, _titleDur / 3), fadeOut = Math.Min(0.5, _titleDur / 3);
            return Math.Clamp(Math.Min(e / fadeIn, (_titleDur - e) / fadeOut), 0, 1);
        }

        // ================================================================== test hooks
        public double TFadeOpacity => FadeOpacity();
        public bool TBarsShown => _barsT > 0.5;
        public double TBarsT => _barsT;
        public string TTitleText => TitleAlpha() > 0 ? _titleText : "";
        public bool TInputLocked => _inputLocks.Count > 0;
        public bool TCameraOverride => _camPan != null;
        public bool TShaking => _gameTime < _shakeUntil;
        public bool TCutsceneRunning(string name) => _runners.Any(r => !r.Finished && r.Mode == SeqMode.Cutscene && r.Owner.Data.Name == name);
        public int TRunners => _runners.Count(r => !r.Finished);
        public bool TCutsceneTakesEscape => CutsceneTakesEscape;
        /// <summary>The overlay visuals as drawn: "fade 0.00 · bars 59 · title 'X' 0.80 · boss bar Visible".</summary>
        public string TOverlays => $"fade {FadeRect.Opacity:0.00} · bars {BarTop.ActualHeight:0}/{BarBottom.ActualHeight:0} · title '{BigTitle.Text}' {BigTitle.Opacity:0.00} · boss bar {BossBar.Visibility}";
        public bool TBarsDrawn => BarTop.ActualHeight > 1 && BarBottom.ActualHeight > 1;
        public bool TOverlaysExist =>FadeRect.Parent == Root && BarTop.Parent == Root && BarBottom.Parent == Root && BigTitle.Parent == Root && BossBar.Parent == Root
                                      && Root.Children.IndexOf(FadeRect) > Root.Children.IndexOf(Hud) && Root.Children.IndexOf(BossBar) < Root.Children.IndexOf(DialogBox);
    }
}
