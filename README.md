# YuiTushonkaLauncher

Cross-platform (Windows and Linux) launcher and instance manager for **Single Player Tarkov (SPT)**.
It replaces the official SPT launcher and adds Prism-Launcher-style instance and mod management:
hard-linked instances, Forge API mod installs, GUID-based mod identification, binary patching,
server/profile management, unified logs, and shareable instance packages.

The authoritative plan lives in [`docs/plan/`](docs/plan/README.md).

**Project**                        | **Function**
---------------------------------- | ------------------------------------------------------------
`YuiTushonkaLauncher.Core`          | Domain model, contracts and pure business rules
`YuiTushonkaLauncher.Patching`      | SPT binary delta engine (`SharpHDiffPatch.Core`)
`YuiTushonkaLauncher.Infrastructure`| EF Core/SQLite, Forge API, file linking, DLL metadata, processes
`YuiTushonkaLauncher.Ui`            | Avalonia UI (assembly name `YuiTushonkaLauncher`)
`YuiTushonkaLauncher.Tests`         | Unit, mocked-HTTP and headless UI tests

## Privacy

This project is open source: your commit credentials as author of a commit will be visible to anyone.
If you prefer not to publish your real identity, use a local override:

```bash
git config --local user.name "USERNAME"
git config --local user.email "USERNAME@SOMETHING.com"
```

## Requirements

- Escape From Tarkov, patched by the official SPT installer (`*.spt-bak` present in `EscapeFromTarkov_Data/Managed`)
- .NET 10 SDK — the same runtime SPT itself requires
- Visual Studio, JetBrains Rider, or the CLI

## Build

```bash
dotnet tool restore
dotnet build YuiTushonkaLauncher.slnx
dotnet test YuiTushonkaLauncher.slnx
dotnet run --project YuiTushonkaLauncher.Ui
```

Publish a single-file build (framework-dependent, since .NET 10 is already required by SPT):

```bash
dotnet publish "./YuiTushonkaLauncher.Ui/YuiTushonkaLauncher.Ui.csproj" \
  -c Release --self-contained false --framework net10.0 \
  --runtime linux-x64 -p:PublishSingleFile=true -p:Version=0.1.0
```

`--runtime` is `win-x64` or `linux-x64`. Without `-p:Version`, the build stamps
`X.Y.Z-pre-<sha>[.dirty]` derived from git (see [`docs/workflow/git.md`](docs/workflow/git.md)).

## Code Style

We use [CSharpier](https://csharpier.com/) to keep the code consistently formatted, exactly like the
official SPT launcher. It is pinned as a local tool, so no global install is required:

```bash
dotnet tool restore
dotnet csharpier format .
```

Run it before opening a PR: the `CI` workflow fails the pull request when formatting changes are
required (`dotnet csharpier check .`).

Semantic preferences (naming, braces, `var`, expression bodies) live in [`.editorconfig`](.editorconfig)
and follow the official launcher's rules: file-scoped namespaces, braces always, block-bodied members,
`_camelCase` private fields, PascalCase constants, 140-column limit.

### Format On Save

Plugins for [VS Code](https://marketplace.visualstudio.com/items?itemName=csharpier.csharpier-vscode)
and [Rider](https://plugins.jetbrains.com/plugin/18243-csharpier) format the file on save.

## Contributing

- Branch from `develop` with a worktree: `wt switch --create feature/<slug>`.
- Conventional Commits are enforced by `.githooks/commit-msg`.
- Docs and code comments are English; commit messages are English.
- Details: [`docs/workflow/git.md`](docs/workflow/git.md) and [`AGENTS.md`](AGENTS.md).
