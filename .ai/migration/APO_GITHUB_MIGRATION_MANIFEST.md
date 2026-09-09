# APO GitHub Workspace Migration Manifest — Phase 1 Baseline

**Project:** AI_Orchestrator (APO)  
**Migration phase:** Prompt 1 of 2 — Reconciliation + Inventory + Migration Design  
**Prepared:** 2026-09-09 12:47 +03:00 (Africa/Cairo)  
**Status:** PREPARED LOCALLY — REPOSITORY COMMIT BLOCKED  
**Target repository path:** `.ai/migration/APO_GITHUB_MIGRATION_MANIFEST.md`

---

## 1. Migration objective

Migrate APO project-management authority from Jira toward a lean GitHub-native workspace while preserving full Jira provenance, accepted delivery truth, FAST V1 / Post-V1 boundaries, parent/capability grouping, dependency relationships, priorities, and historical traceability.

This phase does **not** perform bulk GitHub issue creation, GitHub Project cutover, Jira deletion, Jira bulk closure, or APO-33 implementation.

---

## 2. Verified GitHub repository identity

- Repository: `Hossam1104/AI_Orchestrator`
- Repository ID: `1327165021`
- Default branch: `main`
- Repository visibility: public
- Archived: no
- Historical repository name `Hossam1104/AI-Project-Orchestrator`: not found as a current repository.

### Verified main

- HEAD: `844551fb51d2b3fa2dff1d92c1a7b01ecd0fc809`
- Tree: `e348cbbdf121185649c10e08c5f8003241a46dc4`
- Commit: `Merge APO-50 final integration closeout`
- Parents:
  - `8a60ce17a22493a7be240c96d59e652772cffce1`
  - `8d19618ecdf7033b755f7191d8a387ebd0f00716`

### Recent accepted delivery evidence

| Jira key | Delivery evidence | Reconciled truth |
|---|---|---|
| APO-48 | Product PR #27; accepted merge `7fe179844ceb056c542067485843bc892ebdefcc` | Delivered / accepted / done |
| APO-51 | PR #29; merge `ea96beefeec5b2fc2381ad1d4ade39c6c63fc56c` | Delivered / accepted / done |
| APO-49 | PR #31; merge `8d2934bfba844d30365d2c4f3b0b8a53dc2a6fd6` | Delivered / accepted / done |
| APO-63 | PR #33; merge `aa2b584f0cb2723113775d0962388988741602ff` | Delivered / accepted / done |
| APO-50 | PR #35; product merge `8a60ce17a22493a7be240c96d59e652772cffce1`; docs PR #36; closeout merge `844551fb51d2b3fa2dff1d92c1a7b01ecd0fc809` | Delivered / accepted / done |
| APO-62 | PR #24 | Delivered / done |
| APO-47 | PR #22 | Delivered / done |
| APO-46 | PR #17 | Delivered / done |
| APO-45 | PR #19 | Delivered / done |
| APO-68 | PR #18 | Delivered / done |
| APO-69 | PR #21 | Delivered / done / historical rebaseline |

### Current GitHub issue inventory

- Existing GitHub Issues in repository: `0`
- Existing open PRs: `0`
- Exact-main workflow runs: `0`
- `.github/workflows` on `main`: not present in live repository inspection
- `GITHUB ACTIONS CI = NONE / NOT CLAIMED`

Existing repository label, milestone, and GitHub Projects inventories were **not available through the current connected GitHub tool surface** and must not be fabricated.

---

## 3. Verified Jira inventory totals

Jira project: `APO`

### Totals

- Total work items: `69`
- Epics: `17`
- Stories: `47`
- Bugs: `1`
- Tasks: `4`

### Status totals

- Done: `41`
- In Progress: `6`
- To Do: `22`

### Status shape

- Epics:
  - Done: `APO-1`
  - In Progress: `APO-2` through `APO-6`
  - To Do: `APO-7` through `APO-17`
- Non-epics:
  - Done: `APO-18` through `APO-32`
  - To Do: `APO-33`
  - Done: `APO-34` through `APO-49`
  - In Progress: `APO-50`
  - Done: `APO-51`
  - To Do: `APO-52` through `APO-61`
  - Done: `APO-62` through `APO-69`

---

## 4. Reconciled project state

### FAST V1 completed sequence

`APO-48 -> APO-51 -> APO-49 -> APO-63 -> APO-50`

All five are reconciled as delivered / accepted / done from repository and merge evidence.

### Current FAST V1 gate

`APO-33 = CURRENT GATE / READY / NOT STARTED`

GitHub Actions remains absent and is not claimed.

### After APO-33

`APO-33 -> Final V1 Release Audit -> v1.0.0`

This is sequencing only. This manifest does not authorize implementation or release tagging.

### Post-V1

`APO-52` through `APO-61` remain deferred / Post-V1 / not started.

### Historical / void

`APO-64` through `APO-67` are historical connector-correction artifacts with no product scope.

---

## 5. Jira -> proposed GitHub canonical mapping

Every Jira item should map to exactly one canonical GitHub Issue in Phase 2 because the live GitHub issue inventory is currently empty.

| Jira key | Type | Parent | Jira status | Reconciled classification | Proposed GitHub status | Delivery |
|---|---|---|---|---|---|---|
| APO-1 | Epic | — | Done | Delivered / Done governance epic | Done | Foundation |
| APO-2 | Epic | — | In Progress | Planned / Partially delivered epic | In Progress | Foundation |
| APO-3 | Epic | — | In Progress | Planned / Partially delivered epic | In Progress | Foundation |
| APO-4 | Epic | — | In Progress | Planned / Partially delivered epic | In Progress | Foundation |
| APO-5 | Epic | — | In Progress | Planned / Partially delivered epic | In Progress | Foundation |
| APO-6 | Epic | — | In Progress | Planned / Partially delivered epic | In Progress | Foundation |
| APO-7 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-8 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-9 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-10 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-11 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-12 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-13 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-14 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-15 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-16 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-17 | Epic | — | To Do | Planned / Epic scope open | Backlog | Foundation |
| APO-18 | Story | APO-1 | Done | Delivered / Done | Done | Foundation |
| APO-19 | Story | APO-1 | Done | Delivered / Done | Done | Foundation |
| APO-20 | Story | APO-1 | Done | Delivered / Done | Done | Foundation |
| APO-21 | Story | APO-2 | Done | Delivered / Done | Done | Foundation |
| APO-22 | Story | APO-2 | Done | Delivered / Done | Done | Foundation |
| APO-23 | Story | APO-2 | Done | Delivered / Done | Done | Foundation |
| APO-24 | Story | APO-3 | Done | Delivered / Done | Done | Foundation |
| APO-25 | Story | APO-3 | Done | Delivered / Done | Done | Foundation |
| APO-26 | Story | APO-3 | Done | Delivered / Done | Done | Foundation |
| APO-27 | Story | APO-3 | Done | Delivered / Done | Done | Foundation |
| APO-28 | Story | APO-4 | Done | Delivered / Done | Done | Foundation |
| APO-29 | Story | APO-4 | Done | Delivered / Done | Done | Foundation |
| APO-30 | Story | APO-4 | Done | Delivered / Done | Done | Foundation |
| APO-31 | Story | APO-4 | Done | Delivered / Done | Done | Foundation |
| APO-32 | Story | APO-17 | Done | Delivered / Done | Done | Foundation |
| APO-33 | Story | APO-17 | To Do | Current gate / Ready / Not started | Ready | FAST V1 |
| APO-34 | Story | APO-4 | Done | Delivered / Done | Done | Foundation |
| APO-35 | Story | APO-5 | Done | Delivered / Done | Done | Foundation |
| APO-36 | Bug | APO-4 | Done | Delivered / Done | Done | Foundation |
| APO-37 | Story | APO-6 | Done | Delivered / Done | Done | Foundation |
| APO-38 | Story | APO-8 | Done | Delivered / Done | Done | Foundation |
| APO-39 | Story | APO-5 | Done | Delivered / Done | Done | Foundation |
| APO-40 | Story | APO-10 | Done | Delivered / Done | Done | Foundation |
| APO-41 | Story | APO-10 | Done | Delivered / Done | Done | Foundation |
| APO-42 | Story | APO-10 | Done | Delivered / Done | Done | Foundation |
| APO-43 | Story | APO-3 | Done | Delivered / Done | Done | Foundation |
| APO-44 | Story | APO-9 | Done | Delivered / Done | Done | Foundation |
| APO-45 | Story | APO-11 | Done | Delivered / Done | Done | Foundation |
| APO-46 | Story | APO-6 | Done | Delivered / Done | Done | Foundation |
| APO-47 | Story | APO-7 | Done | Delivered / Done | Done | Foundation |
| APO-48 | Story | APO-12 | Done | Delivered / Accepted / Done | Done | FAST V1 |
| APO-49 | Story | APO-14 | Done | Delivered / Accepted / Done | Done | FAST V1 |
| APO-50 | Story | APO-15 | In Progress | Delivered / Accepted / Done — Jira reconciliation required | Done | FAST V1 |
| APO-51 | Story | APO-13 | Done | Delivered / Accepted / Done | Done | FAST V1 |
| APO-52 | Story | APO-8 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-53 | Story | APO-15 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-54 | Story | APO-16 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-55 | Story | APO-12 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-56 | Story | APO-10 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-57 | Story | APO-11 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-58 | Story | APO-14 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-59 | Story | APO-6 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-60 | Story | APO-6 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-61 | Story | APO-6 | To Do | Deferred Post-V1 / Not started | Backlog | Post-V1 |
| APO-62 | Story | APO-6 | Done | Delivered / Done | Done | Foundation |
| APO-63 | Story | APO-6 | Done | Delivered / Accepted / Done | Done | FAST V1 |
| APO-64 | Task | — | Done | Historical / Void / No project work | Done (not planned) | Historical |
| APO-65 | Task | — | Done | Historical / Void / No project work | Done (not planned) | Historical |
| APO-66 | Task | — | Done | Historical / Void / No project work | Done (not planned) | Historical |
| APO-67 | Task | — | Done | Historical / Void / No project work | Done (not planned) | Historical |
| APO-68 | Story | APO-6 | Done | Delivered / Done | Done | Foundation |
| APO-69 | Story | APO-1 | Done | Delivered / Done / Historical rebaseline | Done | Historical |

---

## 6. Jira provenance block for every migrated issue

Each GitHub issue body should include:

```text
Original tracker: Jira
Jira key: APO-XX
Jira URL: https://hossamsqa.atlassian.net/browse/APO-XX
Original issue type: <Epic|Story|Task|Bug>
Original Jira status: <status>
Reconciled status: <classification>
Parent Jira key: <APO-X|None>
Migration date: 2026-09-09
Relevant GitHub delivery evidence: <PRs/commits where known>
```

No historical commit message should be rewritten.

---

## 7. Dependency / relationship baseline

Preserve parent/Epic grouping separately from lifecycle status.

### Strategic hard dependencies already documented

- APO-38 -> APO-39
- APO-38 -> APO-44
- APO-40 -> APO-41
- APO-40 -> APO-42
- APO-40 -> APO-43
- APO-40 -> APO-45
- APO-39 -> APO-43
- APO-41 -> APO-45
- APO-42 -> APO-45
- APO-43 -> APO-45
- APO-44 -> APO-45
- APO-46 -> APO-45
- APO-45 -> APO-48
- APO-48 -> APO-63
- APO-49 -> APO-63
- APO-62 -> APO-63
- APO-45 -> APO-57
- APO-49 -> APO-58

### Additional live Jira relationship requiring reconciliation

Live Jira also contains:

`APO-45 -> APO-68`

This is a `Blocks` relationship and is not present in the roadmap text claiming an exact 18-link strategic DAG. Preserve the live relationship in migration and record the roadmap as stale rather than dropping the link.

Also preserve:
- APO-19 -> APO-20
- APO-31 -> APO-22
- APO-37 `Relates` APO-59 / APO-60 / APO-61

---

## 8. Known discrepancies

1. **APO-50 tracker drift**
   - GitHub/repository: final accepted / merged / done.
   - Jira: In Progress with `v1-current-gate`.
   - Reconciled truth: APO-50 Done; APO-33 current gate / not started.

2. **README status drift**
   - README still states APO-63 is next and APO-63/APO-50 remain planned.

3. **Strategic roadmap status drift**
   - Status header still states APO-63/APO-50/APO-33 are all not started and APO-63 is next.
   - Sequencing remains useful, but mutable completion state is stale.

4. **AGENTS tracker-authority drift**
   - `AGENTS.md` still declares Jira the authoritative APO work tracker.
   - This must change only after a validated GitHub cutover.

5. **AI routing governance drift**
   - Repository routing documentation is stale relative to the owner’s later cross-project routing override.
   - Do not silently mix this into migration writes.

6. **Dependency drift**
   - Roadmap describes exactly 18 strategic hard dependencies.
   - Live Jira also contains APO-45 -> APO-68.

7. **Implementation-plan wording**
   - Documentation contains wording that can be read as if the APO-33 CI/release workflow already exists.
   - Live repository truth remains GitHub Actions CI none / not claimed.

8. **Display-name identity drift**
   - Jira project display name: `AI Project Orchestrator`.
   - Canonical product: `AI_Orchestrator`.

9. **Local workspace truth**
   - Current local path/branch/HEAD/status/worktrees cannot be verified from this chat environment.
   - Treat as `UNKNOWN / REQUIRES LOCAL READ`.

10. **GitHub metadata inventory limits**
    - Existing labels, milestones, and Projects v2 inventory are `NOT AVAILABLE` through the current connected tool surface.

---

## 9. Proposed GitHub Project design

Project name:

`APO — Delivery Workspace`

Keep the model lean.

### Status

- Backlog
- Ready
- In Progress
- Review
- Blocked
- Done

Mapping:
- Jira To Do -> Backlog by default.
- APO-33 -> Ready because it is the reconciled current gate and not started.
- Jira In Progress -> In Progress unless stronger accepted delivery evidence reconciles it to Done.
- Jira Done -> Done.
- Void artifacts -> Done with not-planned/historical semantics.

### Priority

- High
- Medium
- Low

Preserve Jira priority. Do not add complexity without an actual need.

### Delivery

- Foundation
- FAST V1
- Post-V1
- Historical

### Work Type

Prefer native GitHub issue types when available. Otherwise use:

- Epic
- Story
- Task
- Bug

### Current Gate

Use a separate boolean/single-select field:

- Yes
- blank/no

Only `APO-33` should be current gate after reconciliation.

Do not overload lifecycle `Status` with gate semantics.

### Acceptance

Optional lean field:

- Accepted
- Pending
- N/A / Legacy

Use only where acceptance has meaning.

### Jira Key

Prefer a text field if GitHub Projects supports it, but the issue-body provenance block remains mandatory.

### Parent / capability

Prefer native GitHub parent/sub-issue relationships if available. Otherwise retain:
- parent Jira key in the issue body;
- parent/capability field in the Project.

### Minimal labels

Use labels only when they add information not already represented by Project fields:

- `jira-migrated`
- `fast-v1`
- `post-v1`
- `historical`
- `void`

Do not duplicate status/priority as labels.

### Milestone

Use one milestone only if useful:

`FAST V1`

Do not create a generic Post-V1 milestone until Post-V1 scheduling exists.

### Views

- Current Delivery
- FAST V1
- Current Gate
- Post-V1
- Historical / Done

---

## 10. Duplicate analysis

Live GitHub issue search returned no issues in `Hossam1104/AI_Orchestrator`.

Therefore the proposed Phase 2 baseline is:

`69 Jira items -> 69 canonical GitHub issues`

This must still be re-checked immediately before each creation batch.

Rule:

`one Jira work item -> zero or one canonical GitHub issue`

Never create multiple GitHub issues for one Jira key.

---

## 11. Cutover criteria

Phase 2 must not declare GitHub authoritative until all of the following are proven:

1. All 69 Jira items are accounted for.
2. Exactly one canonical GitHub issue exists per migrated Jira item.
3. Every migrated issue contains the Jira key and URL.
4. Parent/capability relationships are preserved or explicitly represented.
5. Priorities are preserved.
6. FAST V1 / Post-V1 / Historical distinctions are preserved.
7. Done work remains Done.
8. Deferred work remains deferred.
9. APO-50 is represented as Done.
10. APO-33 is represented as Ready / current gate / not started.
11. Current-gate uniqueness = exactly one.
12. GitHub Project counts match this manifest.
13. No workflow/CI completion is claimed for APO-33.
14. Jira remains intact and available as historical provenance.
15. Repository authority documents are reconciled to GitHub-native tracking only after migration validation.
16. No product implementation occurs during migration.

---

## 12. Deterministic Phase 2 validation plan

Required checks:

- `JIRA_TOTAL = 69`
- `GITHUB_CANONICAL_MIGRATED_ISSUES = 69`
- `UNMAPPED_JIRA_ITEMS = 0`
- `DUPLICATE_JIRA_KEYS_IN_GITHUB = 0`
- `ISSUES_MISSING_JIRA_KEY = 0`
- `ISSUES_MISSING_JIRA_URL = 0`
- `PARENT_MAPPING_ERRORS = 0`
- `PRIORITY_MAPPING_ERRORS = 0`
- `FAST_V1_BOUNDARY_ERRORS = 0`
- `POST_V1_BOUNDARY_ERRORS = 0`
- `DONE_REGRESSION_ERRORS = 0`
- `VOID_ARTIFACT_ACTIVE_ERRORS = 0`
- `CURRENT_GATE_COUNT = 1`
- `CURRENT_GATE = APO-33`
- `APO-33_IMPLEMENTATION_STATE = NOT STARTED`
- `APO-50_RECONCILED_STATE = DONE`
- `JIRA_HISTORICAL_ACCESS = AVAILABLE`
- `GITHUB_ACTIONS_CI = NONE / NOT CLAIMED` until APO-33 implementation is separately authorized and proven.

Validate all dependency pairs and preserve the additional live APO-45 -> APO-68 relationship.

---

## 13. Rollback / recovery approach

Phase 2 migration should be batchable and idempotent:

1. Create/search by Jira key before every write.
2. Record GitHub issue number immediately after each successful creation.
3. Stop on ambiguous or partial writes.
4. Do not resend a creation after uncertain outcome until search proves whether the issue exists.
5. Never delete Jira.
6. Never rewrite historical Jira comments.
7. If Project-field application fails after issue creation, leave the issue intact and record `RECONCILIATION REQUIRED`; resume only the missing metadata step.
8. Do not close or mutate Jira until the GitHub migration has been independently validated.
9. Do not use destructive rollback of historical issues; fix forward with auditable corrections.

---

## 14. Explicit items that must NOT change in Phase 1

- No bulk GitHub issue creation.
- No GitHub Project cutover.
- No Jira deletion.
- No Jira bulk close.
- No old Jira comment rewrite.
- No Jira relationship removal.
- No APO-33 implementation.
- No GitHub Actions implementation.
- No roadmap feature work.
- No product refactor.
- No namespace/compatibility mass rename.
- No direct-main governance bypass.

---

## 15. Repository artifact write status

Target artifact:

`.ai/migration/APO_GITHUB_MIGRATION_MANIFEST.md`

Planned branch:

`docs/APO-github-migration-phase1`

The branch did not exist and a branch creation request was attempted from exact current `main`.

Result:

`403 Resource not accessible by integration`

Therefore the migration manifest could not be durably committed to the repository without violating the repository’s no-direct-main governance.

This local file is a prepared fallback artifact only. It is **not** a substitute for the required durable repository manifest.

---

## 16. GitHub Projects tool-surface status

The connected GitHub integration exposes repository, PR, issue, file, branch, and workflow-run operations, but does not expose GitHub Projects v2 project/field/item management operations.

No suitable installed GitHub Projects-specific plugin was found.

Therefore Phase 2 currently lacks a connected tool surface for deterministic GitHub Projects configuration even after issue migration.

---

## 17. Prompt 2 entry criteria

Prompt 2 becomes executable only after:

1. GitHub branch/ref creation permission is available so this manifest can be committed through a normal branch/PR path; and
2. A supported GitHub Projects v2 management surface is available, or the owner explicitly approves a revised GitHub-native target that uses GitHub Issues/labels/milestones without Projects v2.

Until then:

`MIGRATION PHASE 2 BLOCKED`

Smallest immediate blocker:

`GitHub integration cannot create a branch/ref (403), so the required durable Phase 1 migration manifest cannot be committed without bypassing repository governance.`

---

## 18. Runtime boundary

The APO desktop application was not launched by this Phase 1 reconciliation.

Current local process count cannot be read from this chat environment and is therefore:

`APO PROCESS COUNT = NOT AVAILABLE`

No claim is made that the application is running or stopped on the owner’s machine.

