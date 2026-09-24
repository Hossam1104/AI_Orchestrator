# AI_Orchestrator - Execution Contract Library

**Historical prompt library:** The model/version assignments and execution boundaries below record their original dates and are not current routing or execution authority. APO is OWNER-PAUSED since 2026-09-24; see [current state](../.ai/CURRENT_STATE.md) and [model routing](../.ai/AI_MODEL_ROUTING.md). No prompt below authorizes work while paused.

**Product:** AI_Orchestrator (APO)
**BRD:** `docs/BRD.md`
**Plan:** `docs/IMPLEMENTATION_PLAN.md`
**Planner / Router / Acceptance / Prompt Authority:** GPT-5.6 Sol (chat mode only)
**Primary executor:** GPT-5.6 Luna xHigh
**Fallback / special-need executors:** Claude Sonnet 5 Medium / Claude Sonnet 5 High, only when explicitly selected by Sol
**Disabled from active routing:** Claude Haiku 4.5
**Exceptional escalation:** GPT-5.6 Luna Max
**Independent reviewer:** Claude Opus 5
**Specialist assurance:** GPT-5.6 Terra Medium/High

Active execution providers are OpenAI/Codex and Anthropic/Claude only. The canonical model and
execution policy is maintained in `.ai/AI_MODEL_ROUTING.md` and `.ai/AI_EXECUTION_POLICY.md`.

This file is the permanent prompt-library and historical traceability document. The repository,
BRD, Jira scope, and current state are authoritative over old chat context.

---

# COMMON ACTIVE WORK-ITEM INHERITANCE

Every future Sol-authored execution contract inherits these rules from `AGENTS.md`:

- execute only the assigned Jira Story/Task;
- read `AGENTS.md`, `docs/BRD.md`, `.ai/CURRENT_STATE.md`, `docs/IMPLEMENTATION_PLAN.md`, root
  `TASK.md`, and the exact referenced contract before acting;
- inspect Git status and preserve unrelated owner changes;
- use WPF + .NET 10 + MVVM + modular clean architecture;
- use JSON/JSONL local persistence with schema metadata, safe writes, synchronization, resilient
  corruption handling, and no database/ORM runtime;
- keep credentials outside JSON/JSONL/logs and use opaque secure-storage references;
- preserve dynamic quota windows, used/remaining semantics, `DateTimeOffset`, provider truthfulness,
  project isolation, privacy, and last-known-good values;
- honor Windows 10 1809/build 17763 and Windows 11 compatibility goals with x86/x64/ARM64
  consideration, x64 primary, and graceful fallback for optional modern effects;
- do not make a provider CLI, separate runtime, database, Node, embedded browser, or developer
  tooling a consumer prerequisite;
- validate the actual scope, review the diff/secrets/artifacts, update current state, synchronize
  Jira/Git under policy, and stop before another work item.

The active work-item flow is:

```text
BRD
  |
  v
Jira Epic
  |
  v
Jira Story / Task
  |
  v
Sol planning / architecture
  |
  v
TASK.md execution contract
  |
  v
Assigned executor
  |
  v
Validation and evidence
  |
  v
Independent review where required
  |
  v
Sol acceptance
  |
  v
Jira / Git synchronization
```

Sol prepares a complete self-contained `TASK.md` for each approved next work item. Preparing a
task never authorizes its execution. Luna xHigh is the normal primary executor; Sonnet is used only
when Sol explicitly selects it for fallback, quota balancing, task fit, or another special need;
Haiku is disabled from active routing; and no future implementation prompts are pre-generated here.

---

# LEGACY / SUPERSEDED BY APO REBASELINE - DO NOT EXECUTE

The numbered provider-only execution sequence below is retained solely for historical traceability.
It is not an active plan, is not an approved task list, and must not be used to start Session 04 or
any later numbered session. APO Stories/Tasks under APO-2 through APO-17 replace it.

## Historical completed foundation records

- **Session 01 - Repository & Solution Foundation:** completed under the earlier product/stack.
- **Session 02 - Domain & Application Architecture:** completed; provider-independent concepts
  remain candidates for APO reuse.
- **Session 02R - Domain Integrity Remediation:** completed; quota, subscription, and credential
  reference safeguards remain candidates for APO reuse.
- **Session 03 - EF Core 10 + SQL Server LocalDB Persistence:** completed and validated under the
  earlier architecture; architecturally superseded, retained as history only.
- **Session 03R - Portable Consumer Desktop Architecture Migration:** completed; active source was
  migrated to WPF and JSON/JSONL.
- **Session 03R-F - Portable Foundation Resilience Remediation:** completed; startup/storage and
  JSONL resilience were corrected and validated.

## Historical superseded numbered roadmap

The old sequence is recorded as a historical label map only:

| Legacy item | Historical scope | APO disposition |
|---|---|---|
| Session 04 | Provider feasibility investigation | Superseded; replan under APO-4 |
| Gate A | Architecture/provider review | Replaced by Sol/Jira review gates |
| Session 05 | WPF design system | Replan under APO-15 |
| Session 06 | Main dashboard | Replan under APO-15 |
| Session 07 | Tray and Focus HUD | Replan under APO-15/APO-16 |
| Sessions 08-12 | Codex, Claude, Kimi, Copilot, Antigravity | Replan under APO-4/APO-8 |
| Gate B | Provider review | Replan under APO-4/APO-8/APO-13 |
| Session 13 | Subscription management | Replan under APO-4 |
| Session 14 | JSONL history and analytics | Replan under APO-4/APO-16 |
| Session 15 | Capacity recommendation engine | Replan under APO-9 |
| Session 16 | Monitoring and notifications | Replan under APO-4/APO-16 |
| Session 17 | Settings, security, resilience | Replan under APO-3 |
| Session 18 | UX and performance polish | Replan under APO-15/APO-17 |
| Gate C | Pre-release review | Replan under APO-12/APO-13/APO-17 |
| Session 19 | Packaging, CI, release | Replan under APO-17 |
| Session 20 | Final stabilization | Replan under APO-17 and Sol release acceptance |
| Final Release Review | Final Opus review | Replan under APO-13/APO-14/APO-17 |

The legacy prompts and their exact historical wording are available from prior Git commits. They
are intentionally not copied forward as executable prompts because they describe the superseded
provider-only product and session lifecycle.

---

# APO WORK-ITEM PROMPT TEMPLATE

Sol may use this structure when preparing a future `TASK.md`. It is a template, not an executable
assignment:

```text
# TASK - <Jira key> - <Story title>

Status: APPROVED EXECUTION CONTRACT
Assigned executor: <model/role>
Epic: <APO-N>

## Objective
<one bounded outcome>

## Repository and current-state checkpoint
<facts and relevant evidence>

## In scope
- ...

## Out of scope
- ...

## BRD requirements and acceptance criteria
- ...

## Required validation and evidence
- ...

## Review and human approval gates
- ...

## Delivery and stop condition
Follow AGENTS.md. Update CURRENT_STATE, synchronize the assigned Jira item and Git evidence under
policy, and stop. Do not execute another Story.
```

---

# HISTORICAL APO CHECKPOINT (SUPERSEDED)

The former APO-37 checkpoint and its planned APO-38 start are retained for historical traceability
only. APO-38 through APO-47 and APO-68 are now delivered; APO-69 is complete. Read
`.ai/CURRENT_STATE.md` and `TASK.md` for the live post-merge reconciliation boundary. No prompt in
this permanent library authorizes APO-62, APO-48, or any other new Story.
