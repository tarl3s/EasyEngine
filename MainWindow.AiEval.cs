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
        public bool EvalCompact { get; set; } // --aitest --compact: the cloud's shorter prompt on a local model too
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
