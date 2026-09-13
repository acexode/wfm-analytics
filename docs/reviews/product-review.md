# Product review contribution

Status: planning recommendation, 12 September 2026. This review interprets the three original PDFs as rough concepts, not established business facts. It supplies product requirements for the unified project plan; no software has been implemented.

## Unified objective

Give company operations leaders a reliable, explainable view of scheduled capacity, observed workstation time, reported work, and completed operational output so they can identify data gaps and operational exceptions, investigate their causes, and take measurable corrective action.

The first release should answer: (1) where do planned and observed hours differ, (2) which differences require human explanation or an IT fix, (3) how does actual work output compare with available capacity, and (4) did an agreed operational intervention improve the outcome without worsening quality?

Desktop activity is supporting evidence. It must not be labeled productivity, billable time, attendance, or completed work by itself. The product is an internal operations tool for one company, with internal business units, client programs, teams, and employees; commercial multi-tenancy, billing, and public self-service are out of scope.

## Users and daily decisions

| User | Primary workflow | Required outcome |
| --- | --- | --- |
| Employee | Review own previous shift; understand collection status; submit explanation/correction for missing or misclassified time | An understandable record and a traceable correction process |
| Team lead | Review previous shift's coverage and exceptions; compare approved schedule, observed intervals, and work output; assign and resolve follow-up | A small actionable exception list with evidence and ownership |
| Operations manager | Compare programs and teams using matched definitions, work types, time periods, and coverage; review unresolved issues | Capacity and process decisions supported by trustworthy denominators |
| COO | Review weekly capacity, output/quality trends, data completeness, and intervention results | Evidence of value without employee activity rankings |
| IT administrator | Enroll and revoke devices; review upload health, versions, queue problems, and rollout cohorts | Healthy data collection and safe support/update workflows |
| Authorized data steward | Import schedules/output; resolve validation failures; version classifications; manage access and retention | Auditable, reproducible data definitions |

Administrative access must not automatically grant employee-detail visibility. Operational access is constrained to assigned programs/teams, with effective dates and access revocation when responsibilities change.

## MVP scope

1. Organization and identity: employees distinct from login accounts and devices; client/program/team assignments with history; scoped role-based access; deactivate people without silently deleting historical totals.
2. Windows-first collector on company-managed devices, subject to fleet verification. Collect session/lock state, approved application identity, interval timing, device health, and minimal idle state. Display collection status and policy. No screenshots, window-title strings, typed content, clipboard, audio/video, or mouse-distance/key-frequency scores. Browser-domain capture is deferred unless a later business case and privacy review justify it; browser processes otherwise remain generic or unclassified.
3. Resilient, bounded offline capture and later reconciliation. Missing uploads must not appear as zero work. Device health and collection gaps are first-class data.
4. Schedule and work-state imports using versioned CSV templates. Support cross-midnight shifts, local time zones, breaks, leave, training, meetings, and approved overtime. Include employee-declared work context and reason-coded correction with team-lead disposition.
5. A single operational output feed through CSV for the pilot program. Include unique work-item/event IDs, employee/program mapping, source timestamp, completed units, work type, and available quality outcome. If there is no usable output source, explicitly limit the pilot to time visibility; make no productivity or recovered-revenue claims.
6. Daily employee/team summaries showing schedule, observed coverage, unlocked-active time, idle-indicated time, locked time, offline/manual work context, unknown periods, and data freshness. Program output and quality appear alongside these measures, not collapsed into a single score.
7. Explainable exception queue with reason, supporting intervals, freshness/coverage, assigned owner, status, explanation, and audit trail. Begin with data gaps, schedule mismatch, missing classification, and unresolved correction; do not automate disciplinary actions.
8. Historical app classification scoped by program and effective date: work-related, non-work-related, neutral, unclassified. Preserve the rule version used for each report; a browser or unknown process must not automatically be classed non-work-related.
9. Controlled CSV exports, data retention, audit history, backup/restore, collection disable/revoke, employee-facing policy, and operator runbook.

## Explicitly deferred or rejected for this release

- Multi-company SaaS tenancy, subscriptions, external customer portals, and commercial billing.
- macOS/Linux collectors until a verified fleet need justifies them.
- Real-time coaching, generative AI subscriptions, attrition/burnout predictions, emotion or intent inference, and employee trust/productivity scores.
- Screenshots, screen recording, OCR masking, raw page URLs, window-title collection, keylogging, and hidden collection.
- Automated payroll, invoicing, disciplinary decisions, or claims that application activity is billable time.
- Complex forecasting, automated scheduling, live call-center occupancy, and work-item handling time until trustworthy underlying source events exist.
- Broad CRM connectors before the pilot establishes which source is worth integrating.
- "Tamper-proof" promises: report stopped collection, clock anomalies, and device health; use managed deployment controls with a documented emergency stop.

## Metric contract

Use half-open intervals [start, end), UTC storage, and explicitly selected reporting timezone. Split intervals at shift and report boundaries. A person may have multiple devices but overlapping observations must never inflate person-time. Preserve competing evidence and a documented selection policy; flag unresolved overlaps. A calendar day is not necessarily a shift.

Maintain separate dimensions: workstation observation state, declared/approved work context, app classification, and source-system business events. A meeting may overlap idle-indicated workstation time without contradiction. Do not add totals from different dimensions.

| Metric | Definition and safeguards |
| --- | --- |
| Scheduled time | Approved shift duration excluding approved unpaid breaks; retain paid breaks separately. Schedule policy is versioned. |
| Expected collection window | Explicit policy window for a scheduled, enrolled employee, excluding intervals where collection is not permitted. This denominator is distinct from paid hours. |
| Observed workstation time | Union of valid collector intervals within the selected window. State partition: unlocked-active, unlocked-idle-indicated, locked. Sleep/shutdown/unobserved periods are unknown unless a valid source establishes another observation state. |
| Coverage | Observed workstation time / expected collection window. Null when denominator is zero. Coverage outside a known schedule is shown as unscheduled observation, not a fabricated percentage. |
| Unknown time | Expected collection window minus valid observed intervals. Offline buffering may later fill a provisional unknown period. Queue overflow, missing devices, agent stopped, and unmapped identity remain explicit reasons where known. |
| Idle-indicated time | Valid unlocked interval with no input for the configured threshold; only an interaction-state signal. Reading, calls, and offline work can occur during it. The threshold and transition rule must be specified and versioned. |
| App-focused time | Valid foreground application interval grouped by approved app identifier/classification. Not handling time or proof of completed work. |
| Approved work context | Employee/imported and supervisor-approved activity such as meeting, training, break, or offline task. Retained as a separate overlay and never manufactured from missing telemetry. |
| Completed units | Deduplicated work items meeting the source system's agreed completion rule during the period. Reopens/reversals follow a documented adjustment policy. Missing imports are unavailable, not zero output. |
| Units per approved work hour | Completed units / approved work hours only for comparable work types and known source coverage. Display quality and numerator/denominator; do not compare dissimilar work or call it a universal productivity score. |
| Quality rate | Passed eligible reviewed items / eligible reviewed items under the source's documented rule; show sample size and sampling coverage. Null if no eligible observations. |
| Average handling time | Deferred unless source provides genuine handling sessions and a documented rule for holds, after-work, concurrent work, and abandoned/reopened cases. Desktop focus and creation-to-close time cannot substitute. |
| Schedule variance | Approved actual work-state durations compared with scheduled categories. Collector logon/lock alone does not establish attendance or lateness. |

Reports display generated-at time, source freshness, coverage, provisional/final status, scope, timezone, and definition version. "Final" means a defined reconciliation cutoff has passed; later legitimate backfill creates a new revision, not discarded evidence.

## Acceptance criteria and pilot evaluation

Product release gates:

- A synthetic eight-hour shift containing active, idle-indicated, locked, missing, overlapping-device, and cross-midnight intervals reconciles to the expected window exactly under the published state model; unknown is never silently reassigned to idle.
- CSV imports preview errors, reject or quarantine invalid rows, report accepted/rejected counts, support safe retry without duplicate output, and preserve source/version provenance. Corrections can supersede prior imported records without doubling totals.
- A lead cannot retrieve another lead's unassigned employee details using either UI, API, or export. Employee access is restricted to the employee's own record. Deactivated access fails immediately under the agreed revocation mechanism.
- Corrections retain original observation, proposed value/context, actor, reason, review decision, and timestamps. Aggregates are recalculated and revisions can be traced.
- A collector going offline makes current data stale; a later upload fills eligible gaps and updates summaries without double counting. Failed or overflowed collection remains visible after reconciliation.
- Schema, API payload, cache contents, logs, and exports contain none of the prohibited content fields. Collection outside policy is prevented and testable.
- A dashboard user can trace an exception to its evidence and definition, record an explanation, and close/reopen it; absence of evidence does not produce a misconduct label.

Suggested pilot plan: one process with comparable work and a named operations sponsor; first establish a baseline, then use the tool for at least two complete operating cycles. Exact cohort and time commitments depend on fleet/data readiness and are planning assumptions, not a promised calendar.

Suggested pilot success thresholds, to be ratified from baseline: at least 95% collection coverage for healthy enrolled devices during permitted windows; 100% reproducible sampled report totals; no unexplained double counting; zero known unauthorized detail exposure; at least 80% of reviewed exceptions resolvable as operational action, valid explanation, or identifiable data issue. Track manager review time, import/support effort, and avoid treating a high exception count as success. Declare business value only when a named operational intervention changes a baseline outcome with quality and workload context recorded.

## Assumptions to validate without blocking document preparation

The product manager should maintain these as tracked discovery work, using available company records and an operations/IT delegate when access is provided. They are not questions for the user to prioritize.

- Actual fleet operating systems, VDI/shared workstation usage, install rights, shift patterns, staff count, network constraints, and company hosting capacity.
- A pilot program with reliable schedule identifiers, employee mapping, usable output export, and a manager who owns exception review.
- Approved collection policy, permitted hours, role scopes, correction ownership, employee communication, and retention decision. No blanket legal-compliance claim follows from avoiding keystroke content.
- Existing identity provider, device management, reporting workflow, and the expected time cost of CSV preparation.
- Baseline operational problem with measurable definition: reporting effort, unresolved schedule gaps, verified overtime, throughput or rework. Revenue leakage requires finance evidence and is not inferred from inactivity.
- Company deployment authorization is a production gate; agent autonomy covers research, design, implementation, tests, and review, but does not replace institutional authority for collecting staff data or obtaining third-party access.

## Critical corrections to the source concepts

The draft equates input frequency with productivity, lacks schedules/output data and correction workflows, stores potentially sensitive window titles, assumes paid analytics infrastructure before measuring volume, and provides a six-month commitment without resource evidence. Replace these with source-based business metrics, explicit unknown states, minimal collection, a simple internal deployment, measurable readiness gates, and a continuously maintained assumption/decision log. The prior competitive claims about ProHance and statements of automatic regulatory compliance are not established facts and should not carry into the new documents.
