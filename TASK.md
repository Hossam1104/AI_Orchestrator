# AI_Orchestrator — APO-70 V1 Desktop Product Recovery

**Mode:** BOUNDED EXECUTION CONTRACT
**Tracker:** GitHub issue #111 (`APO-70`)
**Branch:** `feature/APO-70-v1-desktop-product-recovery`
**Base:** `origin/main` at `d7c1231df9ea1a4d008aa473210d0ccc735c0302`

## Scope

Recover the V1 WPF desktop shell using the accepted RMS Support Hub visual system while retaining
AI Orchestrator identity and existing application/domain services. Complete the bounded visual and
functional surface work for Mission Control, Projects, AI Capacity, truthful global status, theme
switching, native local-path selection, and Post-V1 navigation treatment.

## Current execution slice

This assigned APO-70 slice also includes the smallest safe owner-facing execution workflow:

- an Application `IExecutionCoordinator` converts bounded owner intent into the existing planning
  contract, work graph, routing decision, handoff, workspace preparation, recovery checkpoint, and
  exact `BoundedExecutionRequest` authorities;
- the Desktop Execution workspace allows explicit project selection, title/objective/criteria/
  constraint authoring, Prepare, Start, and Cancel actions, and displays only real coordinator and
  bounded-service state;
- no raw prompt, internal authority GUID, direct provider process, or arbitrary command is exposed
  from WPF;
- if no exact production `IExecutionAdapter` is registered, Start must remain visibly and
  truthfully `AdapterUnsupported`; this is a proven architectural blocker, not a simulated run.

Validation and delivery remain bounded to this branch and Draft PR #112. Do not merge, push `main`,
create a release/tag/deployment, close Issue #111, or claim owner/Sol acceptance.

## Rounds on this branch

APO-70 is one gate delivered over successive rounds on the same branch. All of them are still
unmerged and unaccepted:

1. **Desktop product recovery** — the RMS-family visual system, shell treatment, truthful global
   state pill, theme switching, native local-path selection, Post-V1 navigation treatment.
2. **Provider authentication and dynamic registry** — local-session-first Codex/Claude
   authentication, optional API-key fallback, registry-driven AI Providers page, generic custom
   providers, explicit availability/authentication/capacity states.
3. **Architecture health, deep clean, and remediation** (owner-authorized Opus execution exception,
   named scope) — the project-folder selection gap, shell lifetime, selection-refresh disposal,
   provider identity, rendered-name collisions, executable resolution, bounded process-host
   lifetime, the deferred CI findings, and documentation/governance reconciliation. Detail and
   evidence are in [`.ai/CURRENT_STATE.md`](.ai/CURRENT_STATE.md).

## Acceptance evidence

- RMS reference source inspected at `D:\AI Tools\Active Projects\RMS_Support_Hub`.
- Light and dark semantic themes, shared cards/controls, header, sidebar, status pills, and
  keyboard/focus behavior are covered by source and automated checks.
- Existing project, Mission Control, provider, persistence, and credential-reference services are
  reused; no fake runtime data, tokens, database, browser, or new provider is authorized.
- Release build, full tests, and `win-x86`, `win-x64`, and `win-arm64` publish validation are
  required before PR handoff.
- Owner visual acceptance is required before merge or release promotion. Automated render checks are
  not owner visual acceptance and never satisfy this gate.

## Governance boundaries

- V1 release remains frozen. Do not create a tag, release, deployment, or release assets.
- Use one feature branch and one Draft PR against `main`; do not merge or bypass protection.
- Jira is historical provenance only; no Jira writes.
- Agents and Activity remain Post-V1 and must not appear as shipped primary navigation.
- Stop at the Sol acceptance boundary after validation and PR handoff.
