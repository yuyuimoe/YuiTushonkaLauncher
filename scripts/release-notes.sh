#!/usr/bin/env bash
# Turns the Conventional Commits between two tags into the release body.
#   scripts/release-notes.sh v0.1.0            (since the previous tag)
#   scripts/release-notes.sh v0.1.0 v0.0.9     (explicit range)
set -euo pipefail

tag="${1:?usage: release-notes.sh TAG [PREVIOUS]}"
previous="${2:-$(git describe --tags --abbrev=0 "$tag^" 2>/dev/null || echo "")}"
range="$([ -n "$previous" ] && echo "$previous..$tag" || echo "$tag")"

emit() {
  local heading="$1" pattern="$2" lines
  lines="$(git log "$range" --no-merges --pretty=format:'%s' | grep -E "$pattern" || true)"
  if [ -n "$lines" ]; then
    echo "### $heading"
    echo
    # "feat(scope): subject" -> "- scope: subject" keeps the scopes visible without the type noise.
    echo "$lines" | sed -E 's/^[a-z]+(\(([^)]+)\))?!?: /- \2: /; s/^- : /- /'
    echo
  fi
}

echo "## What changed"
echo
emit "New features" "^feat(\(|:|!)"
emit "Fixes" "^fix(\(|:|!)"
emit "Performance" "^perf(\(|:|!)"
emit "Other changes" "^(refactor|docs|build|ci|test|chore|style)(\(|:|!)"
emit "Breaking changes" "^[a-z]+(\([^)]+\))?!:"

echo "## Artifacts"
echo
echo "| File | What it is |"
echo "|---|---|"
echo "| \`YuiTushonkaLauncher-$tag-win-x64-setup.exe\` | Windows installer (per-user, no admin) |"
echo "| \`YuiTushonkaLauncher-$tag-win-x64.zip\` | Windows, framework-dependent (needs .NET 10, as SPT does) |"
echo "| \`YuiTushonkaLauncher-$tag-win-x64-selfcontained.zip\` | Windows, no .NET needed |"
echo "| \`YuiTushonkaLauncher-$tag-x86_64.AppImage\` | Linux, single file |"
echo "| \`YuiTushonkaLauncher-$tag-linux-x64.tar.gz\` | Linux, framework-dependent |"
echo "| \`YuiTushonkaLauncher-$tag-linux-x64-selfcontained.tar.gz\` | Linux, no .NET needed |"
echo "| \`SHA256SUMS\` | Checksums for all of the above |"
echo
echo "Every build reports \`$tag\` from \`--version\`, the about box and the file properties."
