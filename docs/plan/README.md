# Implementation plan

Per-phase files. Each phase is one worktree, one branch, one merge; the phase file is the contract
for what has to exist when the branch merges.

**Read order for a new session:** [00-overview](00-overview.md) → [01-architecture](01-architecture.md) →
[03-verified-facts](03-verified-facts.md) → [04-decisions](04-decisions.md) → the phase you are on.

## Status

| Phase | File | Status |
|---|---|---|
| 0 | [phase-00-research](phase-00-research.md) | ☑ done (see [03-verified-facts](03-verified-facts.md)) |
| 1 | [phase-01-foundation](phase-01-foundation.md) | ☑ done |
| 2 | [phase-02-persistence](phase-02-persistence.md) | ☑ done |
| 3 | [phase-03-forge-api](phase-03-forge-api.md) | ☑ done |
| 4 | [phase-04-instances](phase-04-instances.md) | ☑ done |
| 5 | [phase-05-mod-install](phase-05-mod-install.md) | ☑ done |
| 6 | [phase-06-dependencies-migration](phase-06-dependencies-migration.md) | ☑ done |
| 7 | [phase-07-patching](phase-07-patching.md) | ☑ done |
| 8 | [phase-08-export-import](phase-08-export-import.md) | ☑ done |
| 9 | [phase-09-linux-proton](phase-09-linux-proton.md) | ☑ done |
| 10 | [phase-10-server-profiles](phase-10-server-profiles.md) | ☑ done |
| 11 | [phase-11-launch-logs](phase-11-launch-logs.md) | ☑ done |
| 12 | [phase-12-ui](phase-12-ui.md) | ☑ done |
| 13 | [phase-13-qa](phase-13-qa.md) | ☑ done |
| 14 | [phase-14-packaging](phase-14-packaging.md) | ☑ done |
| 15 | [phase-15-instances-home](phase-15-instances-home.md) | ☑ done |
| 16 | [phase-16-forge-browser](phase-16-forge-browser.md) | ☑ done |
| 17 | [phase-17-mods-addons](phase-17-mods-addons.md) | ☑ done |

Supporting documents:

- [00-overview](00-overview.md) — what the product is, scope, stack, non-goals.
- [01-architecture](01-architecture.md) — projects, dependency rules, file-link policy, data flow.
- [02-domain-model](02-domain-model.md) — entities, value objects, invariants.
- [03-verified-facts](03-verified-facts.md) — everything measured/fetched locally, with source paths.
- [04-decisions](04-decisions.md) — the resolved decisions and the reasoning behind each.
- [90-open-questions](90-open-questions.md) — what is still unresolved.
- [../workflow/git.md](../workflow/git.md) — GitFlow, worktrees, Conventional Commits, versioning.
- [../raw_plan.md](../raw_plan.md) — the original (translated) plan, kept as history.
- [../raw_plan_review.md](../raw_plan_review.md) — the critique that produced the decisions.

## Definition of done (every phase)

1. `dotnet build` clean, `dotnet test` green.
2. Every task item in the phase file is either implemented or moved to
   [90-open-questions](90-open-questions.md) with a reason.
3. Public API documented (`GenerateDocumentationFile` is on repo-wide per `Build.props`-style
   conventions in this repo).
4. Acceptance criteria in the phase file are demonstrated — a test, a log line, or a manual run
   recorded in the PR body.
5. Coverage target met for the touched projects — see [phase-13-qa](phase-13-qa.md) (90% overall).
