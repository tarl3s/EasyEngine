using System;
using System.Collections.Generic;
using System.Windows;

namespace EasyEngine;

/// <summary>Shared by the --selftest feature checks: the editor, the log writer, windows to close at the end, and
/// checks to run after the live loop has rendered for a while (return an error message to fail, null to pass).</summary>
internal sealed class SelfTestContext
{
    public MainWindow Editor = null!;
    public Action<string> Write = _ => { };
    public List<Window> CloseLater { get; } = new();
    public List<Func<string?>> LateChecks { get; } = new();
}
