using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EasyEngine
{
    // Hooks for the graded AI evaluation (--aitest, App.AiEval.cs): the same Build step the panel uses, on a known scene.
    public partial class MainWindow
    {
        /// <summary>"local:&lt;ollama model&gt;", "cloud:auto" or "cloud:&lt;provider&gt;[|model]".</summary>
        public void EvalUseAi(string choice)
        {
            _useCloud = choice.StartsWith("cloud:", StringComparison.Ordinal);
            var rest = choice[(choice.IndexOf(':') + 1)..];
            if (!_useCloud) { _model = rest; return; }
            int bar = rest.IndexOf('|');
            _cloudProvider = bar >= 0 ? rest[..bar] : rest;
            _cloudModelOverride = bar >= 0 ? rest[(bar + 1)..] : "";
        }

        public Task<(bool ok, string summary, string reply)> EvalBuild(string prompt) => AiBuildAsync(prompt);
        public void EvalReset() => ResetTestScene();

        /// <summary>The Boss AI / Cutscene card's "✨ Ask the AI" on an object of the scene, written into its behavior.</summary>
        public async Task<(bool ok, string summary, string reply)> EvalSequence(SeqMode mode, string objectName, string request)
        {
            var bh = TestFindObject(objectName)?.Behaviors.FirstOrDefault(b => b.Kind == (mode == SeqMode.Boss ? BehaviorKind.BossAI : BehaviorKind.Cutscene));
            if (bh == null) return (false, "no " + mode + " behavior on " + objectName, "");
            var result = await SequenceAi.MakeAsync((user, system) => AiGenerateAsync(user, system, json: true, temperature: 0.2),
                                                    mode, request, bh.Text("script", ""), _project, _scene, compact: !_useCloud);
            return (result.Ok, ApplySeqAi(bh, mode, result), result.Ok ? result.Json : result.Error);
        }
        public Task<string> EvalLearn(string question) => TeachAsync(question);

        /// <summary>The block editor's "✨ Ask the AI" on an object, as a person would use it: open the blocks, ask, Done.</summary>
        public async Task<(bool ok, string summary, string reply)> EvalBlocks(string objectName, string request)
        {
            var obj = TestFindObject(objectName);
            if (obj == null) return (false, "no object " + objectName, "");
            var custom = obj.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Custom);
            var win = new BlockEditorWindow(_project, _scene, custom?.Text("script", "[]") ?? "[]", obj, (u, s) => AiGenerateAsync(u, s, json: true, temperature: 0.1));
            var status = await win.TestAskAi(request);
            var json = win.SerializeTest();
            try { win.Close(); } catch { }
            if (status.StartsWith("⚠") && (status.Contains("limit", StringComparison.OrdinalIgnoreCase) || status.Contains("quota", StringComparison.OrdinalIgnoreCase)))
                throw new CloudException(status) { RateLimited = true }; // the provider was busy: no answer to grade (the window only shows it)
            if (custom == null) { custom = new BehaviorData { Kind = BehaviorKind.Custom }; obj.Behaviors.Add(custom); }
            custom.Texts["script"] = json;
            return (status.StartsWith("✓"), status.Replace("\n", " "), json);
        }
        public List<GameObjectData> TestAllObjects() => _scene.Layers.SelectMany(l => l.Objects).ToList();
        public Project EvalProject => _project;
        public Scene EvalScene => _scene;
        public string EvalDump() => DumpState();

        public void EvalSelect(params string[] names)
        {
            _selection.Clear();
            foreach (var n in names) { var o = TestFindObject(n); if (o != null) _selection.Add(o); }
            _selected = _selection.FirstOrDefault();
        }
    }
}
