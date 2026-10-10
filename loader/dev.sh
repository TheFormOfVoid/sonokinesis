#!/usr/bin/env bash
# Developer helper for the loader.
#   ./dev.sh deploy        build and copy the plugin into the local game install
#   ./dev.sh restart       stop the game, deploy, start the game again
#   ./dev.sh cmd "<cmd>"   send a dev-console command and print the reply (game must be running)
#   ./dev.sh log [n]       tail the BepInEx log
set -euo pipefail
GAME="${GAME_DIR:-/g/Steam/steamapps/common/Just Shapes & Beats}"
DATA="$GAME/BepInEx/beatform"
HERE="$(cd "$(dirname "$0")" && pwd)"
export PATH="/c/Program Files/dotnet:$PATH"

deploy() {
  local out
  if ! out=$(cd "$HERE/src" && dotnet build -c Release -nologo -v q -p:Deploy=true 2>&1); then
    echo "$out" | grep -E " error " | sort -u | cut -c1-300
    echo "BUILD FAILED"; exit 1
  fi
  echo "build ok"
}

stop_game() {
  if tasklist //FI "IMAGENAME eq JSB.exe" | grep -q JSB.exe; then
    taskkill //IM JSB.exe //F >/dev/null 2>&1 || true
    for _ in $(seq 1 20); do tasklist //FI "IMAGENAME eq JSB.exe" | grep -q JSB.exe || break; sleep 0.5; done
  fi
}

start_game() {
  (cd "$GAME" && cmd //c start "" "JSB.exe")
  # Wait for the console to answer.
  rm -f "$DATA/out.txt"
  for _ in $(seq 1 120); do
    mkdir -p "$DATA"; echo "ping" > "$DATA/cmd.txt"
    sleep 1
    [ -f "$DATA/out.txt" ] && { cat "$DATA/out.txt"; return 0; }
  done
  echo "game did not answer"; return 1
}

send() {
  mkdir -p "$DATA"; rm -f "$DATA/out.txt"
  printf '%s\n' "$@" > "$DATA/cmd.txt"
  for _ in $(seq 1 120); do
    [ -f "$DATA/out.txt" ] && { sleep 0.1; cat "$DATA/out.txt"; return 0; }
    sleep 0.25
  done
  echo "no reply (is the game running?)"; return 1
}

case "${1:-}" in
  deploy) deploy ;;
  restart) stop_game; deploy; start_game ;;
  stop) stop_game ;;
  cmd) shift; send "$@" ;;
  log) tail -n "${2:-40}" "$GAME/BepInEx/LogOutput.log" ;;
  *) echo "usage: $0 deploy|restart|stop|cmd <command...>|log [n]"; exit 1 ;;
esac
