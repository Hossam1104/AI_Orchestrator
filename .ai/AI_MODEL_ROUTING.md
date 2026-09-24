# AI_MODEL_ROUTING.md — Canonical AI Execution Routing Policy

This file is the canonical, cross-project source of truth for AI execution routing, model
portfolio, and provider quota governance. `AGENTS.md` and `CLAUDE.md` reference this file rather
than duplicating it. It governs **AI development/execution tooling**, not APO's product-domain
provider-monitoring functionality (see AGENTS.md §6).

---

## 1. Control Plane

**GPT-5.6 High in Chat (Sol)** is the control plane: planner, architect, model router, quota governor, and
acceptance authority. Sol operates in **chat mode only** and must not become the routine Codex
repository executor.

---

## 2. Active Execution Providers

Only two providers are active as the default APO repository-execution providers in this repository
and development environment:

- **OpenAI / Codex**
- **Anthropic / Claude**

Gemini may be used only as an explicitly owner-authorized auxiliary resource for low-risk or
mechanical cross-project work when appropriate. It is not part of APO's default executor portfolio,
does not change the routing share targets below, and does not authorize a Gemini APO V1 runtime
adapter or executable-provider implementation. Z.ai, GLM, OpenCode, Kimi, and other external
providers remain inactive unless the repository owner explicitly changes this policy. This does not
remove or weaken APO's own product-domain support for monitoring other AI providers — that is
separate product functionality, not orchestration-executor policy.

---

## 3. Model Portfolio

| Model | Current development role |
|---|---|
| GPT-5.6 High in Chat (Sol) | Persistent planner, architect, project-state, requirements, acceptance, governance, and routing authority; 0% normal implementation |
| GPT-6 Luna Reasoning xHigh in Codex | Default substantial executor |
| Claude Sonnet 5 Effort HIGH | Difficult recovery/surgical finalization and bounded isolated bug fixes |
| Claude Opus 5.5 Effort HIGH | Independent critical review; exceptional owner-authorized Grand Master Cleaning |
| GPT-6 Sol Reasoning High in Codex | Emergency/exceptional direct Codex implementation |
| Gemini 3.8 Flash Reasoning HIGH | Explicitly delegated low-risk mechanical auxiliary |

GPT-5.6 Terra is retired. These development roles do not rename persisted APO product-under-test model identities such as `gpt-5.6-sol` and `gpt-5.6-luna`. Historical model records remain historical facts. No APO work is routed while the 2026-09-24 owner hold is active.

The former share targets and old Luna Max/Haiku/Terra routes are superseded. Sol routes by correctness, security, capability, provider quota health, then cost. Sonnet is Sol-selected, never automatic fallback. Opus is independent by default. One canonical GitHub APO Issue is the maximum active scope for an executor.

---

## 4. Provider Quota Pools

Two shared provider quota pools remain: OpenAI/Codex (GPT-6 Luna, exceptional GPT-6 Sol) and Anthropic/Claude (Sonnet 5, Opus 5.5). Gemini 3.8 Flash is auxiliary only when explicitly delegated. Provider-level balancing never overrides capability or the owner hold.

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
| 0 | Planning, architecture, acceptance, route selection, prompt preparation after the `p` gate | GPT-5.6 High in Chat (Sol) |
| 1 | Mechanical/reconnaissance/documentation | GPT-6 Luna primary; Gemini 3.8 Flash only when explicitly delegated |
| 2 | Normal bounded implementation | GPT-6 Luna primary; Sonnet 5 HIGH for Sol-selected isolated bug fixes |
| 3 | Difficult bounded implementation/recovery | GPT-6 Luna primary; Sonnet 5 HIGH for Sol-selected difficult recovery/finalization |
| 4 | High-risk authority/security/integrity execution | Sol selects a capable bounded executor and Opus 5.5 review where required; GPT-6 Sol direct Codex only exceptionally |

No route is executable while the owner hold is active.

### Routing priority order and provider balancing rule

Route selection among candidate executors follows this exact priority order:

1. **Correctness**
2. **Security / required assurance**
3. **Required capability and risk tier**
4. **Provider quota health**
5. **Cost / cheapest capable model**

Quota health informs Sol's explicit selection among already-capable candidates. It must not
silently replace the GPT-6 Luna primary baseline or create an automatic Sonnet
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
require commensurate executor selection and, where appropriate, Opus review:

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

---

## 10. Retry, Benchmarking, and Historical-Performance Routing

The external-reference reconciliation adds future routing behaviors without changing the current
priority policy. The development portfolio above supersedes the former model list.

### 10.1 Failure-classified fallback

A failed executor does **not** automatically imply "try another model". Any future fallback must be
derived from typed failure classification, the latest verified checkpoint, observed side effects,
remaining execution budget, current agent readiness, routing policy, owner policy, and quota/capacity
truth.

Eligible fallback candidates must pass the same normal routing requirements as an initial route.
A model switch must never bypass source/workspace reconciliation, authentication, security,
approval, validation, anti-replay, or destructive-action boundaries.

This behavior is tracked by APO-72 / GitHub Issue #114.

### 10.2 Controlled benchmarking

Future parallel model comparison is an explicit benchmark mode, not ordinary production routing.
Each write-capable candidate receives a separate isolated workspace and execution authority. No
benchmark candidate may affect another candidate's workspace, protected delivery, or production
state by default.

Benchmark results are evidence only. They do not automatically change the production routing policy.
This behavior is tracked by APO-73 / GitHub Issue #115.

### 10.3 Historical performance signal

After APO has sufficient durable execution/validation/review/acceptance evidence, Automatic routing
may use transparent historical performance as an additional advisory signal. The first version must
be deterministic and explainable, not opaque ML.

Historical evidence can only operate **after** correctness, security, required capability/risk, and
explicit project/owner policy have established eligibility. It cannot activate prohibited models,
weaken assurance, or override current authentication/entitlement/capacity truth.

Each historical metric must expose cohort, sample size, freshness, provenance, and limitations.
Sparse/stale evidence remains unavailable rather than being invented into a score.

This behavior is tracked by APO-74 / GitHub Issue #116.

### 10.4 Product roadmap vs current execution policy

APO-71 through APO-74 are product-roadmap capabilities. They do not authorize changing the owner's
current development-time model allocation or the standalone `p` prompt gate. The approved roadmap
integration is recorded in `docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md`.
