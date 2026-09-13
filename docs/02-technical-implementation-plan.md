# BPO Workforce Operations Analytics Technical Implementation Plan

Version 1.0 | 12 September 2026 | Status: implementation baseline pending environment evidence

## 1 Architecture decision

Use a modular monolith: a C# Windows session collector, an ASP.NET Core API and background worker, a React and TypeScript web interface, and PostgreSQL. Start with .NET 10 LTS and a supported PostgreSQL major selected against the company's deployment image. Pin exact package and image versions during setup and maintain security updates. .NET support status is documented by Microsoft [S2]. PostgreSQL uses a permissive open-source license [S1]. Neither fact eliminates compute, support, or maintenance costs.

The browser communicates with the API over HTTPS. The collector sends bounded summary batches over HTTPS to the same service. The API commits accepted records to PostgreSQL before acknowledging them. A worker claims database jobs and builds daily aggregates; the dashboard queries those aggregates. The initial worker can run in the same deployable application with separate execution and database connection limits. Split its process only when operational evidence requires it.

The Linux deployment contains the application, PostgreSQL, and a TLS reverse proxy or existing company ingress. Use a simple service or container deployment compatible with company operations. There is no Kubernetes, Kafka, Redis, ClickHouse, separate data warehouse, or external LLM in the first release. Use an existing company-controlled backup destination on a separate failure domain.

Desktop session helper -> HTTPS API -> PostgreSQL raw summaries -> aggregation worker -> daily read models -> management web interface.

Approved CSV exports -> validated import staging -> revisioned operational facts -> the same read models.

## 2 Deployment and identity

The first infrastructure candidate is an existing Linux VM with 4 vCPU, 16 GB RAM, and 200 GB SSD, plus independent backup capacity. This is a benchmark starting point, not a sizing guarantee. Reserve capacity for operating system, PostgreSQL WAL, indexes, imports, temporary query work, logs, backups in transit, and growth. Do not place the only backup on this VM.

Use the existing company OIDC identity provider when available. The web application uses server-managed secure, HttpOnly session cookies with CSRF protection for writes. Authorization is evaluated against database scopes, not merely identity-provider roles. If no suitable provider exists, the lead designs a bounded local-identity fallback and reviews its password reset and MFA requirements before the employee pilot; development can use seeded synthetic identities.

Device authentication is separate from manager login. A short-lived single-use enrollment token binds an approved device, immutable Windows user SID, and company employee in an enrollment. A physical computer may hold several user enrollments. The server issues a per-enrollment credential protected to the enrolled Windows user; support rotation, expiry, and revocation. The server derives enrollment, employee, and device identity from that credential and rejects spoofed body identifiers. Every event and receipt carries enrollment_id. Rotation preserves enrollment identity; reassignment creates a new enrollment and leaves historical ownership unchanged. Shared-device and fast-user-switch behavior must pass the Windows gate before those modes are enabled. No shared fleet secret is embedded in the binary.

Prefer existing VPN access or an approved company gateway. Do not expose PostgreSQL publicly. Configure HTTPS, firewall limits, request size limits, separate migration and runtime database roles, and least-privilege backup access. SSO logout, account disabling, and device revocation must have explicit propagation tests.

## 3 Windows collection design

Run the collector in the interactive user session. A Windows service alone is not assumed to see user-session foreground activity. Use a minimal session helper; add a privileged installation or update service only if company deployment tooling cannot provide the required capability. Never add a kernel driver or intrusive process-protection scheme.

Use supported Windows interfaces for foreground-process and last-input observations; evaluate session-specific behavior on real Windows hardware and RDP or VDI if present [S3]. Resolve a normalized executable basename or allowlisted application identifier, never full paths containing personal data or window titles. Poll coarse state at an initial five-second cadence and observe lock, sleep, resume, logon, and logoff transitions. Document that brief application changes between samples may be missed.

Accumulate measured state durations into UTC-aligned 60-second buckets. Each bucket has one stable envelope with time-positioned application and state slices. Each slice carries start_offset_ms and end_offset_ms relative to the bucket UTC start, application_id, and state. Offsets are ordered, nonoverlapping, and bounded by the bucket. Preserve unobserved gaps; the sum of slices cannot exceed measured observed time. Treat timing as precise to the documented sampling resolution, not as exact reconstruction of unseen transitions. Collection cadence, bucket duration, and upload interval are different settings. Upload once per minute with random jitter; do not upload individual input events.

For the five-minute idle threshold, mark inactive from the point the elapsed time since last input reaches five minutes until new input or another state transition. Do not retroactively relabel the preceding five minutes. Screen lock is inactive immediately. Sleep and collector absence produce missing evidence rather than fabricated idle time. Cap final partial buckets at the last healthy sample; never fill a multi-hour sleep interval from one observation.

Use a monotonic clock for durations and UTC wall time for alignment. Include boot ID, session ID, sequence, client timestamp, received timestamp, and detected clock discontinuities. A clock jump or uncertain interval is flagged or quarantined; NTP is not used as proof that a client is truthful. For concurrent sessions, server reconciliation detects overlap; it does not sum durations beyond elapsed time or silently choose the most active device.

Application classification runs on the server using an effective-dated rule set and its ID is attached to derived results. The same application can serve different business roles. Browser activity remains generic in the first release; website-specific reporting requires a separately scoped later integration.

## 4 Local durability and transfer

Persist each completed bucket before transmission. SQLite holds encrypted payload blobs; use an authenticated-encryption key protected by Windows DPAPI in the collector's approved user context [S4]. Standard SQLite is not assumed to encrypt its database by itself. Keep content-free metadata minimal and verify that plaintext does not leak into WAL, temporary files, crash output, or diagnostic logs. Key lifecycle and recovery behavior are tested during agent upgrades and OS-profile changes.

Use a random event ID plus enrollment, device, boot, session, collector_instance_id, and increasing sequence to identify a bucket. Generate a new collector_instance_id for each collector process start; retries retain their original instance and sequence. Retain the exact serialized event on retry. A request contains schema version, batch ID, agent version, policy version, and bounded events. Start with at most 200 envelopes and 512 KB uncompressed per request; reject decompression bombs and oversized slice arrays. For application churn, cap slices at 60 per bucket. On reaching the cap, close detailed collection at the current offset and mark the remaining contiguous suffix as detail-unavailable, preserving its start and end offsets. Do not claim active, inactive, or application detail for that suffix or improve coverage with it; it remains unknown in reconciliation. Flag the event as coarsened so capacity pressure is visible.

The API validates identity, allowed policy, durations, timestamp bounds, enum values, version compatibility, and batch limits. Return per-event accepted, already accepted, or rejected outcomes, only after transaction commit. Transient failure receives exponential backoff with jitter; an acknowledgment timeout is retried safely. Permanent rejection is visible as a data-quality issue and cannot cause endless retransmission.

Local pending storage is bounded by seven days and 100 MB. On cap exhaustion, remove the oldest pending payloads according to the recorded policy, increment a loss counter, and retain a compact missing-range manifest for upload. The agent UI and fleet view show the loss. Do not silently discard data or stop enforcing the cap. A queue wipe after lost keys is similarly explicit. Acknowledged payloads are purged locally promptly.

Determine age from immutable event occurrence end, not retry or receipt time. Accept ordinary late data up to seven days old; reject older automatic telemetry with a recorded reason. Quarantine unreasonable future timestamps, initially more than five minutes beyond received time. Quarantined events do not create accepted time totals. Late accepted events mark affected employee-days dirty for recomputation and increment the report revision.

## 5 Database model

Use one company deployment with explicit authorization scope on every business record. Avoid adding SaaS tenancy complexity while retaining stable company and client-account keys where appropriate.

- employees: stable company employee ID, display metadata, identity-provider subject, status, effective employment dates. Do not use email as a durable key.
- teams, client_accounts, assignments: effective-dated employee, role, team, and client scope. Reject conflicting primary assignments or represent intentional multi-account allocation explicitly.
- access_grants: principal, scope, permissions, effective dates. Keep platform administration separate from analytics access.
- devices and enrollments: device ID, enrollment ID, effective employee mapping, immutable OS user SID, credential version, approved status, last health, agent version, and revoked timestamp.
- schedules and schedule_intervals: external source ID, employee, start and end UTC, source timezone, expected state, break or absence type, import version, and conflict status.
- collection_policies and application_rules: effective range, authorized collection windows, idle threshold, retention settings, rule priority, matching application ID, role scope, and policy version.
- activity_envelopes: enrollment ID, device ID, event ID, employee ID, boot ID, session ID, collector instance ID, sequence, bucket start and end, observed duration, received time, schema and policy versions, payload checksum, quality flags, and bounded state slices.
- ingestion_receipts: unique enrollment and event ID, plus a second unique enrollment, collector instance ID, and sequence key; first accepted date, checksum, and receipt expiry. Identity or checksum conflicts reject the payload. Keep at least 37 days from first acceptance so retries within the seven-day late window cannot duplicate retained 30-day data.
- import_batches and import_errors: source type, checksum, mapping version, uploader, timestamps, source timezone, validation totals, errors, commit status, and superseded version.
- work_items or work_daily_facts: source system, work type, stable external item ID or declared daily aggregation key, employee mapping, completion date, count, handling duration when supplied, reviewed and passed counts, and import revision.
- approved_hours_facts: employee, source period, approved duration, external key, source timezone, import revision, and approval provenance; this optional feed is required for output-per-hour denominators.
- daily_metrics: employee, report date and timezone, metric version, rule version, source revisions, category durations, coverage, output and quality facts, conflict count, computed time, and report revision.
- exceptions, annotations, correction_requests: employee interval, source reference, author, reason, assigned owner, state, approved interpretation, and resolution history.
- audit_events, device_health, job_queue, deletion_ledger: scoped immutable history, operational diagnostics, durable work claims, and deletion requests that must be reapplied after restoration.

Store activity envelopes in daily UTC partitions with a primary key including the partition date. A nonpartitioned receipt table enforces event and collector-sequence deduplication across partition boundaries in the same transaction as accepted events and dirty-job updates. Expired replay is rejected by occurrence age even after a receipt expires. PostgreSQL partitioned uniqueness restrictions must be respected [S5]; do not assume a unique event ID alone can be declared globally on a date-partitioned table. If an existing event ID arrives with a changed checksum, reject it as a conflict instead of replacing the observation.

Index raw activity by employee and bucket start, receipts by expiry, summaries by team/date through effective assignments, jobs by ready state, and source records by external identity and revision. Retention drops or clears expired partitions only after related report and deletion checks. Raw data is for drill-down and recomputation within retention; standard dashboards use aggregates. Schema migrations and downgrade compatibility are reviewed explicitly.

## 6 Aggregation and metric implementation

The worker claims employee-day jobs using a database transaction, bounded batches, and a retry count. Jobs are deduplicated by employee, report date, timezone, and metric version. Ingress increments a dirty_generation counter in the same transaction as source changes. The worker captures that generation and a consistent input snapshot, computes a complete replacement aggregate, then publishes with a generation check. A stale worker cannot overwrite a newer published generation. Clear the job only if its current dirty generation equals the computed generation; otherwise keep it queued. This prevents arrivals during computation from being lost. Never increment totals blindly on every retry.

Split intervals at midnight in the report timezone, schedule boundaries, policy changes, and annotation boundaries. Reconcile overlaps using the project definition's precedence. Preserve distinct unknown, conflict, annotated, active, and locked or inactive durations. Also compute telemetry coverage independently from the raw union of valid intervals. Test daylight-saving transitions even if the pilot timezone does not use them.

Daily aggregates are authoritative only for their declared definitions and source revisions. Historical rule changes apply prospectively by default. A requested historical recalculation is possible only while required source detail is retained; old summaries are not presented as if rebuilt when the raw evidence has expired. Reports after retention still expose their metric version and whether reprocessing is possible.

CSV imports use staging and a preview before transactional commit. Reject ambiguous employee mappings, unsupported time formats, impossible counts, duplicate keys, and overlapping schedule periods. A corrected file supersedes a declared source period and revision atomically; an identical file is a no-op. Do not use fuzzy name matching. Work-item-level and daily-aggregate feeds cannot both contribute to the same measure for the same scope and period.

## 7 API and interface contracts

Initial versioned routes are POST /api/v1/enrollments/redeem, POST /api/v1/activity/batches, GET /api/v1/device/policy, POST /api/v1/device/health, POST /api/v1/imports/preview, POST /api/v1/imports/{id}/commit, GET /api/v1/teams/{id}/daily, GET /api/v1/employees/{id}/timeline, POST /api/v1/corrections, POST /api/v1/exceptions, and POST /api/v1/exports. Enrollment-token creation and policy administration are separately privileged actions.

Define OpenAPI schemas, error codes, pagination, time conventions, and size limits before parallel implementation. Return coverage, source freshness, report revision, units, and definition version with each analytics response. Missing optional operational data returns an availability reason, not a numeric zero. Test object-level access checks even when the requester guesses a valid ID.

The web interface contains team overview, employee explanation, output and quality, exceptions, imports, and device health. Lead with source quality. Show meaningful empty and stale states. Export scopes mirror view permissions, expire download links, and audit generation and retrieval. Neutralize formula-like CSV values without altering stored source records.

## 8 Capacity model and cost controls

The initial model assumes eight tracked hours per day, 60-second envelopes, 30 retained calendar days, and continuous activity on each modeled day for a conservative example. Envelope count per agent-day is 8 x 60 = 480. At 50 agents this is 24,000 per day; at 200 it is 96,000; at 1,000 it is 480,000. Thirty-day counts are 0.72 million, 2.88 million, and 14.4 million respectively.

At an illustrative 1 to 2 KB per stored envelope including bounded slices, 200 agents produce approximately 2.9 to 5.8 GB of raw retained payload; 1,000 produce 14.4 to 28.8 GB. These are decimal payload estimates, excluding indexes, receipts, WAL, backups, free space, and database overhead. The 1 to 2 KB assumption represents typical payloads, not a bound. Measure median and p95 bytes per envelope and application-switch distribution in the prototype; a 60-slice payload may be much larger. At 1,000 connected agents, one request per minute averages about 16.7 requests per second; reconnect storms and multi-envelope replays drive the more important burst test.

Maintain at least 30 percent free disk headroom as a proposed alert threshold. Track ingestion rate, queue age, worker lag, rows and bytes per agent-day, backup size, restore time, and p95 query latency. Benchmark 200 and 1,000 agents, a four-hour replay with a global admission limit, 20 concurrent report users, and a concurrent backup. Also demonstrate a full seven-day capped offline queue draining without gaps beyond documented eviction, and record its drain time and current-data freshness. Protect current ingestion from replay with per-device limits and a bounded shared replay budget.

Software license cost is not total cost. The ledger includes host allocation or rental, disks, backup storage, network transfer, Windows testing and CI, trusted code signing or existing internal PKI, identity costs if any, operator time, security patching, and AI development usage. No dollar amount is presented as a supplier quote.

Default incremental recurring software-service commitment is zero. Reuse facilities only after confirming entitlement and capacity. If an external expense becomes necessary, the lead presents a concrete option, actual current quote, alternative, and monthly or annual effect before purchase. Existing AI account limits are a project constraint; agents are bounded and reused rather than run continuously.

Consider a separate analytics engine only after indexes, partitioning, rollups, retention, query limits, and measured host sizing fail the agreed workload target. A sustained failure across repeated representative benchmarks, with a documented comparison of operator effort and infrastructure cost, is the trigger for an architecture decision. Self-hosted ClickHouse also adds operating cost.

## 9 Operations and release safety

Expose health endpoints and record content-free structured logs with request and event IDs. Alert through existing company channels on ingestion unavailability, oldest pending job, low disk, failed backup, expired signing material, and device version drift. Synthetic data is used in development and CI; production data does not enter AI prompts or public issue trackers.

Start with daily encrypted off-host database backups, 30-day rotation, documented secret recovery, and a restore rehearsal before pilot. Target RPO 24 hours and RTO four hours. Acknowledged events purged from endpoints cannot be assumed recoverable after server backup loss. Tighter RPO requires a separately tested and costed WAL archive or equivalent backup design [S6].

Roll out an agent first to controlled test devices, then five pilot canaries, then 25 to 50 pilot users, and only then to 200. Use company-approved signed artifacts and endpoint deployment tooling. Code signing does not guarantee antivirus acceptance; test actual company security software. Updates verify authenticity, stage safely, preserve compatible queued data, and retain the last working version. Maintain server compatibility with at least the current and previous agent schema during rollout.

For incidents, the operations owner can disable collection policy, revoke credentials, stop updates, restrict reports, and restore a known release. Critical database migrations use an expand-and-contract approach with a tested backup and rollback or forward-fix plan. Retention tasks have dry-run counts and bounded permissions. Implement every lifetime in project definition section 9, including normalized facts, correction history, receipts, exports, and the deletion ledger; link expiry alone does not delete an export. Do not remove historical grants or classification versions while retained results still need them for explanation or access control. Export downloads and incident records respect the same employee-data access boundaries.

## 10 Delivery work packages and evidence gates

Timing is an initial engineering planning range, not a six-month promise. A working pilot is estimated at 8 to 12 active engineering weeks after environment access, based on one coordinating lead and up to three specialist agents. Windows availability, review capacity, company source quality, and operational rollout can extend elapsed time. Re-estimate after the first end-to-end slice. No claim is made that an AI agent week equals a full human engineering week.

G0 Foundation and environment evidence, indicative week 1: establish repository structure, decisions, OpenAPI and sample contracts, synthetic fixtures, host and Windows inventory, metric tests, and dependency license record. Exit evidence is a runnable development environment and an explicit test matrix. Unknown identity or deployment details remain tracked dependencies.

G1 End-to-end measurement spike, indicative weeks 2 to 3: collect approved coarse state on a controlled Windows test device, encrypt and replay a local queue, ingest into PostgreSQL, and display one employee-day. Exit evidence includes restart and duplicate replay tests, lock and sleep behavior, plaintext inspection, and measured resource usage. Failure here changes the approach before dashboard expansion.

G2 Operational model and access, indicative weeks 4 to 5: implement roster, schedule, one output feed, scoped identity, rules, and revisioned aggregates. Exit evidence includes source reconciliation, overnight and DST tests, no-double-count invariants, and denied cross-scope access. Access gates must pass before real employee records are loaded.

G3 Management workflow and reliability, indicative weeks 6 to 8: deliver overview, timelines, corrections, exceptions, exports, device health, retention, updates, and backup procedures. Exit evidence includes manager task scripts, 200 and 1,000-agent benchmarks, reconnect admission control, a restore rehearsal, and independent review of security-sensitive changes.

G4 Controlled pilot and hardening, indicative weeks 9 to 12 with at least two weeks of observation: canary and 25 to 50-user rollout, comparison with authoritative source totals, report-preparation baseline comparison, and issue correction. Exit requires the project definition's quality gates, documented business findings, resolved critical defects, and a named company operations owner. The lead records pass, fail, or conditional status with evidence for every gate.

G5 Expansion to 200 and handover follows G4: verify capacity on the actual host, complete runbooks, rehearse agent rollback, and review cost per active device. Expansion beyond 200 is a new measured gate, not implied by synthetic 1,000-agent testing.

## 11 Required verification matrix

- Domain: no duplicate time; exact category reconciliation; zero and unavailable distinction; missing schedule; overnight shifts; DST; idle boundary; conflicting devices; effective team changes; annotations and report revisions.
- Ingestion: timeout after commit; duplicate or changed payload; out-of-order sequence; reconnect storm; malformed version; revoked device; clock skew; queue cap; lost encryption key; expired policy; seven-day replay boundary.
- Imports: wrong timezone; missing employee ID; repeated upload; partial errors; corrected source period; quality numerator beyond denominator; concurrent source versions; CSV export formula safety.
- Authorization: employee self-view; team and client denial; historical transfer; report-job execution and download scope; admin without analytics grant; revoked identity session.
- Windows: supported minimum hardware; install and uninstall; lock and unlock; sleep; reboot; fast-user switching; VPN; RDP or VDI if in scope; endpoint-security compatibility; update and rollback; offline policy expiry.
- Operations: backup failure alert; actual restore; deletion ledger replay; retention expiry; low disk; exhausted connections; worker crash mid-job; schema rollback compatibility; representative load under backup.

Automated tests run in CI where possible. Real Windows behavior, deployment policy, and user workflows require recorded environment evidence. A simulation passing on macOS or Linux does not close the Windows gate.

## 12 Technical references

References checked 12 September 2026. They support platform behavior and licensing, not company-specific feasibility or pricing. Revalidate supported versions at implementation.

[S1] PostgreSQL License. https://www.postgresql.org/about/licence/

[S2] Microsoft .NET support policy. https://dotnet.microsoft.com/en-us/platform/support/policy

[S3] Microsoft GetLastInputInfo function. https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo

[S4] Microsoft CryptProtectData function. https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata

[S5] PostgreSQL table partitioning and unique constraints. https://www.postgresql.org/docs/current/ddl-partitioning.html

[S6] PostgreSQL backup and restore. https://www.postgresql.org/docs/current/backup.html
