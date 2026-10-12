using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// The sample projects that come with the engine (SampleProjects.cs): built once per computer, never twice.
public partial class App
{
    partial void FeatureTestsSamples(FullTestKit t)
    {
        t.Test("samples: a new computer gets every sample built and opened; the next launch adds nothing", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "EE_Full_Samples_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var done = new List<string>(); var opened = new List<string>();
            int first = SampleProjects.Install(folder, done, new List<string>(), opened.Add);
            int second = SampleProjects.Install(folder, done, new List<string>(), opened.Add);
            bool ok = first == SampleProjects.All.Length && second == 0 && opened.Count == first
                      && opened.All(File.Exists) && SampleProjects.All.All(s => done.Contains(s.name));
            TestScenarios.TryDeleteDir(folder);
            return ok;
        });
        t.Test("samples: one already in Recent projects is only remembered, not built again", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "EE_Full_Samples_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var done = new List<string>(); var opened = new List<string>();
            int added = SampleProjects.Install(folder, done, new List<string> { BossDemo.ProjectName }, opened.Add);
            bool ok = added == SampleProjects.All.Length - 1 && !Directory.Exists(Path.Combine(folder, BossDemo.ProjectName))
                      && done.Contains(BossDemo.ProjectName);
            TestScenarios.TryDeleteDir(folder);
            return ok;
        });
        t.Test("samples: one the person removed (already installed) doesn't come back", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "EE_Full_Samples_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var done = SampleProjects.All.Select(s => s.name).ToList();
            int added = SampleProjects.Install(folder, done, new List<string>(), _ => { });
            return added == 0 && !Directory.Exists(folder);
        });
    }
}
