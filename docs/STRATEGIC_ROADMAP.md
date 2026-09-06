# AI_Orchestrator — Strategic Orchestration Roadmap

## 1. Purpose

This roadmap is the concise bridge between the approved BRD, the implementation plan, Jira
Stories, and the owner experience APO is being built to provide. It records the strategic
direction and sequencing after the accepted APO foundation; it is not a runtime feature claim and
does not authorize a new Story. Each implementation boundary still requires a Sol-authored
`TASK.md` contract.

## Current status (6 September 2026)

APO-38 through APO-47 and APO-68 are implemented and marked Done in Jira. APO-69 is also Done.
APO-62 (remote SCM and CI evidence) is also Done. APO-48 is **FINAL ACCEPTED / MERGED / DONE**
with product merge SHA `7fe179844ceb056c542067485843bc892ebdefcc`, accepted product head
`caed10d0486994e9235a66ef44ec6137649dd347`, accepted tree
`f152699b89b4c1f498c3dbb4357ee07ac00fda77`, canonical suite 1,136 passed / 0 failed / 0 skipped,
and build 0 warnings / 0 errors. APO-51 is **FINAL ACCEPTED / MERGED / DONE** with product merge
SHA `ea96beefeec5b2fc2381ad1d4ade39c6c63fc56c`, accepted head
`4aeb7e062320d78ab0323473d8ce321a30b66476`, and accepted tree
`56053615c679fc64464464c9b856a5cf52a50860`. APO-49, APO-63, APO-50, and APO-33 remain To Do and
not started; APO-49 is the current next gate. `GITHUB ACTIONS CI = NONE / NOT CLAIMED`. The live
authority snapshot is maintained in `.ai/CURRENT_STATE.md` and `TASK.md`.

## FAST V1 closeout baseline

### MUST SHIP

1. APO-51 - minimal Review Inbox / bounded remediation loop (delivered and accepted)
2. APO-49 - minimal Human Approval + Delivery Gates (current next gate; not started)
3. APO-63 - controlled remote source-control delivery
4. APO-50 - Mission Control read model + UI
5. APO-33 - GitHub Actions CI + build/test/package

The exact FAST V1 order is `APO-51 -> APO-49 -> APO-63 -> APO-50 -> APO-33 -> Final V1 Release
Audit -> v1.0.0`. APO-51 is complete; the remaining four implementation Stories are To Do. This
roadmap does not authorize implementation.

### ACTIVE V1 AI RESOURCE BOUNDARY

- OpenAI: two GPT accounts; Sol for planning/architecture/acceptance, Luna xHigh as the main
  executor, and Terra HIGH for recovery/finalization when needed.
- Claude: Sonnet 5 for bounded implementation/fixes and Opus 5 for critical independent review
  only.
- Antigravity Plus: auxiliary bounded/mechanical execution, including Gemini-family usage when
  appropriate and available.

`COPILOT = POST-V1`.

### POST-V1

- APO-52 through APO-61
- Copilot-specific functionality
- Inactive-provider-specific enhancements
- Additional provider integrations
- Provider polish not necessary for the core V1 loop or release safety

> No new provider, feature, or integration may enter V1 unless it is required to make the Planner
> -> Executor -> Reviewer -> Approval -> Controlled Delivery loop usable or release-safe.

## 2. Product experience we are building

APO is intended to let an owner supervise AI-assisted software delivery from one local-first
command center without replacing the IDE, GitHub, Azure Repos, Jira, Azure Boards, build tools, or
the owner's authority. The owner should not need to reconstruct project state by copying prompts
between models or by trusting an executor's completion message.

The target workflow is:

```text
Open/select project
    ↓
Smart Continue resolves canonical project checkpoint
    ↓
Sol plans and produces versioned execution contract
    ↓
Quality-first / quota-aware routing selects executor
    ↓
Sol routes the bounded implementation to the appropriate executor under the canonical routing policy
    ↓
APO maintains project-isolated repository/worktree evidence
    ↓
Jira / Azure Boards and GitHub / Azure Repos evidence refreshes
    ↓
Independent build/test/CI/runtime validation
    ↓
Opus review only when cadence/risk requires
    ↓
Sol adjudicates findings
    ↓
Bounded remediation + revalidation
    ↓
Sol acceptance
    ↓
Owner gate for protected/high-risk actions
    ↓
Controlled remote delivery
    ↓
Final independent evidence verification
    ↓
Decision/audit history + next safe checkpoint
```

The end-to-end orchestration runtime, provider execution, controlled delivery, and consolidated
Mission Control experience remain planned capabilities. APO-62 has delivered the provider-
independent read-only remote SCM/CI evidence boundary. APO-43 has delivered the durable Smart
Continue/recovery contract and state boundary; its complete owner-facing experience remains planned.

## 3. Architectural principles

- Local-first Windows desktop operation remains the V1 foundation: C#/.NET 10, WPF, MVVM, clean
  dependency direction, JSON/JSONL persistence, secure external credentials, and self-contained
  Windows artifacts.
- Quality and risk take precedence over quota savings. Capacity can inform routing but cannot
  override capability, policy, risk, or required review.
- Project identity, context, credentials, repository/worktree state, tracker state, and run state
  remain isolated. No chat transcript is the canonical project record.
- Evidence is independently captured, timestamped, source-labeled, freshness-aware, and explicit
  about limitations. Missing evidence is not success.
- Read-only remote evidence and remote writes are separate capabilities. Controlled delivery must
  fail closed when exact targets, validation, approval, or permissions are stale or changed.
- Official APIs, account surfaces, OAuth/device flows, supported SDKs, and verified local evidence
  are preferred. Browser scraping, cookie extraction, secret persistence, and model CLI claims as
  source-of-truth are prohibited.

## 4. Model operating policy

| Model | Default role |
|---|---|
| GPT-5.6 Sol High | Planner, architect, router, quota governor, Jira decomposition, acceptance and prompt authority (chat only) |
| GPT-5.6 Luna xHigh | Primary bounded implementation executor |
| Claude Sonnet 5 Medium | Fallback / special-need bounded implementation when explicitly selected by Sol |
| Claude Sonnet 5 High | Fallback / special-need difficult bounded implementation when explicitly selected by Sol |
| GPT-5.6 Luna Max | Exceptional implementation escalation only |
| Claude Opus 5 | Independent reviewer at configured cadence and critical checkpoints |
| GPT-5.6 Terra Medium/High | Optional risk-triggered security, concurrency, and data-integrity assurance |
| Claude Haiku 4.5 | Disabled from active routing |

One assigned Jira work item is the maximum active scope for an executor. A roadmap Story is not
implementation authorization, and completing one Story never automatically starts the next.

## 5. Current shipped foundation

The accepted foundation includes the WPF/.NET/JSON/JSONL desktop architecture, secure credential
reference boundaries, provider-independent capacity contracts, project/orchestration storage,
Projects workspace, agent/model registry, progressive onboarding, versioned contracts, dependency
graphs, structured handoffs, durable recovery state, explainable routing, isolated workspace
preparation, bounded cancellable execution, workspace-preparation hardening, and APO-37 read-only
local Git repository verification. APO-37 provides bounded local branch/HEAD/status/remote evidence
for a selected configured path; it does not call a remote SCM service, read repository file contents,
or perform Git writes.

APO-33 remains the existing repository-owned GitHub Actions CI/release Story. Local validation in
this roadmap session is not a GitHub CI result. APO-48 independent validation evidence and
evidence-based QA gates are accepted. Provider execution, tracker automation, controlled delivery,
and Mission Control are not shipped by this documentation checkpoint.

## 6. Strategic Jira roadmap

The active strategic roadmap is APO-38 through APO-63 under the approved APO-1 through APO-17
Epics. APO-33 remains a complementary existing CI/release Story.

### P0 control plane — APO-38..43

- APO-38 — Provider-independent agent/model capability and connection truth.
- APO-39 — Progressive project onboarding and canonical context resolution.
- APO-40 — Versioned planning and execution contracts.
- APO-41 — Dependency DAG and safe scheduling.
- APO-42 — Structured planner/executor/reviewer handoffs.
- APO-43 — Persist canonical context, Smart Continue, and recovery checkpoints.

### P0 bounded execution — APO-44..47

- APO-44 — Quality-first, quota-aware routing.
- APO-45 — Bounded execution.
- APO-46 — Isolated worktrees/workspaces.
- APO-47 — Jira/Azure Boards tracker integration (delivered; Jira Done).

### P0 source-control/tracker/evidence — APO-62, APO-48, APO-49, APO-63

- APO-62 — Provider-independent, read-only GitHub/Azure Repos remote SCM and CI evidence (delivered; Jira Done).
- APO-48 — Independent QA and evidence gates (delivered; Jira Done; final accepted).
- APO-49 — Human approval policy.
- APO-63 — Controlled remote source-control delivery operations.

### P0 Mission Control — APO-50

APO-50 consolidates active work, roles, blockers, approvals, repository/tracker state, evidence,
and owner attention into an evidence-backed command-center read model and surface.

### P1 acceleration — APO-51..56

APO-51 Review Inbox and bounded remediation is delivered and accepted. APO-52 through APO-56 remain
POST-V1 / deferred until a later approved phase.

### P2 controlled expansion — APO-57..58

Bounded background automation/housekeeping and an optional remote/mobile approval security design.

### P3 remaining/planned hardening — APO-59..61 (Jira: To Do)

APO-37 local evidence/output bounds, verification deadline/path truthfulness, and real-Git
availability evidence semantics.

### Canonical hard dependency DAG

Jira `Blocks` records only real architectural prerequisites. The repaired strategic graph contains
exactly 18 hard dependencies:

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

The arrows mean the left Story blocks the right Story. APO-46 intentionally precedes APO-45 because
project-isolated repository/worktree safety must be established before autonomous implementation.
The accepted APO-37 traceability links to APO-59, APO-60, and APO-61 are `Relates`, not hard
dependencies. Other useful ordering relationships are planner guidance and are not Jira `Blocks`
links unless a future contract proves a real prerequisite.

## 7. Integration boundaries

### Local Git — APO-37

APO-37 is the local, read-only repository verification slice. It owns local path/repository
inspection, bounded worktree evidence, cancellation, project identity checks, and safe unavailable
states. It does not imply remote reachability, pull-request state, CI success, or permission to
write.

### Tracker awareness — APO-47

Jira and Azure Boards are provider-independent work-item inputs where configured. APO-47 delivers a
bounded Jira-first implementation with auditable, project-isolated identity, keys, status, links,
safe reads, bounded mutations, post-verification, and audit evidence. Azure transport remains
unimplemented and truthful as unsupported/not configured. A model or tracker CLI is not itself proof
of repository or CI state.

### Remote SCM / CI evidence — APO-62

APO-62 is delivered as a read-only provider-independent boundary for configured GitHub and Azure
Repos projects, using official supported integration paths. It captures repository identity,
branch/commit relationships, pull-request identity/state, reviews, checks/status, CI/workflow
evidence, source/provider, freshness, and immutable target identifiers where exposed. It must
distinguish Not Configured, Authentication Required, Permission Denied, Unsupported, Unavailable,
Stale, Partial, and Available.

### Controlled delivery — APO-63

APO-63 is a separate provider-independent write boundary. Any PR metadata transition, review
coordination, bounded delivery comment, or merge operation must bind to the project, work item,
execution-contract version, repository, base/head refs, exact head SHA, current validation,
approval policy, actor, and audit identity. It fails closed on moved refs, stale approval, missing
evidence, failed validation, changed permissions, changed mergeability, or changed project identity.

## 8. Smart Continue and recovery

APO-43 owns the canonical `Continue project` behavior. Its durable checkpoint includes project/run
context, work-item and dependency state, execution-contract identity/version, selected roles,
routing evidence, repository/tracker references, validation, review, approval, blockers, next safe
action, and checkpoint lifecycle.

Smart Continue must distinguish resumable, blocked, stale/needs fresh evidence, completed, approval
required, and context-insufficient states. It must recover after restart or chat/context loss from
persisted project-isolated state, never from old conversation text treated as current Git, tracker,
CI, or approval evidence.

## 9. Mission Control

APO-50 is the planned command-center read model for the owner. It should make active projects,
current work, model roles, dependency blockers, execution state, validation, review findings,
approval gates, repository/tracker evidence, attention items, and next safe actions visible without
inventing health or completion from missing evidence.

## 10. Evidence, review and delivery safety

The progression chain is: planner contract → bounded execution → independent local/remote
repository and tracker evidence → build/test/CI/runtime validation → configured independent review
→ Sol adjudication → bounded remediation and revalidation → Sol acceptance → owner approval where
required → controlled delivery → final independent verification.

Protected/default-branch merges, production actions, destructive changes, credential/billing
actions, material architecture changes, and other owner-defined high-risk actions remain behind
explicit human gates. No remote write may silently bypass the evidence or approval policy.

## 11. Jira hygiene / roadmap identity

The approved APO-1 through APO-17 Epic structure is reused. APO-48 is delivered and the remaining
FAST V1 must-ship backlog remaining after accepted APO-51 is APO-49, APO-63, APO-50, and APO-33.
APO-52 through APO-61 are post-V1 deferred scope. APO-38 through APO-47 are delivered. APO-64 through APO-67 are Done, VOID,
`no-project-work`, and `connector-correction` artifacts retained only as transparent Jira connector
history; they have zero product scope and are excluded from roadmap totals, dependencies, sequencing,
BRD claims, and Mission Control scope. APO-68 is delivered workspace-preparation hardening. APO-69
is the current repository rebaseline/cleanup Story and is not a product-runtime Story.

## 12. Near-term ordering

The following compact map is retained as a historical visual only; it is not the Jira DAG or the
authoritative implementation sequence. Use the canonical DAG above and the explicit sequence below.

```text
Accepted APO-38..47 and APO-68 foundation
        ↓
APO-38 → APO-39 → APO-40 → APO-41 → APO-42 → APO-43
        ↓                                  ↘
APO-44 → APO-46 → APO-45                 recovery context
                                  APO-62 read-only remote SCM/CI evidence
                                         ↓
                              APO-48 QA/evidence gates
                                         ↓
                              APO-49 owner approval policy
                                         ↓
                              APO-63 controlled remote delivery
                                         ↓
                              APO-50 Mission Control
                                         ↓
                              APO-51..56 → APO-57..58
```

Authoritative planner sequence for the remaining backlog:

```text
Accepted APO-38..48 and APO-51 foundation
        v
APO-49 owner approval policy
        v
APO-63 controlled remote delivery
        v
APO-50 Mission Control
        v
APO-33 GitHub Actions CI + build/test/package
        v
Final V1 Release Audit
        v
v1.0.0
```

Read-only remote evidence must precede controlled remote writes because APO cannot safely mutate a
remote target without independently knowing the current repository, ref, review/check, validation,
permission, mergeability, and approval state. The next Story is not selected by this roadmap;
GPT-5.6 Sol must issue a fresh contract after reviewing this post-merge reconciliation. This is
planner sequencing, not a claim that every adjacent pair is a Jira hard dependency.

## 13. Current planner boundary

APO-69 is complete, APO-47 is merged and Jira Done, and APO-62 (remote SCM and CI evidence) is
delivered and Jira Done. APO-48 and APO-51 are final accepted, merged, and Jira Done. The current
FAST V1 gate is APO-49, which remains To Do and not started; APO-63, APO-50, and APO-33 also remain
To Do. No implementation is authorized by this roadmap. GPT-5.6 Sol must provide a fresh,
self-contained contract for each Story, and no automatic roadmap execution is permitted.
