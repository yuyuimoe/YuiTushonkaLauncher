#!/usr/bin/env python3
"""Mutation check for this repository.

Why a script and not "edit and run the tests": a mutant must be judged by a *fresh* test process whose test count is
verified first. In this repository a mutated UI assembly once made `dotnet test` discover 25 fewer tests and run none of
the affected ones, which turned every verdict into a false "survived".

Each mutant:
  1. applies one textual edit (the file is restored in a finally block, always);
  1. builds; a build failure is reported, not counted;
  2. runs the full suite, refusing to judge if the collected test count differs from the baseline;
  3. reports SURVIVED / KILLED with the first failing test.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import subprocess
import sys

MUTANTS = {
    "session-order": (
        "YuiTushonkaLauncher.Core/Launch/LaunchSession.cs",
        "&& progress.Step < furthest",
        "&& progress.Step <= furthest",
        "a stale progress report must not move the state backwards",
    ),
    "session-cancel": (
        "YuiTushonkaLauncher.Core/Launch/LaunchSession.cs",
        "                    is LaunchButtonState.PreFlight\n                        or LaunchButtonState.Patching",
        "                    is LaunchButtonState.Running\n                        or LaunchButtonState.Patching",
        "cancel must not be offered once the pair runs",
    ),
    "language-tag": (
        "YuiTushonkaLauncher.Ui/Localization/JsonLocalizer.cs",
        "var language = requested.Split('-', '_')[0];",
        "var language = string.Empty;",
        "a culture tag like pt_BR must resolve to pt-BR",
    ),
    "busy-guard": (
        "YuiTushonkaLauncher.Ui/ViewModels/PageViewModel.cs",
        "        if (IsBusy)\n        {\n            return;\n        }",
        "        if (IsBusy && !IsBusy)\n        {\n            return;\n        }",
        "a page must refuse a second operation while one is in flight",
    ),
    "active-card": (
        "YuiTushonkaLauncher.Ui/ViewModels/InstancesPageViewModel.cs",
        "new InstanceCardViewModel(Localizer, instance, instance.Id == activeId)",
        "new InstanceCardViewModel(Localizer, instance, false)",
        "the active instance must be marked on its card",
    ),
    "unrecognized-update": (
        "YuiTushonkaLauncher.Infrastructure/Mods/ModUpdateService.cs",
        "if (mod.Recognition is ModRecognition.Unrecognized or ModRecognition.GuidMissing)",
        "if (false)",
        "an unrecognised mod must never be auto-updated",
    ),
    "update-skip": (
        "YuiTushonkaLauncher.Infrastructure/Logs/LogReader.cs",
        "var start = Math.Max(0, stream.Length - WindowBytes);",
        "var start = 0L;",
        "a log tail must read a window, not the whole file",
    ),
}

def run(args: list[str], cwd: pathlib.Path, timeout: int) -> subprocess.CompletedProcess[str]:
    return subprocess.run(args, cwd=cwd, capture_output=True, text=True, timeout=timeout)


def test_count(output: str) -> int | None:
    """The number xunit reported, with the thousands separators some locales add."""
    if "Total:" not in output:
        return None
    tail = output.split("Total:")[-1].replace(",", "")
    digits = tail.split()[0] if tail.split() else ""
    return int(digits) if digits.isdigit() else None


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the mutation check")
    parser.add_argument("--root", default=".", help="repository root")
    parser.add_argument("--only", action="append", default=[], help="mutant name (repeatable)")
    parser.add_argument("--json", default="", help="write the results here")
    arguments = parser.parse_args()

    root = pathlib.Path(arguments.root).resolve()
    names = arguments.only or list(MUTANTS)
    results: list[dict[str, object]] = []

    baseline = run(["dotnet", "test", "YuiTushonkaLauncher.Tests", "--no-build", "-c", "Release", "--nologo"], root, 600)
    expected = test_count(baseline.stdout + baseline.stderr)
    if expected is None:
        print("Could not read the baseline test count; refusing to judge anything.", file=sys.stderr)
        return 2
    print(f"baseline: {expected} tests")

    for name in names:
        relative, old, new, why = MUTANTS[name]
        path = root / relative
        original = path.read_text()
        if original.count(old) != 1:
            print(f"{name:22s} PATTERN x{original.count(old)} (the code moved; update the mutation)")
            results.append({"mutant": name, "verdict": "pattern", "why": why})
            continue

        verdict = "error"
        detail = ""
        try:
            path.write_text(original.replace(old, new))
            build = run(["dotnet", "build", "YuiTushonkaLauncher.Tests", "-c", "Release", "--nologo", "-v", "q"], root, 300)
            if "Build succeeded" not in (build.stdout + build.stderr):
                verdict = "no-compile"
            else:
                test = run(["dotnet", "test", "YuiTushonkaLauncher.Tests", "--no-build", "-c", "Release", "--nologo"], root, 600)
                output = test.stdout + test.stderr
                total = test_count(output)
                if total != expected:
                    # The harness cannot see the affected tests; a verdict here would be a lie.
                    verdict = "unjudged"
                    detail = f"test count {total} != {expected}"
                else:
                    failed = [line.strip() for line in output.splitlines() if line.strip().startswith("Failed Yui")]
                    verdict = "killed" if failed else "survived"
                    detail = failed[0] if failed else ""
        finally:
            path.write_text(original)

        print(f"{name:22s} {verdict.upper():10s} {detail}")
        results.append({"mutant": name, "verdict": verdict, "detail": detail, "why": why})

    if arguments.json:
        pathlib.Path(arguments.json).write_text(json.dumps(results, indent=2))

    killed = sum(1 for result in results if result["verdict"] == "killed")
    judging = sum(1 for result in results if result["verdict"] in {"killed", "survived"})
    print(f"killed {killed}/{judging} judged ({len(results) - judging} not judged)")
    return 0 if killed == judging else 1


if __name__ == "__main__":
    sys.exit(main())
