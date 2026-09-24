# AI_EXECUTION_POLICY.md — Canonical AI Execution Policy

This file is the canonical, cross-project source of truth for the prompt gate, bounded
implementation discipline, acceptance evidence, root-cause debugging method, context budget, and
tool policy. `AGENTS.md` and `CLAUDE.md` reference this file rather than duplicating it. Model
portfolio and routing/quota policy live in `.ai/AI_MODEL_ROUTING.md`.

---

## 1. Universal `p` Prompt Gate

Sol must **not** generate an executable AI-worker prompt until the owner's entire trimmed message
is exactly the single lowercase character:

```
p
```

Examples that do **not** satisfy the gate: `P`, `prompt`, `p now`, `proceed`, `go`, `yes`, `give me
the prompt`.

One standalone `p` authorizes exactly **one** executor/reviewer prompt. After results return to
Sol, the gate resets.

Cycle:

```
DISCUSS -> AGREE -> p -> ONE PROMPT -> RESULT -> SOL REVIEW -> DISCUSS -> p
```

The gate applies to generated executor/reviewer prompts for any current model. It does not override
the owner hold; Hossam must explicitly resume first. It does **not** stop normal Sol analysis, architecture
 discussion, review,
Jira reasoning, or ordinary answers.

This gate is canonical here; any prior duplicate description elsewhere is superseded by this file.

---

## 2. Bounded Implementation Discipline

- Inspect before edit.
- Preserve approved architecture.
- Make the minimum coherent change that satisfies the assigned scope.
- No unrelated cleanup, no speculative abstractions, no requirement invention.
- Stop and report on contradictory evidence rather than guessing.

### 2.1 Fresh APO desktop runs

The canonical local desktop workflow is `scripts/Run-FreshDesktop.ps1`. Requests to run, launch,
or open APO always mean: verify the current repository state, recreate the dedicated local-run
directory, publish the current working tree in Release, validate the publish output, report the
Git state and executable hash, and launch that newly created executable. Existing binaries from
prior publish or artifact directories must never be reused as current-state evidence.

The script may launch an interactive application, but repository prompt completion stops APO by
default under AGENTS.md section 16 unless that specific owner instruction explicitly says to
leave it running.
Use `-SmokeTest` only for bounded runtime verification; it stops only the process it launched after
verification and reports the final APO process count.

---

## 3. Acceptance Evidence

Executors must report:

- the acceptance criteria being satisfied;
- focused verification performed, and broader regression checks where appropriate;
- exact commands run;
- pass/fail counts, warnings, and errors;
- `git diff` and `git status` summaries;
- remaining risk.

Executor self-declaration is **not** final acceptance. **GPT-5.6 Sol is always the final acceptance
authority** for all routed work, regardless of which executor performed it. Opus may provide
independent review, findings, and an approval recommendation; Sonnet may provide bounded recovery
or bug fixes; executors provide evidence. None of these
roles replace Sol's final acceptance decision.

---

## 4. Root-Cause Debugging

```
REPRODUCE -> TRACE -> HYPOTHESIS -> PROVE/DISPROVE -> MINIMAL FIX -> REGRESSION TEST -> SURROUNDING VALIDATION
```

---

## 5. Context Budget

- Search before reading a whole file; use semantic/symbol lookup before a broad grep.
- Do not reread an unchanged file.
- Filter logs to the relevant window instead of dumping full output.
- Run targeted tests before a full regression suite when only a targeted change was made.
- Do not regenerate already-documented architecture.
- Use Context7 only conditionally (see §6).
- Avoid unnecessary multi-model stages for work one model can complete correctly.
- Keep final reports concise and evidence-based.

---

## 6. Tool Policy

### Ponytail

Default mode: `FULL`. Use `ULTRA` only if explicitly requested. Ponytail enforces the smallest
correct implementation and must never weaken trust-boundary validation, security, authorization,
data-loss protection, deterministic acceptance evidence, accessibility, tenant isolation,
financial invariants, or repository governance.

### Serena (semantic code retrieval)

Use for: semantic/symbol overview before a whole-file read; reference/implementation lookup before
a broad grep; full source file reads only when genuinely necessary; never for repeated rereading of
an unchanged file. Ordinary file tools remain valid for non-code artifacts. Do not force Serena into
trivial edits. Project/worktree identity must resolve correctly per checkout — see host
configuration in §7.

### Context7 (external library/API documentation)

Conditional use only. Canonical rule:

```
CURRENT EXTERNAL API UNCERTAINTY -> Context7
```

Do **not** invoke Context7 for internal business logic, repository navigation, mechanical changes,
known project abstractions, or ordinary documentation. Do not commit Context7 API keys; prefer
OAuth/device authorization where supported.

---

## 7. Host Tool Configuration Status

Recorded at migration time (see PR description and `.ai/CURRENT_STATE.md` for the dated entry);
this section states the durable rule, not a point-in-time snapshot:

- **Serena** connects as a local stdio MCP server using `serena start-mcp-server
  --project-from-cwd --context <host-context>` (`claude-code` for Claude Code, `codex` for Codex),
  which resolves project/worktree identity from the current working directory so separate
  repositories/worktrees do not share an incorrect Serena project identity.
- **Context7** connects as a local stdio MCP server (`npx -y @upstash/context7-mcp`) without a
  committed API key by default; an optional authenticated setup (`npx ctx7 setup`) raises rate
  limits and is a manual, interactive, per-developer step.
- **Ponytail** is installed per host using that host's plugin marketplace mechanism
  (`/plugin marketplace add DietrichGebert/ponytail` then, in a separate prompt, `/plugin install
  ponytail@ponytail` for Claude Code; `codex plugin marketplace add DietrichGebert/ponytail` then
  `codex plugin add ponytail@ponytail` for Codex, followed by interactive `/hooks` review and
  approval). Installation and hook trust are per-developer-machine, interactive actions and are not
  automated by repository tooling.

Verify current upstream instructions for all three tools before reinstalling or upgrading; do not
blindly trust commands recorded here if upstream documentation has changed.

---

## 8. Local-PC Execution, Recovery, and Evidence Policy

APO's normal software-delivery execution environment is the owner's persistent local Windows PC and
APO-managed project/workspace state. Temporary chat, test, provider-output, or validation sandboxes
must never become the canonical project or execution store.

### 8.1 Agent readiness

A local AI CLI is not considered ready merely because a command name or executable exists. Where
supported, readiness should derive from independently observable facts such as executable path and
provenance, CLI version, supported invocation modes, authentication/session truth, entitlement when
verifiable, registered capabilities/limitations, freshness, and provider-specific health evidence.

Installation, authentication, entitlement/subscription, and quota/capacity are separate facts and
must never be inferred from one another.

### 8.2 Process ownership

Every local process started by APO must remain bounded and owned by the orchestration runtime. The
runtime must consume stdout/stderr safely, record bounded process evidence where appropriate,
support cancellation/timeout, and either confirm execution-owned process-tree termination or mark
termination as unconfirmed and keep the workspace guarded.

PID/process identity is evidence, not completion proof. A missing process after restart must not be
interpreted as successful business completion.

### 8.3 Restart reconciliation

After APO or Windows restart, persisted Running/Waiting work must be reconciled against durable run
authority, checkpoint, workspace/repository/Git state, process reality, and known side effects before
any automatic continuation. The result must be a typed safe state such as completed externally,
safely interrupted, reconciliation required, blocked, or unsafe to resume automatically.

Retry must resume from the latest **verified authoritative checkpoint**, not merely from a UI stage
number.

### 8.4 Retry and fallback

A fallback executor is never selected solely because another model failed. Retry/fallback policy
must consider failure class, checkpoint truth, side effects, task requirements, allowed agents,
capabilities, routing/owner policy, quota/capacity truth, and remaining execution budget.

Authentication failures, quota exhaustion, unsupported capability, source/workspace conflicts,
validation failures, security boundaries, owner-approval boundaries, and irreversible side-effect
ambiguity require different dispositions. Model switching must never bypass an authority or safety
gate.

### 8.5 Environment fingerprint

Where reproducibility/recovery requires it, APO may persist a lightweight explicit allowlist of
non-secret environment facts such as OS/version, architecture, APO version, selected CLI version,
Git version, project path/branch/commit, working directory, and selected tool/runtime availability.
Never capture unrestricted environment variables or secret-bearing values.

### 8.6 Evidence contents

Persist observable operational evidence: requests, immutable authority references, decisions,
assumptions/limitations, commands/tool results where safe, process metadata, Git/validation/review
findings, timing, failure classification, and final disposition.

Do **not** require or persist private hidden chain-of-thought. Do not retain raw provider transcripts,
prompts, unrestricted stdout/stderr, credentials, or unrelated source content merely to create a
complete-looking execution folder.

### 8.7 Parallel benchmarking

Any future multi-model benchmark with write-capable executors must use a separate isolated workspace,
run authority, checkpoint chain, and evidence set per candidate. Competing executors must never
write to the same working tree. Benchmarking is measurement only unless a separately approved
routing policy explicitly consumes its evidence.

The approved roadmap integration for these behaviors is maintained in
`docs/EXTERNAL_REFERENCE_ROADMAP_INTEGRATION.md` and the associated canonical GitHub APO Issues.
