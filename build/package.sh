#!/usr/bin/env bash
# Builds the release into dist/:
#   Sonokinesis-<version>/                 the app as a folder (what the setup installs):
#     Sonokinesis.exe (+ its data folder)    play, levels, settings and the editor in one window
#     loader/bepinex/                        BepInEx 6 IL2CPP, pinned build, checked by SHA-256
#     loader/plugin/                         Sonokinesis.Loader.dll and its audio decoders
#     loader/levels/                         the demo level
#     library/  docs/                        built-in scripts and the guides
#     LICENSE, THIRD-PARTY-NOTICES.md, Montserrat-OFL.txt
#   Sonokinesis-Setup-<version>.exe        installer of that folder (Inno Setup 6)
#   Sonokinesis-<version>-portable.zip     the same folder zipped, for running without installing
#   Sonokinesis-AI-Tool-<version>.zip      the command-line tool and MCP server (the app downloads it on request)
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
VERSION="$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' shared/Core/BuildInfo.cs | head -1)"
OUT="$ROOT/dist/Sonokinesis-$VERSION"
CACHE="$ROOT/build/cache"
GODOT="${GODOT:-/c/Tools/Godot-dotnet/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}"
ISCC="${ISCC:-$LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe}"
export PATH="/c/Program Files/dotnet:$PATH"

BEPINEX_URL="https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip"
BEPINEX_SHA256="f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a"

echo "== Sonokinesis $VERSION"
rm -rf "$OUT" && mkdir -p "$OUT/loader/plugin" "$OUT/loader/bepinex" "$OUT/loader/levels" "$CACHE"

echo "== core tests"
(cd loader/tests/CoreTests && dotnet run -c Release -- "$ROOT" | tail -1)

echo "== loader"
(cd loader/src && dotnet build -c Release -nologo -v q | grep -E "error|Error\(s\)")
cp loader/src/bin/Release/{Sonokinesis.Loader.dll,NVorbis.dll,NLayer.dll,Jint.dll,Acornima.dll} "$OUT/loader/plugin/"

echo "== BepInEx"
if [ ! -f "$CACHE/bepinex.zip" ] || ! echo "$BEPINEX_SHA256  $CACHE/bepinex.zip" | sha256sum -c --status -; then
  curl -sSL -o "$CACHE/bepinex.zip" "$BEPINEX_URL"
fi
echo "$BEPINEX_SHA256  $CACHE/bepinex.zip" | sha256sum -c -
(cd "$OUT/loader/bepinex" && unzip -q "$CACHE/bepinex.zip")
# The loader's console window stays closed unless the player turns it on in the app.
mkdir -p "$OUT/loader/bepinex/BepInEx/config"
printf '[Logging.Console]\r\n\r\n## Enables showing a console for log output.\r\n# Setting type: Boolean\r\n# Default value: true\r\nEnabled = false\r\n' > "$OUT/loader/bepinex/BepInEx/config/BepInEx.cfg"

echo "== demo level"
cp -r spec/examples/demo "$OUT/loader/levels/Neon Rehearsal"

echo "== library and docs"
cp -r library "$OUT/library"
mkdir -p "$OUT/docs"
cp docs/*.md "$OUT/docs/"
cp spec/level-format.md "$OUT/docs/level-format.md"
# Inside the release the format sits next to the other guides.
sed -i 's#\.\./spec/level-format\.md#level-format.md#g' "$OUT"/docs/*.md
cp LICENSE THIRD-PARTY-NOTICES.md "$OUT/"
cp shared/Fonts/OFL.txt "$OUT/Montserrat-OFL.txt"

echo "== app"
if ! ls "$APPDATA/Godot/export_templates/" 2>/dev/null | grep -q "4.7.2.stable.mono"; then
  echo "   Godot .NET export templates for 4.7.2 are not installed: install them from the Godot editor first."
  exit 1
fi
(cd editor && dotnet build -nologo -v q | grep -E "Error\(s\)" && "$GODOT" --headless --path . --export-release "Windows Desktop" "$(cygpath -w "$OUT/Sonokinesis.exe")" 2>&1 | grep -E "^ERROR" || true)
test -f "$OUT/Sonokinesis.exe"
# Install, switches, levels and uninstall against a stand-in game folder, with the exported app's own files.
"$OUT/Sonokinesis.exe" --headless -- --selftest "$(cygpath -w "$ROOT/build/selftest-game")" 2>&1 | grep -E "^(FAIL|SELFTEST)" || true
"$OUT/Sonokinesis.exe" --headless -- --selftest "$(cygpath -w "$ROOT/build/selftest-game")" >/dev/null 2>&1 || { echo "   the app's self-test failed"; exit 1; }
rm -rf "$ROOT/build/selftest-game"

echo "== AI tool"
TOOL="$ROOT/build/tool-out"
rm -rf "$TOOL"
(cd tools/cli && dotnet publish -c Release -r win-x64 -p:Version=$VERSION --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$TOOL" -nologo -v q | grep -E "error" || true)
rm -f "$TOOL"/*.pdb
test -f "$TOOL/sonokinesis.exe"
# A short name for typing: sono new-level, sono mcp, ...
printf '@"%%~dp0sonokinesis.exe" %%*\r\n' > "$TOOL/sono.cmd"

echo "== zips and setup"
cd "$ROOT/dist"
rm -f "Sonokinesis-$VERSION-portable.zip" "Sonokinesis-AI-Tool-$VERSION.zip" "Sonokinesis-Setup-$VERSION.exe"
powershell -NoProfile -Command "Compress-Archive -Path 'Sonokinesis-$VERSION' -DestinationPath 'Sonokinesis-$VERSION-portable.zip'"
powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$TOOL")\\*' -DestinationPath 'Sonokinesis-AI-Tool-$VERSION.zip'"
"$ISCC" //Q "//DAppVersion=$VERSION" "$(cygpath -w "$ROOT/installer/Sonokinesis.iss")"
rm -rf "$TOOL"
ls -la "$ROOT/dist"
