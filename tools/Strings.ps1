# Translations without reading the 140 KB language files.
#   powershell -ExecutionPolicy Bypass -File tools\Strings.ps1 -Missing
#       runs --strings (rewrites Lang\en.json from the scratch build), then writes every English text a language lacks to
#       %TEMP%\ee_missing.json  as {"English text": "", ...} (the same list for all languages) and prints the counts.
#   powershell -ExecutionPolicy Bypass -File tools\Strings.ps1 -Add <file.json>
#       file = {"pt-BR": {"English": "tradução", ...}, "es": {...}, ...}: appends the entries each language lacks
#       (never changes an existing one), keeping the file UTF-8 without BOM.
param([switch]$Missing, [string]$Add = '')
$root = Split-Path -Parent $PSScriptRoot
$lang = Join-Path $root 'Lang'
$utf8 = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Web.Extensions
$json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$json.MaxJsonLength = 64MB
function Read-Lang($path) { $json.DeserializeObject([IO.File]::ReadAllText($path, $utf8)) }
function Esc($s) { $j = $json.Serialize([string]$s); foreach ($c in "'", '<', '>', '&') { $j = $j.Replace(('\u{0:x4}' -f [int][char]$c), $c) }; $j }   # a JSON string literal, readable

if ($Missing) {
    $exe = Join-Path $env:TEMP 'ee-dev-build2\EasyEngine.exe'
    Start-Process $exe -ArgumentList '--strings' -WorkingDirectory (Split-Path $exe) -Wait -WindowStyle Hidden
    $en = Read-Lang (Join-Path $lang 'en.json')
    $all = [ordered]@{}
    Get-ChildItem $lang -Filter '*.json' | Where-Object Name -ne 'en.json' | ForEach-Object {
        $t = Read-Lang $_.FullName
        $miss = @($en.Keys | Where-Object { -not $t.ContainsKey($_) })
        foreach ($k in $miss) { $all[$k] = '' }
        "{0,-8} {1} missing" -f $_.BaseName, $miss.Count
    }
    $body = ($all.Keys | ForEach-Object { '  ' + (Esc $_) + ': ""' }) -join ",`n"
    [IO.File]::WriteAllText((Join-Path $env:TEMP 'ee_missing.json'), "{`n$body`n}`n", $utf8)
    "list: $env:TEMP\ee_missing.json ($($all.Count) texts)"
}

if ($Add) {
    $new = $json.DeserializeObject([IO.File]::ReadAllText((Resolve-Path $Add), $utf8))
    foreach ($code in $new.Keys) {
        $path = Join-Path $lang "$code.json"
        if (-not (Test-Path $path)) { "skip ${code}: no Lang\$code.json"; continue }
        $have = Read-Lang $path
        $lines = @(foreach ($k in $new[$code].Keys) { if (-not $have.ContainsKey($k) -and $new[$code][$k]) { '  ' + (Esc $k) + ': ' + (Esc $new[$code][$k]) } })
        if (-not $lines.Count) { "{0,-8} nothing new" -f $code; continue }
        $text = [IO.File]::ReadAllText($path, $utf8).TrimEnd()
        $text = $text.Substring(0, $text.Length - 1).TrimEnd()   # drop the closing }
        $text += ",`n" + ($lines -join ",`n") + "`n}`n"
        [IO.File]::WriteAllText($path, $text, $utf8)
        "{0,-8} +{1}" -f $code, $lines.Count
    }
}
