# WP03 Windows evidence guide

Status: manual evidence procedure for the controlled Windows feasibility gate.

Run these commands from PowerShell in the repository root:

```powershell
cd C:\dev\freelance\wfm-analytics

$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_HOME='C:\dev\freelance\wfm-analytics\.local\dotnet-home'
$env:NUGET_PACKAGES='C:\dev\freelance\wfm-analytics\.local\nuget'
$env:APPDATA='C:\dev\freelance\wfm-analytics\.local\appdata'
$env:LOCALAPPDATA='C:\dev\freelance\wfm-analytics\.local\localappdata'

& 'C:\Program Files\dotnet\dotnet.exe' build src/server/WfmAnalytics.Server.slnx --no-restore
New-Item -ItemType Directory -Force tmp
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --evidence-live --duration-seconds 180 --interval-ms 1000 --out tmp\wp03-live-evidence.json --queue-root tmp\wp03-live-queue
```

During the 180-second run:

1. Focus Microsoft Teams for about 20 seconds.
2. Focus Chrome for about 20 seconds.
3. Focus another ordinary work application for about 20 seconds.
4. Lock the workstation for about 20 seconds, then unlock.
5. If safe on the test device, sleep/resume or disconnect/reconnect once during a separate run. Do not do this on a machine where sleep would interrupt important work.

Inspect the collected slices:

```powershell
$report = Get-Content tmp\wp03-live-evidence.json | ConvertFrom-Json
$report.batch.events | ForEach-Object { $_.slices } | Select-Object start_offset_ms,end_offset_ms,application_id,state
$report.queue
$report.resources | Select-Object -First 5
$report.sample_gaps
```

Verify restart-style replay from a separate collector process:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --replay-evidence-queue --queue-root tmp\wp03-live-queue
```

Expected evidence:

- focused applications appear as executable basenames such as `teams.exe`, `ms-teams.exe`, `chrome.exe`, or `notepad.exe`;
- no window titles, URLs, text, screenshots, clipboard data, or full paths appear in the report;
- `queue.key_protection` is `windows-dpapi-current-user`;
- encrypted payload count equals replayed payload count;
- `replay_identity_matches` is `true`;
- `plaintext_leak_detected` is `false`;
- resource samples are present for the collector process;
- sleep/resume or long interruption appears as a sample gap instead of fabricated activity.

Record the command output and keep `tmp\wp03-live-evidence.json` with the review notes. This evidence supports WP03 only; it does not authorize employee deployment or close the full G1 gate without independent review and the remaining controlled-device checks.
