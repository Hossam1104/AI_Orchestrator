# AI_MODEL_ROUTING.md — Canonical AI Execution Routing Policy

This file is the canonical, cross-project source of truth for AI execution routing, model
portfolio, and provider quota governance. `AGENTS.md` and `CLAUDE.md` reference this file rather
than duplicating it. It governs **AI development/execution tooling**, not APO's product-domain
provider-monitoring functionality (see AGENTS.md §6).

---

## 1. Control Plane

**GPT-5.6 Sol** is the control plane: planner, architect, model router, quota governor, and
acceptance authority. Sol operates in **chat mode only** and must not become the routine Codex
repository executor.

---

## 2. Active Execution Providers

Only two providers are active for AI-assisted execution in this repository and this development
environment:

- **OpenAI / Codex**
- **Anthropic / Claude**

No other provider (Gemini, Z.ai, GLM, OpenCode, Kimi, or any other) is an active execution
provider unless the repository owner explicitly changes this policy. This does not remove or
weaken APO's own product-domain support for monitoring other AI providers — that is separate
product functionality, not orchestration-executor policy.

---

## 3. Model Portfolio

| Model | Provider pool | Default role |
|---|---|---|
| GPT-5.6 Sol High | OpenAI/Codex | Planner / Architect / Model Router / Quota Governor / Acceptance Authority / Executor Prompt Authority (chat only) |
| GPT-5.6 Luna xHigh | OpenAI/Codex | Primary bounded implementation executor unless Sol explicitly routes elsewhere |
| Claude Sonnet 5 Medium | Anthropic/Claude | Fallback / special-need bounded implementation |
| Claude Sonnet 5 High | Anthropic/Claude | Fallback / special-need difficult bounded implementation |
| GPT-5.6 Luna Max | OpenAI/Codex | Exceptional implementation escalation only |
| Claude Opus 5 Medium/High | Anthropic/Claude | Independent critical reviewer (not routine executor) |
| GPT-5.6 Terra Medium/High | OpenAI/Codex | Specialist security/concurrency/data-integrity assurance |
| Claude Haiku 4.5 High | Anthropic/Claude | Disabled from active routing |

### Role detail

- **Haiku** - disabled from active routing. Historical references may describe its former
  reconnaissance/mechanical role, but Sol must not route work to it under the current policy.
- **Sonnet Medium** - fallback/special-need implementation when Sol explicitly selects it for a
  bounded task, including ordinary bugs, mappings, validators, API/UI/WPF work, tests, and bounded
  refactors.
- **Sonnet High** - fallback/special-need difficult bounded debugging, substantial isolated
  implementation, integration behavior, or non-trivial regressions when Sol explicitly selects it.
- **Luna xHigh** - the normal primary executor for bounded repository work, including routine,
  architecture-sensitive, cross-cutting, persistence/state, concurrency-sensitive, integration,
  and high-blast-radius implementation. Sol may explicitly route a task elsewhere when justified.
- **Luna Max** — exceptional escalation only; never the default executor.
- **Opus** - independent critical review only, roughly every seventh substantial implementation
  prompt where review adds meaningful value, or at genuinely critical checkpoints. Not for routine
  coding and not a fallback implementation model. Any Opus execution session is an explicitly
  authorized exception granted by the owner for a named scope, and does not change this default.
- **Terra** - specialist assurance for security, trust boundaries, concurrency, authorization, data
  integrity, credential boundaries, destructive operations. Not a general executor.

One assigned canonical GitHub APO Issue remains the maximum active scope for one executor.

### Execution share targets

These are the owner's current targets for how routed execution work is distributed. They are
planning guidance for Sol, not a quota to be spent: an under-used ceiling is never a reason to
route work to a model, and §6's priority order still decides every individual route.

| Model | Target share of routed execution |
|---|---|
| GPT-5.6 Sol | ~0% — control plane only; chat mode, no repository execution |
| GPT-5.6 Luna xHigh | ~60% — the normal primary executor |
| GPT-5.6 Terra High | up to ~20% — specialist assurance only, within §7 risk areas |
| Claude Sonnet 5 | up to ~10% — Sol-selected fallback / special need |
| Claude Opus 5 | up to ~10% — independent review, roughly every seventh substantial prompt |

Opus is review-only by default. An Opus session that changes repository files requires an explicit,
named, owner-granted authorization for that scope; such an authorization is a one-off exception and
must not be treated as establishing a new default route.

---

## 4. Provider Quota Pools

Two shared executor quota pools:

- **OpenAI / Codex pool** — Luna xHigh, Luna Max, Terra Medium, Terra High, other Codex agentic
  execution.
- **Anthropic / Claude pool** - Sonnet and Opus; Haiku is disabled from active routing.

Luna → Terra is **not** provider diversification (same pool). Sonnet → Opus is **not** provider
diversification (same pool). Provider-level routing, not model-level routing, is what matters for
quota balancing.

---

## 5. Quota States

- `GREEN` — > ~60% remaining
- `AMBER` — ~35–60% remaining
- `RED` — ~15–35% remaining
- `CRITICAL` — < ~15% remaining
- `UNKNOWN` — exact quota unavailable

Exact percentages are used only when obtained from a provider UI/CLI, explicit user input, or
another trustworthy local source. Never invent a percentage.

### Shared cross-project quota location

`%USERPROFILE%\.ai-orchestration\` (outside any Git repository) holds:

- `quota-state.json` — current qualitative/quantitative state per provider pool.
- `execution-ledger.jsonl` — append-only non-secret operational history (timestamp, project, work
  item, provider, model, effort, tier, result, qualitative quota before/after, fallback/escalation,
  duration where known).

Never store passwords, tokens, cookies, API keys, credentials, prompts, source code, or transcripts
in either file. Never overwrite valid existing operational history.

---

## 6. Task Risk Tiers and Default Routing

| Tier | Description | Executor |
|---|---|---|
| 0 | Sol planning only: architecture, decomposition, acceptance criteria, route selection, review, discussion, Jira reasoning, executor-prompt preparation after the `p` gate | GPT-5.6 Sol High, chat mode only — no repository execution |
| 1 | Mechanical/reconnaissance: repository reconnaissance, file discovery, deterministic documentation, evidence formatting, diff/log triage, low-risk repetitive edits | GPT-5.6 Luna xHigh primary; Sol-selected Claude Sonnet 5 Medium fallback/special need; Haiku disabled |
| 2 | Normal bounded implementation: ordinary bugs, CRUD, DTOs, mappings, validators, routine API/UI/WPF work, routine tests, CI fixes, bounded refactors | GPT-5.6 Luna xHigh primary; Sol-selected Claude Sonnet 5 Medium fallback/special need |
| 3 | Difficult/substantial bounded execution with dynamic task fit | GPT-5.6 Luna xHigh primary; Sol-selected Claude Sonnet 5 High fallback/special need |
| 4 | High-risk authority/security/integrity execution: credentials, authorization, trust boundaries, tenant isolation, financial integrity, concurrency correctness, destructive operations, remote execution, execution authority, immutable authority/persistence, replay protection, dangerous workspace/Git mutation, data-loss risk, autonomous execution boundaries | GPT-5.6 Luna xHigh primary, with Terra Medium/High and/or Opus Medium/High assurance as Sol requires |

Luna xHigh is the normal primary executor baseline for Tiers 1–4. Sonnet is never an automatic
primary route; it is used only when Sol explicitly selects it for fallback, quota balancing, task
fit, or another special need. Haiku is disabled and has no active tier default. Luna Max remains
exceptional escalation only.

### Tier 3 selection rule

Tier 3 is not an automatic Sonnet path. Luna xHigh is primary. Sol may explicitly select Claude
Sonnet 5 High for difficult but bounded debugging, substantial isolated multi-file implementation,
a larger isolated feature, or a contained regression when that is the best fit. Sol's selection is
based on architecture sensitivity; cross-cutting scope; persistence/state complexity; concurrency
sensitivity; integration complexity; regression blast radius; provider quota state (§4–§5); and
expected model capability for the specific task. Luna Max remains an exceptional escalation beyond
Tier 3/4, never a default.

Independent review (Opus) and specialist assurance (Terra) are applied on top of the tier, not in
place of it, per the criteria in §3.

### Routing priority order and provider balancing rule

Route selection among candidate executors follows this exact priority order:

1. **Correctness**
2. **Security / required assurance**
3. **Required capability and risk tier**
4. **Provider quota health**
5. **Cost / cheapest capable model**

Quota health informs Sol's explicit selection among already-capable candidates. It must not
silently replace the Luna xHigh primary baseline, activate Haiku, or create an automatic Sonnet
route. Quality and risk come before quota preservation — never downgrade tier solely to preserve
quota.

If, after correctness, security/required assurance, required capability/risk tier, and provider
quota health have all been applied, more than one candidate model remains equally valid, select
the **cheapest capable model** among them. This cost tie-break operates only within a candidate
set that is already safe and sufficiently capable: it must never reduce required capability, risk
tier, or reasoning/effort below what the task requires, and must never justify routing to a
weaker, lower-tier, or non-§2 provider merely because it is cheaper. Cost never overrides
correctness, security, required specialist assurance, architecture sensitivity, required risk
tier, required capability, or any explicit project-specific routing requirement.

---

## 7. APO-Specific Risk Appendix

The following APO areas are elevated risk (normally Tier 3 or Tier 4 depending on blast radius) and
require commensurate executor selection and, where appropriate, Opus/Terra review:

- routing authority and selected-agent execution truth;
- persisted execution plans and immutable execution authority;
- replay protection and recovery checkpoints;
- workspace isolation;
- process execution and process termination;
- residual/non-cooperative execution;
- credential boundaries;
- remote execution;
- provider connection-mode drift;
- schema/integrity evidence;
- destructive Git/workspace operations;
- autonomous background execution.

Lower-risk normal-volume APO work (Tier 1–2) includes documentation, deterministic evidence
formatting, straightforward WPF presentation, isolated DTO/view-model mapping, mechanical tests,
simple UI polish, and bounded cleanup.

Project risk overrides generic routing: a nominally small change in a high-risk area above is
routed at Tier 3/4, not by size alone.

---

## 8. Required Sol Route Declaration

After the prompt gate (see `.ai/AI_EXECUTION_POLICY.md`) opens, Sol's generated executor/reviewer
prompt must declare: the assigned canonical GitHub APO Issue, the selected model/role/effort, the provider
pool, the current quota state used to inform (not override) the choice, and the risk tier
justification.

---

## 9. Final Acceptance

Sol remains the final acceptance authority for all routed work, regardless of which executor
performed it. Executor self-declaration is not final acceptance (see
`.ai/AI_EXECUTION_POLICY.md`).
