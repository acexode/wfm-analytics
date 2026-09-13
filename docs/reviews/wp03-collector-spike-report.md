# WP03 collector spike report

Status: in progress, not gate-accepted.

## Package

WP03 Windows feasibility. Owner: endpoint role. Reviewer still required: platform and quality. The package objective is to prove the bounded collector state machine, encrypted local durability, replay identity, and real Windows behavior before any employee deployment.

## Changed paths

- `src/collector/WfmAnalytics.Collector/`
- `tests/collector/WfmAnalytics.Collector.Tests/`
- `src/server/WfmAnalytics.Server.slnx`
- `NuGet.Config`
- `tools/dev-env.sh`
- `tools/verify.sh`
- `docs/04-project-status-and-decisions.md`

## Decisions and assumptions

- The first collector code is a .NET prototype inside the approved stack and uses no paid service, runtime AI dependency, browser extension, screenshots, window titles, URLs, typed content, clipboard data, input counts, or production employee data.
- State accumulation is deterministic and uses 60-second UTC buckets, ordered nonoverlapping slices, lock precedence, five-minute idle boundary semantics, normalized executable basenames only, collector instance ID, sequence, and event ID.
- The local queue spike uses AES-256-GCM encrypted payload files, a Windows DPAPI-protected current-user queue key, bounded seven-day or 100 MB pending retention, and a compact loss manifest so durability and replay can be tested without adding a new package dependency. The accepted baseline still calls for SQLite storage before pilot hardening; that remains pending.
- The Windows probe calls `GetLastInputInfo`, foreground-window process lookup, process session ID lookup, Windows Terminal Services session-state lookup, and a conservative interactive-desktop lock heuristic only on Windows. The live evidence report now records collector-observed session start/end, initial lock state, lock/unlock transitions, and current WTS session state seen during the run. Service-control session-change code mapping is implemented as the foundation for a managed service helper, but managed deployment still needs company tooling and controlled-device proof.
- The expanded sensitive capture policy has been introduced as a default-off setting. The current spike can include window titles, full executable paths, clipboard text, and desktop screenshots only when the evidence command explicitly enables those settings; browser URLs and typed text are represented in policy but not implemented as raw capture paths yet.

## Acceptance evidence produced

- Added collector tests for application identifier normalization, idle threshold boundary behavior, lock precedence, ordered/capped slices, partial-bucket truncation at the last healthy sample, encrypted replay without plaintext application leakage, and JSON contract naming.
- Added a live evidence command that captures foreground app slices, resource samples, sample gaps, encrypted queue replay, and ciphertext plaintext inspection into one JSON report.
- Added policy switches for expanded sensitive capture and tests proving sensitive fields are dropped under the default minimum policy.
- Added policy-gated clipboard text capture and encrypted BMP screenshot artifact capture for physical Windows evidence runs. Screenshot artifacts now stay encrypted under the queue root until explicitly exported for review.
- Added `session_events` to live evidence reports, covering collector-observed session start/end and lock-state transitions in the interactive session.
- Added WTS session snapshots and a service session-change tracker foundation for logon/logoff, lock/unlock, console connect/disconnect, and remote connect/disconnect reasons.
- Added queue retention enforcement, local loss manifest rows, collector health output, and tamper-resistance readiness controls.
- Added a separate queue replay command for process-restart-style verification under the same Windows user.
- Added collector project to the existing solution and added the collector test project to `tools/verify.sh`.
- Added a repository-local `NuGet.Config` and local environment defaults so .NET validation can run without reading protected user-profile NuGet config.
- Updated `tools/dev-env.sh` to discover the standard Windows .NET install location when a project-local SDK is absent.

## Checks actually run

- `dotnet restore src/server/WfmAnalytics.Server.slnx --configfile NuGet.Config`: passed after explicit network approval for NuGet package restore.
- `dotnet build src/server/WfmAnalytics.Server.slnx --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet run --no-build --project tests/collector/WfmAnalytics.Collector.Tests`: passed eight collector test groups.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --self-test`: passed; produced a replayed encrypted queue envelope summary.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --evidence-live --duration-seconds 6 --interval-ms 1000 --out tmp\wp03-smoke-evidence.json --queue-root tmp\wp03-smoke-queue-dpapi`: passed; wrote one smoke evidence event, six resource samples, one encrypted payload, and no plaintext leak.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --replay-evidence-queue --queue-root tmp\wp03-smoke-queue-dpapi`: passed; replayed one encrypted payload with matching identity and `windows-dpapi-current-user` key protection.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --evidence-live --duration-seconds 5 --interval-ms 1000 --out tmp\wp03-expanded-smoke-evidence.json --queue-root tmp\wp03-expanded-smoke-queue --allow-window-titles --allow-full-paths`: passed; wrote an expanded-policy smoke report, one encrypted payload, and no plaintext leak.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --replay-evidence-queue --queue-root tmp\wp03-expanded-smoke-queue`: passed; replayed one expanded-policy encrypted payload with matching identity and `windows-dpapi-current-user` key protection.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --evidence-live --duration-seconds 3 --interval-ms 1000 --out tmp\wp03-screenshot-clipboard-smoke.json --queue-root tmp\wp03-screenshot-clipboard-queue --allow-screenshots --allow-clipboard`: passed; clipboard text was captured in the evidence JSON on this host, while screenshot capture produced no artifact from the Codex background execution context.
- Collector tests include an encrypted artifact-store check proving screenshot-like bytes are not visible in ciphertext and can be exported on demand.
- `dotnet run --no-build --project tests/collector/WfmAnalytics.Collector.Tests`: passed eleven collector test groups after adding session event tracking.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --evidence-live --duration-seconds 5 --interval-ms 1000 --out tmp\phase2-session-evidence.json --queue-root tmp\phase2-session-queue`: passed; wrote one event, five resource samples, no sample gaps, and three session events: `session_observed_start`, `lock_state_initial_unlocked`, and `session_observed_end`.
- `dotnet build tests/collector/WfmAnalytics.Collector.Tests/WfmAnalytics.Collector.Tests.csproj --no-restore`: passed after adding WTS session snapshots, queue retention, loss manifest, collector health, and service session-change mapping.
- `dotnet run --no-build --project tests/collector/WfmAnalytics.Collector.Tests`: passed fifteen collector test groups after adding queue retention, WTS session-state transitions, and Windows session-change mapping.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --evidence-live --duration-seconds 5 --interval-ms 1000 --out tmp\phase2-complete-evidence.json --queue-root tmp\phase2-complete-queue`: passed; wrote one event, five resource samples, no sample gaps, no plaintext leak, and four session events including the WTS session-state event.
- `dotnet run --no-build --project src/collector/WfmAnalytics.Collector -- --collector-health --queue-root tmp\phase2-complete-queue`: passed; reported one pending encrypted payload, no loss records, active WTS session state, and implemented/pending tamper-resistance controls.
- User manually tested live foreground app switching with real applications and reported the behavior verified.

## Checks not run

- `python -m unittest discover -s tests/contracts -v` did not run successfully in this shell because Python 3.14 is missing the `jsonschema` dependency.
- `bash -lc "source tools/dev-env.sh && command -v dotnet && dotnet --version"` did not run because this host returned `Bash/Service/CreateInstance/E_ACCESSDENIED`.
- The server/database integration tests were not run because this turn focused on WP03 and no isolated PostgreSQL test database was started.
- Full controlled-device Windows WP03 evidence is still incomplete: documented managed-service login/logoff, sleep/resume, reboot/restart replay, fast-user switching, VPN or endpoint-security interaction, plaintext inspection of SQLite/WAL/temp files, signed update behavior, and eight-hour resource measurements remain pending.
- No ingestion endpoint was implemented in this package, so server acknowledgement and durable PostgreSQL receipt behavior remain WP04 work.

## Open findings

- P1: WP03 cannot be accepted until a controlled Windows test device runs the collector harness and records the required state-transition, replay, plaintext-inspection, and resource evidence.
- P2: Replace or extend the file-backed queue with the baseline SQLite queue before pilot hardening; this spike now covers encryption, retention caps, acknowledged-payload purge, health, and loss-manifest semantics that the SQLite implementation must preserve.
- P2: The Windows probe now covers last-input timing, foreground process basename, policy-gated window titles and full paths, process session ID, WTS session state, boot ID approximation, conservative lock inference, collector-observed session events, and service session-change reason mapping. Managed-service deployment, sleep/resume boundaries, fast-user switching, RDP/VDI modes, and endpoint-security behavior still need controlled-device tests.
- P1: Browser URL and typed text capture switches exist in policy but raw capture implementations are not yet built. These require separate security review before employee deployment.
- P2: Screenshot capture uses Windows desktop capture APIs and must be verified from the user's physical interactive desktop; the Codex background execution context did not expose a capturable desktop in the smoke run.

## Recommended next step

Continue WP03 with the managed service helper and controlled-device evidence script, then request platform and quality review of the collector semantics, local durability, deployment-hardening boundary, and privacy boundary.
