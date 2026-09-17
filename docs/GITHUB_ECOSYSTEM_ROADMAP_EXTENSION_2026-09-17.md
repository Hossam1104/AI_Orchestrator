# AI_Orchestrator — GitHub Ecosystem Roadmap Extension

**Status:** APPROVED PLANNING EXTENSION  
**Date:** 2026-09-17  
**Product:** AI_Orchestrator (APO)  
**Authority:** Existing APO BRD, architecture, GitHub Issues, accepted source/runtime evidence, and owner direction remain authoritative.  
**Scope:** Planning/tracker extension only. This file does not authorize implementation, model execution, merge, release, or deployment.

---

## 1. Purpose

A broader public GitHub ecosystem review was performed against active/open-source agent and software-engineering orchestration projects, including:

- OpenHands / Agent Canvas and OpenHands Automation;
- Cline;
- Microsoft Agent Framework;
- LangGraph;
- Aider;
- SWE-agent and mini-swe-agent;
- Ruflo;
- historical/secondary references such as AutoGen and Continue.

The goal was not to replace APO or copy another architecture. The goal was to identify product behaviors that can strengthen APO while preserving the existing local-first .NET/WPF control-plane model.

Governing rule:

> APO architecture and verified project truth first. External ideas second.

No reference project may introduce a second project registry, router, planner, execution engine, checkpoint system, workspace authority, evidence store, approval engine, or provider-specific policy layer in WPF.

---

## 2. What the ecosystem confirmed APO already does well

The review reinforced that APO already has unusually strong foundations around:

- local-first Windows execution;
- provider-independent agent/model truth;
- separate installation/authentication/entitlement/capacity semantics;
- exact planning/execution contracts;
- explainable quality/risk-first routing;
- isolated workspaces;
- durable recovery checkpoints;
- immutable execution authority and anti-replay;
- validation/review/acceptance separation;
- human approval boundaries;
- tracker and Git evidence;
- secret-safe diagnostics;
- owner-visible Mission Control direction.

Therefore the plan is incremental extension, not framework migration.

Rejected as replacement architectures:

- Flutter/Dart rewrite;
- cloud-first mandatory execution;
- generic shell runner as the orchestration core;
- hard-coded provider fallback chains;
- shared writable multi-agent workspaces;
- opaque self-learning routing that silently changes owner policy;
- raw transcript/private chain-of-thought persistence;
- arbitrary untrusted plugin/script execution without APO policy gates.

---

## 3. Approved new work items

### APO-75 / GitHub #117 — HEAD-Bound Project Intelligence Map

Priority: **P1 — planning quality and token-efficiency accelerator**.

Purpose:

Provide Sol and later orchestration stages with a compact, reproducible, read-only project model instead of repeatedly rediscovering the repository from raw files.

Canonical snapshot identity:

```text
ProjectId
+ repository/worktree identity
+ branch
+ exact HEAD SHA
+ map schema/version
+ capture time/freshness
= Project Intelligence Map identity
```

Potential bounded contents:

- repository/project tree;
- solution/project/package/config manifests;
- languages/framework/runtime/project types;
- symbol/module relationships where supported;
- dependency/import/reference relationships;
- source-to-test relationships;
- architecture/layer boundaries from source/governance evidence;
- local Git hotspots/history summaries;
- entry points and project-native build/test commands;
- explicit Unknown/Partial states for unsupported analysis.

A **Planner Context Pack** may be generated from the map using a configurable size/token budget and task-relevant selection. It must preserve provenance and must never replace direct source verification when insufficient.

Safety:

- read-only against the owner source checkout;
- stale on HEAD/branch/config/schema change;
- project-isolated;
- timeout/cancellation bounded;
- no repository copy in the evidence store;
- no second routing or planning authority.

### APO-76 / GitHub #118 — Headless APO CLI on Shared Application Core

Priority: **P1/P2 — automation and operability accelerator**.

Purpose:

Expose the exact same Application/Domain control plane through a headless CLI for diagnostics, scripting, CI integration, and advanced owner workflows.

Representative direction:

```text
apo project list
apo project inspect <project>
apo readiness show <agent>
apo run status <run>
apo evidence show <run>
apo continue <project>
```

Required behavior:

- human-readable output;
- versioned JSON/NDJSON structured output;
- stable exit codes;
- explicit non-interactive behavior;
- cancellation/timeouts;
- typed authentication/approval-required results;
- correlation identifiers for ProjectId/RunId/Contract/Checkpoint/Evidence where relevant.

Architecture rule:

> CLI is a presentation/composition surface only. It must not create a second router, planner, runner, project registry, evidence store, checkpoint system, or permission model.

Desktop remains the primary owner-facing UX.

### APO-77 / GitHub #119 — Governed Schedule and Event Trigger Engine

Priority: **P2 — automation multiplier**.

Purpose:

Support scheduled and event-driven engineering work after the core single-executor workflow is stable.

Initial trigger families may include:

- cron/schedule;
- application/restart reconciliation;
- GitHub webhook events;
- tracker/work-item events;
- CI/check completion/failure events;
- explicit manual triggering of saved automation definitions.

Core rule:

> A trigger decides **when to request work**. It does not decide how APO executes the work.

Every triggered request must pass ordinary classification, routing, readiness, workspace, checkpoint, approval, execution, validation, review, anti-replay, and delivery policies.

Reliability requirements:

- event deduplication/idempotency;
- bounded catch-up policy after downtime;
- queue/concurrency limits;
- repeated-failure stop/escalation policy;
- restart reconciliation;
- secret-safe normalized trigger payloads;
- no direct provider-adapter calls from scheduler/webhook handlers.

APO-77 relates to existing APO-57 background automation but does not replace the orchestration runtime.

### APO-78 / GitHub #120 — Governed ACP / A2A / MCP Interoperability

Priority: **P2 — ecosystem/interoperability expansion**.

Purpose:

Represent and integrate standard agent/tool protocols where useful without allowing those protocols to become new orchestration authorities.

Agent Readiness may eventually represent:

- ACP-compatible agent invocation/session capability;
- A2A-compatible agent-to-agent capability;
- MCP client/server tool/resource connectivity;
- protocol versions/capabilities when safely discoverable;
- transport/provenance;
- authentication/trust state;
- freshness/Unknown/Unsupported states.

Protocol adapters remain below existing APO Application contracts.

Protocol metadata never implies:

- authentication;
- entitlement;
- capacity;
- safety;
- write permission;
- owner approval.

MCP/A2A/ACP cannot bypass PlanningExecutionContract, routing, workspace authority, approval, validation, evidence, or anti-replay.

---

## 4. Approved extensions to existing work items

### APO-55 / GitHub #93 — OpenTelemetry-compatible observability projection

Extend runtime/process evidence with an optional telemetry projection correlated to existing APO authority identifiers.

Potential correlation attributes:

- ProjectId;
- Contract / WorkGraph / node;
- RoutingDecisionId;
- Handoff / WorkspacePreparationReceipt;
- RecoveryCheckpoint;
- RunId / ExecutionRunAuthority;
- agent/model/provider/connection;
- validation/review/acceptance identifiers.

Rules:

- canonical APO evidence remains authoritative;
- OpenTelemetry is only an export/projection layer;
- telemetry exporter failure does not invalidate otherwise safe local execution unless a project explicitly requires telemetry;
- default local operation works without any collector;
- only allowlisted/bounded/redacted attributes are emitted.

### APO-54 / GitHub #92 — Run / Trajectory Explorer

Upgrade the owner-facing evidence history from a flat activity list into a normalized run trajectory over canonical evidence.

Conceptual lifecycle:

```text
Owner Request
→ Planning Contract
→ WorkGraph / Node
→ Routing
→ Handoff
→ Workspace Preparation
→ Ready Checkpoint
→ Execution Run Authority
→ Runtime / Process Events
→ Git / Workspace Result
→ Validation
→ Review / Remediation
→ Acceptance / Human Gate
→ Delivery / Final Disposition
```

Expose, when available:

- timestamp/duration;
- actor/role/model;
- typed state/failure reason;
- authority IDs/hashes;
- bounded command/tool result metadata;
- Git diff/change summary;
- validation/review findings;
- retry/remediation lineage;
- telemetry/process correlation IDs;
- stale/missing/partial evidence indicators.

Future checkpoint fork/retry is visualized as a **new linked attempt**, never mutation or reactivation of consumed historical authority.

### APO-52 / GitHub #90 — Versioned skills and constrained lifecycle hooks

Extend workflow templates with bounded, versioned reusable skills and typed lifecycle hooks.

Representative hook points:

```text
BeforePlan
AfterPlan
BeforeExecute
AfterExecute
BeforeValidate
AfterValidate
BeforeReview
AfterReview
OnFailure
BeforeAcceptance
AfterAcceptance
```

Every hook must declare capability, side-effect class, timeout, and failure policy.

Hooks/skills cannot:

- directly invoke provider adapters;
- create execution authority;
- bypass routing/security/approval/workspace/validation/delivery policy;
- execute arbitrary untrusted scripts merely by being labelled a plugin/skill.

### APO-72 / GitHub #114 — explicit attempt lineage / safe fork semantics

Retry, fallback, remediation, or future owner-initiated checkpoint forks must create a new execution attempt with a new run authority linked to the parent checkpoint/attempt.

Never reactivate consumed Ready authority.

The old attempt remains immutable history and is displayed through APO-54.

---

## 5. Strategic sequencing

The expanded planning order is:

```text
NOW
APO-70 real cross-process execution proof
  ↓
APO-70 Desktop functional + visual owner acceptance
  ↓
APO-70 final exact-head Sol acceptance
  ↓
APO-55 runtime/process/restart evidence + observability projection foundation
  ↓
APO-71 local Agent Readiness
  ↓
APO-75 HEAD-bound Project Intelligence Map
  ↓
APO-72 failure-classified retry/fallback + attempt lineage
  ↓
APO-52 versioned workflows / skills / constrained hooks
  ↓
APO-54 Run / Trajectory Explorer and evidence-history UX
  ↓
APO-76 shared-core Headless CLI
  ↓
APO-77 governed schedules / webhook-event triggers
  ↓
APO-73 isolated multi-model benchmarking
  ↓
APO-74 transparent historical-performance routing
  ↓
APO-78 governed ACP / A2A / MCP interoperability
  ↓
remaining Post-V1 strategic work as separately authorized
```

Planning order is not automatically a hard dependency. Hard `Blocks` relationships should exist only where implementation proves a true prerequisite.

P2 work must never delay the first owner-usable, owner-accepted single-executor orchestration product loop.

---

## 6. Why APO-75 is intentionally early

The Project Intelligence Map is placed before broad retry/fallback and workflow expansion because it can improve almost every later planning/execution cycle:

- lower repeated repository-discovery cost;
- reduce token/context waste;
- improve file/module/test targeting;
- provide stable project structure to skills/templates;
- support impact analysis before retry/remediation;
- make later headless/triggered runs more deterministic;
- make benchmark cohorts more reproducible.

It remains advisory context. Exact source/Git evidence always wins.

---

## 7. Shared-core product direction

The ecosystem review strongly supports a shared-core architecture:

```text
               Desktop / WPF
                    │
                    │
Headless CLI ───── Application ───── Trigger Dispatcher
                    │
                    │
          Domain / canonical authorities
                    │
       Infrastructure / Providers / SCM
```

No presentation surface owns orchestration policy.

This lets APO eventually support Desktop, CLI, scheduled/event-driven automation, and protocol adapters while retaining one source of truth for execution, routing, approvals, recovery, and evidence.

---

## 8. Validation and architecture-health implications

Future implementation should explicitly test:

- Project Intelligence Map HEAD/schema invalidation and project isolation;
- bounded planner context-pack selection;
- Desktop/CLI read-model equivalence;
- structured CLI schema and exit codes;
- non-interactive auth/approval fail-closed behavior;
- trigger idempotency, restart recovery, queue/concurrency limits;
- telemetry-disabled/exporter-failure behavior;
- run trajectory reconstruction and attempt lineage after restart;
- lifecycle-hook capability denial, timeout, cancellation, and authority-bypass prevention;
- protocol unsupported/version/auth/trust/disconnect/cancellation paths;
- no duplicate router/runner/checkpoint/evidence subsystem introduced by these features.

The standing Continuous Implementation Guard and periodic Architecture Health Check remain mandatory.

---

## 9. Tracker synchronization

Canonical GitHub work items for this extension:

- #117 — APO-75 Project Intelligence Map;
- #118 — APO-76 Headless APO CLI;
- #119 — APO-77 Governed Schedule/Event Trigger Engine;
- #120 — APO-78 Governed ACP/A2A/MCP Interoperability.

Existing issues extended by the review:

- #90 — APO-52 workflow templates / skills / lifecycle hooks;
- #92 — APO-54 decision ledger / Run-Trajectory Explorer;
- #93 — APO-55 runtime evidence / OpenTelemetry-compatible projection;
- #114 — APO-72 retry/fallback / attempt lineage and safe forks.

No implementation or completion state is implied by tracker creation/update.

---

## 10. Current execution boundary

This planning extension must not interrupt or broaden APO-70.

At the time of this extension, the active source/runtime proof authority remains the APO-70 branch and its exact accepted runtime-remediation head. Planning files are intentionally staged separately when necessary so an already-authorized exact-head runtime proof is not invalidated by documentation-only commits.

No new executor prompt, model invocation, merge, release, deployment, or Issue #111 closure is authorized by this planning document.
