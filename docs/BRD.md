# AI_Orchestrator (APO)

## Business Requirements Document

**Product:** AI_Orchestrator (APO)
**Document Version:** 1.2
**Status:** APPROVED PRODUCT BASELINE — STRATEGIC REBASELINE
**Date:** 25 August 2026
**Previous Product Identity:** AI Usage Monitor, AI Project Orchestrator
**Repository:** `https://github.com/Hossam1104/AI_Orchestrator`
**Local Project Root:** `D:\AI Tools\Active Projects\AI_Orchestrator`
**Active Work Tracker:** GitHub Issues in `Hossam1104/AI_Orchestrator`
**Historical Jira Project:** `APO` at `hossamsqa.atlassian.net`
**Product Owner:** Hossam
**Planner / Architect / Acceptance Authority:** GPT-5.6 Sol

This document is the single authoritative Business Requirements Document for APO. The versioned
BRDs that preceded it are historical inputs and are no longer active authorities.

---

# 1. Purpose and Product Continuity

APO is the rebranded and expanded successor to the existing AI Usage Monitor repository. It is
not a greenfield rewrite. The repository, local folder, Git history, tests, validated foundation,
and reusable implementation remain assets. Existing implementation must be assessed and mapped
before it is treated as complete APO capability.

The previous usage-monitor-only scope is superseded where it conflicts with APO orchestration
requirements. Valid technical and product requirements remain part of this BRD when they support
the APO product, especially:

- local-first Windows operation;
- WPF, .NET, MVVM, and modular clean architecture;
- dynamic provider capacity and truthful remaining-capacity semantics;
- resilient JSON/JSONL persistence;
- secure credential boundaries and privacy;
- accessible, lightweight desktop UX;
- self-contained consumer deployment; and
- focused validation and release evidence.

APO-18 preserved the existing GitHub repository and local project folder while establishing this
rebaseline. APO-20 subsequently renamed the active repository and physical local root to the AI
Project Orchestrator identity. Technical identifiers containing `AIUsageMonitor` remain unchanged
and may be migrated incrementally under planner-controlled work. No big-bang technical rename is
required here.

# 2. Executive Summary and Business Problem

APO is a personal AI engineering control center for managing software projects and the AI models
used to plan, implement, validate, review, and accept project work.

The owner currently needs to coordinate project state, repositories, work trackers, multiple AI
models, subscriptions, capacity limits, validation, review, remediation, and final acceptance.
Manual copying between planner, executor, reviewer, source-control, and work-tracking tools causes
context loss, prompt drift, incorrect model selection, quota waste, unverified completion claims,
missed state updates, and unnecessary owner involvement.

APO SHALL combine:

- AI usage, subscription, quota, credit, reset, and capacity monitoring;
- project registry and isolated local workspace awareness;
- Git and GitHub awareness;
- Jira and Azure DevOps work-item awareness where configured;
- AI agent/model registry and supported connectivity;
- quality- and risk-first model routing with quota awareness;
- planner-generated execution contracts;
- bounded autonomous execution;
- independent validation and evidence capture;
- independent review and bounded remediation;
- final acceptance and explicit human approval gates; and
- auditable activity, history, notifications, and command-center UX.

The core objective is:

> Let the owner supervise AI-assisted software delivery instead of manually acting as the
> clipboard between planner, executor, reviewer, source control, and work tracking.

APO may use approved APIs, CLIs, SDKs, and connected services for long-running local execution
where supported. It must not depend on keeping an interactive browser conversation open.

# 3. Product Vision and Users

APO should let the owner answer from one application:

- Which projects are active?
- What work is running, waiting, blocked, in review, or awaiting approval?
- Which models/agents are available and suitable?
- Which subscriptions have sufficient remaining capacity?
- What changed in the repository?
- Did the required build, tests, and validation actually pass?
- What did the independent reviewer find?
- What requires owner attention?
- What happened while the owner was away?

The primary V1 user is an individual software project owner or technical lead who uses multiple AI
systems, Git/GitHub, and possibly Jira or Azure DevOps. Secondary future users may include
individual developers, QA leads, architects, engineering managers, and advanced AI users.
Multi-user collaboration and team RBAC are not V1 requirements.

# 4. Product Principles

## 4.1 Local First

APO remains primarily a local Windows application. No APO-owned cloud backend or mandatory APO
account is required for V1. Project configuration, orchestration state, routing decisions, audit
history, and cached capacity data are local unless an external integration requires its own remote
system.

## 4.2 Human Authority Over High-Risk Actions

Autonomy reduces repetitive work; it does not remove ownership. High-risk actions require explicit
human approval as defined by this BRD and the applicable Jira execution contract.

## 4.3 Evidence Over Self-Reported Success

An executor's completion message is not sufficient evidence. APO should independently inspect Git
state, changed files, build and test results, CI, review findings, and acceptance evidence where
technically possible.

## 4.4 Quality and Risk Before Quota Savings

Capacity influences routing but never overrides capability, risk, quality, or required review. A
model with more remaining quota must not receive work that requires a stronger or more suitable
model.

## 4.5 Provider and Integration Truthfulness

APO must distinguish official, verified-local, inferred, manual, stale, authentication-required,
unavailable, and unsupported data. It must never fabricate quotas, reset times, plan details,
subscription dates, API access, or agent capabilities.

## 4.6 Project Isolation

Project code, context, prompts, credentials, tracker data, run state, and model interactions must
remain isolated per registered project. APO must not leak one project's data into another project.

## 4.7 Safe, Recoverable, Visible Autonomy

Autonomous loops are bounded by budgets, retries, stop conditions, cancellation, and approval
gates. Significant decisions and actions are visible in activity history with actor, project,
time, outcome, and evidence identifiers where applicable. A restart or interruption must leave a
recoverable or clearly reported safe checkpoint.

## 4.8 Official Integration Paths First

Prefer official APIs, OAuth/device flows, supported account surfaces, SDKs, CLIs, and connector
mechanisms. Browser scraping, cookie extraction, password scraping, and browser automation as the
normal agent transport are prohibited.

# 5. AI Operating Model

The approved default strategy is quality/risk first and quota/cost second:

| Priority | Model | Default role |
|---|---|---|
| 1 | GPT-5.6 Sol High | Planner / Architect / Model Router / Quota Governor / Acceptance Authority / Executor Prompt Authority (chat only) |
| 2 | GPT-5.6 Luna xHigh | Primary bounded implementation executor |
| 3 | Claude Sonnet 5 Medium | Fallback / special-need bounded implementation when explicitly selected by Sol |
| 4 | Claude Sonnet 5 High | Fallback / special-need difficult bounded implementation when explicitly selected by Sol |
| 5 | GPT-5.6 Luna Max | Exceptional implementation escalation only |
| 6 | Claude Opus 5 | Independent critical reviewer |
| 7 | GPT-5.6 Terra Medium/High | Specialist security, concurrency, and data-integrity assurance |
| 8 | Claude Haiku 4.5 | Disabled from active routing |

Sol owns requirement interpretation, architecture, task classification, execution contracts,
routing policy, acceptance criteria, executor prompt authority, and final acceptance. Luna xHigh is
the normal primary executor. Sonnet remains an active fallback/special-need option only when Sol
explicitly selects it; Haiku is disabled from active routing; Opus remains an independent critical
reviewer; and Terra is risk-triggered. Active execution providers are OpenAI/Codex and
Anthropic/Claude only. No external provider is an active orchestration executor unless this policy
is explicitly changed.

Model names, capabilities, availability, routing policy, and project overrides must be represented
as data/configuration rather than scattered hard-coded workflow conditions.

# 6. V1 Capability Scope

V1 includes the following capability families:

1. APO product rebrand and governance rebaseline;
2. existing-code assessment, reuse, controlled refactoring, and legacy backfill;
3. AI usage, subscription, and capacity monitoring;
4. project registry and workspace isolation;
5. local repository and workspace awareness;
6. Git and GitHub integration;
7. Jira and Azure DevOps work-item integration where configured;
8. AI agent/model registry and supported connectivity;
9. intelligent task classification and quota-aware model routing;
10. planner-generated planning and execution contracts;
11. bounded autonomous implementation execution;
12. validation and evidence capture;
13. independent review and remediation;
14. final acceptance and human approval gates;
15. command-center project UX;
16. activity, audit, history, and notifications; and
17. local persistence, security, compatibility, CI, and self-contained release quality.

V1 does not require a commercial SaaS offering, APO cloud account, multi-user collaboration, team
RBAC, autonomous production deployment without approval, destructive data operations, automatic
subscription purchasing, browser-cookie extraction, password scraping, browser automation as the
primary agent transport, replacement of GitHub/Jira/Azure DevOps/the IDE, unlimited
self-modification, or provider data that the provider does not expose.

V1 also excludes AI chat, prompt-runner features unrelated to approved project execution, model
quality ranking, cloud sync, mobile apps, payment workflows, and speculative provider APIs.
Consumer subscriptions and separate API/CLI entitlements must never be assumed equivalent.

# 7. Existing Implementation Reuse Policy

Every significant existing capability is classified during planner-controlled mapping as exactly
one of:

- **Reuse As-Is** - already fits the APO requirements and architecture;
- **Reuse With Extension** - valid foundation requiring additional capability;
- **Refactor** - useful behavior whose boundaries, naming, or design need controlled change;
- **Superseded** - intentionally replaced architecture retained as history; or
- **Remove** - no longer valid, unsafe, or unnecessary.

The following areas are known candidates for assessment:

- .NET solution and project foundation;
- WPF desktop shell and MVVM/composition root;
- Domain/Application separation and provider-independent contracts;
- dynamic quota and used/remaining normalization;
- dependency injection and logging;
- JSON document and monthly JSONL persistence;
- atomic writes, corruption handling, quarantine, and duplicate suppression;
- startup/storage resilience;
- Windows target configuration and self-contained publish profiles; and
- focused xUnit tests and GitHub repository governance/history.

Existing code is not automatically complete against APO. A backfilled Jira item may be accepted
only after its code mapping, requirement mapping, architecture compatibility, and validation
evidence are explicit.

The completed historical EF Core, SQL Server, LocalDB, WinUI, and Windows App SDK implementation
must remain recorded as historical work where applicable, but it is architecturally superseded by
the portable WPF/JSON/JSONL foundation. It must not be revived without explicit planner approval.

# 8. Functional Requirements

## 8.1 Project, Work, and Repository Awareness

- **FR-PROJ-001:** Register multiple projects with name, local path, repository, default branch,
  tracker type/identifier, status, governance files, routing policy, and safety policy.
- **FR-PROJ-002:** Verify local paths and source-control state before execution.
- **FR-PROJ-003:** Isolate each project's code, context, credentials, work items, and execution
  state.
- **FR-PROJ-004:** Support Active, Paused, Blocked, Archived, and equivalent project states.
- **FR-PROJ-005:** Maintain a concise reconstructable project checkpoint.
- **FR-TRACK-001:** Integrate with Jira for work-item discovery, reading, creation, update, and
  synchronization where permissions allow.
- **FR-TRACK-002:** Support Azure DevOps work-item integration for configured projects.
- **FR-TRACK-003:** Keep tracker adapters provider-independent at the orchestration layer and
  preserve tracker IDs/keys and evidence links.
- **FR-TRACK-004:** Distinguish Epic, Story/User Story, Task, Bug, and Sub-task semantics where
  supported. Autonomous tracker updates must be auditable.
- **FR-GIT-001:** Inspect status, branch, HEAD, remote relationship, owner changes, changed files,
  and diffs independently from executor claims.
- **FR-GIT-002:** Preserve unrelated owner changes and prohibit force push, destructive reset, and
  destructive clean by default.
- **FR-GIT-003:** Support safe task branches, draft pull requests, and protected-branch policy.
- **FR-GIT-004:** Distinguish local repository/worktree evidence from remote source-control and CI
  evidence. Remote evidence must use provider-independent orchestration contracts and official
  GitHub or Azure Repos integration paths where configured.
- **FR-GIT-005:** Where exposed, represent remote repository identity, default/current branch
  relationship, remote commit identity, pull-request identity/state, mergeability, reviews,
  checks/status, CI/workflow evidence, provider/source, capture time, freshness, and immutable
  target identifiers.
- **FR-GIT-006:** Keep read-only remote evidence separate from later remote write/delivery
  operations. APO-62 owns remote evidence; controlled delivery belongs to APO-63 and requires its
  own authorization, validation, approval, and audit boundaries.
- **FR-GIT-007:** Distinguish Not Configured, Authentication Required, Permission Denied,
  Unsupported, Unavailable, Stale, Partial, and Available remote evidence. Missing or unavailable
  evidence must never be represented as healthy, passing, synchronized, or up to date.
- **FR-GIT-008:** Bind protected or high-risk remote mutation to the project, work item,
  execution-contract version, repository, base/head refs, exact head SHA, current validation
  evidence, current approval decision, actor, and audit identity; fail closed when any material
  evidence changes.
- **FR-GIT-009:** Treat model CLIs and executor self-report as non-authoritative for remote SCM or
  CI truth; official integrations and independently captured evidence remain the source of truth.

## 8.2 AI Agent and Model Registry

- **FR-AGENT-001:** Maintain a registry of agents/models, roles, capabilities, limitations,
  connection mechanism, availability, cost/quota metadata, and project overrides.
- **FR-AGENT-002:** Distinguish interactive-only access from supported CLI/API/SDK execution.
- **FR-AGENT-003:** Use a provider-independent executor abstraction.
- **FR-AGENT-004:** Never assume a consumer subscription includes API/CLI entitlement.
- **FR-AGENT-005:** Represent unsupported access truthfully and provide manual handoff where safe.

## 8.3 AI Usage, Capacity, and Subscription Monitoring

AI capacity monitoring remains a first-class APO capability.

- **FR-CAP-001:** Represent plan/subscription metadata when exposed.
- **FR-CAP-002:** Support arbitrary quota windows, not fixed five-hour or weekly columns.
- **FR-CAP-003:** Represent used, remaining, limit, unit, window type, start, reset, source,
  confidence, capture time, and freshness independently.
- **FR-CAP-004:** Normalize source semantics once. The primary UI convention is remaining capacity;
  a used percentage must never be displayed as remaining.
- **FR-CAP-005:** Preserve `DateTimeOffset` offsets for source data, calculate safely, and display
  reset times in the user's Windows timezone without guessing a source timezone.
- **FR-CAP-006:** Support safe manual fallback when automatic collection is unavailable.
- **FR-CAP-007:** Keep last-known valid values visible and mark stale/error; never replace them with
  zero. Isolate provider failure from the rest of the application.
- **FR-CAP-008:** Avoid duplicate snapshots when nothing materially changed.
- **FR-CAP-009:** Make capacity available to routing without making it the only routing input.

Supported quota concepts include rolling five-hour, session, daily, weekly, rolling seven-day,
monthly, billing-cycle, credits, AI credits, model-specific, request allowance, tokens,
extra-usage, and custom windows. New provider quota types must be addable without a schema redesign.

Subscription data may include provider, plan, original start, current billing-period start/end,
renewal or paid-through date, cancellation date, auto-renew, cadence, price, currency, source,
confidence, and last-verified time. Missing values are valid and must display as unavailable rather
than being invented. Labels must distinguish active recurring billing, cancelled/paid-through,
and expired subscriptions.

## 8.4 Classification, Routing, and Execution Contracts

- **FR-ROUTE-001:** Classify work by scope, complexity, blast radius, risk, expertise, validation
  cost, and suitable role.
- **FR-ROUTE-002:** Consider policy, capabilities, availability, capacity, prior failures, and
  owner overrides.
- **FR-ROUTE-003:** Quality/risk constraints take precedence over quota conservation.
- **FR-ROUTE-004:** Persist an explanation and evidence for every routing decision.
- **FR-ROUTE-005:** Permit an owner override before execution and record it.
- **FR-PLAN-001:** Substantial work runs from an explicit execution contract containing scope,
  state, requirements, constraints, forbidden scope, deliverables, validation, acceptance, and
  stop conditions.
- **FR-PLAN-002:** Preserve the exact contract and version as evidence.
- **FR-PLAN-003:** Planner and implementation roles remain separate unless explicitly overridden.
- **FR-PLAN-004:** Stop for material ambiguity affecting requirements or architecture.
- **FR-EXEC-001:** Execute supported agents through approved CLI/API/SDK/integration mechanisms.
- **FR-EXEC-002:** Run long-lived local work without manual copy/paste between intermediate steps.
- **FR-EXEC-003:** Provide cancellation, status, active step, elapsed time, project, agent, task,
  recent activity, structured results, and typed failure outcomes.
- **FR-EXEC-004:** Never mark an interrupted process complete and persist enough state for a safe
  restart or resume where supported.

## 8.5 Validation, Review, Remediation, Acceptance, and Gates

- **FR-VAL-001:** Support project-specific build, test, lint, format, static-analysis, security,
  and other approved validators.
- **FR-VAL-002:** Capture command/result metadata and timestamps without leaking secrets.
- **FR-VAL-003:** Distinguish targeted from full regression validation and independently verify
  executor claims where practical.
- **FR-VAL-004:** Block acceptance on validation failure unless an authorized, recorded waiver exists.
- **FR-VAL-005:** Distinguish pre-existing baseline failures from regressions where evidence allows.
- **FR-REV-001:** Require independent review for configured high-value implementation.
- **FR-REV-002:** Claude Opus 5 is the default independent reviewer and must receive repository,
  requirements, diff, and validation evidence rather than only an executor summary.
- **FR-REV-003:** Findings include severity, evidence, affected requirement, disposition, and
  blocking status, and remain traceable through remediation.
- **FR-REV-004:** Terra security review is optional and risk-triggered.
- **FR-FIX-001:** Route accepted findings to an appropriate bounded or substantial fixer, revalidate,
  and re-review. Review/fix cycles are capped; exceeding the cap becomes Human Review Required.
- **FR-ACC-001:** GPT-5.6 Sol is the default final acceptance authority.
- **FR-ACC-002:** Acceptance considers requirements, final state/diff, validation, review outcome,
  unresolved findings, and project policy, and returns Accepted, Rejected, Blocked, or Requires
  Human Decision with reasons.

Explicit owner approval is required before merging to a protected/default branch, production
deployment, destructive data changes or migrations, material architecture or business-requirement
changes, credential/billing/subscription actions, high-risk security changes, and any owner-defined
approval action. APO may prepare branches, commits, evidence, tracker updates, and recommendations
before the gate.

## 8.6 Activity, UX, Notifications, and Settings

- **FR-AUD-001:** Maintain append-oriented activity history with project, run/task, actor, action,
  timestamp, outcome, and non-secret evidence identifiers.
- **FR-AUD-002:** Make routing, approvals, rejections, reviews, fixes, validations, and gates
  inspectable in chronological timelines.
- **FR-UI-001:** Provide a modern command center summarizing capacity, project state, active runs,
  blockers, and owner-attention items.
- **FR-UI-002:** Provide project, execution-center, AI capacity, agent/model, and activity/audit
  views with clear Running, Waiting, Blocked, Failed, Review, Accepted, and Human Approval Required
  states.
- **FR-NOTIFY-001:** Notify for human gates, blocked runs, useful capacity/reset conditions, and
  other configured attention items without excessive noise.
- **FR-SET-001:** Make routing, loop limits, budgets, allowed actions, validation requirements,
  notification thresholds, refresh behavior, and human gates configurable within safe limits.

## 8.7 Strategic Orchestration and Control-Plane Requirements

The approved strategic direction makes APO the owner's local-first engineering control plane. The
following requirements are roadmap capabilities; they are not claims that the current source has
already implemented the full orchestration runtime.

- **FR-CONTEXT-001:** Maintain one versioned, provider-independent canonical project context with
  project/repository/tracker identity, current work, dependencies, active contract, routing,
  validation, review, approval, recovery checkpoint, and next safe action.
- **FR-CONTEXT-002:** Support a truthful Smart Continue operation that resolves the current project
  and bounded checkpoint without requiring manual chat-history reconstruction.
- **FR-CONTEXT-003:** Generate role-specific, bounded planner/executor/reviewer/acceptance handoffs
  with provenance, redaction, context budget, omission, and limitation metadata.
- **FR-ORCH-001:** Represent prerequisites and dependents as a validated dependency graph; never
  launch work with incomplete prerequisites or a detected cycle.
- **FR-ORCH-002:** Resume interrupted work only from a persisted checkpoint and preserve typed
  interrupted, failed, stale, blocked, and human-decision states.
- **FR-ORCH-003:** Prepare isolated worktrees or equivalent execution workspaces only under explicit
  source-control policy, project isolation, and approval boundaries.
- **FR-ORCH-004:** Bound all recurring automation and housekeeping with budgets, stop conditions,
  audit records, retention policy, and owner controls; no endless loops.
- **FR-ONBOARD-001:** Make project onboarding useful after a small number of choices: repository,
  tracker or skip, model defaults, and confirmation; advanced settings use progressive disclosure.
- **FR-EVID-001:** Let independent build/test/Git/tracker/security/runtime evidence determine
  progression and acceptance; executor self-report is never proof.
- **FR-EVID-002:** Represent running-application evidence truthfully, including executable, PID,
  window/process state, revision relationship, freshness, and normal/degraded status when known.
- **FR-UX-003:** Provide a Mission Control read model that summarizes active work, roles, steps,
  blockers, approvals, repository/tracker state, evidence, and owner attention in one view.
- **FR-UX-004:** Provide a Review Inbox and an explainable project-health/attention summary backed by
  evidence, unresolved risk, freshness, and explicit limitations.
- **FR-AUD-003:** Persist an AI Decision Ledger with actor/model, decision, reason, evidence,
  confidence/limitations, override, and final authority without persisting secrets or conversations.
- **FR-APPROVAL-001:** Keep remote/mobile approval optional and subject to a separate security and
  architecture decision; V1 remains functional without an APO-owned cloud backend.

# 9. Orchestration Lifecycle and Safety Controls

The default lifecycle is:

```text
Owner intent / canonical GitHub APO Issue
        |
        v
Project and repository verification
        |
        v
Task classification and routing decision
        |
        v
Sol planning / execution contract
        |
        v
Assigned executor
        |
        v
Independent evidence collection and validation
        |
        v
Independent review
        |
        +---- findings ----> bounded remediation -> revalidation -> re-review
        |
        v
Sol acceptance
        |
        v
Human approval gate where required
        |
        v
GitHub Issue / repository / configured-tracker synchronization
```

The workflow stops safely when required state, requirements, credentials, repository integrity,
validation evidence, or approval is missing. Controls include maximum review/remediation cycles,
executor retries, time and quota budgets, changed-file/blast-radius limits, cancellation, project
and global concurrency policy, dangerous-action blocks, and explicit stop reasons. Default stop
reasons include repository divergence, conflicts, secret detection, destructive out-of-scope work,
architecture drift, ambiguity, missing credentials, insufficient capacity, repeated failure, and
unverifiable validation.

## 9.1 Frozen owner workflow

The strategic owner workflow is:

```text
Owner intent
    ↓
Project / context resolution
    ↓
Sol planner and architect
    ↓
Bounded execution contract and dependency graph
    ↓
Quality-first model routing and quota decision
    ↓
Luna xHigh execution by default
    ↓
Independent repository, tracker, build, test, and runtime evidence
    ↓
Independent review when cadence or risk requires it
    ↓
Finding adjudication and bounded remediation
    ↓
Revalidation
    ↓
Sol acceptance
    ↓
Owner approval where required
    ↓
Delivery and tracker synchronization
    ↓
Persisted audit and recoverable next state
```

APO should automate the transitions and package only the context required by the receiving role.
The owner remains the authority for high-risk actions, material architecture changes, and protected
delivery.

# 10. Provider and Capacity Data Contract

The initial monitored providers are Codex, Claude, Kimi, GitHub Copilot, and Antigravity. Their
availability and fields must be verified before implementation. No provider is considered complete
from assumptions or an undocumented payload.

Collection priority is:

1. official provider API;
2. official OAuth/device/account connection;
3. official authenticated account or usage endpoint;
4. official CLI where useful;
5. safe verified local metadata; and
6. manual configuration/input fallback.

Each field should retain source/method, capture time, confidence, and freshness. Supported
connection outcomes include Connected, Local Detected, Authentication Required, Partial,
Unsupported, Disabled, Rate Limited, Stale, Error, Offline, and Updating. A detected application
does not imply authenticated usage access. A CLI is optional and never a whole-application
prerequisite. Non-developer and browser-only paths must be represented truthfully.

Provider adapters own detection, collection, provider-specific parsing, normalization, errors, and
capability truth. They return normalized domain/application results. WPF must not parse provider
payloads, inspect provider files, or manage credentials directly.

# 11. Historical Monitoring, Analytics, and Alerts

Where capacity data supports it, APO should provide:

- material-change usage snapshots and quota reset events;
- remaining-capacity and consumption history for 24-hour, 7-day, 30-day, and billing-cycle views;
- consumption since the prior snapshot;
- normalized burn rate only when data is sufficient;
- estimated exhaustion and expected remaining at reset only as explicit estimates with adequate
  evidence;
- charts with gaps rather than fabricated interpolation;
- deterministic capacity recommendations using remaining capacity, relevant windows, reset
  proximity, burn rate, provider availability, staleness, and exhaustion; and
- explanation of each recommendation without ranking model intelligence.

Default monitoring is approximately 60 seconds, plus startup, resume, foreground, manual refresh,
and safe local-change triggers. Calls require cancellation, timeout, throttling, retry/backoff, and
rate-limit awareness. Monitoring must not itself create meaningful quota consumption. Providers are
isolated and overlapping refreshes are prevented.

Default alert thresholds are 30% remaining (warning) and 15% remaining (critical), configurable per
provider/window. Alert types include low capacity, exhausted, restored/reset, stale/provider
failure, authentication expiry, and subscription renewal/expiry. Alerts are deduplicated and
cooled down rather than repeated on every refresh.

# 12. Architecture and Technology Constraints

The approved portable V1 foundation is:

- C# and .NET 10;
- WPF and MVVM;
- modular clean architecture;
- `System.Text.Json`;
- JSON for small state/configuration documents;
- monthly-partitioned JSONL for append-oriented history and event streams;
- `HttpClient` and `Microsoft.Extensions.Http.Resilience` where materially justified;
- `Microsoft.Extensions.DependencyInjection`;
- Serilog;
- Windows Credential Manager or another planner-approved secure store;
- Git, GitHub, GitHub Actions, and focused xUnit tests; and
- self-contained Windows release artifacts.

V1 has no mandatory database engine or ORM. Do not introduce EF Core, SQL Server, LocalDB, SQLite,
Angular, Electron, Tauri, Node.js, npm, embedded Chromium, or an APO cloud backend without an
explicit planner-approved architecture change.

The logical dependency direction is:

```text
Desktop / WPF -> Application use cases -> Domain

Infrastructure -> Application / Domain contracts
Providers and integrations -> Application / Domain contracts
```

Domain must not depend on WPF, serialization details, filesystem APIs, HTTP, provider libraries,
or Windows UI. Application owns provider-independent contracts and use cases, not filenames or
LocalAppData paths. Infrastructure owns JSON/JSONL, safe files, paths, secure credentials, logging,
notifications, and OS integration. Providers own provider-specific collection and normalization.

## 12.1 Local File Persistence

Mutable runtime data belongs below `Environment.SpecialFolder.LocalApplicationData`, approximately
`%LOCALAPPDATA%\AIUsageMonitor\` until a later approved migration changes the path. JSON documents
may contain settings, project/provider state, subscriptions, routing policy, approvals, and other
small state. JSONL may contain monthly-partitioned usage snapshots, orchestration history, audit
events, validation summaries, review findings, and alerts.

Every long-lived JSON document contains an explicit schema version. JSONL records contain record
and schema metadata. Critical JSON writes serialize to a temporary file, flush where practical, and
replace the destination atomically where supported. Per-store/append synchronization protects
concurrent writes without distributed locks.

History services stream only relevant partitions, preserve chronological ordering, support time
ranges, and do not load all lifetime history at startup. Unchanged usage snapshots are not appended.
Storage distinguishes missing, empty, valid, unsupported-schema, corrupt, I/O-failure, and
permission-failure states. Optional-file failure must not prevent startup; problems are reported and
quarantined/backed up safely where possible without silently destroying user data.

## 12.2 Credential Safety and Privacy

Passwords, raw provider tokens, refresh tokens, browser cookies, authenticated payloads, prompts,
conversations, source code, repository content, and unrelated credentials must not be stored in
JSON, JSONL, logs, source control, or telemetry. JSON may contain only opaque credential references.
Actual credentials use Windows Credential Manager, DPAPI where justified, or another planner-approved
secure mechanism. Logs and diagnostics redact secrets.

APO may transmit the minimum project context and relevant source/code needed by an explicitly
configured executor. Project isolation, configured exclusions, secret scanning/redaction, visible
destination, least privilege, and no silent APO-owned telemetry are mandatory.

# 13. Windows Compatibility, UX, and Release Requirements

The engineering compatibility objective is Windows 10 version 1809/build 17763 and Windows 11,
with x86, x64, and ARM64 targets and x64 as primary validation. Exact .NET 10/WPF support claims
must be checked against current Microsoft documentation before release claims. Windows 11-only or
post-build-17763 APIs require runtime capability detection and safe fallback.

Core domain, application, provider, persistence, analytics, recommendation, monitoring, and alert
behavior must not depend on modern OS features, a dedicated/recent GPU, AVX/AVX2, TPM, NPU, or an
AI accelerator. Optional modern backdrops/effects/animations degrade to ordinary WPF surfaces with
identical functionality. Background operation must remain lightweight and asynchronous.

The command-center UX is dark-first, supports light/system themes, uses strong typography,
restrained gradients, rounded cards, provider accents, accessible status text/icons, readable
remaining capacity, reset countdowns, polished loading/empty/stale/partial/error states, responsive
resizing, keyboard navigation, high-DPI support, and restrained motion. A compact Focus HUD and
system tray may keep capacity and attention information visible beside an IDE. Color must never be
the only status signal. General-user language is preferred over CLI/shell/SDK terminology unless a
provider genuinely requires it.

The released application must be self-contained and must not require a separately installed .NET
runtime, SDK, Visual Studio, database engine, Node/npm, embedded browser, or provider CLI. Evaluate
`SelfContained=true` and `PublishSingleFile=true` for WPF without blind trimming. Release
consideration covers `win-x64`, `win-x86`, and `win-arm64`, with claims limited to artifacts and
environments actually validated. The app must open with no connected providers, empty history,
missing optional configuration, offline state, absent provider CLI, or an isolated provider failure.

# 14. Non-Functional, Testing, and Release Quality

APO must be reliable, responsive, maintainable, testable, auditable, and usable. One integration
failure must not crash the application. Critical state writes must survive interruption as far as
practical. Long-running work must not freeze WPF. History reads must be partitioned/streamed.

High-risk automated coverage includes quota normalization and used/remaining conversion, reset and
timezone math, provider parsers/source semantics, subscription interpretation, JSON round trips and
schema compatibility, JSONL append/read/range ordering, duplicate suppression, atomic writes,
corrupt/missing/unsupported-file handling, settings/provider/subscription persistence, secret
redaction and credential-reference safety, routing/scoring, state transitions, and critical WPF
view-model behavior. CI uses sanitized fixtures and no live authenticated provider calls.

Before release, validate self-contained launch on clean compatible systems as available, first-run
and empty/missing/corrupt optional data, offline/stale behavior, provider isolation, human gates,
project isolation, security, WPF accessibility/responsiveness, x64 primary release, and x86/ARM64
targets to the level actually performed. Do not claim unperformed OS, architecture, or clean-machine
validation.

# 15. APO Issue Traceability and Approved Epic Structure

GitHub Issues carrying stable APO keys are the active APO work-tracking system. Repository
documentation, this BRD, architecture decisions, and validation evidence remain the
architecture/governance source of truth. Jira project `APO` is historical provenance and a
migration reference; Jira issues must not override them.

All meaningful new and historical work is progressively mapped to the canonical GitHub APO Issue
set with repository evidence; migrated Jira keys remain preserved as provenance.
Stories/Tasks should record BRD requirement IDs, Epic, current/new/refactor classification, scope,
acceptance criteria, executor role, validation, reviewer requirement, branch/PR/commit evidence,
and final acceptance result. Sol owns detailed decomposition and may refine ordering without
inventing scope. Historical items must not be marked Done solely because an old session label exists.

The approved initial Epic structure is:

| Epic | Capability |
|---|---|
| APO-1 | APO Product Rebrand & Governance Rebaseline |
| APO-2 | Windows Platform & Application Foundation |
| APO-3 | Local Persistence, Resilience & Security Foundation |
| APO-4 | AI Usage, Subscription & Capacity Monitoring |
| APO-5 | Project Registry & Workspace Management |
| APO-6 | Git & GitHub Integration |
| APO-7 | Jira & Azure DevOps Work-Item Integration |
| APO-8 | AI Agent / Model Registry & Connectivity |
| APO-9 | Intelligent Model Routing & Quota-Aware Decisioning |
| APO-10 | Planning & Execution Contracts |
| APO-11 | Autonomous Execution Runtime |
| APO-12 | Validation & Evidence Engine |
| APO-13 | Independent Review & Remediation Engine |
| APO-14 | Acceptance & Human Approval Gates |
| APO-15 | Command Center & Project UX |
| APO-16 | Activity, Audit, History & Notifications |
| APO-17 | Packaging, Compatibility, CI & Release Quality |

APO-18 is the first Story under APO-1 and establishes the original consolidated baseline. The
strategic rebaseline deliberately reuses the approved APO-1 through APO-17 Epic structure and
decomposes the approved direction into bounded Stories APO-38 through APO-63. These Stories are
roadmap backlog items, not implementation authorization; each still requires a Sol-authored
TASK.md contract. No competitor-named Epic is created.

### 15.1 Strategic capability-to-APO Issue mapping

| Strategic capability | Roadmap band | APO Issue mapping |
|---|---:|---|
| Agent/model registry and truthful connectivity | P0 | APO-38 under APO-8 |
| Progressive onboarding and canonical context resolution | P0 | APO-39 under APO-5 |
| Versioned contracts, dependency graph, and role handoffs | P0 | APO-40, APO-41, APO-42 under APO-10 |
| Persistent context and recovery checkpoints | P0 | APO-43 under APO-3 |
| Explainable quality-first routing | P0 | APO-44 under APO-9 |
| Bounded execution and isolated workspaces | P0 | APO-45 under APO-11; APO-46 under APO-6 |
| Jira/Azure DevOps work-item awareness | P0 | APO-47 under APO-7 |
| Remote SCM / CI evidence | P0 | APO-62 under APO-6 |
| Evidence-based QA and human delivery gates | P0 | APO-48 under APO-12; APO-49 under APO-14 |
| Controlled remote source-control delivery | P0 | APO-63 under APO-6 |
| Mission Control | P0 | APO-50 under APO-15 |
| Review Inbox and remediation | P1 | APO-51 under APO-13 |
| Composable skills/workflows | P1 | APO-52 under APO-8 |
| Explainable project health | P1 | APO-53 under APO-15 |
| Decision Ledger and activity audit | P1 | APO-54 under APO-16 |
| Runtime evidence and context-budget management | P1 | APO-55 under APO-12; APO-56 under APO-10 |
| Bounded background automation and housekeeping | P2 | APO-57 under APO-11 |
| Optional remote/mobile approval design | P2 | APO-58 under APO-14 |
| APO-37 hardening debt (findings accepted into backlog; Jira: To Do) | P3 | APO-59, APO-60, APO-61 under APO-6 |

APO-33 remains the existing CI/release Story under APO-17 and is reused rather than duplicated.
APO-37 is merged and Done; its accepted P3 findings are retained in APO-59 through APO-61. Rejected
OPUS-06 and OPUS-08 are intentionally not represented as Jira defects.

### 15.2 Canonical strategic dependency semantics

The canonical APO dependency graph reserves `Blocks` for a real architectural prerequisite. The
repaired strategic backlog has exactly 18 hard dependency pairs; the migrated Jira `Blocks` values
remain historical provenance:

```text
APO-38 -> APO-39                 APO-38 -> APO-44
APO-40 -> APO-41                 APO-40 -> APO-42
APO-40 -> APO-43                 APO-40 -> APO-45
APO-39 -> APO-43
APO-41 -> APO-45                 APO-42 -> APO-45
APO-43 -> APO-45                 APO-44 -> APO-45
APO-46 -> APO-45
APO-45 -> APO-48
APO-48 -> APO-63                 APO-49 -> APO-63
APO-62 -> APO-63
APO-45 -> APO-57                 APO-49 -> APO-58
```

The arrows mean the left Story blocks the right Story. APO-46 precedes APO-45 so safe
project-isolated repository/worktree preparation exists before autonomous repository
implementation. APO-37 to APO-59/60/61 remains accepted `Relates` traceability rather than a hard
dependency. Recommended delivery sequencing remains planner guidance and does not imply additional
GitHub Issue dependency state unless a future contract proves a real prerequisite.

# 16. Initial Legacy-to-APO Mapping

This is a planning seed, not a completion claim. Code-level mapping and acceptance remain future
Sol-controlled work.

| Historical area | APO interpretation | Initial classification |
|---|---|---|
| Repository/solution foundation | APO-2 platform foundation | Reuse / verify |
| Domain and Application architecture | APO-2/APO-3 core contracts | Reuse With Extension |
| Domain integrity remediation | APO-3 correctness foundation | Reuse / verify |
| Historical EF/SQL/LocalDB persistence | Superseded storage architecture | Superseded |
| Historical WinUI/Windows App SDK shell | Superseded desktop implementation | Superseded |
| Portable WPF migration | APO-2 desktop foundation | Reuse / revalidate |
| JSON/JSONL persistence | APO-3/APO-16 local persistence | Reuse With Extension |
| Atomic writes/corruption handling | APO-3 resilience | Reuse / revalidate |
| Startup resilience | APO-2/APO-3 reliability | Reuse / revalidate |
| JSONL latest/range optimization | APO-16 history | Reuse / revalidate |
| Interrupted-tail handling | APO-3/APO-16 resilience | Reuse / revalidate |
| Cross-Windows target configuration | APO-2/APO-17 compatibility | Reuse / revalidate |
| Self-contained publish profiles | APO-17 release foundation | Reuse / revalidate |
| Provider-independent quota concepts | APO-4 capacity domain | Reuse With Extension |
| Old provider-feasibility sequence | APO-4 discovery planning | Superseded / replan |

# 17. Success and Release Acceptance

APO V1 is successful when the owner's projects can be registered with isolated state; a suitable
bounded task can progress from approved contract through execution, validation, independent review,
bounded remediation, and acceptance with reduced manual handoffs; human gates stop high-risk work;
routing uses real capacity without sacrificing quality; audit history explains what happened; and
the application remains usable when a provider, tracker, source-control service, quota source, or
local file fails.

Release acceptance requires no known critical safety bypass, project isolation coverage, secure
credential handling, evidence-based provider/model claims, explainable routing, bounded loops,
reliable cancellation and blocked states, required validation evidence, operational independent
review, verified human-gate behavior, compatible local-data handling, evidence-backed Windows and
architecture claims, and a self-contained consumer release.

# 18. Frozen Decisions and Planner Boundary

The following are baseline decisions unless explicitly changed by the owner/planner:

1. Product identity is AI_Orchestrator (APO); APO-20 renamed the active repository and
   established the local-root target while preserving technical identifiers for a later
   controlled migration. A later reconciliation phase normalized the interim display identity
   AI Project Orchestrator to the current canonical identity AI_Orchestrator.
2. GitHub Issues carrying stable APO keys are authoritative for APO work tracking and traceability;
   Jira project `APO` remains historical provenance for migrated records.
3. Existing code and history are preserved and classified before refactoring.
4. AI usage/capacity monitoring remains a first-class capability.
5. Project orchestration is a primary capability.
6. The model portfolio and operating policy in Section 5 are the default.
7. Model routing is quality/risk first and quota-aware second.
8. High-risk actions remain behind explicit human approval gates.
9. The portable WPF/.NET/JSON/JSONL architecture is the active foundation.
10. Historical EF/SQL/LocalDB and WinUI/Windows App SDK work remains historical/superseded.
11. Supported APIs/CLIs/SDKs/integrations are preferred over fragile browser automation.
12. Consumer subscription access and API/CLI entitlement are separate facts.

APO-18 established governance consolidation. Source-code mapping/refactoring and detailed
Story/Task decomposition remain planner-controlled; completed Stories are evidenced in
`.ai/CURRENT_STATE.md` and `docs/IMPLEMENTATION_PLAN.md`, while roadmap presence is not completion.
Each new implementation boundary still requires GPT-5.6 Sol review and a self-contained execution
contract.

# 19. Glossary

**APO** - AI_Orchestrator.
**Capacity** - Available quota, credits, limit headroom, subscription allowance, or provider
availability data.
**Execution Contract** - Planner-approved task specification constraining scope, validation,
acceptance, and stop conditions.
**Human Approval Gate** - A point where APO must stop and request explicit owner approval.
**Independent Review** - Review by a model/agent other than the implementation executor.
**Project Isolation** - Prevention of cross-project code, context, credential, and work-item leak.
**Routing Decision** - Evidence-backed selection of the appropriate model/agent.
**Run** - One persisted orchestration instance from intake to terminal state or gate.
**Remaining Capacity** - Capacity still available under a specific dynamic window or allowance.

# 20. Approval Status

**Status: APPROVED PRODUCT BASELINE — STRATEGIC REBASELINE**

This BRD is the consolidated APO product and technical baseline. Detailed architecture mapping,
Jira Story/Task decomposition and execution sequencing remain planner-controlled. The approved
strategic backlog is recorded in APO-38 through APO-63; current implementation and validation
truth is maintained in `.ai/CURRENT_STATE.md` and `docs/IMPLEMENTATION_PLAN.md`, with the current
planner boundary in `TASK.md`. APO-69 records the repository-wide rebaseline and cleanup handoff.
Roadmap presence is not implementation completion.
