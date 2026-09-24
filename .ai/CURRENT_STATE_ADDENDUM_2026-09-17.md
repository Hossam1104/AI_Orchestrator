# AI_Orchestrator — Current State Addendum — 2026-09-17

**Current authority (2026-09-24):** AI_Orchestrator is OWNER-PAUSED / ON HOLD until Hossam explicitly resumes it. APO-70 / Issue #111 remains the sole logical gate; PR #112 remains Draft/Open/Unmerged. Owner visual acceptance is PASS; owner Desktop functional acceptance is NOT COMPLETE. No implementation or acceptance run is authorized while paused. See [current state](CURRENT_STATE.md#project-status--owner-paused--on-hold) and [resume boundary](CURRENT_STATE.md#resume-here). Dated evidence below retains its historical meaning.


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

## 5. APO-70 backend proof authority

The backend cross-process runtime proof is **SOL-ACCEPTED / COMPLETE**. It ran on source head
`a2719257e6dc2b276152f35aa34ad945a912a20a` for ProjectId
`c743e0da-24a6-4ad3-b309-aeb72658013c`.

Process A:

- `PrepareAsync = 1`;
- real Sol = `1`;
- state = `Ready`;
- Process A exited.

Process B:

- `PrepareAsync = 0`, real Sol = `0`;
- `RestoreAsync = 1`, result = `Restored / Ready`;
- `StartAsync = 1`, real Luna = `1`, model = `gpt-5.6-luna`;
- result = `Succeeded / Completed`;
- Process B exited.

The prepared workspace changed `STATE=before` to `STATE=after`. The original disposable source
remained on the same HEAD, clean, without remotes, and with `STATE=before`.

Process C:

- `RestoreAsync = 1`;
- consumed old Ready executable = `NO`;
- second `StartAsync = 0`, second Luna = `0`;
- duplicate execution authority = `NO`;
- anti-replay = `PASS`.

Totals: `PrepareAsync=1`, `RestoreAsync=2`, `StartAsync=1`, `real Sol=1`, `real Luna=1`,
`fallback=0`, `model retry=0`.

Authority continuity was preserved: replan = `NO`, reroute = `NO`, workspace reprepare = `NO`,
executor replacement = `NO`.

This proves the backend cross-process orchestration boundary only. It does not constitute owner
Desktop functional acceptance, owner visual acceptance, final APO-70 acceptance, merge authority,
or release authority.

## 6. Current APO-70 boundary

The current implementation/product gate is:

**FRESH OWNER-VISIBLE DESKTOP FUNCTIONAL + VISUAL ACCEPTANCE**

Issue #111 still requires a fresh-published Desktop, real existing-project registration/reload,
truthful Mission Control and local provider/session state, a Desktop-started real orchestration
workflow with runtime/result evidence, usable cancellation/validation, the required Cairo/layout/
theme/scrolling/CTA UX recovery, explicit owner functional + visual acceptance, and final exact-head
Sol acceptance.

PR #112 remains Draft/Open/Unmerged and Issue #111 remains Open with `status:in-progress` and
`current-gate`. No merge, release, deployment, or final acceptance is implied.

## 7. Architecture Health Check

**Overall project drift: LEVEL 2 — MODERATE DRIFT**

The code architecture is generally healthy (approximately Level 0/1); no systemic boundary
degradation was found. Level 2 arose primarily from operational documentation/tracker drift that
could direct a future session to repeat already-completed runtime work. No broad architecture
refactor is authorized.

Standing WATCH items:

- `BoundedExecutionService` — largest runtime complexity hotspot: concurrency, anti-replay,
  checkpoints, authority validation, adapter invocation, timeout/cancellation/finalization;
- `ExecutionCoordinator` — broad orchestration responsibility and high collaborator count;
- `ReadyExecutionRehydrator` — long durable-authority reconstruction path;
- repeated reference-equality helpers such as `SameContract`, `SameGraph`, and `SameRouting`.

Record these as WATCH only; correct them only through future evidence-driven related work.

## 8. Integrated planning delta

The planning-only commits from PR #121 were directly fast-forwarded into the APO-70 feature branch.
The final integrated planning head is `9b7d7c3bc7d70d35f1448b5ae67ac48e3ad3d542`. The active branch
now contains the approved APO-75..78 roadmap extension and remains the only delivery line for PR
#112. No product source, test, script, workflow, package, or asset code was changed.

## 9. Guardrails retained

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

## 10. Current boundary

The backend proof is complete and accepted; the remaining APO-70 gate is fresh owner-visible
Desktop functional + visual acceptance.

APO-70 remains the sole implementation gate.

A planning document or tracker update does not authorize implementation. Generated executor/reviewer
prompts remain governed by the standalone lowercase `p` rule in `.ai/AI_EXECUTION_POLICY.md`.

## 11. Add Existing Project / onboarding render remediation

Bounded Desktop defect remediation executed on head `2d18525507e7857cca0ef29e7b234ba0153fb79f`
after the `FUNCTIONAL ACCEPTANCE FAILED - ADD EXISTING PROJECT` report.

### Root cause

`ProjectsViewModel.ShowEmptyRegistryState` was `ShowRegistrySurface && !HasProjects`. In
`MainWindow.xaml` that state drives the whole registry surface, including the detail card
(`Border`, column 2) that is the only host for the editor and the onboarding wizard
(`ContentControl` bound to `Onboarding`). On a first run with zero registered projects the state
was true, so the host card stayed `Visibility=Collapsed`. Starting onboarding therefore produced a
truthful view-model state - `Onboarding` non-null, `CanAddExistingProject=false`,
`AddExistingProjectStateText="Finish or cancel the current onboarding flow first."` - while the
wizard itself could never reach the visual tree. The operator saw a disabled CTA citing an
onboarding flow with no onboarding UI anywhere on screen.

### Remediation

- `ShowEmptyRegistryState` now also requires `!IsEditing && !IsOnboardingVisible`: an active
  create/onboarding flow is no longer reported as an empty registry, so the host card renders.
- `ProjectsViewModel.Onboarding` now assigns through `SetProperty`, so the host content binding
  receives the change notification its contract requires.

No XAML, navigation, command, or MVVM boundary change was needed.

### Regression coverage

- `ProjectOnboardingShellRenderTests` (new) starts onboarding *after* the shell bindings are live -
  the chronology real operators use - and asserts the wizard reaches the visual tree, that cancel
  restores the registry surface, and that Projects navigation never starts onboarding. The existing
  visual acceptance coverage started onboarding before window construction and always used a
  non-empty registry, which is why the defect was invisible.
- `ProjectsWorkspace_EmptyRegistryYieldsToAnActiveCreateFlow` guards the state semantics directly.

### Validation

- Canonical suite: `1,414 passed / 0 failed / 0 skipped`
  (`Domain 28`, `Connection 357`, `Provider 228`, `Desktop 122`, `Infrastructure 679`).
- Release solution build: `0 warnings / 0 errors`; `git diff --check` clean.
- Fresh win-x64 publish reproof with real UIA input on a restored (non-minimized) window: fresh
  startup CTA enabled, Projects navigation renders the registry, Add Existing Project renders the
  onboarding wizard and truthfully disables the CTA, cancel closes onboarding and re-enables it.

### Acceptance-run note

The earlier report also stated that a real click on Projects did not change the screen. The failed
acceptance process (PID 3968) was still running and was `SW_SHOWMINIMIZED` with UI Automation
bounding rectangles at approximately `-32000,-32000`, so clicks at those coordinates could not
reach the window. That part of the report is an automation-harness condition, not a product defect,
and is recorded separately from the proven render defect above.

`PrepareAsync = 0`; `RestoreAsync = 0`; `StartAsync = 0`; `real Sol = 0`; `real Luna = 0`.
No merge, release, deploy, or Issue #111 closure is authorized by this remediation.
