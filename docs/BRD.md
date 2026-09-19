# AI_Orchestrator (APO)

## Business Requirements Document

**Product:** AI_Orchestrator (APO)
**Document Version:** 1.3
**Status:** APPROVED PRODUCT BASELINE — STRATEGIC REBASELINE + LOCAL ORCHESTRATION EXTENSION
**Last Reconciled:** 16 September 2026
**Previous Product Identity:** AI Usage Monitor, AI Project Orchestrator
**Repository:** `https://github.com/Hossam1104/AI_Orchestrator`
**Local Project Root:** `D:\AI Tools\Active Projects\AI_Orchestrator`
**Active Work Tracker:** GitHub Issues in `Hossam1104/AI_Orchestrator`
**Historical Jira Project:** `APO` at `hossamsqa.atlassian.net`
**Product Owner:** Hossam
**Planner / Architect / Acceptance Authority:** GPT-5.6 Sol

This document is the single authoritative Business Requirements Document for APO. Historical BRDs
and roadmap snapshots remain evidence only. Detailed current sequencing lives in
`docs/IMPLEMENTATION_PLAN.md` and `docs/STRATEGIC_ROADMAP.md`. The approved external-reference
harvest is recorded in `docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md` and is subordinate to this
BRD.

---

# 1. Purpose and Product Continuity

APO is the rebranded and expanded successor to the existing AI Usage Monitor repository. It is not
a greenfield rewrite. The repository, local folder, Git history, tests, accepted foundations, and
reusable implementation remain assets. Existing implementation must be assessed and mapped before
it is treated as complete APO capability.

The previous usage-monitor-only scope is superseded where it conflicts with APO orchestration
requirements. Valid technical and product requirements remain part of APO, especially:

- local-first Windows operation;
- WPF, .NET, MVVM, and modular Clean Architecture;
- dynamic provider capacity and truthful remaining-capacity semantics;
- resilient JSON/JSONL persistence;
- secure credential boundaries and privacy;
- accessible, lightweight desktop UX;
- self-contained consumer deployment;
- focused validation and release evidence; and
- durable, recoverable local AI-assisted software delivery.

Technical identifiers containing `AIUsageMonitor` remain compatibility-sensitive and may be
migrated only through separately approved work. No big-bang technical rename is required.

# 2. Executive Summary and Business Problem

APO is a personal AI engineering control center for managing software projects and the AI models
used to plan, implement, validate, review, remediate, accept, and safely deliver project work.

The owner currently needs to coordinate project state, repositories, work trackers, multiple AI
models, subscriptions, capacity limits, local development tools, validation, review, remediation,
recovery, and final acceptance. Manual copying between planner, executor, reviewer, source-control,
and work-tracking tools causes context loss, prompt drift, incorrect model selection, quota waste,
unverified completion claims, missed state updates, duplicated work, unsafe retries, and
unnecessary owner involvement.

APO SHALL combine:

- AI usage, subscription, quota, credit, reset, and capacity monitoring;
- durable project registry and isolated local workspace awareness;
- Git/GitHub/Azure Repos awareness where configured;
- Jira/Azure DevOps work-item awareness where configured;
- AI agent/model registry, supported connectivity, and local readiness truth;
- quality- and risk-first model routing with quota awareness;
- planner-generated versioned execution contracts;
- bounded autonomous execution on the owner's local machine where appropriate;
- durable checkpoints and restart recovery;
- independent validation and evidence capture;
- independent review and bounded remediation;
- final acceptance and explicit human approval gates;
- safe policy-driven retry/fallback;
- auditable activity, history, evidence manifests, and command-center UX; and
- future controlled multi-model benchmarking and evidence-based routing optimization.

The core objective is:

> Let the owner supervise AI-assisted software delivery instead of manually acting as the clipboard
> between planner, executor, reviewer, source control, validation, work tracking, and recovery.

APO may use approved APIs, CLIs, SDKs, and connected services for long-running local execution where
supported. It must not depend on keeping an interactive browser conversation open.

# 3. Product Vision and Users

APO should let the owner answer from one application:

- Which projects are active?
- What work is running, waiting, blocked, recoverable, in review, or awaiting approval?
- Which models/agents are installed, ready, authenticated, suitable, or degraded?
- Which subscriptions have sufficient remaining capacity?
- Which local executable/version/session will actually run?
- What changed in the repository?
- Did the required build, tests, and validation actually pass?
- What did the independent reviewer find?
- What failed, and is retry/fallback safe?
- What survived or was interrupted after APO or Windows restarted?
- What requires owner attention?
- What happened while the owner was away?

The primary V1 user is an individual software project owner or technical lead who uses multiple AI
systems, Git/GitHub, and possibly Jira or Azure DevOps. Secondary future users may include
individual developers, QA leads, architects, engineering managers, and advanced AI users.
Multi-user collaboration and team RBAC are not V1 requirements.

# 4. Product Principles

## 4.1 Local PC First

APO remains primarily a local Windows application. No APO-owned cloud backend or mandatory APO
account is required for V1. Project configuration, project registry, orchestration state, routing
decisions, managed workspaces, recovery checkpoints, execution authorities, evidence, audit history,
and cached capacity data are local unless an external integration requires its own remote system.

For local development projects, the owner's persistent Windows PC is the authoritative execution
environment. APO should use the owner's real registered repositories, local Git, authenticated AI
CLIs where supported, SDKs, compilers, test runners, browsers, local tools, environment/session
state, and durable evidence. Temporary chat, provider-output, test, or validation sandboxes are not
the canonical project store.

Any future remote/sandbox execution target is optional and separately governed; it is not the
foundation of APO's local orchestration model.

## 4.2 Human Authority Over High-Risk Actions

Autonomy reduces repetitive work; it does not remove ownership. High-risk actions require explicit
human approval as defined by this BRD and the applicable execution contract/policy.

## 4.3 Evidence Over Self-Reported Success

An executor's completion message is not sufficient evidence. APO should independently inspect Git
state, changed files, build and test results, CI, process/runtime state, review findings, and
acceptance evidence where technically possible.

## 4.4 Quality and Risk Before Quota Savings

Capacity influences routing but never overrides capability, risk, quality, security, or required
review. A model with more remaining quota must not receive work that requires a stronger or more
suitable model.

## 4.5 Provider and Integration Truthfulness

APO must distinguish official, verified-local, inferred, manual, stale, authentication-required,
unavailable, degraded, partial, and unsupported data. It must never fabricate quotas, reset times,
plan details, subscription dates, API access, entitlement, CLI readiness, or agent capabilities.

Installation, executable resolution, authentication, entitlement/subscription, capabilities,
availability, freshness, quota, and capacity are separate facts.

## 4.6 Project Isolation

Project code, context, credentials, tracker data, workspaces, run state, evidence, and model
interactions must remain isolated per registered project. APO must not leak one project's data into
another project.

## 4.7 Safe, Recoverable, Visible Autonomy

Autonomous loops are bounded by budgets, retries, stop conditions, cancellation, immutable authority,
and approval gates. Significant decisions and actions are visible in activity history with actor,
project, time, outcome, and evidence identifiers where applicable. A restart or interruption must
leave a recoverable or clearly reported safe checkpoint.

Retry/resume must be based on the latest verified authoritative checkpoint and observed side effects,
not merely on a numbered UI stage.

## 4.8 Official Integration Paths First

Prefer official APIs, OAuth/device flows, supported account surfaces, SDKs, CLIs, and connector
mechanisms. Browser scraping, cookie extraction, password scraping, shell-injection convenience,
and broad provider permission bypasses are prohibited as normal execution architecture.

## 4.9 One Authority Per Concern

New capabilities must extend existing APO authorities where sound. Do not introduce duplicate agent
registries, routing engines, process runners, orchestration state machines, project registries,
persistence systems, or WPF-owned provider/execution policies.

## 4.10 Observable Evidence, Not Private Reasoning

APO may retain summaries, decisions, assumptions, uncertainties, risks, actions, commands/tool
results where safe, validation, reviewer findings, and acceptance state. Private hidden
chain-of-thought is not an execution artifact requirement and must not be demanded or persisted as
such.

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
the normal primary executor. Sonnet remains an explicit fallback/special-need option. Opus remains
independent review; Terra is risk-triggered. Active execution providers are OpenAI/Codex and
Anthropic/Claude unless the owner explicitly changes policy.

Model names, capabilities, availability, routing policy, and project overrides must be represented
as data/configuration rather than scattered hard-coded workflow conditions.

A future historical-performance signal may advise routing only after normal capability/security/
policy eligibility is satisfied. It must be deterministic and explainable in its first version and
must never silently rewrite owner policy.

# 6. V1 Capability Scope

V1 capability families remain:

1. APO product rebrand and governance rebaseline;
2. existing-code assessment, reuse, controlled refactoring, and legacy backfill;
3. AI usage, subscription, and capacity monitoring;
4. project registry and workspace isolation;
5. local repository and workspace awareness;
6. Git and remote source-control integration;
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

Reference-derived reliability improvements that support the core product include runtime/process
reconciliation and Agent Readiness. Advanced benchmarking and historical-performance optimization
are Post-V1 unless explicitly re-prioritized.

V1 does not require a commercial SaaS offering, APO cloud account, multi-user collaboration, team
RBAC, autonomous production deployment without approval, destructive data operations, automatic
subscription purchasing, browser-cookie/password extraction, browser automation as the primary
agent transport, model-quality ranking as a product verdict, unlimited self-modification, or data
the provider does not expose.

# 7. Existing Implementation Reuse Policy

Every significant existing capability is classified as one of:

- **Reuse As-Is**;
- **Reuse With Extension**;
- **Refactor**;
- **Superseded**; or
- **Remove**.

External projects follow the same rule at the idea level: harvest behavior and architecture lessons,
not source code. APO must not be reshaped around a reference implementation merely because that
implementation uses a different stack or simpler orchestration model.

Historical EF Core/SQL Server/LocalDB and WinUI/Windows App SDK implementations remain historical/
superseded and must not be revived without explicit architecture approval.

# 8. Functional Requirements

## 8.1 Project, Work, and Repository Awareness

- **FR-PROJ-001:** Register multiple projects with name, local path, repository, default branch,
  tracker identity, status, governance files, routing policy, and safety policy.
- **FR-PROJ-002:** Verify local paths and source-control state before execution.
- **FR-PROJ-003:** Isolate each project's code, context, credentials, work items, workspaces,
  execution state, and evidence.
- **FR-PROJ-004:** Support Active, Paused, Blocked, Archived, and equivalent project states.
- **FR-PROJ-005:** Maintain a concise reconstructable project checkpoint.
- **FR-TRACK-001:** Integrate with Jira for supported work-item discovery/read/create/update/sync.
- **FR-TRACK-002:** Support Azure DevOps work-item integration for configured projects.
- **FR-TRACK-003:** Keep tracker adapters provider-independent at orchestration level and preserve
  IDs/keys/evidence links.
- **FR-TRACK-004:** Distinguish supported work-item semantics and audit autonomous mutations.
- **FR-GIT-001:** Inspect status, branch, HEAD, remote relationship, owner changes, changed files,
  and diffs independently from executor claims.
- **FR-GIT-002:** Preserve unrelated owner changes and prohibit force push, destructive reset, and
  destructive clean by default.
- **FR-GIT-003:** Support safe task branches, draft pull requests, and protected-branch policy.
- **FR-GIT-004:** Distinguish local repository/worktree evidence from remote SCM/CI evidence.
- **FR-GIT-005:** Represent remote repository/ref/PR/review/check/CI identity, source, capture time,
  freshness, and immutable target identifiers where exposed.
- **FR-GIT-006:** Keep read-only remote evidence separate from controlled remote write/delivery.
- **FR-GIT-007:** Represent Not Configured, Authentication Required, Permission Denied, Unsupported,
  Unavailable, Stale, Partial, and Available truthfully.
- **FR-GIT-008:** Bind protected/high-risk remote mutation to project/work item/contract/repository/
  exact refs/current validation/current approval/actor/audit identity and fail closed on drift.
- **FR-GIT-009:** Model CLIs/executor self-report are non-authoritative for remote SCM/CI truth.

## 8.2 AI Agent, Model Registry, and Local Readiness

- **FR-AGENT-001:** Maintain a registry of agents/models, roles, capabilities, limitations,
  connection mechanism, availability, cost/quota metadata, and project overrides.
- **FR-AGENT-002:** Distinguish interactive-only access from supported CLI/API/SDK execution.
- **FR-AGENT-003:** Use provider-independent planner/executor/reviewer abstractions.
- **FR-AGENT-004:** Never assume a consumer subscription includes API/CLI entitlement.
- **FR-AGENT-005:** Represent unsupported access truthfully and provide manual handoff where safe.
- **FR-AGENT-006:** For supported local agents, represent executable resolution/path provenance,
  safely detectable CLI version, evaluated connection mode, supported invocation modes,
  authentication, entitlement when independently verifiable, capabilities/limitations, tested-at,
  freshness, and a normalized readiness state/reason.
- **FR-AGENT-007:** Re-evaluate stale local readiness after restart or relevant environment change;
  never blindly trust an old executable path/session observation.
- **FR-AGENT-008:** Keep installation/executable presence, authentication, entitlement/subscription,
  and quota/capacity separate. None implies another without independent evidence.

## 8.3 AI Usage, Capacity, and Subscription Monitoring

- **FR-CAP-001:** Represent plan/subscription metadata when exposed.
- **FR-CAP-002:** Support arbitrary quota windows.
- **FR-CAP-003:** Represent used, remaining, limit, unit, window, start/reset, source, confidence,
  capture time, and freshness independently.
- **FR-CAP-004:** Normalize source semantics once; primary UI convention is remaining capacity.
- **FR-CAP-005:** Preserve `DateTimeOffset` and never guess source timezone.
- **FR-CAP-006:** Support safe manual fallback when automatic collection is unavailable.
- **FR-CAP-007:** Keep last-known valid values visible and mark stale/error; isolate provider failure.
- **FR-CAP-008:** Avoid duplicate snapshots when nothing materially changed.
- **FR-CAP-009:** Make capacity available to routing without making it the only routing input.

## 8.4 Classification, Routing, Planning, and Execution Contracts

- **FR-ROUTE-001:** Classify work by scope, complexity, blast radius, risk, expertise, validation
  cost, and suitable role.
- **FR-ROUTE-002:** Consider policy, capabilities, availability/readiness, capacity, prior failures,
  and owner overrides.
- **FR-ROUTE-003:** Quality/risk/security constraints take precedence over quota conservation.
- **FR-ROUTE-004:** Persist an explanation and evidence for every routing decision.
- **FR-ROUTE-005:** Permit a recorded owner override before execution.
- **FR-ROUTE-006:** Future historical-performance routing signals must use durable independent
  evidence, expose cohort/sample-size/freshness/provenance/limitations, and be deterministic and
  explainable in their first implementation.
- **FR-ROUTE-007:** Historical performance, quota, or cost must never make an otherwise ineligible
  agent eligible or bypass required security/review/approval.
- **FR-PLAN-001:** Substantial work runs from an explicit execution contract containing scope,
  state, requirements, constraints, forbidden scope, deliverables, validation, acceptance, and
  stop conditions.
- **FR-PLAN-002:** Preserve exact contract/version/hash as evidence.
- **FR-PLAN-003:** Planner and implementation roles remain separate unless explicitly overridden.
- **FR-PLAN-004:** Stop for material ambiguity affecting requirements or architecture.
- **FR-EXEC-001:** Execute supported agents through approved CLI/API/SDK/integration mechanisms.
- **FR-EXEC-002:** Run long-lived local work without manual copy/paste between intermediate steps.
- **FR-EXEC-003:** Provide cancellation, status, active step, elapsed time, project, agent, task,
  recent activity, structured results, and typed failure outcomes.
- **FR-EXEC-004:** Never mark an interrupted process complete and persist enough state for safe
  restart/resume where supported.
- **FR-EXEC-005:** Own each launched local process as a bounded execution resource; consume output
  safely and either confirm process-tree termination or explicitly report termination unconfirmed.
- **FR-EXEC-006:** Persist enough runtime/process/checkpoint/authority evidence to reconcile a
  previously Running/Waiting execution after APO or Windows restart before automatic continuation.
- **FR-EXEC-007:** A missing/stale PID after restart is not evidence of successful business completion.
- **FR-EXEC-008:** Retry/fallback decisions must be based on typed failure, last verified checkpoint,
  observed side effects, task/capability requirements, current routing/owner policy, capacity truth,
  and remaining budget.
- **FR-EXEC-009:** Different failure classes must support different dispositions; switching models
  is not the generic response to authentication, source/workspace, validation, security, approval,
  or side-effect ambiguity failures.
- **FR-EXEC-010:** Retry/fallback must retain prior evidence, prevent replay of consumed authority,
  bound attempts/time, and avoid duplicate irreversible actions.

## 8.5 Validation, Review, Remediation, Acceptance, and Gates

- **FR-VAL-001:** Support project-specific build, test, lint, format, static-analysis, security, and
  other approved validators.
- **FR-VAL-002:** Capture command/result metadata and timestamps without leaking secrets.
- **FR-VAL-003:** Distinguish targeted from full regression and independently verify executor claims.
- **FR-VAL-004:** Block acceptance on validation failure unless an authorized waiver exists.
- **FR-VAL-005:** Distinguish pre-existing baseline failures from regressions where evidence allows.
- **FR-REV-001:** Require independent review for configured high-value implementation.
- **FR-REV-002:** Default independent reviewer receives repository/requirements/diff/validation
  evidence, not only executor summary.
- **FR-REV-003:** Findings include severity, evidence, affected requirement, disposition, and
  blocking status and remain traceable through remediation.
- **FR-REV-004:** Specialist security review is optional/risk-triggered.
- **FR-FIX-001:** Route accepted findings to bounded remediation, revalidate, and re-review within
  capped loops; exceeding limits requires human review/action.
- **FR-ACC-001:** GPT-5.6 Sol is the default final acceptance authority.
- **FR-ACC-002:** Acceptance considers requirements, exact final state/diff, validation, review,
  unresolved findings, and project policy and returns Accepted/Rejected/Blocked/Requires Human
  Decision with reasons.

Explicit owner approval remains required before protected/default-branch merge, production deploy,
destructive data operations, material architecture/requirements changes, credentials/billing,
high-risk security changes, and any owner-defined approval action.

## 8.6 Activity, Evidence History, UX, Notifications, and Settings

- **FR-AUD-001:** Maintain append-oriented activity history with project, run/task, actor, action,
  timestamp, outcome, and non-secret evidence identifiers.
- **FR-AUD-002:** Make routing, approvals, rejections, reviews, fixes, validations, recovery,
  retry/fallback, and gates inspectable chronologically.
- **FR-AUD-003:** Persist an AI Decision Ledger with actor/model, decision, reason, evidence,
  limitations, override, and final authority without persisting secrets or conversations.
- **FR-AUD-004:** Provide an owner-browsable execution evidence manifest/read model that references
  canonical contract/graph/routing/handoff/workspace/checkpoint/run/runtime/Git/validation/review/
  acceptance evidence rather than duplicating raw transcripts.
- **FR-AUD-005:** Historical evidence must expose source/freshness/missing/partial/stale semantics
  and remain reopenable after application restart.
- **FR-UI-001:** Provide a modern command center summarizing capacity, project state, active runs,
  blockers, recoverability, and owner-attention items.
- **FR-UI-002:** Provide project, execution-center, capacity, agent/model, and activity/audit views
  with clear Ready/Running/Waiting/Cancelling/Cancelled/Interrupted/Blocked/Failed/Review/Accepted/
  Human Action Required states where supported.
- **FR-UI-003:** Mission Control summarizes active work, roles, steps, blockers, approvals,
  repository/tracker state, evidence, and owner attention in one view.
- **FR-UI-004:** Review Inbox/project-health/readiness/recovery surfaces must be evidence-backed and
  expose explicit limitations.
- **FR-UI-005:** Agent status UX should show useful readiness evidence such as resolved executable,
  version/authentication/supported modes/freshness where available and explain degradation.
- **FR-NOTIFY-001:** Notify for human gates, blocked/recoverable runs, useful capacity/reset
  conditions, and configured attention items without excessive noise.
- **FR-SET-001:** Make routing, loop limits, budgets, allowed actions, validation requirements,
  notification thresholds, refresh behavior, and human gates configurable within safe limits.

## 8.7 Strategic Orchestration and Control-Plane Requirements

- **FR-CONTEXT-001:** Maintain one versioned provider-independent canonical project context with
  project/repository/tracker identity, current work, dependencies, active contract, routing,
  validation, review, approval, recovery checkpoint, and next safe action.
- **FR-CONTEXT-002:** Support truthful Smart Continue without manual chat-history reconstruction.
- **FR-CONTEXT-003:** Generate role-specific bounded handoffs with provenance, redaction, context
  budget, omission, and limitation metadata.
- **FR-ORCH-001:** Represent prerequisites/dependents as a validated dependency graph and never
  launch work with incomplete prerequisites or a detected cycle.
- **FR-ORCH-002:** Resume interrupted work only from persisted authoritative checkpoint truth and
  preserve typed interrupted/failed/stale/blocked/human-decision states.
- **FR-ORCH-003:** Prepare isolated worktrees/equivalent workspaces only under explicit source-control
  policy, project isolation, and approval boundaries.
- **FR-ORCH-004:** Bound recurring automation/housekeeping with budgets, stop conditions, audit,
  retention policy, and owner controls; no endless loops.
- **FR-ORCH-005:** Support versioned provider-independent workflow templates that compose existing
  contract/graph/handoff/routing/validation/review/checkpoint/approval authorities rather than
  creating a parallel orchestration engine.
- **FR-ORCH-006:** Workflow templates define roles, dependencies, evidence requirements, stop
  conditions, loop limits, and gates; they do not hard-code provider fallback sequences.
- **FR-ORCH-007:** Future multi-model benchmarking must be explicitly initiated, use one immutable
  comparable task definition, and allocate a separate isolated workspace/run authority/checkpoint/
  evidence set per write-capable candidate.
- **FR-ORCH-008:** Benchmark candidates must not merge/push/deploy or perform destructive external
  actions by default, and benchmark results must not automatically rewrite production routing.
- **FR-ONBOARD-001:** Make project onboarding useful after a small number of choices with advanced
  settings progressively disclosed.
- **FR-EVID-001:** Independent build/test/Git/tracker/security/runtime evidence determines
  progression and acceptance; executor self-report is never proof.
- **FR-EVID-002:** Represent running-application/process evidence truthfully, including executable,
  PID where available, process/termination state, revision relationship, freshness, and degraded
  status.
- **FR-EVID-003:** Store a lightweight environment fingerprint only from an explicit non-secret
  allowlist such as OS/version, architecture, APO version, selected CLI version, Git version,
  project path/branch/commit, working directory, and selected tool/runtime availability.
- **FR-EVID-004:** Never capture unrestricted environments, credentials, raw conversations, full
  provider transcripts, unrelated source copies, or private hidden chain-of-thought as evidence.
- **FR-APPROVAL-001:** Remote/mobile approval remains optional and subject to separate security/
  architecture decision; V1 remains functional without APO-owned cloud backend.

# 9. Orchestration Lifecycle and Safety Controls

The target lifecycle is:

```text
Owner intent / canonical GitHub APO Issue
        ↓
Project/context/repository reconciliation
        ↓
Agent Readiness + environment validation
        ↓
Caller-authoritative task classification
        ↓
Sol planning / versioned execution contract
        ↓
Quality-first routing
        ↓
Permission / approval resolution
        ↓
Isolated workspace + authoritative checkpoint
        ↓
Assigned local executor
        ↓
Runtime / process / Git evidence
        ↓
Independent validation
        ↓
Independent review
        ├── findings → bounded remediation → revalidation → re-review
        ↓
Sol acceptance
        ↓
Human approval gate where required
        ↓
Controlled Git / tracker synchronization
        ↓
Persistent evidence manifest + next safe checkpoint
```

The workflow stops safely when required state, requirements, credentials, repository integrity,
readiness, validation evidence, security authority, or approval is missing. Controls include capped
review/remediation/retry cycles, time/quota/change budgets, cancellation, project/global concurrency
policy, dangerous-action blocks, immutable run authority, and explicit stop reasons.

Retry/fallback is a separate decision after failure, not a blind continuation:

```text
failure class + verified checkpoint + side effects + task/capability + policy + capacity + budget
        ↓
retry same / eligible fallback / wait / reconcile / remediate / owner decision / stop
```

# 10. Provider, Readiness, and Capacity Data Contract

Initial monitored providers include Codex, Claude, Kimi, GitHub Copilot, and Antigravity where
supported. Provider fields must be verified before implementation.

Collection priority remains:

1. official provider API;
2. official OAuth/device/account connection;
3. official authenticated account/usage endpoint;
4. official CLI where useful;
5. safe verified local metadata; and
6. manual input/fallback.

Each field should retain source/method, capture time, confidence, and freshness. Provider adapters
own provider-specific detection, collection, parsing, normalization, error handling, CLI/session
checks, and capability truth. Application owns provider-independent readiness/capacity contracts.
WPF must not parse provider payloads, inspect provider files, probe PATH/session/processes directly,
or manage credentials.

A detected application/CLI does not imply authenticated access, entitlement, or consumer-plan
capacity.

# 11. Historical Monitoring, Analytics, and Alerts

Where capacity data supports it, APO should provide material-change snapshots, reset events,
historical remaining/consumption views, bounded burn-rate/exhaustion estimates with explicit
limitations, charts with gaps rather than fabricated interpolation, and deterministic capacity
recommendations based on real evidence.

Monitoring uses cancellation, timeout, throttling, retry/backoff, rate-limit awareness, provider
isolation, and no overlapping refreshes. Monitoring must not materially consume provider quota.
Alerts remain configurable, deduplicated, and cooldown-aware.

# 12. Architecture and Technology Constraints

The approved portable foundation remains:

- C# / .NET 10;
- WPF / MVVM;
- modular Clean Architecture;
- `System.Text.Json`;
- JSON + monthly JSONL;
- `HttpClient`/resilience extensions where materially justified;
- dependency injection;
- Serilog;
- Windows Credential Manager or another planner-approved secure store;
- Git/GitHub/GitHub Actions;
- focused xUnit tests;
- self-contained Windows release artifacts.

No mandatory database engine/ORM/cloud backend is introduced by the reference-derived roadmap.

Logical dependency direction:

```text
Desktop/WPF -> Application use cases -> Domain
Infrastructure -> Application/Domain contracts
Providers/integrations -> Application/Domain contracts
```

### 12.1 Local persistence

Mutable runtime data belongs under per-user LocalApplicationData. Every long-lived document is
versioned. Critical writes are safe/atomic where supported; history is partitioned/streamed; storage
truth distinguishes missing/empty/valid/unsupported/corrupt/I/O/permission states; optional-file
failure does not destroy healthy state.

Durable local orchestration state includes project metadata, contracts, graphs, routing decisions,
handoffs, workspace receipts, checkpoints, run authorities, validation/review/audit evidence, and
future runtime/process/environment evidence as approved.

### 12.2 Credential safety and privacy

Passwords, raw tokens, cookies, authenticated payloads, conversations, unrestricted environments,
unrelated source content, and private hidden reasoning must not be stored in normal APO state/logs.
Credential references are opaque; actual credentials use approved secure storage. Logs/diagnostics
redact secrets.

# 13. Windows Compatibility, UX, and Release Requirements

Engineering compatibility objective remains Windows 10 1809/build 17763 and Windows 11, with x86,
x64, ARM64 targets and x64 primary. Modern APIs require runtime capability detection/fallback.
Core functionality must not depend on modern GPU/TPM/NPU/AVX hardware.

The command-center UX must remain accessible and responsive and should increasingly expose useful
orchestration truth: project/current-work state, agent readiness, runtime/recoverability, evidence,
retry/fallback reason, review/approval state, and next safe action.

The release remains self-contained and must not require a separately installed .NET runtime, SDK,
Visual Studio, database, Node/npm, embedded browser, or provider CLI merely to open. Missing CLIs or
isolated provider failure must degrade truthfully rather than crash the app.

# 14. Non-Functional, Testing, and Release Quality

APO must be reliable, responsive, maintainable, testable, auditable, and usable. Long-running work
must not freeze WPF. Critical state must survive interruption as far as practical.

High-risk automated coverage includes existing quota/persistence/security/routing/state-transition
areas plus:

- executable provenance/version/auth/freshness readiness;
- process-tree cancellation and termination-unconfirmed;
- stale/missing PID and restart reconciliation;
- checkpoint/run-authority anti-replay across restart/retry;
- differentiated retry/fallback dispositions;
- source/workspace/security/approval stop paths;
- secret-safe environment fingerprint;
- evidence-manifest restart/reopen/missing references;
- benchmark workspace/candidate isolation;
- deterministic historical-routing replay and sparse/stale evidence.

CI uses sanitized fixtures and no live authenticated provider credentials.

# 15. APO Issue Traceability and Approved Epic Structure

The approved APO-1 through APO-17 Epic structure remains unchanged:

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

Current reference-derived/extended mappings are:

| Capability | Priority | Canonical issue |
|---|---:|---|
| Runtime process ownership, restart reconciliation, environment evidence | P0/P1 | APO-55 / #93 under APO-12 |
| Local Agent Readiness + executable provenance/version | P1 | APO-71 / #113 under APO-8 |
| HEAD-bound Project Intelligence Map and Planner Context Pack | P1 | APO-75 / #117 under APO-5/APO-10 |
| Failure-classified retry + policy fallback | P1 | APO-72 / #114 under APO-11 |
| Composable workflow templates | P1 | APO-52 / #90 under APO-8 |
| Decision ledger + owner-browsable execution evidence history | P1 | APO-54 / #92 under APO-16 |
| Shared-core Headless APO CLI | P1/P2 | APO-76 / #118 across existing Application/Domain contracts |
| Governed schedule and event triggers | P2 | APO-77 / #119 under APO-11 |
| Isolated multi-model benchmarking | P2 | APO-73 / #115 under APO-9 |
| Historical-performance routing signal | P2 | APO-74 / #116 under APO-9 |
| Governed ACP/A2A/MCP interoperability | P2 | APO-78 / #120 under APO-8/APO-11 |

APO-70 / #111 remains the sole current implementation gate and precedes this follow-on sequence.

# 16. Initial Legacy-to-APO Mapping

Historical implementation remains classified rather than rewritten from scratch:

| Historical area | APO interpretation | Treatment |
|---|---|---|
| Repository/solution foundation | APO-2 | Reuse / verify |
| Domain/Application architecture | APO-2/APO-3 | Reuse With Extension |
| WPF migration | APO-2 | Reuse / revalidate |
| JSON/JSONL persistence | APO-3/APO-16 | Reuse With Extension |
| Atomic/corrupt/tail resilience | APO-3/APO-16 | Reuse / revalidate |
| Cross-Windows/publish profiles | APO-2/APO-17 | Reuse / revalidate |
| Provider-independent quota concepts | APO-4 | Reuse With Extension |
| Historical EF/SQL/LocalDB | Superseded storage architecture | Superseded |
| Historical WinUI/Windows App SDK | Superseded desktop implementation | Superseded |
| External Flutter orchestration reference | Idea/business-behavior reference only | Harvest ideas, do not copy architecture/code |

# 17. Success and Release Acceptance

APO V1 is successful when the owner can register real projects with isolated durable state and
progress a suitable bounded task through planning, routing, execution, evidence/validation, review,
remediation, acceptance, and required owner gates with materially reduced manual handoffs.

Release acceptance requires no known critical safety bypass, project isolation coverage, secure
credentials, truthful provider/readiness/capacity claims, explainable routing, bounded loops,
reliable cancellation/recovery, required validation/review evidence, human-gate behavior, compatible
local-data handling, evidence-backed Windows/architecture claims, and self-contained consumer
release validation.

Advanced P2 benchmarking/historical routing is not required to declare the core single-executor
product loop usable unless the owner later changes release scope.

# 18. Frozen Decisions and Planner Boundary

Baseline decisions remain:

1. Product identity is AI_Orchestrator (APO).
2. GitHub Issues carrying stable APO keys are the operational tracker; Jira is historical provenance.
3. Existing code/history is preserved and classified before refactoring.
4. AI usage/capacity monitoring remains first-class.
5. Project orchestration is primary capability.
6. The model portfolio and canonical routing policy remain the default.
7. Routing is quality/risk/security first and quota/cost later.
8. High-risk actions remain behind explicit human gates.
9. WPF/.NET/JSON/JSONL is the active foundation.
10. Historical EF/SQL/LocalDB and WinUI/Windows App SDK are superseded.
11. Supported APIs/CLIs/SDKs/integrations are preferred over fragile browser/shell shortcuts.
12. Consumer subscription, executable installation, authentication, entitlement, and capacity are separate facts.
13. The owner's persistent local Windows PC is the authoritative normal local-development execution environment.
14. Retry/resume uses verified authoritative checkpoints and side-effect reconciliation.
15. Parallel write-capable benchmarking requires isolated workspaces/authorities per candidate.
16. Private hidden chain-of-thought is not an APO evidence requirement.
17. Reference implementations are subordinate idea sources; APO architecture remains authoritative.

Detailed sequencing remains planner-controlled. Roadmap presence is not implementation completion or
execution permission.

# 19. Glossary

**APO** — AI_Orchestrator.

**Agent Readiness** — Provider-independent, evidence-backed statement of whether a configured agent
can be used through an exact supported local/remote access path, including relevant executable,
version, invocation-mode, authentication, capability, freshness, and limitation truth.

**Capacity** — Available quota, credits, limit headroom, subscription allowance, or provider
availability data.

**Execution Contract** — Planner-approved versioned specification constraining objective, scope,
validation, acceptance, budgets, forbidden scope, and stop conditions.

**Execution Evidence Manifest** — Owner-browsable read model/index referencing canonical immutable
run/contract/routing/workspace/checkpoint/runtime/Git/validation/review/acceptance evidence.

**Human Approval Gate** — A point where APO must stop and request explicit owner approval.

**Independent Review** — Review by a model/agent other than the implementation executor.

**Project Isolation** — Prevention of cross-project code, context, credential, workspace, evidence,
and work-item leakage.

**Recovery Checkpoint** — Persisted authoritative reference describing what is proven complete and
the next safe continuation boundary.

**Retry Disposition** — Typed decision describing whether/how a failed run may continue, wait,
reconcile, remediate, require human action, or stop.

**Routing Decision** — Evidence-backed selection of an eligible model/agent under current policy.

**Run** — One persisted orchestration instance from intake to terminal state or gate.

# 20. Current Roadmap Sequence

The approved current sequence is:

```text
APO-70 backend cross-process proof (SOL-ACCEPTED / COMPLETE)
  ↓
APO-70 Desktop functional + visual owner acceptance
  ↓
APO-70 final exact-head Sol acceptance
  ↓
APO-55 runtime/process/restart evidence
  ↓
APO-71 local Agent Readiness
  ↓
APO-75 HEAD-bound Project Intelligence Map
  ↓
APO-72 failure-classified retry/fallback
  ↓
APO-52 / APO-54 workflow + evidence-history continuation
  ↓
APO-76 shared-core Headless CLI
  ↓
APO-77 governed schedule/event triggers
  ↓
APO-73 isolated multi-model benchmarking
  ↓
APO-74 transparent historical-performance routing
  ↓
APO-78 governed ACP / A2A / MCP interoperability
  ↓
remaining Post-V1 capabilities as separately authorized
```

P2 optimization must not delay the owner-usable single-executor product loop.

# 21. Approval Status

**Status: APPROVED PRODUCT BASELINE — STRATEGIC REBASELINE + LOCAL ORCHESTRATION EXTENSION**

This BRD incorporates the 2026-09-16 and 2026-09-17 external-reference harvests as incremental APO
extensions.
It does not authorize implementation by itself. Current implementation/runtime truth is maintained
in `.ai/CURRENT_STATE.md`, `.ai/CURRENT_STATE_ADDENDUM_2026-09-17.md`, live Git/GitHub state, and
canonical Issues/PRs. The current execution gate is recorded in `TASK.md`.
