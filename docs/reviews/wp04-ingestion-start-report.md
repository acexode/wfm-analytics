# WP04 ingestion start report

Status: started, not accepted.

## Package

WP04 durable ingestion and aggregation. This report covers the first ingestion slice only: activity batch receipt storage and replay outcomes. Aggregation, device enrollment, production authentication, dirty-day jobs, and report recomputation remain pending.

## Changed paths

- `src/server/WfmAnalytics.Server/Database/Migrations/0003_activity_ingestion.sql`
- `src/server/WfmAnalytics.Server/Modules/Ingestion/ActivityIngestionEndpoints.cs`
- `src/server/WfmAnalytics.Server/Bootstrap/ServerApplication.cs`
- `tests/server/WfmAnalytics.Server.Tests/Program.cs`

## Implemented

- Added PostgreSQL tables for `ingestion.ingestion_receipts` and `ingestion.activity_envelopes`.
- Added `POST /api/v1/activity/batches`.
- Added per-event outcomes: `accepted`, `already_accepted`, and `rejected`.
- Added checksum conflict rejection for changed replay of the same event ID.
- Added collector-instance sequence conflict rejection.
- Preserved accepted raw event payload as `jsonb` with a SHA-256 checksum.

## Checks actually run

- `dotnet build src/server/WfmAnalytics.Server.slnx --no-restore`: passed.
- `dotnet run --no-build --project tests/server/WfmAnalytics.Server.Tests` with `WFM_TEST_DATABASE` pointed at an isolated project-local PostgreSQL database on `127.0.0.1:55432`: passed, including migration idempotence, readiness, daily report authorization, first ingestion accept, duplicate replay, changed-checksum rejection, and collector-sequence conflict rejection.
- `dotnet run --no-build --project tests/collector/WfmAnalytics.Collector.Tests`: passed ten collector test groups.

## Checks not run

- Ingestion endpoint malformed-event, oversized-batch, old-event, and future-event rejection still need real PostgreSQL tests.
- No collector-to-server live replay has been run yet; the current server check posts synthetic batch JSON directly over HTTP.

## Open findings

- P1: The ingestion endpoint currently uses a development `X-WFM-Enrollment-Id` header. Production device credential validation and enrollment binding remain required before employee deployment.
- P1: Dirty employee-day job creation and aggregation are not implemented yet.
- P2: Late, expired, and future timestamp policy checks still need to be enforced against the baseline rules.
