# AGENTS.md - AI_Orchestrator (APO) Execution Contract

This is the universal execution contract for every AI model working in this repository.

**Repository:** https://github.com/Hossam1104/AI_Orchestrator
**Local Root:** `D:\AI Tools\Active Projects\AI_Orchestrator`
**Product:** AI_Orchestrator (APO)
**Previous Product Identity:** AI Usage Monitor, AI Project Orchestrator
**Primary Requirements:** `docs/BRD.md`
**Implementation Plan:** `docs/IMPLEMENTATION_PLAN.md`
**Strategic Roadmap:** `docs/STRATEGIC_ROADMAP.md`
**External Reference Roadmap Integration:** `docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md`
**Prompt Library:** `docs/SESSION_PROMPTS.md`
**Live Handoff:** `.ai/CURRENT_STATE.md`
**Current-State Addendum:** `.ai/CURRENT_STATE_ADDENDUM_2026-09-16.md`
**Jira Project:** `APO`

APO-20 renamed the GitHub repository and physical local folder to the current product identity.
Technical identifiers containing `AIUsageMonitor` remain intentionally unchanged and may be
migrated incrementally under planner approval.

---

# 1. AI Operating Model and Roles

The approved default strategy is quality/risk first and quota/cost second. Do not downgrade work
solely to preserve quota.

Canonical, detailed AI execution governance lives in:

- `.ai/AI_MODEL_ROUTING.md` — active providers, model portfolio, provider quota pools, quota
  states, shared cross-project quota location, task risk tiers, default routing, provider
  balancing, and the APO-specific risk appendix.
- `.ai/AI_EXECUTION_POLICY.md` — the universal `p` prompt gate, bounded implementation discipline,
  acceptance evidence, root-cause debugging method, context budget, tool policy, and local-PC
  execution/recovery/evidence rules.

The approved external-reference roadmap delta is maintained in
`docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md`; it extends APO incrementally and never replaces
existing architecture or authorizes implementation by itself.

This section states only the durable operating-model summary; do not duplicate the canonical files
here.

Active execution providers are **OpenAI/Codex and Anthropic/Claude only**. Gemini, Z.ai, GLM,
OpenCode, Kimi, and other external providers are not active orchestration executors unless the
repository owner explicitly changes this policy. This does not remove or weaken APO's own
product-domain support for *monitoring* other AI providers (§6) — that is separate product
functionality, not orchestration-executor policy.

| Priority | Model | Default role |
|---|---|---|
| 1 | GPT-5.6 Sol High | Planner / Architect / Model Router / Quota Governor / Acceptance Authority / Executor Prompt Authority (chat only) |
| 2 | GPT-5.6 Luna xHigh | Primary bounded implementation executor unless Sol explicitly routes elsewhere |
| 3 | Claude Sonnet 5 Medium | Fallback / special-need bounded implementation |
| 4 | Claude Sonnet 5 High | Fallback / special-need difficult bounded implementation |
| 5 | GPT-5.6 Luna Max | Exceptional implementation escalation only |
| 6 | Claude Opus 5 | Independent critical reviewer |
| 7 | GPT-5.6 Terra Medium/High | Specialist security/concurrency/data-integrity assurance; not default reviewer |
| 8 | Claude Haiku 4.5 | Disabled from active routing |

## Planner - GPT-5.6 Sol

Sol owns requirements interpretation, architecture, Jira decomposition, execution contracts,
model-routing policy, acceptance criteria, approved scope changes, and final acceptance. Sol
operates in chat mode only and must not become the routine Codex repository executor.

## Primary Executor - GPT-5.6 Luna xHigh

Luna xHigh is the normal primary executor for bounded repository work across routine, difficult,
cross-cutting, integration-sensitive, and high-blast-radius tasks. See `.ai/AI_MODEL_ROUTING.md`
for the detailed tier mapping and explicit Sol route rules.

## Fallback / Special-Need Executors - Claude Sonnet 5 Medium / High

Sonnet remains an active Claude-family option, but only Sol may explicitly select it for fallback,
quota balancing, task fit, or another special need. Sonnet Medium covers contained bounded work;
Sonnet High covers difficult or substantial isolated work. Neither is the automatic routine primary.

A failed executor is not permission for an executor or UI layer to self-route to another model.
Future automatic retry/fallback is governed by APO-72 and must use typed failure, verified
checkpoint, side-effect, capability, routing/owner policy, quota/capacity, and budget evidence.

## Disabled Model - Claude Haiku 4.5

Haiku is disabled from active routing. Do not assign it automatically or route repository work to it.

## Exceptional Escalation - GPT-5.6 Luna Max

Luna Max is exceptional escalation only and is never the normal executor.

## Independent Reviewer - Claude Opus 5

Opus performs independent review gates, used at critical checkpoints rather than routinely. Opus is
not the normal implementation executor and must remain independent from the implementation executor
by default. Reviewer mode does not add scope or implement fixes unless explicitly requested.

## Specialist Assurance - GPT-5.6 Terra

Terra is a risk-triggered specialist for security, trust boundaries, concurrency, authorization,
data integrity, credential boundaries, and destructive operations. Terra is not the default
reviewer or a general executor.

One assigned canonical GitHub APO Issue is the maximum active scope for one executor. No executor
may choose a different work item, combine unrelated Issues, or continue automatically.

---

# 2. Mandatory Startup Read Order

Before modifying repository files, use targeted authoritative retrieval, not blind full-file
rereading:

1. Always read completely: `AGENTS.md`, `.ai/AI_MODEL_ROUTING.md`, `.ai/AI_EXECUTION_POLICY.md`,
   and the active root `TASK.md`.
2. Always inspect: Git branch, status, remote state, the latest relevant boundary section of
   `.ai/CURRENT_STATE.md`, and the complete `.ai/CURRENT_STATE_ADDENDUM_2026-09-16.md` while that
   addendum remains the current planning delta.
3. Read the `docs/BRD.md` and `docs/IMPLEMENTATION_PLAN.md` sections relevant to the assigned work
   item; search for headings or semantic references before a whole-file read.
4. Read `docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md` when the assigned work touches agent
   readiness, runtime/process recovery, retry/fallback, workflow templates, evidence/history,
   benchmarking, or historical-performance routing.
5. Read the complete BRD only when architecture/requirements scope genuinely requires it, when
   conflicting authority requires it, or when the assigned task genuinely spans the whole product.
6. Read only the exact relevant section of `docs/SESSION_PROMPTS.md` when the work item references
   a prompt or review gate.
7. Inspect task-relevant source/configuration files. Do not reread an unchanged large file without
   a specific reason.
8. Prefer Serena for symbol/reference navigation over a full-file read or broad grep where
   applicable; use Context7 only under the conditional rule in `.ai/AI_EXECUTION_POLICY.md` §6.

Do not depend on previous chat context. The repository is the source of truth. This section governs
retrieval scope only; it does not weaken the authority order in §3.

---

# 3. Authority and Traceability

When instructions conflict, use this order:

1. `docs/BRD.md`;
2. `AGENTS.md`;
3. planner-approved architecture decisions, `.ai/CURRENT_STATE.md`, and the current dated
   current-state addendum;
4. the assigned canonical GitHub APO Issue and its accepted planning/architecture scope;
5. `docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md` for the approved reference-derived capability
   delta when relevant;
6. `docs/IMPLEMENTATION_PLAN.md`;
7. the root `TASK.md`; and
8. executor preference.

GitHub Issues in `Hossam1104/AI_Orchestrator` are the active work-tracking system for APO.
Repository documentation, the BRD, approved architecture decisions, and validation evidence remain
the architecture/governance source of truth. Jira project `APO` at `hossamsqa.atlassian.net` is
historical provenance and a migration reference; it must not override the BRD or approved
architecture.

The required execution flow is:

```text
GitHub APO Issue
      |
      v
Sol execution contract / architecture checkpoint
      |
      v
TASK.md current execution contract
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
GitHub Issue / repository synchronization
```

Every meaningful implementation and historical foundation must be traceable to a canonical GitHub
APO Issue with repository evidence. Stable APO keys and Jira provenance are preserved for migrated
records. Do not create speculative GitHub Issues unless the assigned planner scope explicitly
requires them.

---

# 4. TASK.md and Prompt-Library Lifecycle

`TASK.md` is the current executable work-item contract only when it explicitly contains an
assigned, approved task. It may instead explicitly record that the execution gate is closed; in
that state it is governance/status only and must not be treated as permission to execute. It must
never be used as a historical execution log. `.ai/CURRENT_STATE.md` plus the current dated addendum
contain factual history and live planning status.

`docs/SESSION_PROMPTS.md` is a permanent prompt library and historical record. The old numbered
AI Usage Monitor provider sequence is superseded and must not be treated as executable. Future APO
execution prompts are prepared by Sol only after GitHub decomposition and assignment and only after
the universal standalone lowercase `p` gate opens; do not pre-generate speculative implementation
prompts.

After a completed work item is delivered, Sol determines the next approved work item, prepares its
self-contained execution contract, replaces `TASK.md` when appropriate, commits/pushes that
preparation, and stops. Updating `TASK.md` never authorizes executing the next task automatically.

If work is partial or blocked, keep a remediation/recovery task and do not advance to a normal next
Story. A fresh user instruction is required to execute a newly prepared task, and the `p` prompt
gate still applies to generated executor/reviewer prompts.

---

# 5. Active V1 Architecture

The approved active foundation is:

- C#;
- .NET 10;
- WPF;
- MVVM;
- modular clean architecture;
- `System.Text.Json`;
- JSON for small state/configuration documents;
- monthly-partitioned JSONL for append-oriented history/events;
- `HttpClient` and resilience extensions where materially justified;
- dependency injection;
- Serilog;
- Windows Credential Manager or another planner-approved secure store;
- Git/GitHub/GitHub Actions;
- focused xUnit tests; and
- self-contained Windows release artifacts.

V1 has no mandatory database engine or ORM: no EF Core, SQL Server, LocalDB, or SQLite unless the
planner explicitly changes the architecture after evidence. Do not introduce Angular, Electron,
Tauri, Node.js, npm, embedded Chromium, or an APO-owned cloud backend without an explicit decision.
Historical WinUI/Windows App SDK and EF/SQL/LocalDB work remains historical/superseded context.

The intended dependency direction is:

```text
Desktop/WPF -> Application -> Domain
Infrastructure -> Application/Domain contracts
Providers/integrations -> Application/Domain contracts
```

Domain must not depend on WPF, JSON/file details, HTTP, provider libraries, filesystem APIs, or
Windows UI APIs. Application owns provider-independent contracts and use cases, not filenames,
LocalAppData paths, serializer configuration, or provider payload formats. Desktop consumes
application services/view models and must not parse provider payloads, inspect provider files, or
manage tokens. Infrastructure owns persistence, safe files, paths, credentials, logging, and OS
integration. Providers own detection, collection, parsing, normalization, and capability truth.

External-reference-derived work must extend these boundaries. Do not introduce duplicate agent
registries, runners, state machines, persistence stores, project registries, routing engines, or
provider checks in WPF.

---

# 6. Provider and Data Truthfulness

Initial monitored providers are Codex, Claude, Kimi, GitHub Copilot, and Antigravity. Their behavior
must be verified before implementation. Prefer, in order:

1. official API;
2. official OAuth/device/account connection;
3. official authenticated account/usage endpoint;
4. official CLI where useful;
5. safe verified local metadata; and
6. manual fallback.

Never guess endpoints or file schemas, assume plan/billing/reset fields, fabricate quota windows,
or label inferred values official. If unavailable, use truthful states such as Not Available,
Manual, Partial, Authentication Required, Stale, or Unsupported.

The primary UI convention is remaining capacity. Prove whether source values mean used or remaining,
normalize exactly once, and test source-to-domain transformation. Never interpret `used_percent =
80` as 80% available. Use arbitrary dynamic windows rather than fixed quota columns. Preserve
`DateTimeOffset` source offsets, calculate safely, display in the user's Windows timezone, and
never guess an unknown reset timezone.

Refresh failures retain last-known valid values and mark them stale/error. One provider failure
must not crash or hide other providers. A provider CLI is optional and never a whole-application
prerequisite; browser-only and non-developer fallback paths must be truthful.

For local Agent Readiness, keep installation, executable resolution/provenance, CLI version,
authentication, entitlement/subscription, capabilities, freshness, and quota/capacity as distinct
facts. One fact must never be inferred from another without independent evidence.

---

# 7. Cross-Windows Compatibility Contract

Baseline goal: Windows 10 version 1809/build 17763 and Windows 11, with x86, x64, and ARM64
targets. x64 is primary validation, followed by x86 and ARM64. Exact .NET 10/WPF support claims
must be verified against current Microsoft documentation before release claims.

Mandatory rules:

1. Guard Windows 11-only or post-build-17763 APIs with runtime capability/version detection.
2. Keep domain, application, providers, persistence, analytics, recommendation, monitoring, and
   alerts independent of newer Windows features.
3. Optional visual effects must degrade to ordinary WPF surfaces with identical functionality.
4. Do not require AVX/AVX2, a dedicated/recent GPU, TPM, NPU, AI accelerator, or recent CPU.
5. Keep background operation asynchronous, low-CPU, and low-memory.
6. Preserve functional parity for capacity, subscriptions, history, alerts, notifications, tray,
   Focus HUD, analytics, and recommendations without modern GPU functionality.

Before adopting a package, native component, API, or framework, check the minimum OS and
self-contained consumer contract. Record incompatible dependencies and escalate to Sol; do not
silently raise the minimum.

---

# 8. Local Persistence and Security Contract

Mutable data is stored below `Environment.SpecialFolder.LocalApplicationData`, approximately
`%LOCALAPPDATA%\AIUsageMonitor\` until a separately approved migration. JSON documents contain
settings, provider/project state, subscriptions, alert/routing policy, and other small state.
Monthly JSONL contains usage snapshots, orchestration/audit events, validation/review events, and
alerts.

Every long-lived JSON document has an explicit schema version. JSONL records include record/schema
metadata. Critical writes serialize to a temporary file, flush where practical, and replace the
destination atomically where supported. Concurrent writes use the smallest per-store/append
synchronization; no distributed locks.

History reads stream only relevant partitions, preserve chronological ordering, support ranges, and
do not load all lifetime history at startup. Preserve material-change duplicate suppression.
Distinguish missing, empty, valid, unsupported-schema, corrupt, I/O-failure, and permission-failure
states. Report and isolate/quarantine safely where useful. Never silently destroy user data, write
beside the executable or under Program Files, or require administrator privileges.

APO is local-PC-first for normal local software-delivery work. The owner's persistent registered
repositories, APO LocalAppData state, APO-managed workspaces, durable execution authorities,
checkpoints, and evidence are authoritative. Temporary chat sandboxes, provider temp files, or test
harness directories are never canonical project state.

Never store passwords, raw tokens, refresh tokens, cookies, authenticated payloads, prompts,
conversations, source code, repository content, or unrelated credentials in files, logs, or source
control. JSON may store only an opaque credential reference. Use Windows Credential Manager, DPAPI
when justified, or another planner-approved secure mechanism. Redact secrets from diagnostics.

Where environment fingerprinting is needed for reproducibility/recovery, capture only an explicit
non-secret allowlist such as OS/version, architecture, APO version, selected CLI version, Git
version, registered project path/branch/commit, working directory, and selected tool/runtime
availability. Never persist unrestricted environment variables.

APO may transmit only the context and source/code reasonably required by an explicitly configured
executor. Honor project isolation, exclusions, secret scanning/redaction, least privilege, and
visible destination. APO-owned telemetry and cloud sync are not enabled by default.

Operational evidence should store observable decisions, assumptions/limitations, commands/tool
results where safe, process/Git/validation/review evidence, timing, failure classification, and
final disposition. Private hidden chain-of-thought is not an execution artifact requirement.

---

# 9. UI, Monitoring, and Release Contract

The product is a modern AI command center for developers and non-developers: dark-first with
light/system support, strong typography, restrained gradients, rounded cards, provider accents,
accessible labels/icons, readable remaining capacity, reset countdowns, polished loading/empty/
stale/partial/error states, responsive resizing, keyboard navigation, high DPI support, and
restrained motion. Color alone must not communicate state. A lightweight tray and optional Focus
HUD may provide glanceable status beside an IDE.

Future owner-facing orchestration UX should expose Agent Readiness, current execution/process state,
recoverability, evidence freshness, retry/fallback disposition and reason, execution timeline, and
benchmark comparison only through Application/read-model contracts. WPF must not probe processes,
PATH, provider sessions, or routing policy directly.

Monitoring defaults to approximately 60 seconds plus startup, resume, foreground, manual refresh,
and safe local-change triggers. Use cancellation, timeout, retry/backoff, throttling, rate-limit
awareness, provider isolation, and no overlapping refresh. Monitoring must not meaningfully consume
provider quota. Default warnings are 30% remaining and critical 15%, with deduplicated alerts for
low/exhausted/restored capacity, stale/auth failure, and renewal/expiry.

The released application must be self-contained and must not require a separately installed .NET
runtime, SDK, Visual Studio, database engine, Node/npm, embedded browser, or provider CLI. Evaluate
`SelfContained=true` and `PublishSingleFile=true` for WPF without blind trimming. Consider
`win-x64`, `win-x86`, and `win-arm64`, and claim only validation actually performed. The app must
open with no providers, empty history, missing optional state, offline status, absent CLI, or
isolated provider failure.

---

# 10. Testing and Validation Contract

Target tests at high-risk logic:

- dynamic quota normalization and used/remaining conversion;
- reset and timezone math;
- provider parsers and source semantics;
- subscription and billing interpretation;
- JSON round trips and schema compatibility;
- JSONL append/read/range ordering;
- duplicate suppression and interrupted-tail behavior;
- atomic/safe writes and missing/corrupt/unsupported-file handling;
- settings/provider/subscription/alert persistence;
- credential-reference safety and secret redaction;
- routing, burn rate, recommendations, state transitions, and gates;
- local Agent Readiness executable provenance/version/auth/freshness truth;
- process-tree cancellation, termination-unconfirmed, stale PID, and restart reconciliation;
- checkpoint/authority anti-replay across failure and retry;
- retry/fallback decision correctness for authentication, quota, capability, validation,
  workspace/source, security, approval, and exhausted-budget failures;
- isolated benchmark workspace/candidate independence and absence of delivery side effects;
- deterministic historical-routing replay, sparse/stale evidence, and owner override;
- critical WPF view-model behavior; and
- self-contained publish smoke checks.

No live authenticated provider calls or credentials in CI. Use sanitized fixtures. Documentation-only
work must not claim build/test results for an architecture it did not implement. Every executor must
run validation proportionate to the assigned work, review warnings and diff, inspect secrets and
generated artifacts, and update `.ai/CURRENT_STATE.md` and the current dated addendum when its
planning delta is affected.

---

# 11. GitHub APO Issue and Work-Item Discipline

GitHub Issues carrying stable APO keys are the operational work-tracking authority. The approved
initial Epic structure is APO-1 through APO-17 as listed in `docs/BRD.md` and
`docs/IMPLEMENTATION_PLAN.md`; migrated Jira project `APO` remains historical provenance.
Sol progressively decomposes Epics into canonical GitHub Stories/Tasks and backfills historical
implementation with evidence.

Reference-derived canonical roadmap identities currently include:

- APO-71 / #113 — Local Agent Readiness and executable provenance;
- APO-75 / #117 — HEAD-bound Project Intelligence Map;
- APO-72 / #114 — failure-classified retry and policy-driven fallback;
- APO-73 / #115 — controlled multi-model benchmarking in isolated workspaces;
- APO-74 / #116 — transparent historical-performance routing signals;
- APO-76 / #118 — shared-core Headless APO CLI;
- APO-77 / #119 — governed Schedule/Event Trigger Engine;
- APO-78 / #120 — governed ACP/A2A/MCP interoperability.

Existing APO-52 / #90, APO-54 / #92, and APO-55 / #93 own workflow templates, historical evidence
browsing/decision chronology, and live runtime/process/restart evidence respectively. Do not create
duplicate systems for those capabilities.

Do not create duplicate Epics or speculative Stories during an assigned Story unless instructed.
Do not start source refactoring, a provider, runtime, routing engine, or another Epic early. Use one
bounded work item at a time. When an Issue is complete, synchronize the GitHub Issue/repository
state and any explicitly configured external tracker, then stop at the planner boundary.

---

# 12. Git Delivery Contract

Every completed executor implementation or governance/remediation session must:

1. inspect status and preserve unrelated changes;
2. work on an appropriately named branch such as `refactor/APO-18-product-governance-rebaseline`;
3. validate the assigned scope and review the diff/secrets;
4. update `.ai/CURRENT_STATE.md` and any current authoritative addendum required by the work;
5. commit the completed work;
6. push the session branch to `origin`;
7. open or update a Draft pull request against `main`; and
8. leave the working tree clean.

The implementation executor **stops at this planner boundary and does not merge automatically**.
GPT-5.6 Sol performs exact-head review of the pushed branch/PR. A merge into `main` may occur only
under a separate, explicitly authorized prompt directing that specific bounded merge/finalization
action after Sol acceptance; an ordinary implementation or remediation prompt never integrates into
or pushes `main` itself.

Never use force push, `git reset --hard`, or `git clean -fd` unless explicitly instructed for that
specific action. Preserve unrelated owner changes. Protected-branch and human-approval rules take
precedence; if direct integration is blocked, use the supported PR/merge workflow and document the
restriction. A single post-merge metadata-only synchronization commit is permitted for current
state/task metadata when required to record an already Sol-authorized and completed merge.

---

# 13. Scope Control

Do not add AI chat, unrelated productivity features, mobile/cloud sync, payment workflows, team
billing, prompt/conversation collection, browser-cookie extraction, unsafe credential access,
speculative provider endpoints, database/ORM runtime, Angular/Electron/Node/embedded browser, or
new providers without explicit planner approval.

External references are idea sources only. Do not copy their code, permission bypasses, shell
runner architecture, provider-specific fallback sequences, stage-index recovery semantics, private
reasoning memo requirements, or shared mutable benchmark workspaces into APO.

For APO-18 specifically, do not implement product functionality, providers, orchestration runtime,
model routing, Jira/GitHub adapters, source-code refactoring, namespace/project renaming, LocalAppData
migration, WPF redesign, or speculative Jira Stories. APO-18 is the product/governance consolidation
boundary and ends at the Sol planner checkpoint.

---

# 14. Executor Completion Report

End every executor work item with:

```text
Work item:
Status: COMPLETE / PARTIAL / BLOCKED

Implemented:
- ...

Validated:
- ...

Not validated:
- ...

Blockers / limitations:
- ...

Files/areas changed:
- ...

CURRENT_STATE updated: Yes

Next planner boundary:
- ...
```

Do not claim a work item complete when required work remains or validation was not performed.

---

# 15. Reviewer Rules

Opus review severity is:

- **BLOCKER** - unsafe, fundamentally wrong, or prevents progression;
- **HIGH** - major correctness, security, or reliability issue;
- **MEDIUM** - important but safely deferrable; and
- **LOW** - minor maintainability or polish issue.

Reviewers inspect actual code and evidence, not executor summaries. Required review gates must
explicitly inspect the zero-prerequisite consumer contract, cross-Windows compatibility, provider
truthfulness, dynamic used/remaining semantics, file-persistence integrity, credential security,
project isolation, human approval gates, process/restart evidence where applicable, isolated
benchmark/workspace boundaries where applicable, and self-contained release evidence.

The core product standard is trust: an individual should be able to glance at APO and decide which
paid AI service or model has sufficient remaining capacity and which project work can safely
continue. Accuracy, evidence, and explicit uncertainty outrank visual symmetry.

---

# 16. Application Runtime Contract

Fresh run/open/launch semantics are canonicalized in `.ai/AI_EXECUTION_POLICY.md` §2.1 and
implemented by `scripts/Run-FreshDesktop.ps1`.

Originally established by the APO-37 SOL-37-01..05 remediation (Prompt 4/5) as a leave-running
default; superseded by the governance remediation recorded in `.ai/CURRENT_STATE.md` to a
stop-by-default rule. This rule is permanent and applies to every future local prompt in this
repository — implementation, remediation, review, merge, or planning — that has access to this
local machine, regardless of which model or role is executing.

**Default rule:** after completing the assigned work for a prompt, the APO application must be
**stopped** unless the owner's instruction for that specific prompt explicitly says to leave it
running.

1. Detect any already-running APO instance before touching processes; stop an existing instance
   only if it actually blocks required work, never gratuitously.
2. If the assigned work requires launching/publishing/running APO to validate the change, launch it
   only for as long as needed to verify the process is alive, the main window/shell state is
   normal/non-degraded, and the change is genuinely usable — not just process-alive.
3. Stop the application before the prompt ends. Do not leave it running by default.
4. Never create a duplicate/orphaned process: reuse or replace an existing instance rather than
   stacking a second one, and never leave a child process behind.
5. Report the literal lines `APO PROCESS COUNT = 0` and `APPLICATION LEFT RUNNING = NO` in the
   completion report.
6. An explicit owner instruction for that specific prompt may override this default and request the
   application be left running; in that case report the executable path, process ID, window title,
   normal-or-degraded state, and the literal line `LEFT RUNNING = YES` instead.
7. If startup is unsafe/impossible, or a required stop cannot be completed, state the exact blocker
   truthfully instead of fabricating either runtime claim.

---
