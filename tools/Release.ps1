# One command for a whole release: version bump (EasyEngine.csproj + LauncherWindow.xaml), "vX.Y.Z: <title>" commit,
# annotated tag, push, then Publish-Release.ps1 (self-contained zip + GitHub release). Prints only what matters.
#   powershell -ExecutionPolicy Bypass -File tools\Release.ps1 -Version 0.15.5 -Title "selection box, move to layer" -NotesFile notes.md
# -Version omitted = the version already in the csproj (no bump commit). Stops before doing anything if the tag exists.
param([string]$Version = '', [string]$Title = '', [Parameter(Mandatory)][string]$NotesFile)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$csproj = Join-Path $root 'EasyEngine.csproj'
$launcher = Join-Path $root 'LauncherWindow.xaml'
$utf8 = New-Object System.Text.UTF8Encoding($false) # never Get-Content | Set-Content: it breaks the emoji in the sources

$current = [regex]::Match([IO.File]::ReadAllText($csproj), '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $Version) { $Version = $current }
if (-not (Test-Path $NotesFile)) { throw "notes file not found: $NotesFile" }
if (git tag -l "v$Version") { throw "tag v$Version already exists" }

if ($Version -ne $current) {
    foreach ($f in @($csproj, $launcher)) {
        $t = [IO.File]::ReadAllText($f)
        $t = $t.Replace("<Version>$current</Version>", "<Version>$Version</Version>").Replace("Text=`"v$current`"", "Text=`"v$Version`"")
        [IO.File]::WriteAllText($f, $t, $utf8)
    }
    if (-not (Select-String -Path $launcher -SimpleMatch "v$Version" -Quiet)) { throw "LauncherWindow.xaml version not updated" }
    git add $csproj $launcher | Out-Null
    $msg = "v${Version}: $Title`n`nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
    git commit -q -m $msg
    if ($LASTEXITCODE -ne 0) { throw "commit failed" }
}

git tag -a "v$Version" -m "v${Version}: $Title"
git push -q origin main --tags  # no 2>&1: Windows PowerShell turns git's stderr into errors
if ($LASTEXITCODE -ne 0) { throw "push failed" }

$log = Join-Path $env:TEMP 'easyengine_release.log'
& (Join-Path $PSScriptRoot 'Publish-Release.ps1') -NotesFile $NotesFile -Version $Version *> $log
if (-not $?) { Get-Content $log -Tail 15; throw "publish failed (log: $log)" }
Get-Content $log | Select-String -Pattern '^built |releases/tag' | ForEach-Object { $_.Line }
