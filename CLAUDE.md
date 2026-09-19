# CLAUDE.md - Claude / Sonnet / Opus APO Instructions

**Repository:** https://github.com/Hossam1104/AI_Orchestrator
**Local Root:** `D:\AI Tools\Active Projects\AI_Orchestrator`
**Product:** AI_Orchestrator (APO)

`AGENTS.md` is the universal execution authority. This file adapts Claude-family behavior to that
contract without duplicating volatile project status.

## Roles

Canonical model portfolio, routing, quota, and execution-policy detail live in
`.ai/AI_MODEL_ROUTING.md` and `.ai/AI_EXECUTION_POLICY.md`. This section summarizes only the
Claude-relevant slice; do not duplicate the canonical files here.

- **GPT-5.6 Sol:** planner, architect, model router, quota governor, work-item decomposition owner,
  acceptance authority, and executor prompt authority (chat mode only).
- **GPT-5.6 Luna xHigh:** default substantial implementation executor and workhorse (approximately
  60% target), with first-pass completion preferred.
- **Claude Sonnet 5 Medium:** bounded bug-fix specialist for isolated defects, narrow regressions,
  and localized implementation errors, only when Sol explicitly selects it.
- **Claude Sonnet 5 High:** bounded bug-fix specialist for difficult isolated defects, only when Sol
  explicitly selects it.
- **Claude Haiku 4.5:** disabled from active routing. Do not assign or route work to Haiku.
- **Claude Opus 5:** independent reviewer, approximately 10% ceiling, used at roughly every seven
  meaningful executor prompts or at a critical checkpoint; review-only by default.
- **GPT-5.6 Luna Max:** exceptional implementation escalation only; never the normal executor.
- **GPT-5.6 Terra Medium/High:** protected recovery / difficult surgical finalization resource,
  approximately 20% ceiling, not the normal executor. Sol considers whether Luna can finish with a
  better bounded correction prompt first; security/concurrency/data-integrity assurance may still
  justify Terra when recovery complexity warrants it.

Default APO repository-execution providers are OpenAI/Codex and Anthropic/Claude. Gemini may be an
explicitly owner-delegated auxiliary resource for low-risk/mechanical cross-project work, but is
not a default APO executor and this does not add a Gemini V1 runtime adapter. Quality and risk come
before quota preservation. Only Sol chooses when a Claude executor is explicitly assigned.

## Mandatory Startup

Before changing files, read completely:

1. `AGENTS.md`;
2. `.ai/AI_EXECUTION_POLICY.md`;
3. `.ai/AI_MODEL_ROUTING.md`;
4. `docs/BRD.md`;
5. `.ai/CURRENT_STATE.md`;
6. `docs/IMPLEMENTATION_PLAN.md`;
7. `docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md` when the assigned work touches local agent readiness, runtime/process recovery, retry/fallback, workflow templates, execution evidence/history, benchmarking, or historical-performance routing;
8. the active root `TASK.md`; and
9. the referenced prompt or review section in `docs/SESSION_PROMPTS.md`, if any.

Then inspect Git status and only the files relevant to the assigned GitHub APO work item. The repository,
not an old chat, is authoritative.

## Sonnet Executor Mode

When Sonnet is explicitly assigned, execute only the bounded GitHub APO work item. Preserve the approved
WPF/.NET/JSON/JSONL architecture, use existing abstractions where sound, avoid speculative provider
work, validate the actual change, inspect the diff and secrets, update `.ai/CURRENT_STATE.md`,
complete the Git Delivery Contract, and stop at the planner boundary.

A failed executor is not permission to self-route to another model. Retry/fallback must follow the
canonical policy in `.ai/AI_EXECUTION_POLICY.md` and `.ai/AI_MODEL_ROUTING.md`; future automatic
fallback is owned by APO-72 and must be based on typed failure/checkpoint/policy evidence rather than
a hard-coded provider order.

## Opus Reviewer Mode

When Opus is assigned as reviewer, inspect implementation, repository evidence, requirements,
validation, security, provider truthfulness, persistence, project isolation, human gates, and
release prerequisites independently. Classify findings as BLOCKER/HIGH/MEDIUM/LOW. Do not add
scope or implement fixes unless explicitly requested. Opus must remain independent from the
implementation executor by default.

For future benchmark work, reviewer independence and equivalent validation criteria must be
preserved per candidate; competing write-capable models must never share one working tree.

## Active Architecture Reminder

The active foundation is WPF + .NET 10 + MVVM + modular clean architecture with JSON/JSONL local
persistence and secure external credential storage. V1 has no active EF Core, SQL Server, LocalDB,
ORM, SQLite, WinUI, Windows App SDK, Angular, Electron, Node/npm, embedded Chromium, or mandatory
cloud backend. Historical superseded implementations remain in Git history and the handoff only as
clearly labeled historical evidence.

Domain/Application boundaries, dynamic quota windows, remaining-capacity semantics, `DateTimeOffset`,
last-known-good data, atomic writes, schema handling, JSONL history, cross-Windows graceful
degradation, and self-contained consumer deployment are mandatory. Do not invent provider endpoints,
scrape cookies, log tokens, or treat a CLI as a whole-application prerequisite.

APO is local-PC-first. Durable project state, execution authority, workspaces, checkpoints, and
evidence belong to APO's persistent local storage and registered repositories. Temporary chat/test
sandboxes are never canonical project state. Do not persist unrestricted environment variables,
raw provider transcripts, credentials, or private chain-of-thought as recovery evidence.

## Jira and Work Items

GitHub Issues in `Hossam1104/AI_Orchestrator` are the active APO work tracker. Jira project `APO` is
historical provenance and a migration reference. `docs/BRD.md`, approved architecture decisions,
and repository evidence remain the governance source of truth. APO product integrations may still
support configured GitHub, Jira, Azure DevOps, and other tracker providers; that capability does not
change the active workspace tracker. Follow:

```text
GitHub Issue -> Sol contract -> TASK.md -> executor -> validation
-> independent review -> Sol acceptance -> GitHub/Jira synchronization where configured
```

Do not create duplicate Epics or speculative Stories. Work on one assigned item at a time. The old
numbered provider Session 04 sequence is legacy/superseded and must not be executed.

Current reference-derived roadmap identities are:

- APO-71 / #113 — Local Agent Readiness and executable provenance;
- APO-75 / #117 — HEAD-bound Project Intelligence Map;
- APO-72 / #114 — failure-classified retry and policy-driven fallback;
- APO-73 / #115 — isolated multi-model benchmarking;
- APO-74 / #116 — transparent historical-performance routing signals;
- APO-76 / #118 — shared-core Headless APO CLI;
- APO-77 / #119 — governed Schedule/Event Trigger Engine;
- APO-78 / #120 — governed ACP/A2A/MCP interoperability.

APO-52 / #90 and APO-54 / #92 remain the existing owners of workflow templates and decision/evidence
history; APO-55 / #93 remains the runtime/process/restart evidence owner.

## Delivery and Validation

Executor work must use a named branch, validate the assigned scope, update current state, commit,
push the branch, open or update one Draft PR against `main`, verify the exact branch/base state,
and leave the tree clean. Do not merge or push `main` unless a separate explicit finalization prompt
authorizes that action. Never force-push, hard-reset, or destructively clean. Preserve owner changes
and document any protected-branch limitation.

Documentation-only governance work must not claim source build/test validation it did not perform.
For implementation work, run appropriate restore/build/tests and review warnings, diff, secrets,
debug artifacts, and generated output honestly.
