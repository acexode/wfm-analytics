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

For an expanded sensitive-capture evidence run, add only the switches that are approved for the test:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --evidence-live --duration-seconds 180 --interval-ms 1000 --out tmp\wp03-expanded-evidence.json --queue-root tmp\wp03-expanded-queue --allow-window-titles --allow-full-paths --allow-screenshots --allow-clipboard
```

The policy also accepts `--allow-browser-urls` and `--allow-typed-text`; those switches record the policy intent, but raw capture for those two fields is not implemented in this spike.

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
$report.batch.events | ForEach-Object { $_.slices } | Select-Object start_offset_ms,end_offset_ms,@{Name='screenshot';Expression={$_.sensitive.screenshot_ref}},@{Name='clipboard_chars';Expression={ if ($_.sensitive.clipboard_text) { $_.sensitive.clipboard_text.Length } else { 0 } }}
$report.artifacts
$report.queue
$report.session_events | Select-Object event_type,at,session_id,source
$report.resources | Select-Object -First 5
$report.sample_gaps
```

Inspect local collector health, retention state, loss manifest totals, WTS session state, and tamper-resistance readiness:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --collector-health --queue-root tmp\wp03-live-queue
```

Export encrypted screenshot artifacts for manual review:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --export-evidence-artifacts --queue-root tmp\wp03-expanded-queue --out tmp\wp03-screenshots-review
Get-ChildItem tmp\wp03-screenshots-review
```

Verify restart-style replay from a separate collector process:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --replay-evidence-queue --queue-root tmp\wp03-live-queue
```

Upload queued envelopes to the local development API after running server migrations and starting the API:

```powershell
$migrationPassword=(Get-Content .local\migration-password)
$appPassword=((Get-Content .local\database.env | Where-Object { $_ -like 'WFM_APP_PASSWORD=*' }) -replace 'WFM_APP_PASSWORD=','')
$env:ConnectionStrings__Primary="Host=127.0.0.1;Port=55432;Database=wfm_dev;Username=wfm_app;Password=$appPassword"
$env:ConnectionStrings__Migration="Host=127.0.0.1;Port=55432;Database=wfm_dev;Username=wfm_migrator;Password=$migrationPassword"
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5080'

& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/server/WfmAnalytics.Server -- --migrate
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/server/WfmAnalytics.Server -- --seed-development

# In a separate PowerShell window, keep the API running:
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --no-launch-profile --project src/server/WfmAnalytics.Server

# Then upload the queue:
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --upload-evidence-queue --queue-root tmp\wp03-live-queue --server-url http://127.0.0.1:5080 --enrollment-id dddddddd-dddd-4ddd-8ddd-dddddddddddd

# Upload current collector health:
& 'C:\Program Files\dotnet\dotnet.exe' run --no-build --project src/collector/WfmAnalytics.Collector -- --upload-collector-health --queue-root tmp\wp03-live-queue --server-url http://127.0.0.1:5080 --enrollment-id dddddddd-dddd-4ddd-8ddd-dddddddddddd

# Confirm the daily report now uses live development evidence:
Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/v1/teams/team-synthetic/daily?date=2026-09-13' -Headers @{'X-Development-Principal'='demo-manager'; 'X-Development-Scopes'='analytics:daily:read'} | ConvertTo-Json -Depth 8
```

Expected evidence:

- focused applications appear as executable basenames such as `teams.exe`, `ms-teams.exe`, `chrome.exe`, or `notepad.exe`;
- under the minimum policy, no window titles, URLs, text, screenshots, clipboard data, or full paths appear in the report;
- under an expanded policy, only the explicitly enabled and implemented fields appear;
- screenshot references appear as `artifact:<id>` values in `sensitive.screenshot_ref`;
- screenshot bytes are stored encrypted under `tmp\wp03-expanded-queue\artifacts\pending` and can be exported to BMP files only with `--export-evidence-artifacts`;
- clipboard text appears in `sensitive.clipboard_text` only when `--allow-clipboard` is enabled and text clipboard content is available;
- `queue.key_protection` is `windows-dpapi-current-user`;
- encrypted payload count equals replayed payload count;
- `replay_identity_matches` is `true`;
- `plaintext_leak_detected` is `false`;
- `session_events` includes `session_observed_start`, one initial lock-state event, any WTS session-state evidence available to the current process, any `workstation_locked` or `workstation_unlocked` transitions observed during the run, and `session_observed_end`;
- `--collector-health` reports pending queue count and bytes, oldest pending item, loss manifest totals, current WTS session state, and tamper-resistance controls;
- resource samples are present for the collector process;
- sleep/resume or long interruption appears as a sample gap instead of fabricated activity.
- successful upload reports `accepted` or `already_accepted` outcomes and acknowledges only those queue items;
- successful health upload reports `"status":"accepted"`;
- the daily report returns `synthetic: false` for dates with uploaded live development evidence, while completed output remains unavailable until an approved operational source is connected.

Record the command output and keep `tmp\wp03-live-evidence.json` with the review notes. This evidence supports WP03 only; it does not authorize employee deployment or close the full G1 gate without independent review and the remaining controlled-device checks.
