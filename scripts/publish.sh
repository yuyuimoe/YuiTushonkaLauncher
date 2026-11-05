#!/usr/bin/env bash
# Publishes the launcher for the supported targets and packs what a player downloads.
#
#   scripts/publish.sh [--version X.Y.Z] [--out DIR] [--no-tests] [--only linux|windows]
#
# Framework-dependent is the primary download because SPT itself requires .NET 10; the self-contained build is the
# fallback for a machine where that is not true yet. No trimming, no AOT (D14).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version=""
out="$root/dist"
run_tests=1
only=""

while [ $# -gt 0 ]; do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --no-tests) run_tests=0; shift ;;
    --only) only="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [ -z "$version" ]; then
  version="$(cd "$root" && git describe --tags --always --dirty 2>/dev/null || echo "0.1.0")"
fi

# A tag names a release; anything else is a development build and keeps the sha/.dirty so it can never be mistaken for
# one.
release_version="${version#v}"
release_args=(-p:GitVersion=false "-p:VersionPrefix=$release_version" "-p:Version=$release_version")

echo "==> version $release_version"
echo "==> output $out"
rm -rf "$out"
mkdir -p "$out"

if [ "$run_tests" = 1 ]; then
  echo "==> build and test"
  dotnet build "$root/YuiTushonkaLauncher.slnx" -c Release --nologo
  dotnet test "$root/YuiTushonkaLauncher.Tests" --no-build -c Release --nologo
fi

# Packs a published folder into a zip. The Windows runner has no `zip` (a Git Bash without Info-ZIP), and the job had
# never run, so the first real Windows release died on `zip: command not found`; 7-Zip is what the image ships instead.
zip_folder() {
  local destination="$1" folder="$2"
  rm -f "$destination"

  if command -v zip >/dev/null 2>&1; then
    (cd "$(dirname "$folder")" && zip -qr "$destination" "$(basename "$folder")")
  elif command -v 7z >/dev/null 2>&1; then
    (cd "$(dirname "$folder")" && 7z a -tzip -bso0 -bsp0 "$destination" "$(basename "$folder")")
  else
    echo "no zip tool found (need 'zip' or '7z'): cannot pack $folder" >&2
    exit 1
  fi
}

publish() {
  local rid="$1" self_contained="$2" destination="$3"
  echo "==> publish $rid (self-contained: $self_contained)"
  dotnet publish "$root/YuiTushonkaLauncher.Ui" -c Release -r "$rid" \
    --self-contained "$self_contained" -o "$destination" --nologo \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=$([ "$self_contained" = true ] && echo true || echo false) \
    -p:DebugType=none \
    -p:AllowedReferenceRelatedFileExtensions=none \
    "${release_args[@]}"
}

appimage() {
  local published="$1" appdir="$2" target="$3"
  rm -rf "$appdir"
  install -Dm755 "$published/YuiTushonkaLauncher" "$appdir/usr/bin/YuiTushonkaLauncher"
  install -Dm644 "$root/packaging/linux/yui-tushonka-launcher.desktop" "$appdir/yui-tushonka-launcher.desktop"
  install -Dm644 "$root/packaging/linux/yui-tushonka-launcher.desktop" "$appdir/usr/share/applications/yui-tushonka-launcher.desktop"
  install -Dm644 "$root/packaging/linux/icons/yui-tushonka.png" "$appdir/com.sptushonka.YuiTushonkaLauncher.png"
  install -Dm644 "$root/packaging/linux/icons/yui-tushonka.png" "$appdir/usr/share/icons/hicolor/256x256/apps/com.sptushonka.YuiTushonkaLauncher.png"
  install -Dm644 "$root/packaging/linux/application-x-sptpack.xml" "$appdir/usr/share/mime/packages/application-x-sptpack.xml"
  install -Dm644 "$root/packaging/linux/com.sptushonka.YuiTushonkaLauncher.metainfo.xml" "$appdir/usr/share/metainfo/com.sptushonka.YuiTushonkaLauncher.metainfo.xml"
  printf '#!/bin/sh\nexec "$APPDIR/usr/bin/YuiTushonkaLauncher" "$@"\n' > "$appdir/AppRun"
  chmod +x "$appdir/AppRun"
  ln -sf com.sptushonka.YuiTushonkaLauncher.png "$appdir/.DirIcon"

  if command -v appimagetool >/dev/null 2>&1; then
    ARCH=x86_64 VERSION="$release_version" appimagetool --no-appstream "$appdir" "$target"
    echo "==> $target"
  else
    # An AppImage needs appimagetool; the AppDir itself is a usable artifact and is what the release workflow converts.
    tar -C "$(dirname "$appdir")" -czf "$target.appdir.tar.gz" "$(basename "$appdir")"
    echo "==> appimagetool not found: kept the AppDir as $(basename "$target").appdir.tar.gz"
  fi
}

linux_tarball() {
  local published="$1" destination="$2"
  local staging="$out/.tarball"
  rm -rf "$staging"
  mkdir -p "$staging"
  cp -a "$published/." "$staging/"
  install -Dm644 "$root/packaging/linux/yui-tushonka-launcher.desktop" "$staging/share/applications/yui-tushonka-launcher.desktop"
  install -Dm644 "$root/packaging/linux/icons/yui-tushonka.png" "$staging/share/icons/hicolor/256x256/apps/com.sptushonka.YuiTushonkaLauncher.png"
  install -Dm644 "$root/packaging/linux/application-x-sptpack.xml" "$staging/share/mime/packages/application-x-sptpack.xml"
  install -Dm644 "$root/packaging/linux/com.sptushonka.YuiTushonkaLauncher.metainfo.xml" "$staging/share/metainfo/com.sptushonka.YuiTushonkaLauncher.metainfo.xml"
  install -Dm755 "$root/packaging/linux/install.sh" "$staging/install.sh"
  tar -C "$staging" -czf "$destination" .
  echo "==> $destination"
}

case "$only" in
  ""|linux)
    publish linux-x64 false "$out/linux-x64-fdd"
    publish linux-x64 true "$out/linux-x64-scd"
    linux_tarball "$out/linux-x64-fdd" "$out/YuiTushonkaLauncher-$release_version-linux-x64.tar.gz"
    linux_tarball "$out/linux-x64-scd" "$out/YuiTushonkaLauncher-$release_version-linux-x64-selfcontained.tar.gz"
    appimage "$out/linux-x64-scd" "$out/YuiTushonkaLauncher.AppDir" "$out/YuiTushonkaLauncher-$release_version-x86_64.AppImage"
    ;;
esac

case "$only" in
  ""|windows)
    publish win-x64 false "$out/win-x64-fdd"
    publish win-x64 true "$out/win-x64-scd"
    zip_folder "$out/YuiTushonkaLauncher-$release_version-win-x64.zip" "$out/win-x64-fdd"
    zip_folder "$out/YuiTushonkaLauncher-$release_version-win-x64-selfcontained.zip" "$out/win-x64-scd"
    echo "==> windows zips"
    ;;
esac

echo "==> checksums"
(
  cd "$out"
  # A pre-release is Linux only, and a machine without appimagetool keeps the AppDir instead; a pattern that matched
  # nothing must not fail the run.
  shopt -s nullglob
  artifacts=(*.tar.gz *.zip *.AppImage *.appdir.tar.gz)
  if [ ${#artifacts[@]} -eq 0 ]; then
    echo "no artifacts to checksum" >&2
    exit 1
  fi
  # "*.tar.gz" also matches the AppDir tarball; sort -u keeps one line per file.
  sha256sum "${artifacts[@]}" | sort -k2 -u | tee SHA256SUMS
)

echo "==> done"
