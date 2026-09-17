# AI_Orchestrator — Current State Addendum — 2026-09-17

**Mode:** PLANNING / ROADMAP SYNCHRONIZATION ONLY  
**Execution gate:** unchanged; no implementation permission is granted by this file.  
**Source runtime gate:** APO-70 / GitHub Issue #111 remains the sole current implementation gate.

## 1. Why this addendum exists

A broad public GitHub ecosystem review was completed against OpenHands/Agent Canvas, OpenHands Automation, Cline, Microsoft Agent Framework, LangGraph, Aider, SWE-agent/mini-swe-agent, Ruflo, and related references.

The review was used only to harvest product/architecture ideas compatible with APO's existing local-first .NET/WPF architecture. It does not authorize a rewrite or replacement framework.

Detailed planning rationale is recorded in:

`docs/GITHUB_ECOSYSTEM_ROADMAP_EXTENSION_2026-09-17.md`

## 2. New canonical backlog items

The following GitHub Stories now represent the newly approved capability boundaries:

- **APO-75 / #117 — HEAD-Bound Project Intelligence Map for Planning and Impact Analysis**
  - P1 / Post-V1 follow-on;
  - read-only ProjectId + repository + branch + exact HEAD bound map;
  - bounded Planner Context Pack for Sol;
  - invalidates/refreshes on HEAD/config/schema drift;
  - intended to improve targeting and token efficiency without becoming a new source authority.

- **APO-76 / #118 — Headless APO CLI on the Shared Application Core**
  - P1/P2 / Post-V1;
  - human-readable plus versioned JSON/NDJSON output;
  - same Application/Domain orchestration core as WPF;
  - cannot bypass approval/auth/workspace/routing/anti-replay/validation/delivery gates.

- **APO-77 / #119 — Governed Schedule and Event Trigger Engine**
  - P2 / Post-V1;
  - cron/manual/webhook/tracker/CI event families where configured;
  - trigger decides when to request work, not how to execute it;
  - idempotency, queue limits, catch-up policy, restart reconciliation, bounded failure policy required.

- **APO-78 / #120 — Governed ACP / A2A / MCP Interoperability**
  - P2 / Post-V1;
  - extends Agent Readiness and provider/connection adapters;
  - protocol metadata does not imply auth, entitlement, safety, capacity, or write permission;
  - existing routing/workspace/approval/checkpoint/evidence authorities remain mandatory.

## 3. Existing backlog items extended

The GitHub ecosystem review also extends existing capability ownership instead of creating duplicates:

- **APO-55 / #93**
  - optional OpenTelemetry-compatible observability projection;
  - correlated to existing Project/Contract/Graph/Routing/Checkpoint/Run identities;
  - canonical APO evidence remains authoritative.

- **APO-54 / #92**
  - Run / Trajectory Explorer over canonical evidence;
  - state transitions, durations, typed failures, Git/validation/review/acceptance evidence;
  - attempt/fork lineage is visualized without reactivating consumed authority.

- **APO-52 / #90**
  - versioned skills and constrained lifecycle hooks;
  - hooks declare capability, side effects, timeout, and failure policy;
  - no arbitrary plugin execution or authority bypass.

- **APO-72 / #114**
  - explicit retry/fallback/remediation/fork attempt lineage;
  - every permitted new attempt receives a new execution authority linked to the parent checkpoint/attempt;
  - old consumed authority remains immutable and non-executable.

## 4. Updated strategic sequence

```text
APO-70 real cross-process execution proof
  ↓
APO-70 Desktop owner functional + visual acceptance
  ↓
APO-70 final exact-head Sol acceptance
  ↓
APO-55 runtime/process/restart evidence + telemetry projection foundation
  ↓
APO-71 local Agent Readiness
  ↓
APO-75 Project Intelligence Map
  ↓
APO-72 retry/fallback + attempt lineage
  ↓
APO-52 workflow templates / skills / constrained hooks
  ↓
APO-54 Run / Trajectory Explorer
  ↓
APO-76 shared-core Headless CLI
  ↓
APO-77 governed schedule/event triggers
  ↓
APO-73 isolated multi-model benchmarking
  ↓
APO-74 transparent historical-performance routing
  ↓
APO-78 protocol interoperability
```

This is planning order, not automatic hard dependency.

## 5. Planning-branch isolation

The active APO-70 runtime proof is exact-head sensitive. To avoid invalidating an already-authorized runtime proof with documentation-only commits, this 2026-09-17 planning synchronization is staged on:

`docs/APO-75-78-github-ecosystem-roadmap`

based on accepted APO-70 head:

`a2719257e6dc2b276152f35aa34ad945a912a20a`

The active APO-70 feature branch is intentionally not moved by this planning update.

## 6. Guardrails retained

No change to these principles:

- local PC remains authoritative for normal development;
- existing APO architecture first;
- one authority per concern;
- Desktop/WPF -> Application -> Domain dependency direction;
- provider truth remains explicit and separated;
- evidence beats executor self-report;
- recovery uses authoritative checkpoints;
- fail closed on authority ambiguity;
- private chain-of-thought is not an artifact;
- parallel write-capable models never share one mutable working tree;
- no merge/release/deploy/Issue #111 closure is authorized by planning work.

## 7. Current boundary

The newly added/extended work is backlog planning only.

APO-70 remains the sole implementation gate.

A planning document or tracker update does not authorize implementation. Generated executor/reviewer prompts remain governed by the standalone lowercase `p` rule in `.ai/AI_EXECUTION_POLICY.md`.
