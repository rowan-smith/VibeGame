#!/usr/bin/env bash
# One-time Wine prefix setup for compiling MonoGame MGFX shaders on Linux.
# See: https://docs.monogame.net/articles/getting_started/1_setting_up_your_os_for_development_ubuntu.html
set -euo pipefail

if ! command -v wine >/dev/null 2>&1; then
  echo "wine is required. Install with: sudo apt install wine"
  exit 1
fi
if ! command -v 7z >/dev/null 2>&1; then
  echo "7z is required. Install with: sudo apt install p7zip-full"
  exit 1
fi
if ! command -v wine64 >/dev/null 2>&1; then
  sudo ln -sf "$(command -v wine)" /usr/local/bin/wine64
fi

export WINEARCH=win64
export WINEPREFIX="${MGFXC_WINE_PATH:-$HOME/.winemonogame}"
export MGFXC_WINE_PATH="$WINEPREFIX"
TEMP_DIR="${TMPDIR:-/tmp}/winemg2"
mkdir -p "$TEMP_DIR"

if [[ ! -f "$WINEPREFIX/drive_c/Program Files/dotnet/dotnet.exe" ]]; then
  rm -rf "$WINEPREFIX"
  xvfb-run -a wineboot --init
  mkdir -p "$WINEPREFIX/drive_c/Program Files/dotnet"
  DOTNET_URL="https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.201/dotnet-sdk-8.0.201-win-x64.zip"
  curl -L "$DOTNET_URL" --output "$TEMP_DIR/dotnet-sdk.zip"
  7z x "$TEMP_DIR/dotnet-sdk.zip" -o"$WINEPREFIX/drive_c/Program Files/dotnet/" -y
  FIREFOX_URL="https://download-installer.cdn.mozilla.net/pub/firefox/releases/62.0.3/win64/ach/Firefox%20Setup%2062.0.3.exe"
  curl -L "$FIREFOX_URL" --output "$TEMP_DIR/firefox.exe"
  7z e "$TEMP_DIR/firefox.exe" "core/d3dcompiler_47.dll" -o"$WINEPREFIX/drive_c/windows/system32/" -aoa
  cat > "$TEMP_DIR/winepath.reg" <<'EOF'
REGEDIT4

[HKEY_CURRENT_USER\Environment]
"PATH"="C:\\Program Files\\dotnet;C:\\windows\\system32;C:\\windows;C:\\windows\\system32\\wbem"
EOF
  xvfb-run -a wine regedit /S "$TEMP_DIR/winepath.reg"
fi

if ! dotnet tool list -g | grep -q dotnet-mgfxc; then
  dotnet tool install -g dotnet-mgfxc
fi

echo "MGFXC_WINE_PATH=$MGFXC_WINE_PATH"
echo "Add to your shell profile: export MGFXC_WINE_PATH=\"$MGFXC_WINE_PATH\""
echo "Then run: scripts/compile-terrain-shaders.sh"
