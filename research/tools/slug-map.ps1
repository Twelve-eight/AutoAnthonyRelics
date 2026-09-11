$r = [regex]'([A-Za-z0-9]|\G(?!^))([A-Z])'
$wsre = [regex]'\s+'
$spre = [regex]'[^A-Z0-9_]'

function Slug([string]$t) {
    $s = $r.Replace($t.Trim(), '$1_$2')
    $s = $wsre.Replace($s.ToUpperInvariant(), '_')
    return $spre.Replace($s, '')
}

function TitleSnake([string]$t) {
    $parts = $t.Split('_')
    $o = @()
    foreach ($p in $parts) {
        if ($p.Length -eq 0) { $o += ''; continue }
        $o += $p.Substring(0, 1).ToUpperInvariant() + $p.Substring(1).ToLowerInvariant()
    }
    return ($o -join '_')
}

$lines = @()
foreach ($n in (Get-Content -LiteralPath 'G:\omp works\.tmp\names.txt')) {
    if ([string]::IsNullOrWhiteSpace($n)) { continue }
    $nn = $n.Trim()
    $new = $nn
    if ($nn.Contains('_')) { $new = TitleSnake $nn }
    $lines += ($nn + "`t" + (Slug $nn) + "`t" + $new + "`t" + (Slug $new))
}
$lines | Set-Content -LiteralPath 'G:\omp works\.tmp\slug-map.tsv' -Encoding UTF8
