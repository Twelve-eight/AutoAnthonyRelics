# Deferred deploy for QuriousCraftingRelics 0.5.5: waits for the game to
# exit (DLLs locked while running), then deploys to the three targets and
# verifies hashes. Runs unattended.
$src = "G:\omp works\AutoAnthonyRelics\mod\.godot\mono\temp\bin\Release"
$modDir = "G:\omp works\AutoAnthonyRelics\mod"
$targets = @(
    "G:\steam\steamapps\common\Slay the Spire 2\mods\QuriousCraftingRelics",
    "G:\steam\steamapps\common\Slay the Spire 2\mods_disabled\QuriousCraftingRelics",
    "G:\omp works\AutoAnthonyRelics\workshop\content\QuriousCraftingRelics"
)
Write-Output "waiting for SlayTheSpire2.exe to exit..."
while (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) {
    Start-Sleep -Seconds 20
}
Start-Sleep -Seconds 3
foreach ($t in $targets) {
    if (-not (Test-Path $t)) { Write-Output "MISSING TARGET DIR: $t"; continue }
    Copy-Item "$modDir\QuriousCraftingRelics.json" $t -Force
    Copy-Item "$src\QuriousCraftingRelics.dll" $t -Force
    Copy-Item "$src\QuriousCraftingRelics.pdb" $t -Force -ErrorAction SilentlyContinue
    $pck = "$src\QuriousCraftingRelics.pck"
    if (Test-Path $pck) { Copy-Item $pck $t -Force }
    else { Copy-Item "$modDir\QuriousCraftingRelics.pck" $t -Force -ErrorAction SilentlyContinue }
    Write-Output "deployed -> $t"
}
# verify
$ok = $true
$hash = (Get-FileHash "$src\QuriousCraftingRelics.dll" -Algorithm MD5).Hash
foreach ($t in $targets) {
    $h = (Get-FileHash "$t\QuriousCraftingRelics.dll" -Algorithm MD5 -ErrorAction SilentlyContinue).Hash
    $v = (Get-Content "$t\QuriousCraftingRelics.json" -Raw | ConvertFrom-Json).version
    if ($h -ne $hash -or $v -ne "0.5.5") { $ok = $false; Write-Output "MISMATCH $t dll=$($h -eq $hash) version=$v" }
}
if ($ok) { Write-Output "DEFERRED DEPLOY OK: 0.5.5, all targets hash+version verified" } else { Write-Output "DEFERRED DEPLOY FAILED" }
