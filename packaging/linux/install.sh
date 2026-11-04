#!/bin/sh
# Installs the launcher for the current user: no root, no package manager.
#   ./install.sh            install into ~/.local
#   PREFIX=/usr ./install.sh (as root) install into /usr
set -eu

here="$(cd "$(dirname "$0")" && pwd)"
prefix="${PREFIX:-$HOME/.local}"
bindir="$prefix/bin"
share="$prefix/share"

install -Dm755 "$here/YuiTushonkaLauncher" "$bindir/YuiTushonkaLauncher"
install -Dm644 "$here/share/applications/yui-tushonka-launcher.desktop" "$share/applications/yui-tushonka-launcher.desktop"
install -Dm644 "$here/share/icons/hicolor/256x256/apps/com.sptushonka.YuiTushonkaLauncher.png" "$share/icons/hicolor/256x256/apps/com.sptushonka.YuiTushonkaLauncher.png"
install -Dm644 "$here/share/mime/packages/application-x-sptpack.xml" "$share/mime/packages/application-x-sptpack.xml"
install -Dm644 "$here/share/metainfo/com.sptushonka.YuiTushonkaLauncher.metainfo.xml" "$share/metainfo/com.sptushonka.YuiTushonkaLauncher.metainfo.xml"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$share/applications" || true
command -v update-mime-database >/dev/null 2>&1 && update-mime-database "$share/mime" || true
command -v xdg-mime >/dev/null 2>&1 && xdg-mime default yui-tushonka-launcher.desktop application/x-sptpack || true

echo "Installed into $prefix. Make sure $bindir is on your PATH."
