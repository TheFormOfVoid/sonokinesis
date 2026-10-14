#!/usr/bin/env bash
# Builds a release folder and zip in dist/:
#   Beatform-<version>/
#     Beatform Editor/        exported editor (needs Godot .NET export templates; skipped if missing)
#     loader/bepinex/         BepInEx 6 IL2CPP, pinned build, checked by SHA-256
#     loader/plugin/          Beatform.Loader.dll and its audio decoders
#     loader/levels/          the demo level
#     Install-Beatform.bat / Uninstall-Beatform.bat (+ .ps1)
#     README.md, THIRD-PARTY-NOTICES.md, Montserrat-OFL.txt
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
VERSION="$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' loader/src/Plugin.cs | head -1)"
OUT="$ROOT/dist/Beatform-$VERSION"
CACHE="$ROOT/build/cache"
GODOT="${GODOT:-/c/Tools/Godot-dotnet/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}"
export PATH="/c/Program Files/dotnet:$PATH"

BEPINEX_URL="https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip"
BEPINEX_SHA256="f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a"

echo "== Beatform $VERSION"
rm -rf "$OUT" && mkdir -p "$OUT/loader/plugin" "$OUT/loader/bepinex" "$OUT/loader/levels" "$CACHE"

echo "== core tests"
(cd loader/tests/CoreTests && dotnet run -c Release -- "$ROOT" | tail -1)

echo "== loader"
(cd loader/src && dotnet build -c Release -nologo -v q | grep -E "error|Error\(s\)")
cp loader/src/bin/Release/{Beatform.Loader.dll,NVorbis.dll,NLayer.dll,Jint.dll,Acornima.dll} "$OUT/loader/plugin/"

echo "== BepInEx"
if [ ! -f "$CACHE/bepinex.zip" ] || ! echo "$BEPINEX_SHA256  $CACHE/bepinex.zip" | sha256sum -c --status -; then
  curl -sSL -o "$CACHE/bepinex.zip" "$BEPINEX_URL"
fi
echo "$BEPINEX_SHA256  $CACHE/bepinex.zip" | sha256sum -c -
(cd "$OUT/loader/bepinex" && unzip -q "$CACHE/bepinex.zip")

echo "== demo level"
cp -r spec/examples/demo "$OUT/loader/levels/Neon Rehearsal"

echo "== installer"
cp installer/*.ps1 installer/*.bat "$OUT/"
cp README.md THIRD-PARTY-NOTICES.md "$OUT/" 2>/dev/null || true
cp shared/Fonts/OFL.txt "$OUT/Montserrat-OFL.txt"

echo "== editor"
if ls "$APPDATA/Godot/export_templates/" 2>/dev/null | grep -q "4.7.2.stable.mono"; then
  mkdir -p "$OUT/Beatform Editor"
  (cd editor && dotnet build -nologo -v q | grep -E "Error\(s\)" && "$GODOT" --headless --path . --export-release "Windows Desktop" "$(cygpath -w "$OUT/Beatform Editor/Beatform Editor.exe")" 2>&1 | grep -E "^ERROR" || true)
  test -f "$OUT/Beatform Editor/Beatform Editor.exe"
else
  echo "   Godot .NET export templates for 4.7.2 are not installed: the editor is not exported."
  echo "   Run it from source with: Godot_v4.7.2-stable_mono --path editor"
fi

echo "== zip"
(cd "$ROOT/dist" && rm -f "Beatform-$VERSION.zip" && powershell -NoProfile -Command "Compress-Archive -Path 'Beatform-$VERSION' -DestinationPath 'Beatform-$VERSION.zip'")
ls -la "$ROOT/dist"
