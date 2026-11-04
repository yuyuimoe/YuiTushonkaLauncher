# AGENTS.md

## Language

- **Documentation, comments, commit messages and identifiers: English.**
- **Conversation with the maintainer: Portuguese (pt-BR).**

## Commands

- Restore toolchain: `dotnet tool restore` (CSharpier, `dotnet-ef`, both pinned)
- Build all: `dotnet build YuiTushonkaLauncher.slnx`
- Test all: `dotnet test YuiTushonkaLauncher.slnx`
- Format: `dotnet csharpier format .` — CI fails on `dotnet csharpier check .`
- Run: `dotnet run --project YuiTushonkaLauncher.Ui`
- Migrations (`Microsoft.EntityFrameworkCore.Design` must be referenced by the startup project too):
  - `dotnet ef migrations add <Name> -p YuiTushonkaLauncher.Infrastructure -s YuiTushonkaLauncher.Ui`
  - `dotnet ef database update -p YuiTushonkaLauncher.Infrastructure -s YuiTushonkaLauncher.Ui`
- Smoke test without a display: `dotnet run --project YuiTushonkaLauncher.Ui -- --migrate-only`
- Coverage: `dotnet test YuiTushonkaLauncher.slnx --settings coverlet.runsettings --collect:"XPlat Code Coverage"`

## Code style

Mirrors the official SPT launcher: **CSharpier** formats (never hand-align, never fight it), and
`.editorconfig` holds the semantic rules — 140 columns, braces always, block-bodied members,
file-scoped namespaces, `var`, `_camelCase` private fields, PascalCase constants.

## Workflow

- GitFlow + worktrees via `wt`: `wt switch --create feature/<slug>`, `wt merge`, `wt remove`.
  Details: `docs/workflow/git.md`.
- Conventional Commits, enforced by `.githooks/commit-msg` (`core.hooksPath=.githooks`).
- Version is derived from git: `X.Y.Z-pre-<sha>[.dirty]` (`Directory.Build.props`).
- Never commit to `main`/`develop` directly; one phase per branch.

## Map

- `docs/plan/` — the plan, split per phase. Start at `docs/plan/README.md`.
- `docs/plan/03-verified-facts.md` — **the** source of truth for SPT/Forge behaviour. Every claim about
  SPT comes from there (or gets added there).
- `docs/plan/04-decisions.md` — decisions that override the raw plan.
- `docs/plan/90-open-questions.md` — unresolved items; check before inventing behaviour.
- `docs/raw_plan.md` — original (translated) plan, history only.
- `docs/raw_plan_review.md` — the critique that produced the decisions.

## Projects

- `YuiTushonkaLauncher.Core` — domain (`SemanticVersion`, `VersionRange`, entities), interfaces, pure
  rules. No I/O.
- `YuiTushonkaLauncher.Patching` — `SharpHDiffPatch.Core` wrapper, delta discovery, backup/restore.
- `YuiTushonkaLauncher.Infrastructure` — EF Core/SQLite, Forge API, file linking, DLL metadata readers,
  processes, Proton.
- `YuiTushonkaLauncher.Ui` — Avalonia views/view models, locale JSON, composition root.
- `YuiTushonkaLauncher.Tests` — unit, mocked HTTP, headless UI.

## Non-negotiables

- **Never** download Tarkov game files. The user supplies an SPT-installer output folder.
- `Assembly-CSharp.dll` is **copied**, never hard-linked, in any instance.
- **Copy-on-write before every write**: no write through a hard link, ever. Use
  `ILinkAwareFileWriter`.
- Every patch backs up to `<file>.spt-bak` first, and the patch is applied `backup → target`.
- No path from an archive or manifest is used without `RelativePath` validation (zip-slip).
- Hard links require the same volume; check `st_dev`/volume serial before creating an instance.
- Logs, docs and errors in the UI are localized; log *messages* stay English.

## Sources to consult

- Forge API: `https://sp-mod.com/api/v0` (no auth, 300 req/min, `x-ratelimit-*` headers).
  Local OpenAPI snapshot: `../TushonkaMirror/openapi.yaml`.
- Official launcher: `https://github.com/SP-Tushonka/launcher` (`4.1.x-dev`, `mod-manager`).
  Use `SP-Tushonka`, **never** `sp-tarkov`; deepwiki repo names are case-sensitive and its
  `sp-tarkov/server` wiki is stale (pre-4.0).
- Local SPT installs for evidence: `~/Games/spt40/drive_c/Program Files/base_tarkov{41,}`.
