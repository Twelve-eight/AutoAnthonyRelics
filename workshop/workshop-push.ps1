param(
    [Parameter(Mandatory=$true)][string]$Vdf,
    [string]$GuardCode
)
# Universal Steam Workshop pusher for all mods (STEAMCMD-ACCESS.md).
# Tries login; if a Steam Guard code is provided it is passed inline (instant, no prompt).
$cmd = "+login $env:STEAM_ACCOUNT $env:STEAM_PASSWORD"
if ($GuardCode) { $cmd = "+login $env:STEAM_ACCOUNT $env:STEAM_PASSWORD $GuardCode" }
$full = "$cmd +workshop_build_item `"$Vdf`" +quit"
& "G:\omp works\.tooling\steamcmd\steamcmd.exe" $full.Split(' ') 2>&1 |
    Tee-Object -FilePath "G:\omp works\.tmp\workshop-push-out.txt" |
    Select-String -Pattern "PublishFileID|Success|ERROR|Waiting for confirmation" |
    ForEach-Object { $_.Line }
