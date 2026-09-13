# Project Status and Decisions

Updated 13 September 2026

## Current state

The planning baseline remains the implementation authority. WP01 executable contracts and the WP02 synthetic vertical-slice foundation have been implemented and independently checked by the lead. The slice includes strict JSON contracts, synthetic fixtures, an ASP.NET Core API, ordered PostgreSQL migrations, scoped development identity, a real PostgreSQL-backed daily report route, and a responsive React dashboard.

Executable evidence now exists on macOS with .NET SDK 10.0.401, PostgreSQL 14.2, Python 3.12, and the bundled Node.js runtime. Contract checks, server authorization and database integration checks, web unit checks, production web build, direct API requests, Vite proxy requests, and desktop/mobile browser review passed. This is development-foundation evidence only: no collector, ingestion implementation, operational import, production identity, repository remote, CI pipeline, employee deployment, or paid service has been created.

WP01 is accepted. WP02 is accepted as the development foundation. G0 remains partially open pending repository/CI and company environment inventory. WP03 has begun with a bounded .NET collector prototype for deterministic bucket slicing, contract-shaped events, live foreground-process sampling, DPAPI-protected encrypted local queue replay, resource samples, sample-gap reporting, and ciphertext plaintext inspection; the solution build and collector unit tests pass on this host, and the user manually verified foreground app switching with real applications. G1 remains open because the prototype has not yet been independently reviewed or exercised on a controlled Windows test device for the full lock, sleep, restart, fast-user-switching, endpoint-security, and resource-duration evidence set.

## Lead decisions

- D01 Internal company deployment first. Accepted. Removes SaaS provisioning, tenant billing, and public signup from scope.
- D02 PostgreSQL-only modular monolith. Accepted. ClickHouse and other additional services deferred until measured need and cost review.
- D03 Windows first with a session helper. Accepted as a planning default. Actual supported workstation and VDI modes require G1 evidence.
- D04 Minimum collection. Accepted. No window titles, URLs, screenshots, typed content, input counts, or runtime employee AI scores.
- D05 Schedules and one work-output import in the first release. Accepted. Output claims require authoritative source evidence.
- D06 Unknown and conflict states are explicit. Accepted. Context annotations remain separate from raw evidence; reported time cannot exceed elapsed time.
- D07 Bounded 60-second envelopes. Accepted. One envelope stores time-positioned slices; measure payload growth with application churn. Specialist per-row sizing is superseded by the envelope model in document 02.
- D08 Pilot of 25 to 50, then 200. Accepted as a planning assumption. A 1,000-agent load test is a separate technical gate.
- D09 Daily independent backups, RPO 24 hours, RTO four hours. Proposed test gate. The delivery baseline uses this target instead of the specialist's alternative eight-business-hour target; it must pass an actual restore drill.
- D10 No new recurring software-service commitment. Accepted. Unknown facilities and operating costs remain unquoted rather than assumed free.
- D11 Lead-managed bounded agent work with independent review. Accepted. Saved files support continuity; no unattended scheduler is configured.
- D12 Eight to twelve active engineering weeks is an initial planning range only. Accepted with low confidence until G1; it is not a delivery commitment.
- D13 First executable slice uses only synthetic data in an isolated local PostgreSQL cluster. Accepted. Development identity is fixed server-side and disabled outside Development; production identity remains a WP05 dependency.
- D14 The daily dashboard exposes freshness, coverage, unknown, conflict, and unavailable output before any productivity interpretation. Accepted. The browser consumes the real API contract rather than a bundled UI mock.
- D15 Expanded sensitive capture is policy-gated and default-off. Accepted as a scope change requested by the user on 13 September 2026. Window titles, browser URLs, typed text, screenshots, clipboard text, and full paths may be captured only when an explicit collection policy enables the exact field for an authorized scope with documented purpose, employee notice, retention, encryption, access audit, and company approval. Minimum collection remains the default, and audio, webcam capture, mouse trails, covert collection, and runtime employee AI scores remain prohibited.

## Dependencies and when they matter

- Company fleet and Windows test access: needed for WP03 exit, not for synthetic fixtures.
- Stable employee IDs, schedules, and one approved output export: needed for WP05 real-source mapping and pilot value validation.
- Identity, host, backup destination, and deployment mechanism: needed before operational readiness and employee data loading.
- Operational data policy and authorized pilot cohort: needed before collecting actual employee activity.
- Remote repository: needed when remote collaboration or CI begins; user may create it then. No remote destination is assumed.

## Review disposition

Accepted from product review: operational decision focus, employee correction workflow, effective assignments, and separation of output from activity.

Accepted from architecture review: session-specific Windows collection, DPAPI-protected queue keys, global deduplication across partition boundaries, overlap-safe time slices, and explicit operating costs.

Accepted from delivery review: evidence gates, actual Windows testing, independent review, restore and rollback requirements, and minimal user involvement through concrete dependencies.

Alternative specialist gate labels, benchmarks, and numeric targets are advisory. The numbered baseline documents and this decision log resolve those differences. Subsequent changes must update the relevant baseline and this log together.

Second-review corrections incorporated: time-positioned slices and explicit overflow gaps; per-user device enrollments; collector-instance sequence deduplication; generation-safe aggregate publication; approved-hours source; comparison coverage eligibility; and complete retention lifetimes. Both independent reviewers identified these changes; the lead incorporated them before PDF generation.

Document QA complete: the 19-page reading copy was rendered and visually inspected on every page. The product reviewer verified closure of its four findings; the lead verified incorporation of the five architecture findings. Details and PDF fingerprint are saved in docs/reviews/lead-acceptance.md. Foundation implementation evidence is saved in the WP01, WP02 backend, WP02 web, and phase acceptance reports under docs/reviews.
