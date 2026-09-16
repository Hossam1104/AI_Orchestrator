# AI_Orchestrator — External Reference Harvest and Roadmap Integration

**Status:** Approved planning extension
**Date:** 2026-09-16
**Reference repository:** `monocsp/flutter_ai_orchestration`
**APO authority:** Existing APO architecture, BRD, current source, GitHub Issues, and accepted evidence remain authoritative.

---

## 1. Purpose

This document records the approved outcome of the external-reference review. The reference project is used only to harvest useful product and orchestration ideas. It does not replace APO, does not change APO's .NET 10/WPF architecture, and does not authorize implementation by itself.

The governing rule is:

> Existing APO architecture and verified project truth first. External ideas second.

The current implementation gate remains APO-70 / GitHub Issue #111. No reference-derived feature may interrupt or broaden that gate unless a verified P0 correctness defect requires it.

---

## 2. Current APO product direction

APO remains a local-first Windows engineering control plane whose target lifecycle is:

```text
Discover
  ↓
Reconcile
  ↓
Plan
  ↓
Route
  ↓
Execute
  ↓
Validate
  ↓
Review
  ↓
Remediate when required
  ↓
Accept
  ↓
Human Gate when required
  ↓
Sync / Deliver
  ↓
Persist Evidence + Next Safe Checkpoint
```

APO's core local development workflow uses the owner's Windows PC as the authoritative execution environment. Durable project configuration, project registry, orchestration state, routing evidence, workspaces, checkpoints, validation evidence, reviews, and audit state remain local unless an external integration has its own remote source of truth.

Temporary validation folders, provider temp output files, test sandboxes, or chat environments are not authoritative project storage.

---

## 3. What the external reference confirmed APO already does better

The external repository reinforced several useful ideas but did not justify a technology or architecture change. APO already has stronger foundations for:

- provider-independent agent/model identity;
- authentication, entitlement, availability, capability, and quota truth separation;
- caller-authoritative task classification;
- explainable quality-first routing;
- versioned planning/execution contracts;
- exact executor resolution;
- structured no-shell local process execution;
- concurrent stdout/stderr consumption;
- bounded output and timeout handling;
- entire-process-tree cancellation and termination confirmation;
- isolated workspaces;
- immutable execution authority and anti-replay;
- Smart Continue / recovery checkpoints;
- durable persisted Ready recovery;
- independent validation;
- independent review/remediation;
- Sol acceptance and owner approval gates;
- project-isolated JSON/JSONL persistence;
- secret-safe diagnostics.

These areas must be strengthened incrementally where needed, never reimplemented as parallel systems.

---

## 4. Approved additions

### 4.1 APO-55 — runtime process ownership, restart reconciliation, and environment evidence

APO-55 remains the existing owner of truthful runtime evidence. It is extended rather than duplicated.

Desired behavior:

- Every bounded local process started by APO can expose process identity when safely available: executable identity/path provenance, PID, start/end timestamps, exit/termination truth, and freshness.
- Cancellation must either prove execution-owned process-tree termination or explicitly report termination as unconfirmed and keep the workspace guarded.
- After APO restart or Windows restart, previously persisted Running/Waiting execution state must be reconciled against process, checkpoint, workspace, repository, and Git reality before any resume or retry decision.
- A missing PID is never proof that work completed.
- APO records a lightweight, allowlisted, non-secret environment fingerprint for reproducibility and recovery.

Safe fingerprint candidates include:

- OS/version;
- process architecture;
- APO version;
- selected agent/model identity;
- CLI version where safely detectable;
- Git version;
- registered project path;
- branch and commit;
- prepared workspace path;
- selected SDK/runtime/tool availability where safely detectable.

Never capture unrestricted environment variables, credentials, tokens, raw conversations, complete provider transcripts, or private model reasoning.

Priority: **P0/P1 correctness foundation**, implemented through APO-55 after the current APO-70 runtime proof boundary.

---

### 4.2 APO-71 — Local Agent Readiness and executable provenance

GitHub Issue #113 / APO-71 extends the existing APO-8 / APO-38 agent truth model.

The target readiness composition is:

```text
Agent / Model Identity
        +
Executable Resolution + Provenance
        +
CLI Version
        +
Connection Mode
        +
Authentication Truth
        +
Entitlement Truth where independently verifiable
        +
Capabilities / Limitations
        +
Freshness
        =
Agent Readiness
```

Readiness states should be truthful and bounded, such as Ready, Degraded, Unavailable, Authentication Required, Unsupported, or Unknown using existing domain conventions where possible.

Important boundaries:

- installation never implies authentication;
- authentication never implies consumer subscription or API entitlement;
- executable detection never implies quota/capacity;
- provider-specific probes stay in Provider boundaries;
- WPF consumes Application/read-model results and does not inspect PATH or provider sessions directly;
- existing AgentDefinition / AgentConnectionResult authorities remain canonical.

Priority: **P1 / Post-V1 follow-on**.

---

### 4.3 APO-72 — failure-classified retry and policy-driven fallback

GitHub Issue #114 / APO-72 composes existing execution, routing, checkpoint, authority, and evidence capabilities into safe recovery behavior.

The decision model is:

```text
Typed Failure
    +
Last Verified Authoritative Checkpoint
    +
Observed Side Effects
    +
Task Requirements
    +
Agent Capabilities
    +
Routing Policy
    +
Owner Policy
    +
Quota / Capacity Truth
    +
Remaining Budget
      ↓
Retry / Fallback Disposition
```

Representative dispositions:

- RetrySameExecutor;
- RouteEligibleFallback;
- WaitForAuthentication;
- WaitForCapacity;
- ReconcileWorkspace;
- ReconcileSource;
- RunRemediation;
- RequireOwnerApproval;
- StopNotRetryable.

A different model is not a valid generic response to every failure.

Fallback must not solve:

- moved source authority;
- dirty/ambiguous workspace;
- security boundary;
- owner approval boundary;
- already-consumed run authority;
- unresolved validation failure;
- irreversible side-effect ambiguity.

Priority: **P1 / Post-V1 follow-on** after reliable single-run runtime evidence.

---

### 4.4 APO-73 — controlled multi-model benchmarking in isolated workspaces

GitHub Issue #115 / APO-73 adapts the reference project's parallel comparison idea to software-delivery safety.

A benchmark is explicitly owner-initiated and uses one immutable task/acceptance/validation definition. Each candidate receives:

- its own isolated disposable workspace;
- its own execution authority;
- its own checkpoint chain;
- its own validation evidence;
- its own reviewer evidence where configured.

No two write-capable candidates share one working tree.

Potential metrics include:

- success/failure and failure class;
- first-attempt completion;
- validation/build/test results;
- reviewer disposition;
- scope adherence;
- changed files/lines;
- retries;
- elapsed time;
- provider-truthful resource usage;
- architecture/regression findings;
- final acceptance disposition.

Benchmarking is measurement first. It must not silently change production routing weights or select a production winner.

Priority: **P2 / Post-V1**.

---

### 4.5 APO-74 — transparent historical-performance routing signals

GitHub Issue #116 / APO-74 is an advanced routing optimization that must wait for trustworthy execution history.

Historical performance may eventually inform routing only after normal eligibility has already passed.

The priority order remains:

1. correctness;
2. security / required assurance;
3. required capability / task risk;
4. explicit project and owner policy;
5. current availability/authentication/entitlement;
6. quota/capacity constraints where applicable;
7. transparent historical performance;
8. cost/resource tie-break where safe.

Historical metrics must expose cohort, sample size, freshness, provenance, and limitations. Sparse or stale evidence must remain Unknown rather than becoming an invented score.

The first implementation should be deterministic/rules-based, not ML-based.

Priority: **P2 / Post-V1**.

---

## 5. Existing backlog items to extend instead of duplicating

### APO-52 — Composable Skills and Workflow Templates

APO-52 remains the owner of reusable workflow shapes. Future templates may express, without hard-coding provider names:

```text
Implementation:
Planner → Executor → Verifier → Reviewer → Remediator → Acceptance

Recovery:
Diagnostician → Fixer → Verifier

Architecture Review:
Analyzer → Independent Reviewer → Architecture Decision

Security Review:
Implementer → Security Reviewer → Remediation → Verification
```

Templates define workflow structure. Routing still selects eligible models dynamically.

### APO-54 — AI Decision Ledger and Activity Audit Timeline

APO-54 should provide an owner-browsable execution/history timeline and evidence manifest that references existing immutable APO authorities rather than duplicating raw evidence.

An execution manifest may reference:

- owner request / work item;
- planning contract;
- WorkGraph;
- routing decision;
- handoff package;
- workspace receipt;
- recovery checkpoint;
- execution authority;
- process/runtime evidence;
- environment fingerprint;
- Git state/diff evidence;
- validation evidence;
- review/remediation evidence;
- acceptance and owner gate;
- final disposition.

Do not persist private chain-of-thought, unrestricted environment data, or raw provider transcripts merely to make a session folder look complete.

### APO-55 — Truthful Runtime Evidence

APO-55 owns active execution/process/restart truth and feeds Mission Control. APO-54 owns the chronological/audit browsing experience. The two must reference shared evidence rather than copy it.

---

## 6. Explicitly rejected reference patterns

APO must not adopt:

- Flutter/Dart rewrite or stack replacement;
- reference-repository copy/transplant;
- generic shell-based command construction;
- `cmd.exe /c` as the core runner;
- broad permission bypasses such as `--dangerously-skip-permissions`;
- provider-specific fallback order such as Claude → Gemini → Codex;
- retry from a numbered stage without checkpoint/side-effect reconciliation;
- multiple write-capable models operating in the same workspace;
- raw stdout/command/transcript persistence without secret-safe policy;
- unrestricted environment capture;
- mandatory private reasoning / chain-of-thought memo capture;
- model-selected task authority that can strengthen/weaken caller classification;
- installation treated as authentication, entitlement, subscription, or capacity truth.

---

## 7. Architecture ownership

### Domain / Application

Own provider-independent concepts:

- readiness state;
- execution runtime evidence contracts;
- retry/fallback disposition;
- benchmark definition/result references;
- historical-performance routing input when later approved;
- evidence-manifest/read-model contracts;
- restart reconciliation classification.

### Infrastructure

Own OS/filesystem/process details:

- process/PID inspection;
- process-tree observation;
- environment fingerprint collection;
- persistent evidence storage implementation;
- local Git/tool/runtime probes that are infrastructure concerns.

### Providers

Own provider-specific truth:

- executable/session checks specific to the provider;
- CLI version/auth probes;
- supported invocation modes;
- provider-specific process/result normalization;
- provider capability limitations.

### Desktop / WPF

Presentation only:

- readiness display;
- execution timeline/status;
- retry/fallback explanation and owner action;
- benchmark comparison UI;
- evidence/history browsing.

WPF must not become the routing engine, provider parser, process manager, persistence authority, retry policy, or recovery state machine.

---

## 8. Delivery sequence

The approved sequence is:

```text
CURRENT GATE
APO-70 real cross-process execution proof
  ↓
APO-70 fresh Desktop functional + visual owner acceptance
  ↓
Final exact-head Sol acceptance for APO-70
  ↓
P0/P1 runtime evidence continuation — APO-55
  ↓
P1 local Agent Readiness — APO-71
  ↓
P1 retry/fallback policy — APO-72
  ↓
P1 workflow/evidence UX continuation through APO-52/APO-54 as separately authorized
  ↓
P2 isolated model benchmarking — APO-73
  ↓
P2 transparent historical routing signals — APO-74
```

APO-73 and APO-74 must not delay the owner-usable single-executor path.

---

## 9. Current gate preservation

The active APO-70 runtime boundary remains:

```text
Process A
  real Sol PrepareAsync
       ↓
     Ready
       ↓
Coordinator/process restart

Process B
  Restore persisted Ready
       ↓
Exactly one real gpt-5.6-luna StartAsync
       ↓
Bounded disposable workspace mutation
       ↓
Execution/workspace/checkpoint evidence
       ↓
Anti-replay validation
       ↓
STOP
```

This document does not authorize that execution. The repository's universal standalone lowercase `p` prompt gate remains authoritative.

---

## 10. Planning rules for the new roadmap

Every reference-derived implementation must:

- reuse an existing APO issue/authority when one already owns the capability;
- create a new issue only for a genuinely separate bounded behavior;
- state the current gap and why it is not duplicate work;
- define business behavior before architecture/service names;
- have testable acceptance criteria;
- preserve local-PC durability;
- preserve fail-closed security and approval boundaries;
- preserve immutable checkpoint/evidence semantics;
- preserve project isolation;
- keep provider-specific behavior behind adapters;
- avoid broad refactors and architecture replacement;
- run the Continuous Implementation Guard and exact evidence required by the assigned issue.

---

## 11. Roadmap status summary

| Item | Capability | Priority | Delivery | Status |
|---|---|---:|---|---|
| APO-70 / #111 | V1 Desktop recovery + real orchestration spine | P0 | FAST V1 | Current gate |
| APO-55 / #93 | Runtime/process evidence + restart reconciliation + environment fingerprint | P0/P1 | FAST V1 continuation | In Progress |
| APO-71 / #113 | Local Agent Readiness + executable provenance/version | P1 | Post-V1 | Backlog |
| APO-72 / #114 | Failure-classified retry + policy-driven fallback | P1 | Post-V1 | Backlog |
| APO-52 / #90 | Composable workflow templates | P1 | Post-V1 | Backlog |
| APO-54 / #92 | Decision ledger + owner-browsable execution/evidence history | P1 | Post-V1 | Backlog |
| APO-73 / #115 | Controlled isolated multi-model benchmarking | P2 | Post-V1 | Backlog |
| APO-74 / #116 | Transparent historical-performance routing signals | P2 | Post-V1 | Backlog |

---

## 12. Stop boundary

This roadmap integration is planning/governance work only.

It does not authorize:

- production-code implementation;
- a new executor session;
- real Luna execution;
- merge to `main`;
- release/tag/deployment;
- destructive local/remote actions;
- model-policy changes outside the approved issues.

Implementation begins only under the existing APO execution governance and the standalone lowercase `p` prompt gate.
