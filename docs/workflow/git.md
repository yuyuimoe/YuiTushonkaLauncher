# Git workflow

## Model

**GitFlow**, initialized with `git flow init -d` (git-flow-next 2.1.0):

| Branch | Role |
|---|---|
| `main` | released history; every commit is a tag |
| `develop` | integration branch; the app is always buildable here |
| `feature/<slug>` | one phase or one feature; forks `develop`, merges back with `--no-ff` |
| `bugfix/<slug>` | like feature, for defects found during integration |
| `release/<X.Y.Z>` | stabilisation; forks `develop`, finishes into `main` + `develop`, tagged |
| `hotfix/<X.Y.Z>` | from `main`, finishes into `main` + `develop` and bumps the patch |
| `support/<X.Y>` | maintenance of an old line |

Never commit directly to `main` or `develop`; never rebase a branch that someone else may have pulled
(only `feature`/`bugfix` branches are rebased, and only while private).

## Worktrees

One worktree per branch, managed with **`wt`** (worktrunk):

```
wt switch --create feature/phase-04-instances   # new worktree + branch from develop
wt list                                        # status of every worktree
wt switch develop                              # jump back
wt merge                                       # merge current branch into its target, run hooks
wt remove                                      # delete the worktree once merged
```

`git flow feature start X` is not used: `wt switch --create feature/X` gives the same branch with a
worktree, and `wt merge` respects the flow. Finish a `release/` branch with
`git flow release finish <ver>` because that is what tags and bumps `main` + `develop` together.

Project hooks are declared in `.config/wt.toml` (`post-start` restores packages, `pre-merge` builds
Release). `wt` asks for approval the first time it runs them.

## Commits — Conventional Commits

Enforced by `.githooks/commit-msg` (`git config core.hooksPath .githooks`):

```
<type>(<scope>)!: <subject>
```

- types: `build` `ci` `chore` `docs` `feat` `fix` `perf` `refactor` `revert` `style` `test`
- scopes: `instances`, `mods`, `forge`, `patch`, `launcher`, `persistence`, `ui`, `logs`, `plan`,
  `repo`, `ci`
- subject: imperative, lower-case, no trailing period, ≤ 100 chars
- `!` marks a breaking change; the body must then start with `BREAKING CHANGE:`
- bodies explain *why*; keep them wrapped at 100 columns
- `git merge`/`git flow` merge commits are exempt

## Versioning — `X.Y.Z-pre-<hash>[.dirty]`

`Directory.Build.props` derives the version at build time:

| Tree state | Version |
|---|---|
| clean, at commit `a1b2c3d` | `0.1.0-pre-a1b2c3d` |
| tracked files modified/staged | `0.1.0-pre-a1b2c3d.dirty` |
| no git available | `0.1.0` |

- `VersionPrefix` in `Directory.Build.props` is the single place to bump `X.Y.Z`.
- `AssemblyVersion` stays `0.0.0.0` and `FileVersion` is `X.Y.Z.0`: nothing binds to them, and a sha in
  an assembly version breaks binding.
- A tagged release overrides the whole thing: `dotnet publish -p:Version=0.2.0`.
- Semver meaning: `MAJOR` = database/instance-format break, `MINOR` = feature, `PATCH` = fix.
  Pre-release identifiers are only produced by the build, never committed.

## Tags

`v<X.Y.Z>` on `main` only, created by `git flow release finish` / `hotfix finish`. Annotated
(`git tag -a`) with the release notes summary in the message.

## Pre-merge checklist

1. `dotnet build --nologo -c Release` clean.
2. `dotnet test` green, coverage target met for touched projects
   ([phase 13](../plan/phase-13-qa.md)).
3. Phase file updated: task boxes ticked, deviations recorded, or items moved to
   [90-open-questions](../plan/90-open-questions.md).
4. `CHANGELOG.md` entry under `Unreleased` (Keep a Changelog format).
5. No `TODO` without an issue reference or an open-question link.

## Repository hygiene

- Docs and code comments are **English**; the conversation with the user is Portuguese.
- `.gitattributes` normalizes to LF; `*.bat`/`*.cmd` stay CRLF.
- Never commit `bin/`, `obj/`, `local/`, downloaded game files, instance directories, or logs.
- Generated artifacts (build output, coverage) go in CI artifacts, not in the tree.
