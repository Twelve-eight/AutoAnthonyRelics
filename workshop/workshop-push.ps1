param(
    [Parameter(Mandatory=$true)][string]$Vdf,
    [string]$GuardCode
)
# Universal Steam Workshop pusher. Login via STEAM_ACCOUNT / STEAM_PASSWORD env
# vars; optional -GuardCode passes the one-time Steam Guard code inline
# (email/mobile code; steamcmd caches the sentry in .tooling\steamcmd\config\
# after the first successful login, after which no code is needed).
# Arguments are passed as an ARRAY (not a space-split string) so VDF paths with
# spaces and passwords with spaces survive intact.
$exe = "G:\omp works\.tooling\steamcmd\steamcmd.exe"
$args = @("+login", $env:STEAM_ACCOUNT, $env:STEAM_PASSWORD)
if ($GuardCode) { $args += $GuardCode }
$args += @("+workshop_build_item", $Vdf, "+quit")
& $exe @args 2>&1 |
    Tee-Object -FilePath "G:\omp works\.tmp\workshop-push-out.txt" |
    Select-String -Pattern "PublishFileID|Success|ERROR|Waiting for confirmation|Logging in user|Failed" |
    ForEach-Object { $_.Line }
