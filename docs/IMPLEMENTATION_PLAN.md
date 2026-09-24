# AI_Orchestrator — Implementation Plan

**Current authority (2026-09-24):** AI_Orchestrator is OWNER-PAUSED / ON HOLD until Hossam explicitly resumes it. APO-70 / Issue #111 remains the sole logical gate; PR #112 remains Draft/Open/Unmerged. Owner visual acceptance is PASS; owner Desktop functional acceptance is NOT COMPLETE. No implementation or acceptance run is authorized while paused. See [current state](../.ai/CURRENT_STATE.md#project-status--owner-paused--on-hold) and [resume boundary](../.ai/CURRENT_STATE.md#resume-here). Dated evidence below retains its historical meaning.

**Version:** 1.5
**Date:** 16 September 2026
**Product:** AI_Orchestrator (APO)
**Primary Requirements:** `docs/BRD.md`
**Strategic Roadmap:** `docs/STRATEGIC_ROADMAP.md`
**Reference-Derived Roadmap Delta:** `docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md`
**Active Work Tracker:** GitHub Issues in `Hossam1104/AI_Orchestrator`
**Historical Jira Project:** `APO` at `hossamsqa.atlassian.net`
**Repository:** `https://github.com/Hossam1104/AI_Orchestrator`
**Default Branch:** `main`
**Planner / Architect / Acceptance / Prompt Authority:** GPT-5.6 Sol (chat mode only)
**Primary Executor:** GPT-6 Luna Reasoning xHigh in Codex unless Sol explicitly routes elsewhere
**Independent Reviewer:** Claude Opus 5.5 Effort HIGH at configured/critical checkpoints
**Specialist Recovery:** Claude Sonnet 5 Effort HIGH when Sol selects difficult recovery or bounded isolated bug fixes; Terra retired

Canonical model routing and execution governance live in `.ai/AI_MODEL_ROUTING.md` and
`.ai/AI_EXECUTION_POLICY.md`. This plan does not override them.

---

## 1. Purpose and authority

This plan explains how APO proceeds from the current repository state to an owner-accepted
local-first orchestration product. It supersedes stale sequencing statements in older versions of
this plan while preserving historical delivery evidence in `.ai/CURRENT_STATE.md`,
`.ai/history/`, Git history, merged PRs, and canonical GitHub Issues.

Authority order remains the one defined by `AGENTS.md`. The current dated planning delta is
`.ai/CURRENT_STATE_ADDENDUM_2026-09-16.md`.

A plan item is never implementation permission. Generated executor/reviewer prompts require the
owner's standalone lowercase `p` message exactly as defined in `.ai/AI_EXECUTION_POLICY.md`.

---

## 2. Active architecture

APO remains:

- C# / .NET 10;
- WPF / MVVM;
- modular Clean Architecture;
- `System.Text.Json`;
- versioned JSON for bounded state/configuration;
- monthly JSONL for append-oriented history/events;
- dependency injection and Serilog;
- Windows Credential Manager / approved secure external credential storage;
- Git/GitHub/GitHub Actions;
- focused xUnit tests;
- self-contained Windows publishing.

Dependency direction:

```text
Desktop / WPF -> Application -> Domain
Infrastructure -> Application / Domain contracts
Providers / integrations -> Application / Domain contracts
```

No reference-derived work may introduce a second agent registry, runner, router, orchestration
state machine, project registry, workspace authority, evidence store, configuration system, or
provider-specific business policy in WPF.

V1 still has no mandatory EF Core, SQL Server, LocalDB, SQLite, Angular, Electron, Tauri, Node/npm,
embedded Chromium, or APO-owned cloud backend.

---

## 3. Current authoritative delivery state

### 3.1 Preserved accepted foundation

The repository already contains accepted/delivered foundations for:

- WPF/.NET/JSON/JSONL platform and persistence;
- provider/capacity domain foundations;
- project registry and durable local project state;
- read-only local Git evidence;
- agent/model registry truth;
- progressive onboarding/context;
- planning/execution contracts;
- WorkGraph/dependency handling;
- role handoffs;
- Smart Continue / recovery checkpoint foundations;
- explainable quality-first routing;
- isolated workspace preparation;
- bounded cancellable execution;
- tracker synchronization foundation;
- remote SCM/CI evidence;
- independent validation evidence and QA gates;
- Review Inbox / bounded remediation;
- human approval policies;
- controlled remote delivery;
- Mission Control/read-model foundation;
- GitHub Actions CI and multi-RID packaging.

Historical exact SHAs, test counts, merges, and review evidence remain recorded in current-state and
Issue/PR history and are not recopied here as mutable plan truth.

### 3.2 Sole current gate — APO-70 / #111

APO-70 remains **OPEN / IN PROGRESS / SOLE CURRENT GATE** on Draft PR #112.

The accepted backend proof source head was `a2719257e6dc2b276152f35aa34ad945a912a20a`, with
PR CI run `35162849445` succeeding on that exact head:

- feature SHA `a2719257e6dc2b276152f35aa34ad945a912a20a`;
- base `main` `d7c1231df9ea1a4d008aa473210d0ccc735c0302`;
- PR CI run `35162849445`: SUCCESS;
- 1,410 passed / 0 failed / 0 skipped;
- Release build 0 warnings / 0 errors;
- win-x86, win-x64, win-arm64 self-contained publish validation PASS.

On that head, the production-composed backend cross-process proof reached `Ready`, restored it in a
fresh process, performed exactly one real `gpt-5.6-luna` `StartAsync` workspace mutation, and passed
fresh-process anti-replay. Sol accepted the proof. It does not constitute Desktop owner functional
or visual acceptance, final APO-70 acceptance, merge authority, or release authority.

A prior integrated documentation head was `9b7d7c3bc7d70d35f1448b5ae67ac48e3ad3d542`; future
exact-head validation must use the live branch HEAD rather than carrying forward the accepted proof
or CI SHA.

---

## 4. Current execution boundary — APO-70 Desktop acceptance

### Phase 4A — backend cross-process proof complete

The backend proof is **SOL-ACCEPTED / COMPLETE**. It proved the following bounded sequence on source
head `a2719257e6dc2b276152f35aa34ad945a912a20a` for ProjectId
`c743e0da-24a6-4ad3-b309-aeb72658013c`:

```text
Process A
  PrepareAsync = 1
  real Sol = 1
  state = Ready
  Process A exited

Process B
  RestoreAsync = 1 -> Restored / Ready
  StartAsync = 1 -> real Luna = 1 -> Succeeded / Completed
  prepared workspace: STATE=before -> STATE=after
  Process B exited

Process C
  RestoreAsync = 1
  consumed old Ready executable = NO
  second StartAsync = 0
  second Luna = 0
  anti-replay = PASS
```

The original disposable source remained on the same HEAD, clean, without remotes, and with
`STATE=before`. Totals were `PrepareAsync=1`, `RestoreAsync=2`, `StartAsync=1`, real Sol `=1`, real
Luna `=1`, fallback `=0`, and model retry `=0`. Authority continuity was preserved: no replan,
reroute, workspace reprepare, or executor replacement.

### Phase 4B — APO-70 Desktop owner workflow

Owner visual acceptance is **PASS**. The remaining implementation/product gate is **OWNER-VISIBLE DESKTOP FUNCTIONAL ACCEPTANCE**, currently paused by the owner. After Hossam explicitly resumes and Sol reconciles live state:

1. fresh-publish the current exact tree;
2. register/reload a real existing project;
3. prove Mission Control uses durable project truth;
4. prove supported local provider/session states are truthful;
5. start one actual bounded orchestration workflow from Desktop;
6. expose real runtime/result/cancellation/validation evidence;
7. complete the already-recorded UX recovery requirements;
8. retain the recorded owner visual PASS and obtain explicit owner functional acceptance;
9. run final exact-head validation/CI/publish checks;
10. obtain final Sol acceptance.

Only a separate finalization authorization may merge/release after those gates. No backend proof,
documentation update, or CI result substitutes for owner acceptance.

---

## 5. Reference-derived reliability extension — APO-55 / #93

After APO-70 reaches Desktop and final Sol acceptance, continue the existing APO-55 runtime-evidence
Story.

### Product behavior

When APO starts local work, it must be able to tell the owner what process it owns, whether that
process/process tree ended, what checkpoint/run authority is current, and what is safe after an APO
or Windows restart.

### Implementation areas

**Application / Domain contracts**

- provider-independent execution process/runtime evidence;
- typed restart-reconciliation result;
- freshness/stale/partial/unavailable semantics;
- environment-fingerprint contract;
- Mission Control/read-model projection.

**Infrastructure**

- Windows process/PID inspection where safe;
- process-tree reality checks;
- non-secret environment/tool fingerprint probes;
- persistence/rehydration implementation.

**Providers**

- expose provider invocation/process facts through existing adapter boundaries where appropriate.

**Desktop**

- presentation only; no direct process probing.

### Acceptance themes

- process identity and termination truth;
- termination-unconfirmed keeps workspace guarded;
- restart reconciliation before any automatic resume;
- missing PID never equals success;
- checkpoint/run/workspace/Git truth reconciled together;
- allowlisted fingerprint only;
- secret-safe persistence;
- regression coverage around crash/restart/process absence.

Priority: **P0/P1 reliability**.

---

## 6. P1 local Agent Readiness — APO-71 / #113

Extend APO-8/APO-38 instead of creating a new registry.

### Product behavior

Before routing/execution, APO can explain whether an exact local agent path is usable and why.

Target evidence:

```text
identity
+ executable path/provenance
+ CLI version
+ connection mode / supported invocation modes
+ authentication
+ entitlement when independently verifiable
+ capabilities / limitations
+ tested-at / freshness
= readiness
```

### Implementation rules

- provider-specific probes remain in Providers;
- executable/OS probing remains outside WPF;
- Application composes normalized readiness;
- stale results are re-evaluated rather than blindly trusted after restart;
- installation != authentication != entitlement/subscription != capacity;
- no unrestricted environment capture;
- no shell-interpolated discovery architecture.

Priority: **P1 / Post-V1 follow-on**.

---

## 7. P1 failure-classified retry/fallback — APO-72 / #114

### Product behavior

A failure produces an explainable retry disposition, not a generic automatic provider switch.

Inputs:

- typed failure;
- latest verified checkpoint;
- observed side effects;
- task requirements;
- current source/workspace truth;
- eligible/ready agents and capabilities;
- routing/owner policy;
- quota/capacity truth;
- remaining execution budget;
- validation/security/approval state.

Outputs may include:

- retry same executor;
- route eligible fallback;
- wait for authentication/capacity;
- reconcile source/workspace;
- run bounded remediation;
- require owner approval;
- stop/not retryable.

### Architecture

Implement in Application orchestration/recovery policy by composing existing authorities. Do not
put fallback selection in provider adapters or WPF. Existing routing remains the only candidate
eligibility/selection authority.

### Safety

Prevent:

- replay of consumed Ready authority;
- duplicate commits/external actions;
- automatic continuation across source/workspace conflict;
- model-switch bypass of security/approval gates;
- unbounded retry loops;
- loss/overwrite of prior evidence.

Priority: **P1 / Post-V1** after stable runtime evidence.

---

## 8. P1 workflow and evidence usability — APO-52 / #90 and APO-54 / #92

### APO-52 — workflow templates

Create provider-independent, versioned templates that instantiate existing APO authorities.

Representative patterns:

```text
Implementation: Planner -> Executor -> Verifier -> Reviewer -> Remediator -> Acceptance
Recovery: Diagnostician -> Fixer -> Verifier
Architecture: Analyzer -> Independent Reviewer -> Architecture Decision
Security: Implementer -> Security Reviewer -> Remediation -> Verification
```

Templates may define roles, dependency shape, evidence requirements, loop limits, stop conditions,
and gates. Routing still chooses actual eligible models dynamically.

### APO-54 — decision/evidence history

Build an owner-browsable timeline and evidence manifest that references existing canonical evidence
instead of duplicating it. A run may reference:

- request/work item;
- contract;
- WorkGraph;
- routing decision;
- agent/readiness evidence;
- handoff;
- workspace receipt;
- checkpoint chain;
- run authority/execution outcome;
- APO-55 runtime/process/environment evidence;
- local/remote Git evidence;
- validation;
- review/remediation;
- acceptance/owner gate;
- final disposition and next safe checkpoint.

Do not persist private chain-of-thought, unrestricted environments, or raw provider transcripts as
a completeness requirement.

Priority: **P1 / Post-V1**.

---

## 9. P2 controlled benchmarking — APO-73 / #115

### Product behavior

The owner explicitly starts a benchmark for one immutable bounded task. Each eligible candidate gets
its own disposable workspace, run authority, checkpoint chain, and evidence set.

### Candidate comparison

Use comparable independent evidence where available:

- typed execution outcome;
- first-attempt completion;
- build/test/validation result;
- reviewer disposition;
- scope/architecture compliance;
- changed files/lines;
- retry count;
- duration;
- provider-truthful usage/cost signals;
- regression findings;
- final acceptance disposition.

### Safety

- no shared writable workspace;
- no merge/push/deploy/destructive external action by default;
- cancellation of one candidate must not damage another;
- results survive restart;
- no automatic production-routing change from benchmark result.

Priority: **P2 / Post-V1**.

---

## 10. P2 historical-performance routing — APO-74 / #116

Only after sufficient trustworthy history exists, extend the existing router with a deterministic,
versioned, explainable historical-performance input.

Potential evidence dimensions:

- task cohort/classification;
- first-attempt success;
- validation pass rate;
- reviewer rejection/remediation rate;
- scope/architecture compliance;
- retries;
- duration;
- provider-truthful resource/cost metrics;
- regression rate;
- final acceptance.

Every statistic must include sample size, freshness, provenance, and limitations. Sparse/stale data
is unavailable/low-confidence rather than fabricated.

Historical performance cannot make an ineligible agent eligible and cannot override correctness,
security, required capability/risk, explicit owner/project policy, current authentication,
entitlement, or required approval/review.

The first version is rules/scoring based — no opaque ML routing.

Priority: **P2 / Post-V1**.

---

## 11. Existing strategic work retained

The approved 2026-09-17 planning extension adds these bounded follow-ons:

- APO-75 / #117 — HEAD-bound, read-only Project Intelligence Map and Planner Context Pack; it is
  advisory and never a second repository authority.
- APO-76 / #118 — Headless APO CLI over the same Application/Domain orchestration core; it is not a
  second router, runner, registry, checkpoint, evidence, or permission system.
- APO-77 / #119 — governed schedules and event triggers; triggers request work but never bypass the
  normal policy, approval, workspace, evidence, validation, or delivery pipeline.
- APO-78 / #120 — governed ACP/A2A/MCP interoperability; protocol adapters cannot bypass Agent
  Readiness, routing, approval, workspace, evidence, or execution authorities.

The new roadmap does not remove or implicitly complete existing work. Continue separately when
sequenced:

- APO-53 — explainable project health / owner attention;
- APO-56 — context budgets and handoff compression;
- APO-57 — bounded background automation and housekeeping;
- APO-58 — optional remote/mobile approval security design;
- APO-59..61 — accepted APO-37 hardening debt;
- capacity/provider monitoring and remaining Epic responsibilities;
- accessibility, Windows compatibility, packaging, CI, release quality.

No low-priority item may displace APO-70 or the reliability sequence above.

---

## 12. Technical guardrails for all phases

### Local-PC-first

Persistent registered projects, APO LocalAppData, local Git repositories, APO-managed workspaces,
local AI CLIs, local SDK/test/browser/toolchain access, and durable evidence form the normal local
development environment. Remote/sandbox targets are optional future execution targets, not the
foundation.

### Process safety

Use structured no-shell execution, concurrent bounded stdout/stderr consumption, explicit timeout,
process-tree cancellation, termination confirmation, and secret-safe diagnostics. Do not copy broad
permission bypasses or shell command composition from external references.

### Persistence

Keep versioned/atomic/project-isolated state. Existing immutable authorities/evidence remain
canonical; new manifests/read models reference rather than duplicate them.

### Provider truth

Installation, version, authentication, entitlement/subscription, availability, capabilities,
capacity, and freshness are separate evidence dimensions.

### Recovery

Resume/retry only from the latest verified authoritative checkpoint after reconciling side effects.
No stage-number-only retry.

### Benchmark isolation

Every write-capable candidate uses a separate workspace and authority.

### Evidence privacy

Never persist credentials, unrestricted environment variables, unrelated source content, raw
conversations, or hidden/private chain-of-thought as execution artifacts.

---

## 13. Validation strategy

Each implementation item runs proportional targeted validation first and broader regression where
its risk requires it. High-risk coverage now explicitly includes:

- Agent Readiness executable provenance/version/auth/freshness;
- process-tree termination and termination-unconfirmed behavior;
- restart reconciliation with stale/missing PID;
- checkpoint/run-authority anti-replay across retry/restart;
- differentiated retry dispositions;
- source/workspace/security/approval stop paths;
- isolated benchmark candidate workspaces;
- deterministic historical-routing replay and sparse/stale evidence;
- secret-safe environment fingerprinting;
- evidence-manifest restart/reopen and missing referenced evidence.

CI remains sanitized and must not require live authenticated provider credentials.

---

## 14. Architecture-health cadence

Continuous Implementation Guard applies to every executor change. The bounded architecture-health
review after the first real restored-Ready Luna execution proof recorded generally healthy source
architecture (approximately Level 0/1) and no systemic boundary degradation. Overall project drift
was **LEVEL 2 — MODERATE DRIFT**, caused primarily by stale operational documentation/tracker
authority. No broad refactor is authorized.

Keep these as WATCH items only:

- `ExecutionCoordinator` — broad orchestration responsibility and high collaborator count;
- `ReadyExecutionRehydrator` — long durable-authority reconstruction path;
- `BoundedExecutionService` — largest runtime complexity hotspot: concurrency, anti-replay,
  checkpoints, authority validation, adapter invocation, timeout/cancellation/finalization;
- repeated reference-equality helpers such as `SameContract`, `SameGraph`, and `SameRouting`.

The reviewed runtime chain remains:

```text
ExecutionCoordinator
    -> RecoveryCheckpoint
    -> ExecutionRunAuthority
    -> BoundedExecutionService
    -> provider execution adapter
    -> BoundedProcessHost
    -> runtime evidence
```

Review for:

- duplicate lifecycle truth;
- God-service/ViewModel drift;
- process ownership gaps;
- stale evidence semantics;
- restart reconciliation ownership;
- retry/fallback ownership;
- workspace/authority anti-replay correctness;
- validation/review boundary drift;
- duplicated provider checks or persistence concepts.

Record the watch items without refactoring them in this documentation synchronization. Future
correction must be evidence-driven during related implementation. Do not convert the health check
into a repository rewrite.

---

## 15. Delivery sequence

The approved current sequence is:

```text
APO-70 backend cross-process proof (SOL-ACCEPTED / COMPLETE)
        ↓
APO-70 owner visual PASS; Desktop functional owner acceptance pending
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
remaining Post-V1 items as separately authorized
```

Hard `Blocks` dependencies should be added only when a future implementation contract proves an
architectural prerequisite. Planning order alone is not a hard dependency.

---

## 16. Definition of Done

A work item is complete only when:

- exact assigned scope is implemented;
- architecture boundaries remain sound;
- acceptance criteria are met;
- required targeted/full validation actually ran;
- warnings/errors are explained;
- secrets/debug/temp production artifacts are absent;
- diff/changed files are reviewed;
- current state is updated truthfully;
- tracker evidence is synchronized;
- branch/PR state is recorded;
- independent review occurs when required;
- Sol accepts the exact implementation state;
- protected delivery remains behind the configured owner gate;
- limitations and next boundary are explicit.

Documentation-only planning work must not claim source/runtime validation it did not run.

---

## 17. Current planning boundary

The roadmap/planning reconciliation is complete for this external-reference harvest.

Current gate status: **CLOSED**.

No implementation prompt has been generated by this plan. APO-70 remains the sole current gate, at
the pending owner-visible Desktop functional acceptance boundary, paused until Hossam explicitly resumes. A future standalone
lowercase `p` is required before Sol generates one executor or reviewer prompt.

No merge, `main` push, release, tag, deployment, or Issue #111 closure is authorized by this plan.
