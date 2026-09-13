# WP04 ingestion and first activity report

Status: implemented as a development slice, not accepted for employee deployment.

## Package

WP04 durable ingestion and aggregation. This report covers the first development path from collector queue replay to PostgreSQL ingestion, dirty-day recomputation, and the daily dashboard contract. Production device enrollment, production authentication, schedules, operational output imports, and a real background worker remain pending.

## Changed paths

- `src/server/WfmAnalytics.Server/Database/Migrations/0003_activity_ingestion.sql`
- `src/server/WfmAnalytics.Server/Database/Migrations/0004_activity_daily_reports.sql`
- `src/server/WfmAnalytics.Server/Database/Migrations/0005_runtime_activity_grants.sql`
- `src/server/WfmAnalytics.Server/Modules/Analytics/ActivityDailyAggregator.cs`
- `src/server/WfmAnalytics.Server/Modules/Ingestion/ActivityIngestionEndpoints.cs`
- `src/server/WfmAnalytics.Server/Modules/Demo/DailyReportEndpoints.cs`
- `src/server/WfmAnalytics.Server/Bootstrap/ServerApplication.cs`
- `src/collector/WfmAnalytics.Collector/Program.cs`
- `src/web/src/main.tsx`
- `tests/server/WfmAnalytics.Server.Tests/Program.cs`

## Implemented

- Added PostgreSQL tables for `ingestion.ingestion_receipts` and `ingestion.activity_envelopes`.
- Added `POST /api/v1/activity/batches`.
- Added 512 KB request limit, 200-event batch limit, required batch metadata, seven-day late window, five-minute future clock-skew rejection, and slice ordering/bounds validation.
- Added per-event outcomes: `accepted`, `already_accepted`, and `rejected`.
- Added checksum conflict rejection for changed replay of the same event ID.
- Added collector-instance sequence conflict rejection.
- Preserved accepted raw event payload as `jsonb` with a SHA-256 checksum.
- Added a development enrollment mapping for `dddddddd-dddd-4ddd-8ddd-dddddddddddd` to the synthetic team and live test employee.
- Added dirty employee-day rows when a mapped enrollment accepts new activity.
- Added a conservative activity daily recompute path that preserves unknown gaps and keeps completed output unavailable.
- Added activity daily report storage and made `GET /api/v1/teams/{teamId}/daily` prefer activity-backed reports over the static synthetic fixture for the same team/date.
- Added collector `--upload-evidence-queue` to POST encrypted queued envelopes and acknowledge only `accepted` or `already_accepted` outcomes.
- Updated dashboard copy so it distinguishes static synthetic data from live development evidence.

## Checks actually run

- `dotnet build src/server/WfmAnalytics.Server.slnx --no-restore`: passed.
- `dotnet run --no-build --project tests/server/WfmAnalytics.Server.Tests` with `WFM_TEST_DATABASE` pointed at an isolated project-local PostgreSQL database on `127.0.0.1:55432`: passed, including migration idempotence, readiness, daily report authorization, malformed JSON, unsupported schema, oversized body, future event rejection, old event rejection, first ingestion accept, duplicate replay, changed-checksum rejection, collector-sequence conflict rejection, aggregation, and daily report retrieval.
- `dotnet run --no-build --project tests/collector/WfmAnalytics.Collector.Tests`: passed ten collector test groups.
- `node --experimental-strip-types --test ../../tests/web/*.test.ts` from `src/web` using the bundled Node runtime: passed four web report parser tests.
- `pnpm install` and `pnpm run build` from `src/web` using the bundled PNPM runtime: passed production TypeScript/Vite build.
- Short real Windows smoke: `--evidence-live --duration-seconds 5` wrote one encrypted queued envelope; `--upload-evidence-queue --server-url http://127.0.0.1:5080 --enrollment-id dddddddd-dddd-4ddd-8ddd-dddddddddddd` returned one `accepted` outcome and acknowledged the queue item; the daily report endpoint returned `synthetic: false` with `employee-live-test`, 5 active seconds, 40 unknown seconds, fresh source status, and output unavailable.
- User-run clean physical test: the user ran the current end-to-end local flow after the seed path and upload-command fixes and reported the test was clean. This marks the Phase 1 physical validation item complete for the development slice.

## Checks not run

- Lock, sleep/resume, restart, fast-user-switching, endpoint-security, and resource-duration G1 evidence remain pending on a controlled Windows test device.
- Browser visual review of the live activity-backed dashboard has not been rerun after the dashboard copy change.

## Open findings

- P1: The ingestion endpoint currently uses a development `X-WFM-Enrollment-Id` header. Production device credential validation and enrollment binding remain required before employee deployment.
- P1: The current recompute path runs inline after ingestion for the development slice. The production worker claim/retry/generation model from the baseline still needs implementation.
- P1: The activity report uses a fixed development enrollment mapping; real roster, assignment, schedule, timezone, and approved-hours data remain WP05/WP06 work.
- P2: The server accepts sensitive fields inside raw payloads when the collector policy sends them; production access audit, retention, and role boundaries for those fields remain required before any real employee rollout.
- P2: ASP.NET emits noisy Windows Data Protection key-ring warnings in this local host profile even though this slice does not use production cookies or protected server credentials; this should be cleaned up when WP05 identity is implemented.
