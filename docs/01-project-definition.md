# BPO Workforce Operations Analytics Project Definition

Version 1.0 | 12 September 2026 | Project lead: coordinating AI agent

## 1 Purpose and project decision

Build an internal operations analytics system that helps the COO and team leaders understand how scheduled capacity is used, identify data and process problems, and follow up on operational exceptions. The first release will combine workforce schedules, conservative desktop activity summaries, and imported operational results. Every reported measure must identify its source, coverage, definition, and limitations.

The company will operate one deployment. PostgreSQL will serve both operational records and initial analytics. Existing company compute, identity, network, and backup facilities are preferred when they meet the requirements. The initial release has no mandatory ClickHouse, commercial analytics, hosted queue, or paid AI inference dependency.

The system succeeds when managers can resolve useful operational questions with trustworthy evidence. Installing a tracker or producing a dashboard is not sufficient evidence of business value.

## 2 Context and assumptions

The three original PDFs are discovery inputs. This document and the technical plan supersede their implementation recommendations where they differ. The original documents remain unchanged. Competitor descriptions and predicted commercial returns in those notes are not treated as verified requirements.

Planning assumptions, rather than confirmed company facts, are:

- One company with several teams and client accounts; internal deployment only.
- A Windows workstation pilot of 25 to 50 people, followed by a controlled expansion to 200. A 1,000-agent synthetic workload is a capacity test, not a promised production limit.
- A company-managed Windows test device and a small Linux VM can be made available. Shared desktops, remote sessions, VPN access, and identity-provider compatibility require an early environment check.
- Initial roster, schedule, and work-output data can be supplied as approved CSV exports. Live CRM integrations are not required to begin development.
- Managers need daily and intraday visibility; a five-minute healthy-data freshness target is adequate for the pilot.
- Existing company authentication and deployment policies take precedence over development defaults.

The lead records new evidence in the decision log and adjusts the plan. Unknowns do not block synthetic-data development. Actual employee collection requires company-authorized deployment, a named operational owner, and an agreed collection policy. No geography, employment policy, regulatory certification, or permission to deploy to employees is inferred from this plan.

## 3 Objectives and business outcomes

Objective O1 is to explain scheduled capacity: distinguish observed activity, observed inactivity, approved non-desktop work, and missing or conflicting evidence.

Objective O2 is to reduce manual reporting effort: combine approved roster, schedule, and output exports into reproducible team reports, without repeated spreadsheet reconciliation.

Objective O3 is to make exceptions actionable: let a team leader investigate an unusual interval, record context, assign an action, and track its resolution.

Objective O4 is to control operating cost and reliability: use a small deployable system, bounded collection and retention, and tested recovery procedures.

Before the pilot, record two weeks of the team's current reporting effort, exception-resolution time, and existing output and quality measures. Proposed pilot value targets are a 30 percent reduction in report-preparation time and at least three verified, actionable process findings over two weeks. These are evaluation targets, not promised savings. Record the actual baseline, sample size, and result; do not declare success from activity changes alone.

Recovered revenue or billable hours will not be claimed unless a client-approved billing rule and authoritative billing data support the calculation. The initial release does not produce payroll or invoices.

## 4 Users and access

- COO and operations manager: view authorized teams, coverage, trends, imported output and quality, and unresolved exceptions. Cross-team comparisons show comparable work types and definitions.
- Team leader: inspect assigned employees and periods, validate schedule mismatches, document non-desktop work, and manage follow-up actions. Access follows effective-dated team assignment.
- Employee: see their own collected categories, collection status, daily explanation, and correction requests. The tray application visibly indicates collection and connectivity status.
- Platform administrator: manage enrollment, policies, imports, and health. Administration does not automatically grant employee-report access.
- Operational data owner: validate import mappings, source totals, work-type definitions, and report revisions. This may be an existing manager rather than a new job.

Access is enforced on the server for dashboards, employee details, exports, and background report jobs. A client account is a reporting and authorization boundary within the company, not a separate SaaS tenant. Employee IDs come from a stable company identifier; display names and email addresses are not matching keys.

## 5 Core workflows

### Daily team review

A team leader selects a date and team. The dashboard first shows source freshness and scheduled-time coverage. It then shows time categories and available output and quality measures. The leader opens an exception, reviews its source intervals and schedule, records an explanation, and assigns a follow-up. A missing desktop interval is investigated as missing evidence, not automatically labeled an employee performance issue.

### Operational trend review

An operations manager compares the same work type over time and reviews capacity alongside completed work and quality. The report discloses absent integrations and missing denominators. A role-specific application classification may support investigation but cannot independently establish productive output.

### Import and reconciliation

An authorized user uploads a roster, schedule, or operational export, previews mapping and errors, and commits a validated batch. The system stores source identity, checksum, counts, rejected rows, reporting timezone, and revision. Repeated uploads cannot silently duplicate facts. Corrections create a traceable replacement version.

### Employee correction

An employee identifies a period that needs context, such as approved training or a customer call. A scoped manager reviews the request. The original observation is retained and an approved annotation is added with author and reason. Reports distinguish observed data from business interpretation.

### Device recovery

The agent buffers summaries during disconnection. Reconnection uploads acknowledged batches without duplicating time. If buffering overflows or collection stops, the dashboard shows a gap and the device-health view records the cause. A stopped process is a health event requiring investigation, not proof of misconduct.

## 6 First release scope

P0 requirements must pass before the employee pilot:

- R01: company identity integration, scoped roles, employee and device enrollment, revocation, and effective-dated team and client assignment.
- R02: validated roster and schedule CSV imports, including overnight shifts, timezone handling, breaks, and rejected-row reports.
- R03: visible Windows session collector for process identity, coarse active or inactive state, lock state, and collection health, with encrypted local buffering.
- R04: retry-safe ingestion, durable acknowledgment, clear missing-data indicators, and reproducible versioned aggregation.
- R05: daily team overview and employee timeline with drill-down, metric definitions, coverage, and freshness.
- R06: one agreed operational-output import mapping, supporting completed work and quality where the source contains them. Without it, the pilot is explicitly an activity-visibility pilot and cannot pass the output-linkage business gate.
- R07: role-specific application classification with effective dates; unknown applications remain unclassified. No default assumption that all browser use is productive or wasteful.
- R08: exception records, contextual annotations, correction requests, and audited state changes.
- R09: authorization-safe CSV export with formula-injection protection, report revision, filters, and metric definitions.
- R10: collection notice, retention configuration, access audit, device health, backup restore, and staged update and rollback procedures.

Deferred scope includes macOS, browser extensions and URL inspection, live CRM or telephony connectors, automated forecasting, staffing optimization, client billing, payroll, multi-company SaaS, screenshots, keystroke content or counts, clipboard capture, AI-generated employee scores, attrition prediction, and generative coaching. New scope must replace or defer existing work unless the lead explicitly revises capacity and cost assumptions.

## 7 Metric contract

All durations use seconds internally and a half-open interval convention: start inclusive and end exclusive. Reports state their timezone. Zero and unavailable are different values. Rates with a missing or zero denominator display unavailable with a reason.

### Time reconciliation

Scheduled eligible time is imported scheduled time minus approved breaks and excluded absence. Preserve both the original schedule and its revision. Within eligible time, assign each instant exactly one category in this precedence order: approved non-desktop work; conflicting device evidence; observed locked or inactive; observed active; unknown. Inactivity is evaluated only when the collector is healthy. For a single session, locked state overrides its input state. Conflicts are resolved only by a recorded source-selection rule or approved correction.

The five categories sum to eligible time. Observations outside schedule are reported separately and are not automatically classified as overtime or billable work. For overlapping shifts, reject or explicitly normalize schedule conflicts before calculating a denominator. Authorized annotations do not alter raw observations.

Desktop telemetry coverage is the union of valid observed desktop intervals within eligible time divided by eligible time. Approved non-desktop annotations do not artificially improve telemetry coverage. Time explained by annotations is reported as a separate quantity. Show conflict duration separately even if desktop telemetry is present.

Observed active share is observed active desktop seconds divided by active plus locked or inactive desktop seconds, after removing annotated and conflicting periods. It describes input activity, not output or effort. Reports show coverage alongside it; comparison eligibility requires at least 95 percent valid coverage within expected desktop collection windows and no unresolved conflicts. Expected desktop windows exclude prospectively scheduled approved non-desktop periods; a later annotation cannot shrink this denominator. Continue displaying unadjusted telemetry coverage over eligible time alongside the eligibility measure.

Application-classified active share is active time in a configured application category divided by total eligible observed active time. Show unclassified active time explicitly. Rules are scoped to role or process and versioned. This is a usage measure, not a general productivity score.

### Work and quality

Completed work is the deduplicated count of source-defined completed work items in a stated period and work type. Quality pass rate is passed reviewed items divided by reviewed items, with the sample count and source definition visible.

Output per approved work hour uses completed comparable items divided by authoritative approved work hours from an agreed source, stored with its own source revision. An optional approved-hours CSV feed supplies this denominator; until available the rate remains unavailable. Do not substitute keyboard-active hours. Average handling time is published only when the source supplies actual handling durations and matching completed items, with explicit rules for transfers, reopened work, and parallel work. Otherwise it remains unavailable.

Schedule variance in this release means a difference between imported expected presence and observed or approved evidence. It is not formal adherence unless an authoritative work-state integration supplies the necessary states. Idle duration is never automatically translated into financial loss.

## 8 Quality and acceptance targets

These targets are proposed release gates and must be measured, not assumed:

- Correctness: replaying the same accepted batch ten times leaves totals unchanged; late valid events produce one traceable report revision; approved categories reconcile exactly to eligible seconds.
- Coverage: at least 98 percent of scheduled eligible minutes have valid desktop evidence on healthy, supported pilot devices, excluding explicitly scheduled non-desktop periods from this gate's test population. Reports still display the unadjusted coverage definition above.
- Freshness: 95 percent of connected-device summaries appear within five minutes under the agreed load test. Offline periods are measured separately.
- Usability: a team leader can identify a data gap, compare an imported output measure, and resolve an exception without developer assistance in a scripted pilot session.
- Endpoint resource budget: provisional p95 collector CPU below 1 percent over sampled one-minute windows and resident memory below 150 MB on the nominated minimum-spec Windows machine during an eight-hour script. Collect baseline and added load, startup behavior, and VPN and endpoint-security effects. Revise targets only with measured evidence.
- Performance: p95 daily-team and 30-day aggregate views below two seconds at 20 simultaneous dashboard sessions and the 1,000-agent synthetic workload on a documented test host. Large exports run as bounded jobs.
- Security: tests deny access across team and client scopes, including exports and revoked users or devices. Sensitive screen text must be absent from payloads, local queues, logs, and backups.
- Recovery: demonstrate database restoration within four hours and no more than 24 hours of acknowledged-data loss with the initial daily-backup design. If the company needs tighter recovery, add and cost a tested continuous-archive design before rollout.

## 9 Collection and trust policy

Collect the minimum required to explain coarse workstation state: pseudonymous employee and device IDs, approved process identifiers, coarse state durations, sequence and version fields, and health diagnostics. Resolve employee names on the server only for authorized viewers.

Do not collect window titles, URLs, document names, typed characters, keystroke counts, screenshots, audio, clipboard contents, or mouse trails. No per-keystroke hooks are required for the first release. An idle threshold of five minutes is a configurable pilot default; changing it creates a new policy version. The exact threshold boundary semantics are defined in the technical plan.

Collection follows a company-managed work session and visible policy. Outside an explicit collection window the agent stops activity capture and reports only non-content health as authorized. A missing schedule does not silently enable all-day tracking. Device enrollment initially receives a test collection window; production windows derive from approved schedules and an explicit grace period, default zero. Expired offline policy stops collection and records a gap.

Proposed retention is 30 days for raw summaries and successful input source files, 13 months for daily aggregates, 90 days for audit and health logs, and 30 days for backup copies. Local queue retention is at most seven days or 100 MB, whichever limit is reached first. Rejected import files are deleted after seven days unless needed for an active correction. The company data owner validates these defaults before employee deployment. Deletion must cover primary data, derived views, source uploads, and eventual backup expiry; restoration reapplies a deletion ledger before access is restored. Retain minimal normalized schedules, operational facts, approved-hours facts, classifications, and correction or annotation history for 13 months to explain retained daily results. Keep import metadata for 13 months, with rejected-row payloads removed after seven days. Delete generated exports after 24 hours, independently of link expiry. Retain pseudonymous ingestion receipts for 37 days from acceptance. Retain the minimal deletion ledger until all affected backups have expired and restoration controls have been verified. A 30-day raw limit applies to the live database; backup rotation can retain a deleted record for up to 30 additional days. Operational logs expire after 90 days; approval history necessary to explain a retained correction follows the 13-month record lifetime.

## 10 Decision ownership and boundaries

The coordinating AI lead owns requirements, technical decisions, task sequencing, integration review, and the delivery report. Specialist agents work within bounded assignments and report evidence to the lead. The user is not asked to select routine implementation details.

The company retains authority over employee deployment, operational definitions, access to internal systems, expenditure, and operational policy. The lead bundles these concrete dependencies when they become necessary. Repository creation is optional until collaboration requires a remote; local planning and synthetic-data implementation can proceed without it.

No cloud resource, subscription, paid API, or employee rollout is created by this planning work. A production launch requires both a passed engineering gate and a company-designated operational owner who can receive and act on incidents.
