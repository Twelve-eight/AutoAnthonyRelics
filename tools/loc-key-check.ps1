# loc-key-check.ps1 - guard for the loc key form BaseLib actually resolves.
#
# BaseLib builds a config row label as {ModPrefix}{Slugify(PropertyName)}.title
# and its hover tip as ...{Slugify(PropertyName)}.hover.desc, where StringHelper.Slugify is
#   ([A-Za-z0-9]|\G(?!^))([A-Z]) -> $1_$2, whitespace -> '_', strip [^A-Z0-9_], upper-case.
# A property declared in ALL_CAPS therefore slugifies to garbage and its label falls back
# to the raw name, so every VISIBLE property needs the slugified key in every language.
#
# This script fails when a visible property lacks that key, when a loc file has a true
# duplicate key (ordinal), or when a loc file is not valid JSON. Case-variant key pairs
# are reported as information: the file intentionally keeps both spellings.
param(
    [string]$ConfigPath,
    [string]$LocalizationRoot,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ConfigPath) { $ConfigPath = Join-Path $repoRoot 'mod\Code\QuriousCraftingRelicsConfig.cs' }
if (-not $LocalizationRoot) { $LocalizationRoot = Join-Path $repoRoot 'mod\QuriousCraftingRelics\localization' }

function Get-Slug([string]$name) {
    $s = [regex]::Replace($name, '([A-Za-z0-9]|\G(?!^))([A-Z])', '$1_$2')
    $s = [regex]::Replace($s, '\s+', '_')
    $s = [regex]::Replace($s, '[^A-Z0-9_]', '', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return $s.ToUpperInvariant()
}

if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "config source not found: $ConfigPath" }
if (-not (Test-Path -LiteralPath $LocalizationRoot)) { throw "localization root not found: $LocalizationRoot" }

$lines = [System.IO.File]::ReadAllLines($ConfigPath)
$properties = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*public static (int|bool)\s+([A-Za-z0-9_]+)\s*\{\s*get;\s*set;\s*\}') {
        $name = $Matches[2]
        $hidden = $false
        for ($j = $i - 1; $j -ge 0 -and $j -gt $i - 6; $j--) {
            if ($lines[$j] -match '^\s*\[') {
                if ($lines[$j] -match 'ConfigHideInUI') { $hidden = $true }
            } else { break }
        }
        $properties += [pscustomobject]@{ Name = $name; Hidden = $hidden; Slug = (Get-Slug $name) }
    }
}
if ($properties.Count -eq 0) { throw "no config properties parsed from $ConfigPath" }

$failures = @()
$languageDirs = @()
foreach ($dir in (Get-ChildItem -LiteralPath $LocalizationRoot -Directory | Sort-Object Name)) {
    $candidate = Join-Path $dir.FullName 'settings_ui.json'
    if (Test-Path -LiteralPath $candidate) { $languageDirs += $dir } else { Write-Output "[$($dir.Name)] skipped: no settings_ui.json (nothing to check)" }
}
if ($languageDirs.Count -eq 0) { throw "no language directory with settings_ui.json under $LocalizationRoot" }
$languages = $languageDirs
foreach ($lang in $languages) {
    $file = Join-Path $lang.FullName 'settings_ui.json'
    $text = [System.IO.File]::ReadAllText($file)
    try { $null = $text | ConvertFrom-Json -AsHashtable } catch { $failures += "[$($lang.Name)] invalid JSON: $($_.Exception.Message)" }

    $keys = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($text, '"(?<k>[^"]+)"\s*:')) { $keys.Add($m.Groups['k'].Value) }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $trueDuplicates = @()
    foreach ($k in $keys) { if (-not $seen.Add($k)) { $trueDuplicates += $k } }

    $missingVisible = @()
    $hiddenWithoutKey = 0
    foreach ($p in $properties) {
        $key = 'QURIOUSCRAFTINGRELICS-' + $p.Slug + '.title'
        if ($seen.Contains($key)) { continue }
        if ($p.Hidden) { $hiddenWithoutKey++ } else { $missingVisible += ($p.Name + ' -> ' + $key) }
    }

    $casePairs = 0
    foreach ($group in ($keys | Group-Object { $_.ToUpperInvariant() })) {
        $distinct = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
        foreach ($k in $group.Group) { [void]$distinct.Add($k) }
        if ($distinct.Count -gt 1) { $casePairs++ }
    }

    Write-Output ("[{0}] keys={1} distinct={2} trueDuplicates={3} caseVariantPairs={4} visibleMissing={5} hiddenMissing={6}" -f `
        $lang.Name, $keys.Count, $seen.Count, $trueDuplicates.Count, $casePairs, $missingVisible.Count, $hiddenWithoutKey)
    if (-not $Quiet) {
        $missingVisible | ForEach-Object { Write-Output ("    MISSING visible loc key: " + $_) }
        $trueDuplicates | Select-Object -First 10 | ForEach-Object { Write-Output ("    DUPLICATE key: " + $_) }
    }
    if ($trueDuplicates.Count -gt 0) { $failures += "[$($lang.Name)] $($trueDuplicates.Count) duplicate key(s)" }
    if ($missingVisible.Count -gt 0) { $failures += "[$($lang.Name)] $($missingVisible.Count) visible config row(s) without the slugified loc key" }
}

if ($failures.Count -gt 0) {
    Write-Output ''
    $failures | ForEach-Object { Write-Output ("FAIL " + $_) }
    Write-Output "loc-key-check: $($failures.Count) failure(s)"
    exit 1
}
Write-Output ''
Write-Output "loc-key-check: OK ($($properties.Count) properties checked, $($languages.Count) language(s))"
exit 0