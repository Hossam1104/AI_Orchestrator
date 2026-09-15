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

The gate applies to prompts for Luna, Codex, Terra, Haiku, Sonnet, Opus, and any Claude/OpenAI
investigation agent. It does **not** stop normal Sol analysis, architecture discussion, review,
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

The script's default interactive mode leaves the fresh application running for owner inspection.
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
independent review, findings, and an approval recommendation; Terra may provide security,
concurrency, data-integrity, or trust-boundary assurance; executors provide evidence. None of these
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
