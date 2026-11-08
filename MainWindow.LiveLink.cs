using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using BE = BuilderEngine.Script;

namespace EasyEngine
{
    // 🔗 Live link (BuilderEngine's docs/EASYENGINE-LINK.md, "Live link"): the editor watches the file its project is saved in
    // (a .eeproj, or a BuilderEngine folder's project.json + mechanisms) with BuilderEngine's LiveLink (debounce + echo
    // detection). A change made outside — BuilderEngine, or another EasyEngine window — is loaded keeping the editor's
    // state (scene, selection, zoom and scroll, the active layer; the panels stay as they are), never while the game is
    // playing (it is loaded when the game closes). While the link is on, every edit is saved ~500 ms after it (atomically),
    // so the other side sees it; with the link off, an outside change over unsaved edits asks first. The link starts on
    // for projects shared with BuilderEngine and for a project given on the command line ("Open in EasyEngine").
    public partial class MainWindow
    {
        /// <summary>Opened from the command line (BuilderEngine's "Open in EasyEngine"): the live link starts on.</summary>
        public bool LinkOnOpen { get; set; }

        private BE.LiveLink? _link;
        private bool _linkOn;
        private bool _linkReloadPending; // an outside change arrived while the game was playing
        private bool _playing;
        private DispatcherTimer? _autosave;

        // dev/smoke-test aid: "EasyEngine.exe <project> --link-probe" logs the link to %TEMP%\easyengine_link.log and moves the
        // first object 16 px two seconds after opening (an edit the live link saves)
        private static readonly bool LinkProbe = Environment.GetCommandLineArgs().Contains("--link-probe");
        private static void LinkLog(string line)
        {
            if (!LinkProbe) return;
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "easyengine_link.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); } catch { }
        }

        private void StartLiveLink()
        {
            if (_link != null || string.IsNullOrEmpty(_project.FilePath) || !File.Exists(_project.FilePath)) { UpdateLinkButton(); return; }
            _linkOn = LinkOnOpen || BuilderLink.IsLinked(_project);
            try
            {
                _link = new BE.LiveLink(_project.FilePath, 300);
                _link.Changed += () => Dispatcher.BeginInvoke(new Action(OnOutsideChange));
                _link.Start();
                Closed += (_, _) => { _autosave?.Stop(); _link?.Dispose(); _link = null; };
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                _link = null; _linkOn = false; // a folder that can't be watched: plain saving still works
            }
            UpdateTitle(); UpdateLinkButton();
            LinkLog($"opened {_project.FilePath} (live link {(_linkOn ? "on" : "off")})");
            if (LinkProbe)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    var o = _scene.Layers.SelectMany(l => l.Objects).FirstOrDefault(x => !IsLockedObj(x));
                    if (o == null) return;
                    PushUndo(); o.X += 16; RenderScene(); Dirty();
                    LinkLog($"probe moved '{o.Name}' to x={o.X}");
                };
                t.Start();
            }
        }

        private void LiveLink_Click(object sender, RoutedEventArgs e) => SetLiveLink(!_linkOn);

        private void SetLiveLink(bool on)
        {
            if (_link == null) StartLiveLink();
            _linkOn = on && _link != null;
            UpdateTitle(); UpdateLinkButton();
            Status(_linkOn ? L.T("🔗 Live link on: every change is saved right away, and changes made outside show up here")
                           : L.T("Live link off: save with Save; changes made outside still show up when there's nothing unsaved"));
            if (_linkOn && IsDirty) LinkAfterEdit();
        }

        private void UpdateLinkButton()
        {
            if (LinkButton == null) return;
            LinkButton.Content = _linkOn ? "🔗 " + L.T("Live link") : "🔗";
        }

        /// <summary>Saves the project through the link: the watcher knows it is our own write (an echo, not an outside change).</summary>
        private void SaveProjectFile()
        {
            _link?.BeginWrite();
            try { ProjectIO.Save(_project); }
            finally { _link?.EndWrite(); }
            LinkLog("saved");
        }

        // after each edit (Dirty): with the link on, save ~500 ms later (a burst of edits is one save)
        private void LinkAfterEdit()
        {
            if (!_linkOn || _link == null || _closing) return;
            if (_autosave == null)
            {
                _autosave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _autosave.Tick += (_, _) => Autosave();
            }
            _autosave.Stop(); _autosave.Start();
        }

        private void Autosave()
        {
            _autosave?.Stop();
            if (!_linkOn || _link == null || _closing || !IsDirty) return;
            if (_playing) { _autosave?.Start(); return; }
            if (_link.ChangedOnDisk()) { ReloadFromDisk(); return; } // the other side saved meanwhile: its version first
            try { SaveProjectFile(); _dirty = false; UpdateTitle(); }
            catch (Exception ex) { Status(L.F("Error saving: {0}", ex.Message)); }
        }

        private void OnOutsideChange()
        {
            if (_closing || _link == null) return;
            if (_playing) { _linkReloadPending = true; return; } // never while the game is playing
            if (!_linkOn && IsDirty && IsVisible &&
                MessageBox.Show(this, L.T("This project was changed outside EasyEngine (in BuilderEngine or another window). Load that version? Your unsaved changes here would be lost."),
                                "EasyEngine", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            ReloadFromDisk();
        }

        private void AfterPlayLink()
        {
            if (!_linkReloadPending) return;
            _linkReloadPending = false;
            if (_link?.ChangedOnDisk() != false) OnOutsideChange();
        }

        /// <summary>Loads the project from disk again, keeping the editor's state. Ctrl+Z brings back the scene as it was.</summary>
        internal bool ReloadFromDisk()
        {
            if (string.IsNullOrEmpty(_project.FilePath)) return false;
            Project fresh;
            try { fresh = ProjectIO.Load(_project.FilePath, addRecent: false); }
            catch (Exception ex)
            {
                Status(L.F("Couldn't load the outside change yet: {0}", ex.Message)); // half written: the link looks again on the next change
                LinkLog("reload failed: " + ex.Message);
                return false;
            }
            _link?.RememberDisk();

            string sceneName = _scene.Name;
            int sceneIdx = _project.Scenes.IndexOf(_scene);
            var selIds = _selection.Select(o => o.Id).ToList();
            int? primaryId = _selected?.Id;
            string? layerName = _activeLayer?.Name;
            int layerIdx = _activeLayer != null ? _scene.Layers.IndexOf(_activeLayer) : -1;
            double zoom = _zoom, h = SceneScroll.HorizontalOffset, v = SceneScroll.VerticalOffset;
            string before = SceneJson();

            CopyProjectFrom(fresh);
            FillMissingBehaviorKeys(_project);
            var scene = _project.Scenes.FirstOrDefault(s => s.Name == sceneName)
                        ?? (sceneIdx >= 0 && sceneIdx < _project.Scenes.Count ? _project.Scenes[sceneIdx] : _project.Scenes[0]);
            bool sameScene = scene.Name == sceneName;

            _restoring = true;
            try
            {
                _scene = scene;
                ProjectNameText.Text = _project.Name;
                RefreshSceneCombo(); SelectSceneInCombo(scene);
                UpdateSceneLockButton();
                SceneFrame.Width = scene.Width; SceneFrame.Height = scene.Height;
                _selected = null; _selection.Clear();
                RefreshLayers();
                int li = scene.Layers.FindIndex(l => l.Name == layerName);
                if (li < 0) li = layerIdx >= 0 && layerIdx < scene.Layers.Count ? layerIdx : IndexOfBaseLayer();
                if (LayersList.Items.Count > 0) LayersList.SelectedIndex = Math.Clamp(li, 0, LayersList.Items.Count - 1);
                RenderScene();
                RefreshPalette();
                var all = scene.Layers.SelectMany(l => l.Objects).ToList();
                foreach (var id in selIds) if (all.FirstOrDefault(o => o.Id == id) is GameObjectData o) _selection.Add(o);
                SetPrimary(primaryId is int pid ? _selection.FirstOrDefault(o => o.Id == pid) ?? _selection.LastOrDefault() : null);
                // the view stays where it was
                _zoom = zoom; SceneScale.ScaleX = SceneScale.ScaleY = zoom; UpdateZoomLabel();
                SceneScroll.UpdateLayout();
                SceneScroll.ScrollToHorizontalOffset(h); SceneScroll.ScrollToVerticalOffset(v);
            }
            finally { _restoring = false; }

            if (sameScene) { _undo.Push(before); _redo.Clear(); } else { _undo.Clear(); _redo.Clear(); }
            _dirty = false; UpdateTitle();
            Status(L.T("🔗 Loaded the changes made outside EasyEngine"));
            LinkLog($"reloaded scene '{scene.Name}': " + string.Join(", ", scene.Layers.SelectMany(l => l.Objects).Take(6).Select(o => $"{o.Name}@{o.X:0},{o.Y:0}")));
            return true;
        }

        // every saved property of the project, in place: the editor (and everything holding _project) keeps the same object
        private static readonly System.Reflection.PropertyInfo[] SavedProjectProps = typeof(Project).GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute))).ToArray();

        private void CopyProjectFrom(Project fresh)
        {
            foreach (var p in SavedProjectProps) p.SetValue(_project, p.GetValue(fresh));
        }

        // ---- test hooks (FullTest.BuilderLink.cs) ----
        internal bool TestLinkOn => _linkOn;
        internal void TestSetLinkOn(bool on) => _linkOn = on;
        internal bool TestReload() => ReloadFromDisk();
        internal void TestSave() => SaveProjectFile();
        internal double TestZoom { get => _zoom; set { _zoom = value; SceneScale.ScaleX = SceneScale.ScaleY = value; } }
    }
}
