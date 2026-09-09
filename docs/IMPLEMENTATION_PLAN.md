# AI_Orchestrator - Implementation Plan

**Version:** 1.4
**Date:** 30 August 2026
**Product:** AI_Orchestrator (APO)
**Primary Requirements:** `docs/BRD.md`
**Active Work Tracker:** GitHub Issues in `Hossam1104/AI_Orchestrator`
**Historical Jira Project:** `APO` at `hossamsqa.atlassian.net`
**Repository:** `https://github.com/Hossam1104/AI_Orchestrator`
**Default Branch:** `main`
**Planner / Architect / Acceptance / Prompt Authority:** GPT-5.6 Sol (chat mode only)
**Primary Executor:** GPT-5.6 Luna xHigh for bounded work unless Sol explicitly routes elsewhere
**Fallback / Special-Need Executors:** Claude Sonnet 5 Medium / Claude Sonnet 5 High, only when explicitly selected by Sol
**Disabled from Active Routing:** Claude Haiku 4.5
**Exceptional Escalation:** GPT-5.6 Luna Max
**Independent Reviewer:** Claude Opus 5
**Specialist Assurance:** GPT-5.6 Terra Medium/High

Canonical routing, quota, and execution-policy detail lives in `.ai/AI_MODEL_ROUTING.md` and
`.ai/AI_EXECUTION_POLICY.md`; this header summarizes only the roles relevant to this plan and does
not override those canonical files.

---

## 1. Purpose and Authority

This plan explains how APO will be delivered from the existing repository. It is subordinate to
`docs/BRD.md` and does not create product scope by itself. GitHub Issues carrying stable APO keys
are the active work-tracking system; repository documentation and evidence remain the
architecture/governance source of truth. Jira `APO` is historical provenance for migrated records.

Authority order for execution is:

1. `docs/BRD.md`;
2. `AGENTS.md`;
3. planner-approved architecture decisions and `.ai/CURRENT_STATE.md`;
4. assigned canonical GitHub APO Issue acceptance scope;
5. this plan;
6. root `TASK.md`; and
7. executor preference.

The active execution flow is:

```text
GitHub APO Epic -> canonical GitHub Story/Task -> Sol planning/architecture
-> TASK.md execution contract -> assigned executor -> validation
-> independent review where required -> Sol acceptance -> GitHub Issue/repository synchronization
```

Only one bounded assigned work item is active at a time. Detailed Stories/Tasks are progressively
decomposed by Sol after repository evidence and acceptance dependencies are understood. The
strategic rebaseline now records the approved bounded backlog in canonical GitHub Issues APO-38
through APO-63; their presence does not authorize implementation. Jira remains historical
provenance for the migrated keys.

---

## 2. APO Epic Capability Structure

The approved initial Jira capability map is:

| Epic | Capability | Primary dependency |
|---|---|---|
| APO-1 | APO Product Rebrand & Governance Rebaseline | None; current checkpoint |
| APO-2 | Windows Platform & Application Foundation | APO-1 |
| APO-3 | Local Persistence, Resilience & Security Foundation | APO-1, APO-2 |
| APO-4 | AI Usage, Subscription & Capacity Monitoring | APO-3 |
| APO-5 | Project Registry & Workspace Management | APO-2, APO-3 |
| APO-6 | Git & GitHub Integration | APO-2, APO-5 |
| APO-7 | Jira & Azure DevOps Work-Item Integration | APO-5, APO-6 |
| APO-8 | AI Agent / Model Registry & Connectivity | APO-3, APO-4 |
| APO-9 | Intelligent Model Routing & Quota-Aware Decisioning | APO-4, APO-8 |
| APO-10 | Planning & Execution Contracts | APO-5, APO-8, APO-9 |
| APO-11 | Autonomous Execution Runtime | APO-6, APO-7, APO-8, APO-10 |
| APO-12 | Validation & Evidence Engine | APO-6, APO-10, APO-11 |
| APO-13 | Independent Review & Remediation Engine | APO-8, APO-11, APO-12 |
| APO-14 | Acceptance & Human Approval Gates | APO-7, APO-12, APO-13 |
| APO-15 | Command Center & Project UX | APO-4, APO-5, APO-11, APO-14 |
| APO-16 | Activity, Audit, History & Notifications | APO-3, APO-4, APO-11, APO-14 |
| APO-17 | Packaging, Compatibility, CI & Release Quality | Cross-cutting; evidence from all Epics |

The Epic list is approved. Sol may refine names, dependencies, and Story ordering only within the
BRD and owner-approved scope. APO-18 established the governance baseline, APO-19 produced the
legacy implementation inventory, and APO-20 completed the repository identity rename, including
the physical local-root move. The next work item remains planner-controlled and is represented by
the safe planner checkpoint in the root `TASK.md`; it does not authorize any unrelated Epic or
Story.

---

## 3. Legacy Backfill and Reuse Classification

The repository contains completed work from the former AI Usage Monitor scope. That work is not
discarded and is not automatically accepted as APO-complete. Sol will backfill meaningful work into
Jira with links to code, commits, tests, and historical validation, then classify each significant
area as:

- **Reuse As-Is**;
- **Reuse With Extension**;
- **Refactor**;
- **Superseded**; or
- **Remove**.

Backfilled work may be marked Done only after the requirement mapping, code mapping, architecture
check, and relevant validation evidence are explicit. Historical work must retain its original
truth even when the architecture later changed.

Known historical implementation and initial interpretation:

| Historical implementation | Current interpretation | Initial treatment |
|---|---|---|
| Repository/solution foundation | APO-2 platform foundation | Reuse / verify |
| Domain/Application foundation | APO-2/APO-3 contracts | Reuse With Extension |
| Domain integrity remediation | APO-3 correctness | Reuse / verify |
| EF Core + SQL Server LocalDB | Earlier persistence baseline | Superseded; history only |
| WinUI / Windows App SDK shell | Earlier desktop baseline | Superseded; history only |
| Portable WPF migration | APO-2 desktop foundation | Reuse / revalidate |
| JSON/JSONL stores | APO-3 and APO-16 persistence | Reuse With Extension |
| Atomic writes/corruption recovery | APO-3 resilience | Reuse / revalidate |
| Startup/storage resilience | APO-2/APO-3 reliability | Reuse / revalidate |
| Latest/range JSONL optimization | APO-16 history | Reuse / revalidate |
| Interrupted-tail handling | APO-3/APO-16 resilience | Reuse / revalidate |
| x86/x64/ARM64 solution targets | APO-2/APO-17 compatibility | Reuse / revalidate |
| Self-contained publish profiles | APO-17 release foundation | Reuse / revalidate |
| Provider-independent quota concepts | APO-4 capacity domain | Reuse With Extension |
| Provider-only feasibility sequence | APO-4 discovery | Superseded; replan |

The current source is a reusable, partially delivered APO foundation rather than a finished product.
APO-38 through APO-47 and APO-68 have delivered the documented control-plane, routing, workspace,
bounded-execution, workspace-preparation, and bounded Jira tracker slices. Source naming and
LocalAppData migration remain separately planner-controlled; remaining remote evidence, QA,
approval, controlled delivery, and command-center capabilities remain in the Jira roadmap.

---

## 4. Existing Foundation and Active Architecture

The active target is WPF + .NET 10 + MVVM + modular clean architecture with JSON/JSONL persistence,
secure external credentials, and self-contained Windows artifacts. V1 has no database engine or
ORM. The intended dependency direction is:

```text
Desktop/WPF -> Application -> Domain
Infrastructure -> Application/Domain contracts
Providers/integrations -> Application/Domain contracts
```

The current source already contains a WPF desktop foundation, provider-independent Domain and
Application contracts, JSON/JSONL Infrastructure stores, resilience behavior, control-plane
services, routing, isolated workspace preparation, bounded cancellable execution, focused tests,
target configuration, and publish profiles. Existing `AIUsageMonitor` project/namespace names and
the minimal shell are retained until a future approved mapping/refactor item.

The historical EF/LocalDB and WinUI/Windows App SDK implementations are preserved in Git history
and current-state evidence only. They are not active APO runtime dependencies and must not be
revived without an explicit architecture decision.

---

## 5. Delivery Approach and Dependencies

Delivery proceeds in capability families, but actual work is always driven by one assigned
canonical GitHub Story/Task and its Sol-authored `TASK.md` contract.

### Phase A - Governance and accepted foundation

APO-18 through APO-37 establish the governed WPF/.NET/JSON/JSONL foundation, capacity surfaces,
project workspace, and the first local read-only Git evidence slice. APO-38 through APO-46 and
APO-68 extend that foundation with the control plane, routing, isolated workspace, bounded
execution, and workspace-preparation safeguards. APO-33 remains the existing CI/release Story and
is not duplicated.

### Phase B - Inputs and control-plane contracts (P0)

APO-38 (agent/model registry truth), APO-39 (progressive onboarding and canonical context
resolution), APO-40 (versioned planning contracts), APO-41 (dependency-aware work graphs), APO-42
(compact role handoffs), and APO-43 (durable recovery checkpoints) are delivered. These slices
remain provider-independent, project-isolated, and free of chat-history or secret persistence.

### Phase C - Quality-first decision, bounded execution, and evidence inputs (P0)

APO-44 delivered explainable routing; APO-46 delivered safe isolated workspaces; APO-45 delivered
bounded cancellable execution; and APO-47 delivered the bounded Jira-first/Azure-optional tracker
input. Tracker awareness is an independent input, not a model-CLI side effect. Add provider-independent, read-only
remote SCM/CI evidence (APO-62) after the local/work-item evidence inputs are defined. APO-62 is
distinct from APO-37 local Git verification and APO-33 repository-owned CI. The ordering is
deliberate: capability and contract truth precede quota-aware selection, safe repository/worktree
preparation, process execution, and independently captured evidence.

### Phase D - Evidence, review, controlled delivery, and command center (P0)

Build independent evidence/QA gates (APO-48) and human approval policy (APO-49). Then implement
controlled remote source-control delivery (APO-63) only against exact immutable targets and
current evidence. Read-only remote evidence must exist before controlled remote writes so delivery
cannot rely on a model claim, stale branch state, or missing CI/review proof. Finish this P0 band
with the Mission Control read model/surface (APO-50). Evidence remains the progression authority;
the owner remains the authority for protected delivery and high-risk changes.

### Phase E - Workflow acceleration (P1)

Add the Review Inbox/remediation state (APO-51), composable skills/workflows (APO-52), explainable
project health (APO-53), the AI Decision Ledger and activity timeline (APO-54), truthful runtime
evidence (APO-55), and context-budget management (APO-56). These capabilities reduce manual
handoffs without becoming an opaque plugin or prompt framework.

### Phase F - Controlled optional automation (P2)

Only after the P0/P1 evidence and approval boundaries are sound, consider bounded background
automation/housekeeping (APO-57) and a separately designed optional remote/mobile approval boundary
(APO-58). Neither makes an APO-owned cloud backend mandatory for V1.

### Phase G - APO-37 hardening debt (P3)

APO-59 covers remote/output evidence bounds and conservative normalization (OPUS-01/02/03/09).
APO-60 covers aggregate verification bounds and truthful path-state UX (OPUS-04/05). APO-61 covers
explicit unavailable/skipped semantics for real-Git integration evidence (OPUS-07). Rejected
OPUS-06 and OPUS-08 are not backlog work.

No family is considered complete merely because code exists. Acceptance requires the canonical
GitHub Issue scope, BRD requirements, validation, review, and Sol decision to align.

---

## 6. Technical Guardrails

### Persistence and security

Use per-user LocalAppData; JSON for small documents; monthly JSONL for append-oriented records;
schema/record metadata; atomic temporary-file replacement; synchronized writes; streaming/range
history; material-change duplicate suppression; explicit missing/empty/corrupt/unsupported/I/O/
permission handling; safe quarantine; and last-known-good retention. Never write secrets to files
or logs; use opaque references to secure storage.

### Provider truth

Prefer official APIs, account surfaces, OAuth/device auth, optional official CLIs, safe verified
local metadata, and manual fallback in that order. Prove used versus remaining semantics, preserve
`DateTimeOffset`, support arbitrary quota windows, and never fabricate plan, reset, subscription,
or credit information.

### Compatibility and consumer release

Target Windows 10 1809/build 17763 and Windows 11 with x86/x64/ARM64 consideration, x64 primary,
guarded modern APIs, graceful WPF fallback, no modern hardware prerequisite, low idle resource use,
and self-contained release artifacts. Do not claim a clean-machine or architecture validation not
actually performed.

### Testing

Prioritize quota normalization, reset/timezone math, provider parsing, subscription semantics,
JSON/schema round trips, JSONL ordering/range/duplicates, atomic writes, corrupt-file recovery,
credential-reference safety, routing/recommendation, gate/state behavior, WPF view-model behavior,
and self-contained publish smoke checks. CI uses sanitized fixtures and no live authenticated calls.

---

## 7. Review, Security, and Release Gates

Every applicable implementation Story defines its validation and review requirements in its
canonical GitHub Issue and `TASK.md`. At minimum:

1. Sol confirms scope, dependencies, and acceptance criteria before execution.
2. The executor validates the change and records evidence.
3. Opus independently reviews high-value or high-risk implementation.
4. Findings are classified BLOCKER/HIGH/MEDIUM/LOW, fixed in a bounded loop, and revalidated.
5. Sol accepts or rejects the result against the BRD, GitHub Issue scope, diff, validation, and review.
6. Human approval is required for protected-branch merges and other high-risk actions unless the
   explicit owner-approved execution contract provides the applicable authorization.
7. The GitHub Issue and repository are synchronized only after the required gate; configured
   external trackers follow their own explicit integration policy.

Release review must explicitly check zero-prerequisite deployment, active WPF/JSON/JSONL
architecture, no hidden database/runtime prerequisites, credential safety, project isolation,
provider truthfulness, remaining-capacity semantics, file integrity, Windows compatibility, and
actual packaging evidence.

---

## 8. Definition of Done

A Story/Task is complete only when:

- its assigned scope is implemented without future scope;
- relevant build/tests/manual checks actually ran;
- warnings/errors are explained;
- secrets/debug artifacts are absent;
- the diff and changed-file list are reviewed;
- `.ai/CURRENT_STATE.md` is updated;
- GitHub Issue status/evidence is synchronized by the authorized workflow;
- the branch is committed and pushed;
- one Draft PR is opened or updated against `main`;
- the exact branch head and unchanged `origin/main` base are recorded;
- merge is left to a separate, explicitly authorized finalization action;
- the tree is clean; and
- limitations, blockers, and the next planner boundary are explicit.

For documentation or repository-hygiene work, validation is document consistency, stale-reference
review, source-scope confirmation, and proportionate restore/build/test checks requested by the
Story. A Draft PR is not Sol acceptance or a merge.

---

## 9. Current Planning Boundary

APO-20 completed the repository identity rename under APO-1, including the physical local-root
move. APO-27, APO-35, APO-36, and APO-37 are historical accepted deliveries. APO-38 through
APO-47 and APO-68 are implemented and marked Done in Jira. APO-69 is also Done. APO-62 (remote
SCM and CI evidence) is Done. The current `origin/main` baseline is
`98cb8e86bad0729aa07d33ec6f93b86a49a668bf`. APO-48 is In Progress and not yet accepted; its Phase B
remediation is executor-complete with local validation at 1,130 passed / 0 failed / 0 skipped,
pending independent review and Sol acceptance. APO-49,
APO-63, and APO-50 remain To Do and are not started. No executor may start another Story from
this document or `TASK.md` until Sol authorizes it with a fresh contract.

Sections 10-17 below are retained historical delivery records and planning snapshots. They do not
override this current boundary or the latest evidence in `.ai/CURRENT_STATE.md`.

## 10. APO-35 Delivery Boundary

APO-35 implements the first usable Projects workspace over the accepted APO-27 project registry
foundation. The bounded delivery includes an Application `ProjectRegistryService`, testable clock
semantics through the existing `IClock`, DI-backed `ProjectsViewModel`, enabled Projects shell
navigation, project list/detail/editor UX, in-memory search/status filtering, lifecycle state
editing including archive/restore, hidden metadata preservation, truthful empty/loading/error and
degraded-storage states, and focused regression coverage.

Repository and tracker fields remain metadata-only. APO-35 does not inspect local paths, invoke Git,
call Jira/Azure DevOps, read credentials, scan repository contents, implement routing, or start the
orchestration runtime. Delivery state is **IMPLEMENTATION COMPLETE / AWAITING SOL ACCEPTANCE** on
`feat/APO-35-projects-workspace` from main base
`34569abee50bdb708770e134e9db7db18752a80d`. The next planner boundary is GPT-5.6 Sol acceptance.
See section 11 for the SOL-35-01/SOL-35-02 bounded delta remediation applied on top of this delivery.

## 11. APO-35 Delta Remediation (SOL-35-01 / SOL-35-02)

Sol's review of the APO-35 draft identified one confirmed defect (SOL-35-01: `ProjectsViewModel`
resolved its save target from live selection instead of an immutable edit-target captured at
edit-start, allowing a save to redirect to the wrong project if selection changed mid-edit) and
requested sanitized runtime visual evidence (SOL-35-02). The bounded remediation, executed by
Claude Sonnet 5 as executor under the same Story/PR, is:

- `ProjectsViewModel` now captures `_editingProjectId` when editing starts and uses it exclusively
  as the save/update target, independent of later selection or filter changes; save fails closed
  with a truthful error if the edit target becomes unavailable; the edit target and editor contents
  are preserved on a failed save so retry targets the original project; the edit target clears only
  on successful save or cancel.
- A new `IsRegistryInteractionEnabled` property disables the project list, search box, and status
  filter while editing, and the existing `New project`/`Refresh` command predicates were extended
  to stay disabled while editing, so the registry cannot be mutated out from under an in-progress
  edit through the UI.
- Six new focused regression tests were added covering selection-change isolation, filter-driven
  selection loss, failed-save retry targeting, command disablement while editing, and edit-target
  clearing on cancel/successful save. Full-suite validation is 214/214 passing (Desktop 44, up from
  38).
- SOL-35-02 visual evidence was attempted using Windows-native UI Automation and GDI screen capture
  against the real published `win-x64` self-contained executable. Capture revealed that the
  published shell starts in a fully degraded, no-persistence fallback mode on every launch, for both
  the Projects and AI Capacity workspaces, because of an unrelated, pre-existing DI constructor
  ambiguity in `AiCapacityViewModel` (two applicable public constructors) that makes
  `_host.Services.GetService<MainWindow>()` throw during startup and fall back to a null-backed
  shell. This defect was introduced in the APO-34 delivery commit and is unrelated to
  `ProjectsViewModel`/SOL-35-01; per the bounded remediation's no-scope-expansion constraint it was
  not fixed here. Because no privacy-safe, functionally real screenshot of the Projects workspace
  could be captured, no image was added to `docs/evidence/`, and this limitation is reported
  truthfully rather than substituting degraded-mode captures. Fixing the `AiCapacityViewModel`
  constructor ambiguity is recommended as a follow-up Jira item under Epic APO-5 before SOL-35-02
  evidence can be captured or before the workspace is considered release-qualified.

## 12. APO-36 Blocking Startup Regression (Prompt 4 continuation)

APO-36 fixed the pre-existing `AiCapacityViewModel` DI constructor ambiguity identified while
attempting SOL-35-02 evidence capture in section 11 above. The regression predated APO-35 (it was
introduced in the already-merged APO-34 delivery) but blocked runtime acceptance evidence for the
APO-35 merge candidate, so Sol authorized the fix on the same branch/PR as a bounded continuation of
the same Prompt-4 delta, without advancing to Prompt 5 or invoking Claude Opus.

**Root cause:** `AiCapacityViewModel` exposes both a normal provider-backed constructor
`(IProviderRegistry, IProviderConnectionService)` and a degraded/manual fallback constructor
`(IExecutableLocator)`. An implicit `services.AddSingleton<AiCapacityViewModel>()` left
Microsoft.Extensions.DependencyInjection unable to disambiguate them, so `App.OnStartup` threw while
resolving `MainWindow` and the existing outer `catch` silently fell back to the degraded,
no-persistence shell on every normal launch.

**Fix:** a new `DesktopServiceCollectionExtensions.AddDesktopWorkspaceServices()` extension method
registers `AiCapacityViewModel` via an explicit factory that resolves the normal provider-backed
constructor directly, alongside `IProjectRegistryService`, `ProjectsViewModel`, `MainWindowViewModel`,
and `MainWindow`. `App.OnStartup` now composes the container as `AddInfrastructure` → `AddProviders`
→ `AddDesktopWorkspaceServices` — the same three calls a new `ProductionCompositionTests.cs` suite
exercises against the real Microsoft DI container to prove `AiCapacityViewModel`,
`MainWindowViewModel`, and `ProjectsViewModel` all resolve normally (non-degraded). The degraded
fallback constructor and `App.OnStartup`'s outer `catch` are unchanged.

With the fix in place, SOL-35-02 sanitized visual evidence (blocked in section 11) was captured
successfully from the real, non-degraded published `win-x64` shell and committed at
`docs/evidence/APO-35-projects-workspace.png`. A subsequent Claude Opus 5 independent review
(Prompt 5/5) returned `CHANGES REQUIRED` against four Projects UI findings (`OPUS-01`..`OPUS-04`);
GPT-5.6 Sol adjudicated the review in Jira comment `11838` and authorized a bounded Claude Sonnet 5
remediation (Prompt 1/5 of a new Opus cadence), which fixed all four findings plus a related
`ListBox` rendering regression the `OPUS-01` fix exposed. Full-suite validation is 225/225 passing
(Desktop 55, up from 219/49). See `TASK.md` and `.ai/CURRENT_STATE.md` for exact SHAs and evidence
details.

## 13. APO-35 + APO-36 Final Delivery and Merge

APO-35 and APO-36 were finalized, Sol-accepted, and squash-merged into `main` via PR #6 at merge
commit `beab8072551a84aad60df7744135c74c75e51acb`. Full-suite validation is 225/225 tests passing
(Domain 28, Provider 46, Infrastructure 86, Connection 10, Desktop 55). OPUS-01..OPUS-04 findings
are closed; OPUS-05..OPUS-08 are deferred as P3. APO-35 and APO-36 are transitioned to Done in Jira;
parent Epics APO-5 and APO-4 remain In Progress.

## 14. APO-37 Read-Only Local Git Repository Verification

APO-37 is the first bounded APO-6 vertical slice, implemented from exact main base
`8a81017b25fe0cfd8efcd4febafd66a1bee6c41e` on `feat/APO-37-local-git-verification`. The slice
adds provider-independent Application repository-state contracts and a project-aware verification
service, plus an Infrastructure-only Git process runner and inspector. Desktop/WPF owns explicit
Verify repository and Refresh repository state commands; selection changes cancel and obsolete the
previous request, and a generation/identity check prevents a late result for Project A from being
published into Project B.

Production inspection is local-only and uses only `git --version`, `git -C <path> rev-parse
--show-toplevel`, `symbolic-ref --quiet --short HEAD`, `rev-parse --verify HEAD`, upstream
resolution, `status --porcelain=v1 -z --untracked-files=all`, and `remote -v`. Process execution
uses `UseShellExecute=false`, argument-list transport, `GIT_TERMINAL_PROMPT=0`,
`GIT_OPTIONAL_LOCKS=0`, asynchronous cancellation, and a ten-second per-command timeout. Remote
URLs are sanitized before entering application state; changed-file evidence is capped at 100
repository-relative entries and exposes total/truncation truthfully. No remote request, file
content read, diff, patch, or Git mutation is part of APO-37.

The accepted project lifecycle is Active, Paused, Blocked, Archived, with status filters All,
Active, Paused, Blocked, Archived. Draft and Completed are not ProjectStatus values.

---

## 15. Strategic Rebaseline Capability Map

The owner-requested Compare-AI-Orchestrators direction is incorporated as a capability map over
the existing Epic structure. Competitor names are not product boundaries, and no duplicate Epic is
introduced.

| Band | Capability slices | Jira Stories |
|---|---|---|
| P0 | Agent/model truth and progressive onboarding | APO-38, APO-39 |
| P0 | Contracts, dependency graph, handoffs, and recovery context | APO-40, APO-41, APO-42, APO-43 |
| P0 | Quality-first routing, bounded execution, workspaces, and tracker evidence | APO-44, APO-46, APO-45, APO-47 |
| P0 | Remote SCM / CI evidence | APO-62 |
| P0 | Independent QA gates, human gates, controlled delivery, and Mission Control | APO-48, APO-49, APO-63, APO-50 |
| P1 | Review Inbox, skills, health, decision ledger, runtime evidence, and context budgets | APO-51 through APO-56 |
| P2 | Bounded background automation/housekeeping and optional remote approval design | APO-57, APO-58 |
| P3 | APO-37 hardening debt (findings accepted into backlog; Jira: To Do) | APO-59, APO-60, APO-61 |

Existing work reused rather than recreated includes APO-27 project/orchestration storage, APO-35/36
the Projects workspace and normal composition, APO-37 local Git evidence, and APO-33 CI/release
workflow. Existing Epic dependencies remain the architectural ownership map. Jira issue links record
the critical predecessor relationships among APO-38 through APO-63.

## 16. Canonical Hard Dependency DAG

Jira `Blocks` records only a real architectural prerequisite. The repaired strategic graph contains
exactly the following 18 hard dependencies; the planner ordering in the next section is separate
and does not turn every adjacent planning step into a `Blocks` link.

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

The arrows mean the left Story blocks the right Story. APO-46 intentionally precedes APO-45 so
project-isolated repository/worktree safety is established before autonomous implementation. The
accepted APO-37 links to APO-59/60/61 remain `Relates`, not hard dependencies. Other useful
sequencing relationships remain planner guidance unless a future contract proves a real
architectural prerequisite.

## 17. Recommended Planner Ordering

The explicit planner sequence is:

```text
Accepted APO foundation / APO-37
        v
APO-38
        v
APO-39
        v
APO-40
        v
APO-41
        v
APO-42
        v
APO-43
        v
APO-44
        v
APO-46 - isolated workspace safety
        v
APO-45 - bounded autonomous execution
        v
APO-47 + APO-62 evidence integrations
        v
APO-48
        v
APO-49
        v
APO-63
        v
APO-50
        v
P1
        v
P2
```

The compact capability map below remains a visual summary of that sequence:

```text
APO-3/APO-4 foundations
        ↓
APO-38 agent/model truth → APO-39 onboarding/context resolution
        ↓
APO-40 contracts → APO-41 dependency graph → APO-42 role handoffs
        ↓                         ↘
APO-43 recovery context       APO-44 quality-first routing
        ↓                         ↓
APO-46 isolated workspaces → APO-45 bounded execution
                                      + APO-47/APO-62 evidence integrations
                                      ↓
                         APO-48 independent QA evidence
                                      ↓
                          APO-49 approval policy
                                       ↓
                         APO-63 controlled remote delivery
                                       ↓
                               APO-50 Mission Control
                                       ↓
                  APO-51..56 workflow acceleration
                                      ↓
                         APO-57..58 optional P2 work
```

The remote-evidence/delivery ordering is explicitly:

```text
APO-47 tracker evidence + APO-62 read-only remote SCM/CI evidence
                              |
                              v
                         APO-48 QA evidence
                              |
                              v
                         APO-49 approval policy
                              |
                              v
                         APO-63 controlled remote delivery
```

Read-only remote evidence must exist before controlled remote writes so an operation cannot rely on
a model claim, stale branch state, or missing CI/review proof. This ordering intentionally places
capability truth, persistence, contracts, evidence, and human authority before broad autonomous
behavior or consolidated UX. APO-17/33 CI remains a cross-cutting
release prerequisite and may be sequenced by Sol when the release risk warrants it.

## 18. Strategic Acceptance Boundary

This document is a roadmap and architecture rebaseline, not implementation authorization. APO-69 is
complete and APO-47 is merged, Sol-accepted, post-merge verified, and Jira Done. The current planner
boundary is the post-merge state reconciliation recorded in `.ai/CURRENT_STATE.md` and `TASK.md`.
Sol must replace `TASK.md` with one self-contained contract for a selected remaining Story; no
executor may start APO-62, APO-48, or any other Story from roadmap presence alone.
