using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyEngine
{
    /// <summary>
    /// Updates a downloaded EasyEngine from the GitHub releases. The launcher asks GitHub for the latest release; if it is
    /// newer, the person is asked, the release's zip is downloaded and unpacked to %TEMP%, and a small script (run after
    /// the engine closes) removes the old version's files — only those listed in its manifest, so nothing the person put
    /// in the folder is touched — copies the new ones in and opens the engine again.
    /// A build run from the source code (bin\Debug next to EasyEngine.csproj) never updates this way: the code is the update.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "tarl3s/EasyEngine";
        public const string AssetName = "EasyEngine-win-x64.zip";
        public const string ManifestName = "easyengine-files.txt"; // every file of a release, one relative path per line

        public record Release(Version Version, string Tag, string Notes, string ZipUrl);

        public static Version Current
        {
            get { var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0); return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        public static string InstallDir => AppContext.BaseDirectory.TrimEnd('\\', '/');

        /// <summary>Running from a source checkout (an EasyEngine.csproj a few folders up)?</summary>
        public static bool IsDevBuild
        {
            get
            {
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
                    if (File.Exists(Path.Combine(d.FullName, "EasyEngine.csproj"))) return true;
                return false;
            }
        }

        /// <summary>"v0.11.0" / "0.11" → a 3-part version (null when it isn't one).</summary>
        public static Version? ParseVersion(string tag)
        {
            var s = (tag ?? "").Trim().TrimStart('v', 'V');
            if (!Version.TryParse(s, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        /// <summary>Reads GitHub's "latest release" answer; null if it has no version tag or no engine zip.</summary>
        public static Release? ParseLatest(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var version = ParseVersion(root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "");
                if (version == null || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
                var zip = assets.EnumerateArray().FirstOrDefault(a => a.TryGetProperty("name", out var n) && n.GetString() == AssetName);
                if (zip.ValueKind != JsonValueKind.Object || !zip.TryGetProperty("browser_download_url", out var url)) return null;
                var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                return new Release(version, t.GetString()!, notes, url.GetString() ?? "");
            }
            catch { return null; }
        }

        private static HttpClient Http()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EasyEngine-updater");
            return http;
        }

        /// <summary>The latest release if it is newer than this engine; null if not, or GitHub can't be reached.</summary>
        public static async Task<Release?> CheckAsync()
        {
            try
            {
                using var http = Http();
                var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                var r = ParseLatest(json);
                return r != null && r.Version > Current ? r : null;
            }
            catch { return null; }
        }

        private static string WorkDir => Path.Combine(Path.GetTempPath(), "EasyEngine_update");

        /// <summary>Downloads and unpacks the release; returns the folder holding the new EasyEngine.exe.</summary>
        public static async Task<string> DownloadAsync(Release r, IProgress<double>? progress = null)
        {
            if (Directory.Exists(WorkDir)) try { Directory.Delete(WorkDir, true); } catch { }
            Directory.CreateDirectory(WorkDir);
            var zip = Path.Combine(WorkDir, AssetName);
            using (var http = Http())
            {
                http.Timeout = TimeSpan.FromMinutes(15);
                using var resp = await http.GetAsync(r.ZipUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(zip);
                var buf = new byte[81920]; int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n)); done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            var unpacked = Path.Combine(WorkDir, r.Tag);
            ZipFile.ExtractToDirectory(zip, unpacked);
            return FindExeFolder(unpacked) ?? throw new InvalidDataException("The download doesn't contain EasyEngine.exe.");
        }

        /// <summary>The folder (the given one, or the single folder inside it) that holds EasyEngine.exe.</summary>
        public static string? FindExeFolder(string dir)
        {
            if (File.Exists(Path.Combine(dir, "EasyEngine.exe"))) return dir;
            var subs = Directory.GetDirectories(dir);
            return subs.Length == 1 && File.Exists(Path.Combine(subs[0], "EasyEngine.exe")) ? subs[0] : null;
        }

        /// <summary>Starts the swap (it waits for this engine to close): removes the old version's files, copies the new
        /// ones from <paramref name="newDir"/> into <paramref name="installDir"/> and, unless <paramref name="start"/> is
        /// false, opens the new engine. Returns the script process.</summary>
        public static Process StartSwap(string newDir, string installDir, int waitForPid, bool start = true)
        {
            Directory.CreateDirectory(WorkDir);
            var script = Path.Combine(WorkDir, "swap.ps1");
            File.WriteAllText(script, SwapScript); // plain ASCII; the paths come in as arguments
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                                      "-WaitPid", waitForPid.ToString(), "-NewDir", newDir, "-InstallDir", installDir, "-Start", start ? "1" : "0" })
                psi.ArgumentList.Add(a);
            return Process.Start(psi)!;
        }

        private const string SwapScript = @"
param([int]$WaitPid, [string]$NewDir, [string]$InstallDir, [string]$Start = '1')
$log = Join-Path $env:TEMP 'easyengine_update.log'
""$(Get-Date) updating $InstallDir from $NewDir"" | Out-File $log

# a small window with a progress bar, so the swap (it can take a while) doesn't look like nothing is happening
$form = $null; $bar = $null; $label = $null
try {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'EasyEngine'; $form.Width = 380; $form.Height = 130; $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'; $form.ControlBox = $false; $form.TopMost = $true
    $form.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 36)
    $label = New-Object System.Windows.Forms.Label
    $label.Text = 'Updating EasyEngine...'; $label.AutoSize = $false; $label.Dock = 'Fill'
    $label.TextAlign = 'MiddleCenter'; $label.ForeColor = [System.Drawing.Color]::FromArgb(230, 236, 245)
    $label.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $bar = New-Object System.Windows.Forms.ProgressBar
    $bar.Dock = 'Bottom'; $bar.Height = 14; $bar.Style = 'Marquee'
    $form.Controls.Add($label); $form.Controls.Add($bar); $form.Show(); [System.Windows.Forms.Application]::DoEvents()
} catch { $form = $null }
function Show-Step([string]$text, [int]$done = -1, [int]$total = 0) {
    if (-not $form) { return }
    if ($text) { $label.Text = $text }
    if ($done -ge 0 -and $total -gt 0) { $bar.Style = 'Continuous'; $bar.Maximum = $total; $bar.Value = [Math]::Min($done, $total) }
    [System.Windows.Forms.Application]::DoEvents()
}

Show-Step 'Waiting for EasyEngine to close...'
$deadline = (Get-Date).AddSeconds(60)
while ($WaitPid -gt 0 -and (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Show-Step ''; Start-Sleep -Milliseconds 100 }
Start-Sleep -Milliseconds 500
$manifest = Join-Path $InstallDir 'easyengine-files.txt'
if (Test-Path $manifest) {
    # remove exactly the old version's files (never anything else the person put in the folder)
    $old = @(Get-Content $manifest | Where-Object { $_ -and -not $_.Contains('..') }); $i = 0
    foreach ($rel in $old) {
        $f = Join-Path $InstallDir $rel
        if (Test-Path -LiteralPath $f -PathType Leaf) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue }
        if ((++$i % 10) -eq 0) { Show-Step 'Removing the old version...' $i $old.Count }
    }
    # folders the old version left empty
    Get-ChildItem -LiteralPath $InstallDir -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } | Remove-Item -Force -ErrorAction SilentlyContinue
}
# copy the new version file by file, so the window stays alive and shows how far it got
$src = (Resolve-Path -LiteralPath $NewDir).Path.TrimEnd('\')
$new = @(Get-ChildItem -LiteralPath $src -Recurse -File); $i = 0
foreach ($file in $new) {
    $dest = Join-Path $InstallDir $file.FullName.Substring($src.Length + 1)
    $dir = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
    if ((++$i % 5) -eq 0 -or $i -eq $new.Count) { Show-Step ""Installing the new version... $i / $($new.Count)"" $i $new.Count }
}
""$(Get-Date) copied"" | Out-File $log -Append
if ($Start -eq '1') { Show-Step 'Opening EasyEngine...'; Start-Process (Join-Path $InstallDir 'EasyEngine.exe') -WorkingDirectory $InstallDir -ArgumentList '--updated' }
if ($form) { $form.Close() }
";
    }
}
