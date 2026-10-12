# Opens EasyEngine, always at the latest version of the code in this folder.
# If any source file is newer than the last build, it rebuilds first (with a small "Updating..." window), then starts
# bin\Debug\net8.0-windows\EasyEngine.exe. The desktop shortcut runs this script:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "<engine>\tools\Start-EasyEngine.ps1"
param([switch]$Force) # -Force: rebuild even if nothing looks changed

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'bin\Debug\net8.0-windows'
$exe = Join-Path $bin 'EasyEngine.exe'
$dll = Join-Path $bin 'EasyEngine.dll'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

function Test-Stale {
    if ($Force -or -not (Test-Path $dll)) { return $true }
    $built = (Get-Item $dll).LastWriteTime
    $newer = Get-ChildItem $root -File | Where-Object { $_.Extension -in '.cs', '.xaml', '.csproj' -and $_.LastWriteTime -gt $built } | Select-Object -First 1
    return $null -ne $newer
}

function Test-Running {
    @(Get-Process EasyEngine -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }).Count -gt 0
}

trap {
    # anything unexpected: say so and leave a log, instead of silently not opening
    "$(Get-Date) $_`n$($_.ScriptStackTrace)" | Out-File (Join-Path $env:TEMP 'easyengine_start.log') -Append
    [System.Windows.Forms.MessageBox]::Show("EasyEngine couldn't start: $_`n`nLog: $env:TEMP\easyengine_start.log", 'EasyEngine', 'OK', 'Error') | Out-Null
    exit 1
}

if ((Test-Stale) -and -not (Test-Running)) {
    # a small window while it builds, so a slow first build doesn't look like nothing happened
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'EasyEngine'; $form.Width = 360; $form.Height = 110; $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'; $form.ControlBox = $false; $form.TopMost = $true
    $form.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 36)
    $label = New-Object System.Windows.Forms.Label
    $label.Text = 'Updating EasyEngine to the latest version...'; $label.AutoSize = $false; $label.Dock = 'Fill'
    $label.TextAlign = 'MiddleCenter'; $label.ForeColor = [System.Drawing.Color]::FromArgb(230, 236, 245)
    $label.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $form.Controls.Add($label); $form.Show(); [System.Windows.Forms.Application]::DoEvents()

    $log = Join-Path $env:TEMP 'easyengine_build.log'
    $p = Start-Process dotnet -ArgumentList 'build', "`"$(Join-Path $root 'EasyEngine.csproj')`"", '-nologo', '-v:q' `
        -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput $log -PassThru
    $null = $p.Handle # PowerShell 5.1 only keeps the exit code if the handle was taken while the process ran
    while (-not $p.WaitForExit(100)) { [System.Windows.Forms.Application]::DoEvents() }
    $form.Close()

    if ($p.ExitCode -ne 0) {
        $errors = (Get-Content $log | Where-Object { $_ -match ' error ' } | Select-Object -First 5) -join "`n"
        [System.Windows.Forms.MessageBox]::Show("The update failed, so the previous version will open.`n`n$errors`n`nFull log: $log", 'EasyEngine', 'OK', 'Warning') | Out-Null
    }
}

if (Test-Path $exe) { Start-Process $exe -WorkingDirectory $bin }
else { [System.Windows.Forms.MessageBox]::Show("EasyEngine isn't built yet and the build failed. See $env:TEMP\easyengine_build.log", 'EasyEngine', 'OK', 'Error') | Out-Null }
