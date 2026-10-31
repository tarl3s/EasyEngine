# Runs the graded AI eval (--aitest) on local models from a copy of the scratch build (so a rebuild doesn't break a run),
# at low priority with a memory cap, and prints per model: the score and the failed case ids with why.
# Logs are kept as %TEMP%\ai_<tag>_<model>.log.
#   powershell -ExecutionPolicy Bypass -File tools\AiEval.ps1                         # 1.5b and 3b, every case
#   powershell -ExecutionPolicy Bypass -File tools\AiEval.ps1 -Models 3b -Only shop,combo
#   powershell -ExecutionPolicy Bypass -File tools\AiEval.ps1 -Extra '--in pt' -Tag pt   # the cases in Portuguese (or es)
param([string[]]$Models = @('1.5b', '3b'), [string]$Only = '', [string]$Extra = '', [string]$Tag = 'run', [switch]$Build)
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $env:TEMP 'ee-dev-build2'
$copy = Join-Path $env:TEMP "ee-aieval-$Tag"
if ($Build) {
    dotnet build (Join-Path $root 'EasyEngine.csproj') "-p:OutDir=$bin/" -nologo -v:q | Out-Null
    if ($LASTEXITCODE -ne 0) { "BUILD FAILED"; exit 1 }
}
if (Test-Path $copy) { Remove-Item $copy -Recurse -Force }
Copy-Item $bin $copy -Recurse -ErrorAction Stop
foreach ($m in $Models) {
    $log = Join-Path $env:TEMP 'easyengine_aitest.log'
    Remove-Item $log -ErrorAction SilentlyContinue
    $argv = @('--aitest', '--model', "qwen2.5-coder:$m", '--maxmem', '2500')
    if ($Only) { $argv += @('--only', $Only) }
    if ($Extra) { $argv += ($Extra -split ' ') }
    $p = Start-Process (Join-Path $copy 'EasyEngine.exe') -ArgumentList $argv -WorkingDirectory $copy -WindowStyle Hidden -PassThru
    $p.PriorityClass = 'BelowNormal'
    $p.WaitForExit()
    $keep = Join-Path $env:TEMP "ai_${Tag}_$m.log"
    Copy-Item $log $keep -ErrorAction SilentlyContinue
    "== $m  ($keep)"
    $lines = Get-Content $keep -Encoding UTF8
    $lines | Where-Object { $_ -match '^\s*\d+/\d+ passed' } | ForEach-Object { '  ' + $_.Trim() }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^FAIL\s+\[([^\]]+)\]') {
            $why = if ($i + 1 -lt $lines.Count) { $lines[$i + 1].Trim() } else { '' }
            '  ' + $Matches[1] + ' - ' + $why.Substring(0, [Math]::Min(160, $why.Length))
        }
    }
}
Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $env:TEMP -Directory -Filter 'EE_AiTest_*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
