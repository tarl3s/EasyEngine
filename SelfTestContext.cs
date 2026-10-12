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

    /// <summary>Renders one of the engine's own windows (its content only — nothing else on the screen) to
    /// %TEMP%\easyengine_snap_&lt;name&gt;.png (…_light.png in the light theme), so a run leaves pictures of what was actually drawn. Returns the path.</summary>
    public string Snapshot(Window w, string name)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"easyengine_snap_{name}{(Theme.IsLight ? "_light" : "")}.png");
        try
        {
            if (w.Content is not FrameworkElement root || root.ActualWidth < 1 || root.ActualHeight < 1) return "";
            // the content is drawn at its offset in the window (its margin), so the picture includes the margin around it
            double width = root.ActualWidth + root.Margin.Left + root.Margin.Right, height = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            // the window's own background first (it isn't part of its content), so translucent things look as on screen
            var back = new System.Windows.Media.DrawingVisual();
            using (var dc = back.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, width, height));
            bmp.Render(back);
            bmp.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
            Write($"snapshot: {path}");
        }
        catch (Exception ex) { Write($"snapshot {name} failed: {ex.Message}"); return ""; }
        return path;
    }
}
