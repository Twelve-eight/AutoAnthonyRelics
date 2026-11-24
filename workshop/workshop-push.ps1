param(
    [Parameter(Mandatory=$true)][string]$Vdf,
    [string]$GuardCode,
    [switch]$Force,
    [switch]$GuardsOnly,
    [switch]$SkipRefresh
)
# Per-repo Workshop push shell for AutoAnthonyRelics / Qurious Crafting - Relics (finding R03-02).
#
# This file is a THIN FORWARDING SHELL. The real work - payload refresh, staged-bytes
# held-back scan, VDF quote/backslash, byte-size, description-drift and Markdown guards,
# the login throttle, and the post-push verification - all live in the controlled entry:
#     G:\omp works\.tooling\workshop-push-all.ps1
# Calling steamcmd directly from here would bypass every one of them, so it no longer does.
#
# The entry uploads only the ONE row whose Name contains -Only (it exits 2 unless exactly
# one row matches), while its pre-flight guards still run over ALL rows. That is why a
# failure here can never degrade into a full eight-item upload.
#
# ASCII only, PowerShell 5.1 compatible.

# ---- TARGET MAPPING (this repo's own VDF + its unique -Only row substring) ----------
# Both literals are explicit per repo and are never derived at runtime. If this repo's VDF
# path or its row name in workshop-push-all.ps1 ever changes, update BOTH sides by hand.
$TargetVdf  = "G:\omp works\Sts\AutoAnthonyRelics\workshop\workshop_upload.vdf"
$OnlyTarget = "Qurious"
$Entry      = "G:\omp works\.tooling\workshop-push-all.ps1"
# ------------------------------------------------------------------------------------

# One-target-per-shell: refuse any -Vdf that is not THIS repo's VDF, before anything runs.
# Normalising both sides first means forward slashes, mixed case, a doubled separator or an
# embedded .. cannot slip past the comparison.
$given = $Vdf
$expected = $TargetVdf
try { $given = [System.IO.Path]::GetFullPath($Vdf) } catch { }
try { $expected = [System.IO.Path]::GetFullPath($TargetVdf) } catch { }
if (-not $given.Equals($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Output "REFUSING TO PUSH: -Vdf is not this repo's VDF."
    Write-Output ("  given    : {0}" -f $Vdf)
    Write-Output ("  expected : {0}" -f $TargetVdf)
    Write-Output ("  This shell publishes only '{0}'. Use that repo's own shell, or call the" -f $OnlyTarget)
    Write-Output "  controlled entry directly when you really mean a different item."
    exit 11
}

if (-not (Test-Path $Entry)) {
    Write-Output ("FATAL: controlled entry not found at {0} - refusing to push unguarded." -f $Entry)
    exit 12
}

# Arguments are passed as an ARRAY so paths and codes containing spaces survive intact.
$fwd = @("-Only", $OnlyTarget)
if ($GuardCode) { $fwd += @("--2FACode", $GuardCode) }
if ($Force) { $fwd += "-Force" }
if ($GuardsOnly) { $fwd += "-GuardsOnly" }
if ($SkipRefresh) { $fwd += "-SkipRefresh" }

Write-Output ("Forwarding to the guarded entry (refresh + held-back byte scan + login throttle all live there): {0} -Only {1}" -f $Entry, $OnlyTarget)

& $Entry @fwd
# Propagated unchanged: a single-target failure must never look like success.
exit $LASTEXITCODE
