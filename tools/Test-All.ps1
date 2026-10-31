# Builds into a scratch folder (never the owner's locked bin\Debug) and runs the test suites one after another, printing
# one line per suite (its log's summary) and the failed lines of the suites that failed. Exit code 1 if any failed.
#   powershell -ExecutionPolicy Bypass -File tools\Test-All.ps1                 # every suite (selftest dark + light too)
#   powershell -ExecutionPolicy Bypass -File tools\Test-All.ps1 -Quick          # fulltest + e2e only
#   powershell -ExecutionPolicy Bypass -File tools\Test-All.ps1 -Suites behtest,exprtest -NoBuild
param([string[]]$Suites = @(), [switch]$Quick, [switch]$NoBuild)
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $env:TEMP 'ee-dev-build2'
$exe = Join-Path $out 'EasyEngine.exe'
if (-not $Suites.Count) {
    $Suites = if ($Quick) { @('fulltest', 'e2e') } else { @('fulltest', 'e2e', 'behtest', 'exprtest', 'blocktest', 'aijsontest', 'pairtest', 'selftest', 'selftest-light') }
}

if (-not $NoBuild) {
    $build = dotnet build (Join-Path $root 'EasyEngine.csproj') "-p:OutDir=$out/" -nologo -v:q 2>&1
    if ($LASTEXITCODE -ne 0) { "BUILD FAILED"; $build | Select-String ' error ' | Select-Object -First 15 | ForEach-Object { $_.Line.Trim() }; exit 1 }
    "build ok"
}

$failed = 0
foreach ($s in $Suites) {
    $name = $s -replace '-light$', ''
    $argv = @("--$name") + $(if ($s -like '*-light') { @('--theme', 'light') } else { @() })
    $log = Join-Path $env:TEMP "easyengine_$name.log"
    Remove-Item $log -ErrorAction SilentlyContinue
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Start-Process $exe -ArgumentList $argv -WorkingDirectory $out -Wait -WindowStyle Hidden
    $secs = [int]$sw.Elapsed.TotalSeconds
    if (-not (Test-Path $log)) { "{0,-15} NO LOG ({1}s)" -f $s, $secs; $failed++; continue }
    $lines = Get-Content $log -Encoding UTF8
    $summary = ($lines | Where-Object { $_ -match 'passed|SELFTEST|pairtest:' } | Select-Object -Last 1)
    $ok = $summary -match 'ALL PASS|ALL CLEAN|SELFTEST PASS'
    "{0,-15} {1} ({2}s)" -f $s, $(if ($summary) { $summary.Trim() } else { '(no summary line)' }), $secs
    if (-not $ok) {
        $failed++
        $lines | Where-Object { $_ -cmatch '^\s*(FAIL|SUSPICIOUS)|\bFAIL\b' } | Select-Object -First 20 | ForEach-Object { '    ' + $_.Trim().Substring(0, [Math]::Min(220, $_.Trim().Length)) }
    }
}
Get-ChildItem $env:TEMP -Directory -Filter 'EE_*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
if ($failed) { "$failed suite(s) FAILED"; exit 1 } else { "ALL SUITES GREEN" }
