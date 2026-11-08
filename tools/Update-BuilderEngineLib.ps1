<#
.SYNOPSIS
  Rebuilds BuilderEngine's UI-free library (BuilderEngine.Script + BuilderEngine.Plugin) and copies the DLLs into
  lib\BuilderEngine\, which EasyEngine references as binaries (the EasyEngine ⇄ BuilderEngine link).
  Run it after BuilderEngine's link or runtime changes, then build and test EasyEngine and commit the new DLLs.
.EXAMPLE
  tools\Update-BuilderEngineLib.ps1
  tools\Update-BuilderEngineLib.ps1 -BuilderRepo C:\src\BuilderEngine
#>
param(
    # the BuilderEngine repo (default: a "BuilderEngine" folder next to this repo)
    [string]$BuilderRepo = (Join-Path $PSScriptRoot '..\..\BuilderEngine')
)
$ErrorActionPreference = 'Stop'
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'lib\BuilderEngine'
$script = Join-Path $BuilderRepo 'tools\Build-LinkLib.ps1'
if (-not (Test-Path $script)) { throw "BuilderEngine repo not found at $BuilderRepo (pass -BuilderRepo <folder>)." }
& $script -Target $target
Remove-Item (Join-Path $target '*.xml') -ErrorAction SilentlyContinue # docs aren't needed to build or run
Get-ChildItem $target -Filter *.dll | ForEach-Object { '{0,-28} {1,9:N0} bytes' -f $_.Name, $_.Length }
