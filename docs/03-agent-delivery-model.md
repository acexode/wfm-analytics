# AI Team Delivery and Project Control

Version 1.0 | 12 September 2026

## 1 Operating model

The coordinating AI lead is the project manager and integration owner. The lead chooses the next work package, delegates bounded tasks, reviews evidence, resolves conflicts, and maintains one accepted baseline. The user receives outcomes and indispensable dependency requests, rather than a menu of routine decisions.

This model has been used for the planning work: product, architecture, and delivery agents produced independent contributions; the lead read and consolidated them, then requested a second review of the baseline. Those contributions remain in docs/reviews for traceability. They are advisory, and their alternative metrics, recovery targets, or milestone labels do not override the three numbered baseline documents.

Agents execute while assigned work is active. This document does not establish an unattended scheduler or promise work will continue after an active run ends. Future work resumes from the saved backlog and decision log, rather than relying on an agent remembering the conversation.

## 2 Roles and assignment rules

The lead owns product scope, dependency choices, budget recommendations, task sequencing, shared contracts, integration tests, and release recommendations. The lead also performs local work while specialist tasks run.

The product and data role owns metric examples, import mappings, workflows, requirements traceability, and pilot evaluation. The platform and data role owns API, PostgreSQL, ingestion, aggregation, identity, and operations. The endpoint role owns the Windows collector, local queue, installer, and real-device evidence. The interface role owns manager and employee views. The independent quality and security role challenges assumptions, tests boundaries, and reviews failure cases.

These are rotating responsibilities, not six permanently running agents. Use at most the available four concurrent slots: one lead and up to three specialists. Activate only roles with independent, useful work. A contributor cannot be the sole reviewer of its own change; another agent reviews it before the lead accepts it. Security-sensitive collection, authorization, update, and deletion changes receive explicit independent review.

Before delegation, the lead assigns a work-package ID, objective, dependency state, permitted files, prohibited scope, acceptance criteria, required tests, and report format. Shared contracts are finalized first. Agents do not modify a shared schema or API contract without returning the proposed change to the lead. Once Git is available, isolated worktrees are preferred for overlapping development; otherwise use exclusive file ownership.

## 3 Required agent report

Each report contains the package ID, changed file paths, decisions and assumptions, acceptance evidence, commands actually run and results, checks not run and why, open findings with severity, cost or scope impact, and the recommended next step. A statement that work is complete is not test evidence.

The lead checks the actual diff or artifact, relevant test output, and cross-module behavior. Package outcomes are accepted, revision required, or blocked on a named dependency. Critical data loss, exposure, or incorrect totals prevent acceptance. Lower-priority findings may be deferred only with an owner, reason, and target gate. Document acceptance never implies software tests passed.

## 4 Initial implementation queue

- WP01 Contracts and fixtures. Owner: product and data. Define CSV templates, OpenAPI shapes, state transitions, employee-day reference fixtures, and source-version rules. Reviewer: platform. Depends on the accepted planning baseline. Exit: independently calculated expected totals for at least eight edge-case days and valid and invalid examples for each import.
- WP02 Development foundation. Owner: platform. Create the .NET and web skeleton, PostgreSQL migrations, synthetic identity, repeatable local startup, and meaningful CI checks. Reviewer: quality. Depends on WP01 contract boundaries. Exit: clean setup from instructions, health check, scoped synthetic request, migration test, and no required paid account.
- WP03 Windows feasibility. Owner: endpoint. Implement the bounded collector state machine and encrypted queue on a controlled test device. Reviewer: platform and quality. Can run alongside WP02 after WP01. Exit: observed Windows state transitions, replay, resource measurements, and content inspection. Windows access is a real dependency for this exit.
- WP04 Durable ingestion and aggregation. Owner: platform. Connect device enrollment, idempotent receipts, versioned daily aggregation, and queue jobs. Reviewer: endpoint and quality. Depends on WP02 and the agreed WP03 event contract. Exit: retry, late-arrival, overlap, clock, and transaction-crash fixtures pass.
- WP05 Sources and identity. Owner: product and platform in separate files. Add company login integration, scope enforcement, roster, schedule, and the first operational feed. Reviewer: quality. Depends on WP02 and WP01. Exit: source totals reconcile, repeat imports are safe, and guessed cross-scope IDs are denied.
- WP06 Decision workflows. Owner: interface. Add overview, employee explanation, corrections, exceptions, and safe exports against the approved API. Reviewer: product and quality. Depends on WP04 and WP05; mocked interface work may begin sooner. Exit: manager task scripts pass with missing, conflicting, and corrected evidence.
- WP07 Operational readiness. Owner: platform and endpoint in separate packages. Add policy rollout, signed update path, retention, backup, alerts, runbooks, and bounded load tests. Reviewer: quality. Depends on WP03 through WP06. Exit: measured restore, rollback, access, and 200 and 1,000-agent test reports.
- WP08 Pilot and release review. Owner: lead with company operations delegate. Gather baseline and at least two weeks of pilot evidence; resolve defects and record rollout decision. Depends on WP07 and company employee-deployment authorization. Exit: every G4 gate has evidence and the operational owner accepts support responsibility.

The lead starts with WP01 and WP02 when implementation begins; Windows feasibility is brought forward as soon as a controlled device is available. Repository creation is not a prerequisite for improving the local contracts. These work packages are queued, not reported as implemented.

## 5 Cost and change control

The default is no new recurring software-service commitment. Agents may select open-source libraries within the approved stack after license and maintenance review. A new service, expanded collection field, runtime AI call, additional platform, or larger rollout requires a recorded decision explaining value, alternatives, tests, operating effort, and cost.

Maintain a cost ledger with quantity, unit, current allocation, incremental cost, price source and date, payer, spending authority, and renewal or capacity limit. Unverified costs remain unquoted rather than being entered as zero. Initial entries are:

- Company Linux VM and disk: proposed reuse; availability and internal allocation unknown; no purchase committed.
- Independent backup destination: proposed reuse; capacity and restore access unknown; no purchase committed.
- Company identity and endpoint management: existing entitlement to be checked; do not assume additional features are included.
- Windows test machine and build runner: access pending; existing-device route preferred.
- Signing and installer distribution: company PKI or signing facility to be checked; any certificate purchase remains a separate budget decision.
- Development AI usage: uses the current account; bounded delegation consumes its available usage; no new API subscription is planned.
- Operations time and security maintenance: identify the company owner and recurring effort during pilot; it is part of total cost even without a vendor bill.

Monthly incremental cost equals incremental compute plus storage plus backup plus transfer plus signing allocation plus build and identity charges plus AI charges, where applicable. Also report internally allocated facilities and operator hours separately. A paid proposal must include an actual quote and a lower-cost alternative before expenditure.

## 6 Risk register and ownership

- K01 Misleading performance interpretation. Owner: product. Mitigation: separate activity, coverage, context, and output; require source and denominator labels. Release blocker: a view presents missing data as zero work or activity as billable output.
- K02 Windows or VDI incompatibility. Owner: endpoint. Mitigation: real-device spike before interface expansion. Trigger: state or permission behavior differs from assumptions; narrow supported scope and re-estimate.
- K03 Lost or duplicated intervals. Owner: platform. Mitigation: local durability, global receipts, deterministic recomputation, overlap and replay tests. Release blocker: unexplained category totals or acknowledged loss outside the declared recovery model.
- K04 Unauthorized employee detail. Owner: quality and security. Mitigation: scoped API and export tests, revocation, minimum collection. Release blocker: any unresolved unauthorized-access finding.
- K05 Hidden operating cost. Owner: lead. Mitigation: allocation ledger, measured storage, no automatic service signup. Trigger: any proposal adds a recurring dependency or exceeds measured host capacity.
- K06 Unrecoverable server. Owner: operations. Mitigation: independent backups, key recovery, restore drills. Release blocker: restoration has not been demonstrated against the target.
- K07 Weak operational sources. Owner: product and company data owner. Mitigation: one validated feed, exact identity mapping, revisioned source totals. Trigger: no authoritative output feed; label time-visibility-only and hold the output-linkage value gate.
- K08 AI work accepted without evidence. Owner: lead. Mitigation: independent review, actual checks, fixed ownership, saved decisions. Release blocker: a required gate is asserted from simulation or narrative alone.
- K09 No accountable company operator. Owner: lead and company sponsor. Mitigation: name the incident and data-policy owners before pilot. Continue synthetic work while access is pending.

## 7 Reporting and company dependencies

At a meaningful milestone, report what is accepted, what was actually verified, the next work package, a material forecast or cost change, and any indispensable company action. Do not ask the user to choose which ordinary task happens next.

Bundle environment dependencies into a concrete request when needed: repository destination if remote collaboration is required; access to a controlled Windows device; approved anonymized source exports; company identity and hosting details; and the appropriate operations or IT delegate. Credentials belong in secure configuration, never chat, documents, commits, or agent reports.

Engineering autonomy does not substitute for company authority over employees, secrets, or spending. The lead prepares a reviewable configuration, deployment package, collection notice, and test record before asking for the final company deployment action. Repository creation and granting access may be performed by the user or their delegate; routine implementation remains the lead's responsibility.
