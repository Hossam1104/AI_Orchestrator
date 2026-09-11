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

## Acceptance evidence

- RMS reference source inspected at `D:\AI Tools\Active Projects\RMS_Support_Hub`.
- Light and dark semantic themes, shared cards/controls, header, sidebar, status pills, and
  keyboard/focus behavior are covered by source and automated checks.
- Existing project, Mission Control, provider, persistence, and credential-reference services are
  reused; no fake runtime data, tokens, database, browser, or new provider is authorized.
- Release build, full tests, and `win-x86`, `win-x64`, and `win-arm64` publish validation are
  required before PR handoff.
- Owner visual acceptance is required before merge or release promotion.

## Governance boundaries

- V1 release remains frozen. Do not create a tag, release, deployment, or release assets.
- Use one feature branch and one Draft PR against `main`; do not merge or bypass protection.
- Jira is historical provenance only; no Jira writes.
- Agents and Activity remain Post-V1 and must not appear as shipped primary navigation.
- Stop at the Sol acceptance boundary after validation and PR handoff.
