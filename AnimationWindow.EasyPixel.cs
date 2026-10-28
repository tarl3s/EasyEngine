using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace EasyEngine;

// "🖌 Import from EasyPixel": the animations exported by EasyPixel (EasyPixelBridge.cs) become frames here.
public partial class AnimationWindow
{
    private void ImportEasyPixel_Click(object sender, RoutedEventArgs e)
    {
        string? folder;
        var recent = EasyPixelBridge.ListRecent();
        if (recent.Count == 0)
        {
            if (EasyPixelBridge.FindEasyPixel() != null)
            {
                MessageBox.Show(this, L.T("Export from EasyPixel first (File → Export to EasyEngine)"), L.T("Import from EasyPixel"));
                return;
            }
            var ask = MessageBox.Show(this,
                L.T("EasyPixel is not installed, and nothing was exported from it yet.") + "\n\n" + L.T("Download it free:") + " " + EasyPixelBridge.DownloadUrl
                + "\n\n" + L.T("Do you have an export folder to choose by hand?"),
                L.T("Import from EasyPixel"), MessageBoxButton.YesNo);
            if (ask != MessageBoxResult.Yes) return;
            folder = PickHandoffFolder();
        }
        else folder = PickHandoff(recent);
        if (folder == null) return;

        if (!EasyPixelBridge.TryRead(folder, out var handoff, out var error))
        {
            MessageBox.Show(this, error, L.T("Import from EasyPixel"));
            return;
        }

        try
        {
            if (_cur != null)
            {
                EasyPixelBridge.Apply(_project, _obj, _cur, handoff!, _cur.Name);
                RefreshFrames(); RestartPreview(); RefreshAnimList();
            }
            else
            {
                var name = InputDialog.Prompt(this, L.T("New animation"), L.T("Animation name (e.g. idle, walk):"), handoff!.Name.Length > 0 ? handoff.Name : SuggestName());
                if (name == null) return;
                var anim = EasyPixelBridge.Apply(_project, _obj, null, handoff!, name);
                RefreshAnimList(); SelectAnim(anim);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, L.T("Could not import the pictures:") + " " + ex.Message, L.T("Import from EasyPixel")); }
    }

    private string? PickHandoffFolder()
    {
        var dlg = new OpenFolderDialog { Title = L.T("Choose an EasyPixel export folder") };
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    /// <summary>The newest exports first (name, frames, speed, when); returns the chosen folder, or null.</summary>
    private string? PickHandoff(System.Collections.Generic.IReadOnlyList<HandoffInfo> recent)
    {
        string? chosen = null;
        var win = new Window
        {
            Title = L.T("Import from EasyPixel"), Width = 460, SizeToContent = SizeToContent.Height, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
            Background = (System.Windows.Media.Brush)FindResource("Bg0"), FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        };
        var list = new ListBox { Height = 260, ItemContainerStyle = (Style)FindResource("ListRow"), Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        L.SetSkip(list, true); // the person's export names
        foreach (var i in recent)
            list.Items.Add(new ListBoxItem { Content = L.F("{0} — {1} frames · {2} fps · {3}", i.Name, i.FrameCount, i.Fps, i.When.ToString("g")), Tag = i.Folder });
        list.SelectedIndex = 0;

        Button Btn(string text, string style, double w) => new() { Content = text, Style = (Style)FindResource(style), Width = w, Height = 34, Margin = new Thickness(8, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var ok = Btn(L.T("Import"), "AccentButton", 100); ok.IsDefault = true;
        var folderBtn = Btn(L.T("Open folder…"), "CardButton", 120);
        var cancel = Btn(L.T("Cancel"), "CardButton", 90);
        ok.Click += (s, ev) => { if (list.SelectedItem is ListBoxItem it) { chosen = (string)it.Tag; win.Close(); } };
        list.MouseDoubleClick += (s, ev) => { if (list.SelectedItem is ListBoxItem it) { chosen = (string)it.Tag; win.Close(); } };
        folderBtn.Click += (s, ev) => { var f = PickHandoffFolder(); if (f != null) { chosen = f; win.Close(); } };
        cancel.Click += (s, ev) => win.Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(folderBtn); buttons.Children.Add(cancel); buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = L.T("Choose what to import (newest first):"), FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(list); panel.Children.Add(buttons);
        win.Content = panel;
        win.ShowDialog();
        return chosen;
    }
}
