# Delivery plan and independent risk review

Status: planning recommendation for the project lead. No implementation, purchases, production access, or employee rollout authorized by this document. Estimates become commitments only after the discovery and Windows feasibility gates.

Source review: all three original PDFs were read. The schema/roadmap's six-month launch, 200-user pilot, 5,000-user load test, and mandatory ClickHouse clustering are unsupported planning choices and should be superseded by the gates below. The PRD's anti-tampering thread locks, screenshot masking, and attrition inference should not become MVP commitments. Missing collection is a data-quality signal, not proof of employee manipulation. The original claim that avoiding actual keystroke content establishes PCI-DSS compliance is not a sufficient compliance determination; company security review must assess the actual environment. A code-signing certificate is part of a distribution strategy, not evidence that endpoint security will accept the collector.

## Delivery principles

The first release should help an internal operations manager reconcile scheduled and observed time, investigate missing or misleading data, and identify exceptions for discussion. It must not treat desktop activity as proof of productivity, quality, attendance, or misconduct. Business outcomes require work-system data and agreed definitions.

Use one modular application, PostgreSQL, an ASP.NET Core API and background worker, a React/TypeScript UI, and a Windows C# collector. Run on existing suitable company infrastructure when its capacity, ownership, backup, and availability are verified. Do not introduce ClickHouse, Kubernetes, Kafka, Redis, a commercial analytics product, or paid cloud services without a measured need and a reviewed cost decision. Free software does not mean free operation.

## Milestones and evidence gates

| Gate | Deliverable | Required evidence before proceeding |
|---|---|---|
| G0: agreed working baseline | Versioned product scope, decision log, assumptions, metric dictionary, data inventory, pilot criteria, cost ledger | Lead reconciles source sketches; every metric identifies its source and exclusions; unresolved scale and infrastructure assumptions are explicit. Existing company policy and named sponsor identify who can authorize employee collection. |
| G1: feasibility and trust | Small Windows collector prototype, API ingestion prototype, privacy design, measured storage estimate | Tests on actual company Windows devices establish feasibility under standard user permissions and company endpoint controls. Lock, idle, sleep, resume, disconnect, user switching, offline buffering, restart, and duplicate delivery are demonstrated. A security/privacy reviewer accepts the proposed data fields and access model before a live pilot. |
| G2: one complete workflow | Schedule import, collector enrollment, ingestion, reconciled daily view, employee self-view, manager exception workflow | A controlled synthetic dataset produces agreed totals. Missing coverage is shown as unknown. Cross-team access is denied. Durable acknowledgements and retries do not lose or double-count events. Restore drill and audit logging pass. |
| G3: controlled pilot | Small authorized team, limited retention, support runbook, rollout and rollback packages | The sponsor authorizes the employee deployment and communication. Operators validate reports against independent records over a representative work cycle. Collector overhead, event gaps, ingestion delay, support burden, and usefulness meet thresholds fixed at G1. No unresolved critical security or data-loss issue. |
| G4: production readiness | Supported release, deployment record, backup and recovery procedure, assigned operational owner | Independent release review; verified permissions; measured load at expected fleet size plus agreed headroom; restore drill; approved signing/distribution approach; rollback exercise; documented ongoing cost and support capacity. |
| G5: expansion | Additional teams and only justified integrations | Pilot provides evidence of decision value and acceptable operational load. Each expansion rechecks authorization, capacity, visibility boundaries, and support ownership. Outcome metrics are added only when source integrations support them. |

Do not attach a speculative six-month promise to these gates. After G1, estimate remaining work from actual throughput, test-device availability, company deployment lead time, and known integration complexity. Report a forecast range, dependencies, and confidence; revise when evidence changes.

## AI team and lead review protocol

The lead owns scope, prioritization, architecture decisions, integration, and release recommendation. Delegated agents receive bounded work packages with one owner, permitted files, dependencies, acceptance criteria, and required evidence. Default assignments:

1. Product and data agent: workflows, metric definitions, source mappings, backlog, and acceptance examples.
2. Architecture and implementation agent: schema, API, collector, and operational design; later narrowly scoped implementation changes.
3. Independent quality and security agent: adversarial review, access-control tests, privacy checks, operational failure tests, and release findings.

With four concurrent slots, the lead plus three specialists can work together. Roles are logical responsibilities, not a need to keep every agent running continuously. Split implementation packages only when independent work exists; avoid agents editing the same files. Use isolated branches/worktrees when a repository exists. The author of a change cannot be its only reviewer. The lead resolves conflicting recommendations and checks integration evidence directly instead of accepting agent summaries as proof.

Every agent report includes: deliverable paths, assumptions, decisions, changed scope, checks actually run and their results, checks not run, open risks, and recommended next action. The lead marks each package accepted, revision required, or blocked. Critical findings require a fix and recheck. Lower-severity deferrals require a named owner, rationale, and target gate. Keep a single decision log and backlog to prevent contradictory plans.

User involvement is by exception: repository/account creation where access is unavailable; credential provision through secure channels; company permissions; and decisions reserved to a responsible human employer or budget owner. The lead makes ordinary technical and sequencing decisions autonomously. Do not treat broad project-management delegation as authority to approve employee monitoring policy, purchase services, or deploy to company endpoints without the company's authorized process. Prepare concrete reviewable deliverables before requesting any necessary intervention. Never place secrets in agent prompts or reports.

Agents work during dispatched tasks. They do not provide continuous monitoring or future autonomous execution unless an actual scheduler is configured and authorized. Status reporting should describe completed evidence, upcoming gate, material changes, and only indispensable human actions; avoid repeated unchanged updates.

## First implementation backlog and acceptance

| Package | Acceptance and meaningful verification |
|---|---|
| Identity and scope | Company identity integration or an explicitly approved temporary authentication path; employees see their data; managers see assigned teams only; administrative actions logged; negative authorization tests cover guessed IDs and exported data. |
| Device enrollment | Enrollment credentials are scoped and revocable; revoked devices cannot ingest; reassignment preserves correct historical ownership; duplicated device/user registrations have deterministic handling. |
| Collector state machine | Mutually exclusive observed states have explicit boundaries. Actual Windows tests cover lock/unlock, sleep/resume, application switches, idle with no input, active reading, multi-session behavior, RDP/VDI where used, and clock changes. Validate only supported environments and state unsupported ones clearly. |
| Offline durability | Bounded encrypted local queue survives restarts; network outage and crash tests demonstrate retry and deduplication; queue saturation signals a gap rather than silently rewriting history; server acknowledges only durable writes. |
| Time reconciliation | UTC storage plus declared reporting timezone; schedules crossing midnight and correction history supported; scheduled time is not inferred from collection; deterministic fixtures cover partial coverage, overlaps, late arrivals, breaks, and late events. |
| Ingestion and retention | Input limits, schema versioning, event identity, and rejection telemetry; replay leaves aggregates stable; batched ingestion load tested at assumed scale; retention removes expired raw data and documents treatment of derived data and backups. |
| Manager workflow | Dashboard differentiates activity, coverage, schedule, and outcomes; every number has a definition and denominator; manager can record a reviewed exception; corrections preserve original evidence and audit history. |
| Deployment and support | Versioned installer/update path, rollback, uninstall, health reporting, and runbook; real hardware verifies endpoint security compatibility and bounded CPU, memory, disk, and network use against thresholds agreed from prototype measurements. |
| Recovery | Encrypted backups and restore procedure; restore drill on a separate environment measures recovery time and data loss against agreed targets; database/disk failure produces actionable operator signals. |

Synthetic tests cannot certify Windows behavior, employee workflow, company endpoint compatibility, or expected production scale. Where hardware or access is absent, report the gate as pending evidence, not passed.

## Cost ledger

Maintain a monthly total-cost ledger with quantity, unit cost, owner, source/date, current spend, incremental spend, and maximum authorized spend. Do not invent prices. Existing subscriptions and spare hardware may reduce incremental spend, but record their allocation and capacity limits.

Include:

- Existing server capacity, power, storage growth, hardware maintenance, Windows development/test devices, and any OS/virtualization licenses.
- Off-host encrypted backup storage, backup transfer, restore capacity, and retention overhead. A backup on the database server alone is insufficient.
- AI subscriptions/API usage and coding-assistant limits; agent concurrency can increase consumption. Record usage and bound delegated tasks.
- CI/build minutes or self-hosted runner maintenance; a Windows build/test runner; release artifact storage; dependency and vulnerability checks.
- Code signing certificate or company signing infrastructure, certificate renewal, installer distribution, and endpoint-management licensing where applicable. Settle the Windows trust/distribution strategy before pilot.
- Identity-provider or endpoint-management features that may require a different existing license tier; avoid assuming every company account includes them.
- Monitoring and alert delivery, certificates/domain requirements, patching time, on-call/support capacity, incident response, and operational handover.
- Third-party software license review and obligations, including dependencies and redistribution. Record licenses without asserting unverified legal conclusions.

Use a measured sizing model: enrolled users × observed hours × events per hour × measured bytes per stored event, plus indexes, derived tables, WAL, backup copies, retention, and headroom. Measure with representative records before sizing infrastructure. PostgreSQL remains the default until realistic query and ingest tests, retention tuning, and indexing demonstrate a specific unmet requirement.

## Risk register

| Risk | Mitigation / trigger | Owner |
|---|---|---|
| Activity interpreted as performance | Explicit metric labels; unknown coverage; contextual exceptions; no employee ranking or automated disciplinary decisions in MVP | Product lead |
| Sensitive customer data collected | Allowlisted metadata; exclude window titles, content, keystrokes, screenshots, and full URLs by default; review new fields before collection | Privacy/security reviewer |
| Windows environment differs from assumptions | G1 real-device feasibility matrix; unsupported modes excluded; endpoint-team participation at rollout gate | Collector owner |
| Misleading totals from event loss or clocks | Durable queue, idempotency, sequence/gap detection, server receipt time, coverage reporting, reconciliation tests | Data/API owner |
| Database and backup share a failure domain | Off-host backup and actual restoration; capacity alerts; recovery ownership | Operations owner |
| AI produces plausible but untested work | Independent review, reproducible test evidence, deterministic fixtures, human-domain validation at pilot | Lead / QA |
| Hidden costs defeat low-cost objective | Full operating ledger and measured capacity; no new paid dependency by default | Lead |
| Scope expands toward ProHance parity | Explicit exclusions; change decisions tied to a daily operational decision and justified cost | Lead |
| No accountable production operator | Named operational owner, access handover, runbook walkthrough and restore demonstration before G4 | Sponsor / lead |
| Company policy or authorization is unavailable | Continue synthetic development; hold employee-data pilot until competent company authority clears rollout | Sponsor |

## Operational handover

Before production, provide architecture and data-flow diagrams, deployment inventory, secrets ownership (never secret values), versioned configuration references, monitoring thresholds, backup/restore procedure, incident severity and escalation guide, retention jobs, access review procedure, update/rollback/uninstall instructions, dependency/license inventory, known limitations, cost ledger, and the next prioritized backlog. The operational owner must demonstrate recovery and an ordinary release using the runbook. AI assistance does not replace an accountable company owner for incidents, employee concerns, access, and policy.
