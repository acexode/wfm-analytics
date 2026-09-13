# WP01 executable contracts

The planned collector contract consists of `activity-batch.schema.json` and the durable acknowledgement in `activity-batch-response.schema.json`.

`daily-report.schema.json` is the shared first-slice wire contract. `fixtures/daily-report.json` is a synthetic example, served for `team-synthetic` on `2026-09-13`. Durations are integer seconds. The endpoint is a single bounded team-day response; pagination is deferred until roster limits are measured. The current fixture is not live telemetry. Freshness `unknown` and null receipt time explicitly signal that fact.

`activity-batch.schema.json` defines the planned collector request. The development report endpoint does not implement ingestion. Enrollment identity comes from credentials; employee and device IDs cannot be supplied in the payload. Strict additional-property rejection excludes titles, URLs, paths, counts, and content. Executable basename/application identifiers are restricted to simple normalized names. The transport must enforce 512 KiB after decompression, up to 200 events, up to 60 slices, authorized policy and collection window, seven-day occurrence age, five-minute future quarantine, and enrollment revocation. JSON Schema cannot establish database identity or transaction durability.

Times are offset-qualified ISO timestamps. Buckets start on UTC minute boundaries, last at most 60 seconds, and contain ordered nonoverlapping half-open slices. Gaps stay unknown. A coarsened suffix has `detail_unavailable`, null application, and ends at bucket end. Its duration does not improve coverage. Retry identity is enrollment/event ID and enrollment/collector-instance/sequence; changed payload rejects rather than replacing evidence. A durable response returns one outcome per event with event ID, `accepted`, `already_accepted`, or `rejected`, and a machine-readable rejection reason. Server implementation and transaction tests remain pending.

## Metric semantics

Daily categories sum exactly to scheduled eligible time after breaks and excluded absence. No schedule yields no denominator, never assumed full-day collection. Per instant, precedence is approved annotation, concurrent source conflict, healthy locked/inactive, healthy active, unknown. Overlapping independent device/session sources remain conflict even if states match; duplicate observations from the same source do not add time. Overlapping schedules must be rejected before reconciliation. Observations outside the reporting day/schedule do not count in eligible totals.

Telemetry coverage is valid observed union divided by eligible time, independently of category precedence. If denominator is zero, value is null with `no_eligible_schedule`; available values have null reason. Output absent from the selected source is null with `source_not_connected`, never zero. An imported zero remains 0 with null reason. The first-slice response does not expose comparison eligibility or an output rate. Future comparison eligibility must use at least 95% coverage over prospectively expected desktop windows and no unresolved conflicts. Later annotations cannot shrink that expected denominator. Approved-hours input is required for output per hour.

`fixtures/reconciliation.json` contains 12 synthetic interval cases with independently specified expected seconds. Includes gaps, healthy inactivity, lock precedence, device overlap, duplicate evidence, annotation precedence, coarsening, midnight clipping, and both 23-hour and 25-hour DST days. The reference sweep in tests is an executable specification, not the production worker; future worker tests must consume these expected results separately.

## CSV samples and checks

`fixtures/imports` contains valid and invalid schedule and daily-output CSVs. Schedule rows use stable source/employee IDs, explicit-offset start/end and IANA source timezone. Invalid sample has missing employee, naive/reversed timestamps and invalid timezone. Output sample declares one daily aggregate source key and counts; invalid sample has passed greater than reviewed. These examples do not implement import staging, replacement versions, or deduplication. Those remain WP05 responsibilities. All records are synthetic.

Run `python3 -m unittest discover -s tests/contracts -v` with Python 3.12 and jsonschema 4.x available. Tests validate strict schemas, examples, forbidden extra fields, slice geometry, all expected reconciliation totals, and CSV example validity. No production authorization, PostgreSQL aggregation, Windows behavior, or G1 evidence is implied by these checks.
