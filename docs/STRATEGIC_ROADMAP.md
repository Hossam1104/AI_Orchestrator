# AI_Orchestrator — Strategic Orchestration Roadmap

**Current authority (2026-09-24):** AI_Orchestrator is OWNER-PAUSED / ON HOLD until Hossam explicitly resumes it. APO-70 / Issue #111 remains the sole logical gate; PR #112 remains Draft/Open/Unmerged. Owner visual acceptance is PASS; owner Desktop functional acceptance is NOT COMPLETE. No implementation or acceptance run is authorized while paused. See [current state](../.ai/CURRENT_STATE.md#project-status--owner-paused--on-hold) and [resume boundary](../.ai/CURRENT_STATE.md#resume-here). Dated evidence below retains its historical meaning.

**Status:** ACTIVE ROADMAP
**Last reconciled:** 2026-09-24 (owner hold)
**Product:** AI_Orchestrator (APO)
**Active tracker:** GitHub Issues in `Hossam1104/AI_Orchestrator`
**Historical tracker:** Jira project `APO`

## 1. Purpose and authority

This roadmap is the concise strategic bridge between `docs/BRD.md`,
`docs/IMPLEMENTATION_PLAN.md`, the canonical GitHub APO Issues, the current source/evidence, and the
owner experience APO is being built to provide.

It is a planning authority, not an implementation prompt. A roadmap item never authorizes source
changes, model execution, merge, release, or deployment. The universal standalone lowercase `p`
prompt gate in `.ai/AI_EXECUTION_POLICY.md` remains mandatory for generated executor/reviewer
prompts.

The 2026-09-16 external-reference integration is part of this roadmap. Detailed rationale,
architecture ownership, rejected patterns, and acceptance direction are recorded in
`docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md`.

## 2. Current authoritative status

The accepted FAST V1 foundation remains preserved. GitHub Issues are the operational tracker;
Jira is historical provenance. Accepted capabilities include the major control-plane, routing,
workspace, bounded execution, tracker/evidence, validation, review/remediation, approval,
controlled-delivery, Mission Control foundation, and GitHub Actions CI slices recorded in the
repository/current-state history.

The sole active implementation/recovery gate is:

**APO-70 / GitHub Issue #111 — Recover V1 Desktop UX and Complete Functional Surfaces**

Current APO-70 product/runtime truth:

- branch: `feature/APO-70-v1-desktop-product-recovery`;
- PR #112: Draft / Open / Unmerged;
- base `main`: `d7c1231df9ea1a4d008aa473210d0ccc735c0302` at the pre-planning baseline;
- source/runtime validation baseline before the 2026-09-16 planning-only commits:
  `14d8d470d63c17e726278cb0336e737b4fdc5022`;
- exact-head PR CI for that source/runtime baseline: `34987372556`, success;
- canonical suite at that baseline: **1,406 passed / 0 failed / 0 skipped**;
- Release build: **0 warnings / 0 errors**;
- self-contained publish validation: PASS for `win-x86`, `win-x64`, and `win-arm64`;
- real Sol production-composed `PrepareAsync` reached `Ready`;
- routing naturally selected the real Luna configuration;
- persisted Ready rehydrates across coordinator/process restart;
- cross-project cached-Ready isolation is enforced;
- durable single-consumption/anti-replay protects the immutable Ready input authority;
- backend cross-process proof is **SOL-ACCEPTED / COMPLETE** on source head
  `a2719257e6dc2b276152f35aa34ad945a912a20a`;
- exactly one real `gpt-5.6-luna` `StartAsync` executed after fresh-process Restore;
- prepared workspace mutation and original-source isolation passed;
- fresh-process anti-replay passed with no duplicate execution authority;
- owner visual acceptance is PASS; Desktop functional acceptance and final exact-head Sol acceptance remain pending.

The branch now also contains documentation-only roadmap/governance commits after the source/runtime
baseline above. Therefore future exact-head validation must use the live branch HEAD and must not
reuse the older source SHA as proof for the newer documentation head.

The live planning delta is summarized in `.ai/CURRENT_STATE_ADDENDUM_2026-09-17.md`.

## 3. Product experience we are building

APO is the owner's local-first engineering control plane. The normal lifecycle is:

```text
Discover registered local project
        ↓
Reconcile current project / repository / work state
        ↓
Resolve local Agent Readiness and environment truth
        ↓
Plan — versioned Sol execution contract
        ↓
Route — quality/risk/policy first, quota/cost later
        ↓
Resolve permissions / human gate requirements
        ↓
Prepare isolated workspace + authoritative checkpoint
        ↓
Execute through the selected local provider adapter
        ↓
Capture runtime / process / Git evidence
        ↓
Validate independently
        ↓
Review independently where required
        ↓
Remediate within bounded policy when required
        ↓
Revalidate / re-review
        ↓
Sol acceptance
        ↓
Owner approval where required
        ↓
Controlled Git / tracker delivery
        ↓
Persistent evidence manifest + next safe checkpoint
        ↓
Continue
```

The owner should not need to reconstruct project truth from old chats or manually copy every
intermediate prompt between models.

## 4. Architectural principles

1. **Local PC is authoritative for normal local development.** APO state, registered projects,
   workspaces, checkpoints, run authorities, and evidence live persistently on the owner's Windows
   PC. Temporary chat/test/provider sandboxes are not canonical project state.
2. **Existing APO architecture first.** Reference projects are idea sources, never replacement
   architectures.
3. **One authority per concern.** Do not create duplicate agent registries, routers, runners,
   orchestration state machines, project registries, evidence stores, or configuration systems.
4. **Clean dependency direction.** `Desktop/WPF -> Application -> Domain`; Infrastructure and
   Providers implement Application/Domain contracts.
5. **Provider truth is explicit.** Installation, executable resolution, authentication,
   entitlement/subscription, capabilities, availability, freshness, quota, and capacity are
   separate facts.
6. **Evidence beats self-report.** Executor output is not acceptance evidence by itself.
7. **Recovery uses authoritative checkpoints.** Retry/resume is never merely "repeat stage N".
8. **Fail closed at authority boundaries.** Source drift, workspace ambiguity, security boundaries,
   consumed authority, stale approval, or unknown destructive side effects stop automatic progress.
9. **Private chain-of-thought is not an artifact.** Persist observable decisions, assumptions,
   limitations, actions, tool results, and evidence instead.
10. **Parallel write-capable models never share one working tree.** Every benchmark candidate is
    isolated.

## 5. Model operating policy

Canonical policy remains in `.ai/AI_MODEL_ROUTING.md`. The roadmap does not change the development
portfolio simply because another reference project supports a provider.

| Model | Default role |
|---|---|
| GPT-5.6 High in Chat (Sol) | Persistent planner / architect / project-state / requirements / acceptance / governance / router |
| GPT-6 Luna Reasoning xHigh in Codex | Default substantial executor |
| Claude Sonnet 5 Effort HIGH | Difficult recovery/surgical finalization and bounded isolated bug fixes |
| Claude Opus 5.5 Effort HIGH | Independent critical review; exceptional owner-authorized Grand Master Cleaning |
| GPT-6 Sol Reasoning High in Codex | Emergency/exceptional direct implementation |
| Gemini 3.8 Flash Reasoning HIGH | Explicitly delegated low-risk mechanical auxiliary |

GPT-5.6 Terra is retired. Product runtime model IDs are distinct from development roles. No route is active during the owner hold.

## 6. Current P0 gate — APO-70

APO-70 remains first. The reference-derived roadmap does **not** preempt it.

### 6.1 Accepted backend runtime proof

The backend cross-process proof is **SOL-ACCEPTED / COMPLETE**. It ran on source head
`a2719257e6dc2b276152f35aa34ad945a912a20a` for ProjectId
`c743e0da-24a6-4ad3-b309-aeb72658013c`. The proven shape was:

```text
Process A
  real Sol PrepareAsync
       ↓
     Ready
       ↓
Coordinator/process restart

Process B
  Restore persisted Ready
       ↓
Exactly one real gpt-5.6-luna StartAsync
       ↓
Bounded disposable workspace mutation
       ↓
Execution/workspace/checkpoint evidence
       ↓
Anti-replay validation
       ↓
STOP
```

It recorded `PrepareAsync=1`, `RestoreAsync=2`, `StartAsync=1`, real Sol `=1`, real Luna `=1`,
fallback `=0`, and model retry `=0`; authority continuity was preserved with no replan, reroute,
workspace reprepare, or executor replacement. The prepared workspace changed `STATE=before` to
`STATE=after`; the original disposable source remained on the same HEAD, clean, without remotes,
and with `STATE=before`. This proves the backend boundary only.

### 6.2 Current product acceptance gate

The remaining APO-70 gate is **OWNER-VISIBLE DESKTOP FUNCTIONAL ACCEPTANCE after explicit owner resume**:

- a fresh Desktop publish;
- real registered-project workflow;
- truthful provider/session state;
- owner-visible execution and cancellation/result evidence;
- recovered Mission Control UX;
- visual requirements already recorded on Issue #111;
- owner visual PASS remains recorded; obtain owner functional acceptance after explicit resume;
- final exact-head Sol acceptance.

Only then can APO-70 move toward merge/finalization under a separately authorized gate.

## 7. P0/P1 reliability continuation — APO-55 / #93

APO-55 is promoted/extended as the existing owner of truthful runtime evidence.

Required follow-on behavior:

- execution-owned process identity/path provenance/PID/start-end times where safely available;
- process-tree termination confirmation;
- guarded state on termination-unconfirmed;
- restart reconciliation of persisted Running/Waiting executions against checkpoint, run authority,
  process, workspace, repository, and Git reality;
- no assumption that missing PID means success;
- secret-safe environment fingerprint using an explicit allowlist;
- freshness/stale/partial/unavailable runtime evidence in Mission Control;
- references to immutable APO authorities rather than a second raw evidence hierarchy.

This is the first new reference-derived implementation area after APO-70 because it materially
improves local-PC execution correctness and recovery.

## 8. P1 local Agent Readiness — APO-71 / #113

APO-71 extends APO-8/APO-38 rather than replacing them.

Target product model:

```text
Agent / Model Identity
        +
Executable Resolution + Provenance
        +
CLI Version
        +
Supported Invocation Modes
        +
Authentication Truth
        +
Entitlement Truth where independently verifiable
        +
Capabilities / Limitations
        +
Freshness
        =
Agent Readiness
```

Readiness should explain Ready / Degraded / Unavailable / Authentication Required / Unsupported /
Unknown truth using existing domain conventions where possible. WPF must not probe PATH, versions,
or provider sessions directly.

## 9. P1 safe retry/fallback — APO-72 / #114

APO-72 composes existing checkpoints, execution authority, routing, budgets, workspace evidence,
validation, approval, and runtime evidence into a typed retry/fallback policy.

```text
Failure classification
      +
Last verified checkpoint
      +
Observed side effects
      +
Task requirements
      +
Allowed / ready agents
      +
Routing and owner policy
      +
Quota/capacity truth
      +
Remaining budget
        ↓
Retry disposition
```

Possible dispositions include retry same executor, route eligible fallback, wait for auth/capacity,
reconcile source/workspace, run bounded remediation, require owner approval, or stop/not retryable.

No fixed `Claude -> Gemini -> Codex` or equivalent chain is allowed.

## 10. P1 workflow and evidence UX — APO-52 / #90 and APO-54 / #92

### APO-52 — composable workflow templates

Provider-independent, versioned templates may express patterns such as:

```text
Implementation:
Planner → Executor → Verifier → Reviewer → Remediator → Acceptance

Recovery:
Diagnostician → Fixer → Verifier

Architecture review:
Analyzer → Independent Reviewer → Architecture Decision

Security review:
Implementer → Security Reviewer → Remediation → Verification
```

Templates instantiate existing PlanningExecutionContract, WorkGraph, Handoff, routing,
validation/review, checkpoint, and approval authorities. They are not a second orchestration engine
and do not hard-code provider fallback order.

### APO-54 — decision ledger and execution-evidence history

Expose an owner-browsable timeline/manifest that references existing canonical evidence, including
contract, WorkGraph, routing, agent/readiness, handoff, workspace receipt, checkpoint chain, run
authority, process/runtime/environment evidence, Git/validation/review/remediation/acceptance, and
final disposition.

Do not duplicate raw evidence merely to imitate another project's `results/prompts/memos/meta`
folder structure. Do not persist private chain-of-thought.

## 11. P2 controlled benchmarking — APO-73 / #115

Benchmarking is explicit owner-started measurement, not production routing.

```text
                 Immutable benchmark task
                           │
          ┌────────────────┼────────────────┐
          ▼                ▼                ▼
      Candidate A      Candidate B      Candidate C
      workspace A      workspace B      workspace C
      authority A      authority B      authority C
      evidence A       evidence B       evidence C
          └────────────────┼────────────────┘
                           ▼
                   Evidence comparison
```

Potential comparable metrics:

- typed success/failure;
- first-attempt completion;
- build/test/validation result;
- independent reviewer disposition;
- scope/architecture compliance;
- changed files/lines;
- retries;
- elapsed duration;
- provider-truthful resource/cost signals;
- regression findings;
- final acceptance disposition.

No candidate can merge, push, deploy, or perform destructive external actions by default. Results
do not automatically alter production routing.

## 12. P2 transparent historical-performance routing — APO-74 / #116

Only after sufficient trustworthy history exists, Automatic routing may use historical performance
as an **advisory** deterministic signal.

Each statistic must expose:

- task cohort/classification;
- sample size;
- freshness;
- evidence provenance;
- limitations/confidence.

History cannot make an otherwise ineligible agent eligible. The routing priority remains:

```text
Correctness
  ↓
Security / required assurance
  ↓
Required capability / risk
  ↓
Project + owner policy
  ↓
Current readiness / auth / entitlement
  ↓
Quota / capacity constraints
  ↓
Transparent historical performance
  ↓
Cost/resource tie-break where safe
```

The first version should be rules/scoring based, not ML.

## 13. Existing strategic capabilities retained

The following remain part of the broader roadmap and are not replaced by APO-71..74:

- APO-53 — explainable project health and owner-attention summary;
- APO-56 — model context budgets and handoff compression;
- APO-57 — bounded background automation and safe housekeeping;
- APO-58 — optional remote/mobile approval security boundary;
- APO-59..61 — accepted APO-37 hardening debt;
- provider/capacity monitoring and other existing Epic responsibilities;
- cross-cutting packaging, compatibility, CI, release quality, and accessibility.

## 14. Canonical dependency guidance

Historical delivered dependency links remain preserved in the tracker. For the new roadmap delta,
use the following sequencing/dependency guidance; create hard `Blocks` links only where a future
implementation contract proves a true architectural prerequisite.

```text
APO-70 owner-accepted execution spine
        ↓
APO-55 stable runtime/process/restart evidence
        ↓
APO-71 Agent Readiness
        ↓
APO-75 Project Intelligence Map
        ↓
APO-72 retry/fallback
        ↓
APO-52 / APO-54 workflow + evidence-history acceleration
        ↓
APO-76 shared-core Headless CLI
        ↓
APO-77 governed schedule/event triggers
        ↓
APO-73 benchmarking
        ↓
APO-74 historical-performance routing
        ↓
APO-78 governed ACP / A2A / MCP interoperability
```

APO-73 and APO-74 must not delay the first owner-usable single-executor product loop.

The 2026-09-17 extensions remain bounded: APO-75 is advisory HEAD-bound planning context, APO-76
is a presentation surface over the existing Application/Domain core, APO-77 submits triggers to
the normal policy/approval/workspace/evidence pipeline, and APO-78 protocol adapters cannot bypass
Agent Readiness, routing, approval, workspace, evidence, or execution authorities.

## 15. External-reference anti-patterns explicitly rejected

APO will not adopt:

- a Flutter/Dart rewrite or external-repository transplant;
- broad permission bypass such as `--dangerously-skip-permissions`;
- generic shell-command construction as the execution architecture;
- `cmd.exe /c` as the core Windows process runner;
- unrestricted environment forwarding/capture;
- hard-coded provider/model fallback order;
- retry by numbered stage without authoritative checkpoint/side-effect reconciliation;
- multiple write-capable models in one mutable workspace;
- raw transcript/prompt persistence as the evidence model;
- private reasoning/chain-of-thought memo persistence;
- model-selected authority that can rewrite caller task/security/routing classification.

## 16. Architecture-health cadence

Continue the standing Continuous Implementation Guard for every implementation. After the first
real restored-Ready Luna execution proof, perform a bounded architecture-health review of:

`ExecutionCoordinator -> RecoveryCheckpoint -> ExecutionRunAuthority -> BoundedExecutionService -> provider adapter -> BoundedProcessHost -> runtime evidence`

Review for duplicate lifecycle truth, process ownership gaps, stale evidence semantics,
restart-reconciliation ownership, retry/fallback ownership, validation/review boundary drift, and
workspace/authority anti-replay correctness.

This is a bounded architecture health check, not a rewrite authorization.

## 17. Final roadmap sequence

```text
NOW
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
APO-52 / APO-54 workflow and evidence-history continuation
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
remaining post-V1 strategic capabilities as separately authorized
```

## 18. Current planner boundary

The execution gate is **closed**. This roadmap update is planning/governance work only.

No new real Luna `StartAsync` is authorized by this file. No new implementation session, merge, main
push, release, tag, deployment, or Issue #111 closure is authorized by this file.

The next executable AI-worker prompt may be generated only after a future owner message whose
entire trimmed content is exactly lowercase `p`.
