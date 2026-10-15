# The sonokinesis tool (command line and MCP)

`sonokinesis` makes and checks levels without the editor's window. It uses the same level, script and
audio code as the game loader and the editor, so what it validates and draws is what the game plays.
Every command is also an **MCP tool**: run `sonokinesis mcp` and an AI client (Claude Code, Claude
Desktop, or any MCP client) can build levels with it. The companion repository
[sonokinesis-ai](https://github.com/TheFormOfVoid/sonokinesis-ai) has ready-made setup and instructions for
AI agents.

- [Getting it](#getting-it)
- [Using it from an AI client (MCP)](#mcp)
- [Commands](#commands)
- [Edit operations](#edit-operations)
- [Talking to the game](#the-game)
- [Talking to the editor](#the-editor)
- [Environment variables](#environment-variables)

## Getting it

The release zip has `tools/sonokinesis.exe` next to the editor (with `library/` and `docs/` beside it),
and `tools/sono.cmd`, a short name for it: `sono new-level ...` works the same.
To build it from source you need the .NET 8 SDK:

```bash
dotnet build -c Release tools/cli
```

The program lands in `tools/cli/bin/Release/net8.0/sonokinesis.exe`. It finds the script library and
these docs by looking next to itself and in the folders above it, or through `SONOKINESIS_LIBRARY` and
`SONOKINESIS_DOCS`.

On the command line, arguments are `--name value`; the first one or two may be given without a name
(see `sonokinesis help <command>`). Values that look like JSON (numbers, `[...]`, `{...}`, `true`) are
read as JSON, and `@file` reads a file (for `--ops`, `--params`, `--code`, `--json`). Output is JSON
(or plain text for docs and file contents). The exit code is 1 on errors, and when `validate-level`
finds errors.

## MCP

`sonokinesis mcp` speaks the Model Context Protocol over stdin/stdout. Register it with your client:

Claude Code (in the folder you work in):

```bash
claude mcp add sonokinesis -- "C:/path/to/sonokinesis.exe" mcp
```

Claude Desktop or other clients, in their MCP server settings:

```json
{
  "mcpServers": {
    "sonokinesis": { "command": "C:/path/to/sonokinesis.exe", "args": ["mcp"] }
  }
}
```

The tools have the same names and arguments as the commands below (with `_`: `render_frames`). Renders
and screenshots come back as images the model can look at. The docs are also offered as resources
(`sonokinesis://docs/<topic>`).

## Commands

| command | what it does |
|---|---|
| `docs [topic]` | the documentation; without a topic, the list of topics |
| `new-level <folder> --song <file> [--title --artist --bpm --offsetMs]` | create a level folder: copies the song, writes level.json, detects tempo and first beat unless given |
| `level-info <folder> [--objects false]` | meta, length, beat length, checkpoints, events, scripts and every object (id, name, what it is, start, end, group, parent) |
| `read-level <folder>` | level.json as text |
| `write-level <folder> --json <text or @file>` | replace level.json (the old one is kept as `level.json.bak`) |
| `edit-level <folder> --ops <json or @file>` | apply [edit operations](#edit-operations) |
| `list-scripts [folder] [--kind attack/effect/pattern]` | the level's scripts and the library's, with settings, defaults and errors |
| `read-script <name> [--folder]` | a script's source (the level's copy, else the library's) |
| `write-script <folder> <name> --code <text or @file>` | create or replace `scripts/<name>.js`, then compile and trial-run it |
| `check-script <folder> <name>` | compile and trial-run a level script: patterns are built with their defaults, attacks run for their length at the centre; reports errors and `log()` lines |
| `place-script <folder> <name> --at <ms> [--end ms / --beats n] [--x --y --rot] [--params {...}] [--label]` | add an object that runs an attack or effect script (copied from the library if needed) |
| `add-pattern <folder> <name> --at <ms> [--x --y] [--params {...}]` | build a pattern's objects into the level, grouped |
| `validate-level <folder> [--stepMs]` | check structure and scripts, then run the whole level and report script errors, logs and warnings |
| `render-frames <folder> --times 1000,2000 [--width 960] [--seed] [--playerX --playerY] [--out dir]` | pictures of the level at song times |
| `render-sheet <folder> --from <ms> --to <ms> [--count 12] [--columns 4] [--width 400] [--out file]` | evenly spaced frames in one picture |
| `analyze-song <file or folder> [--bpm]` | tempo, first beat, loudness and busyness per bar, sections, strongest hits |
| `beat-times <folder> [--from --to --every]` | song times of beats with bar.beat labels |
| `play-in-game <folder> [--from ms]` | start the level in the running game |
| `game-status` | is the game running, is the loader installed, the last play request, is the dev console on |
| `game-screenshot [--out]` | a screenshot of the game (needs the developer console) |
| `editor <command> [--args {...}]` | send a command to the running editor |
| `editor-screenshot [--ms] [--out]` | a screenshot of the running editor |

Renders show the level as the game does: background, hazards (warnings pulse, active shapes solid),
camera (sway, zoom, script camera), flashes, and a stand-in player (the cyan square, at -300, 150
unless moved) that imposters chase and `@player` objects follow. Each frame is labelled with its song
time and bar.beat. Without `--out`, pictures go to the temp folder (`%TEMP%/sonokinesis/renders`).

`analyze-song` reports times two ways: `startMs`/`ms` are positions in the audio file, `songTime` is
level time once `meta.offsetMs` is set to the reported `offsetMs` (`new-level` does that). Bars carry
`energy` (loudness 0–1 against the loudest bar) and `busy` (onsets per bar against the song's
average); `sections` split the song where the loudness changes, with a `feel` (quiet, calm, driving,
intense); `strongHits` are the 40 strongest accents, at least half a beat apart. If the strong hits
sit half a beat off the grid, use `offsetIfOffBeat`.

## Edit operations

`edit-level` takes a JSON array of operations, applied in order and saved once:

| op | fields | effect |
|---|---|---|
| `add` | `objects: [...]` (or `object: {...}`) | add objects; missing ids are generated (`o12`); a missing shape becomes a rect; text without a `w` track gets the width its letters need at its height |
| `update` | selector, `set: {...}` | replace those fields of each selected object; a `null` value removes the field |
| `shift` | selector, `by: ms` | move objects in time (start and end) |
| `remove` | selector | delete objects |
| `addEvents` | `events: [...]` | add events (`t` and `type` required) |
| `removeEvents` | `type`, `from`, `to` (all optional) | delete matching events |
| `meta` / `theme` / `settings` | `set: {...}` | change those blocks |
| `checkpoints` | `set: [ms, ...]` | replace the checkpoints (0 is always kept) |

A selector is one of `id: "o3"`, `ids: ["o3", "o4"]`, `group: "burst1"` (objects a pattern made), or
`from`/`to` (objects starting in that time range).

```json
[
  { "op": "add", "objects": [
    { "name": "wall", "shape": { "type": "rect" }, "start": 3750, "warn": 469, "end": 5625, "outro": 150,
      "tracks": { "x": [[0, 700], [469, 700], [1875, -700, "inOutSine"]], "y": [[0, 0]], "w": [[0, 60]], "h": [[0, 720]] } }
  ]},
  { "op": "addEvents", "events": [ { "t": 4219, "type": "shake", "duration": 300, "intensity": 10 } ] },
  { "op": "update", "group": "burst1", "set": { "color": "#33e5ff" } },
  { "op": "shift", "from": 30000, "to": 45000, "by": 469 }
]
```

## The game

`play-in-game` writes a request into the game folder (`BepInEx/sonokinesis/bridge/play.json`); the
Sonokinesis loader inside the running game picks it up, loads the level from its folder and starts it
from the given song time (like the editor's **Test in game**). It answers in `status.json`. If the
game is not running, the request waits there until it reaches the main menu.

`game-screenshot` and the developer console need `DevConsole = true` in
`BepInEx/config/dev.sonokinesis.loader.cfg` (restart the game after changing it). Turn it off again when
you're done. While testing it is also wise to set `BlockAllSaves = true` there.

The game is found through Steam's library folders, or set `SONOKINESIS_GAME_DIR`.

## The editor

The running Sonokinesis Editor watches `%LOCALAPPDATA%/Sonokinesis/editor-bridge/command.json` and answers
in `result.json`. `sonokinesis editor <command>` does the round trip:

| command | arguments | effect |
|---|---|---|
| `ping` | | is the editor there |
| `state` | | open folder, title, playhead, playing, unsaved edits, counts, selection, script errors and the latest script logs |
| `open` | `folder` | open a level folder |
| `reload` | `force` | reload the level from disk (refuses with unsaved edits unless `force: true`) |
| `save` | | save |
| `seek` | `ms` | move the playhead |
| `play` / `stop` | `ms` (play) | preview with the song |
| `select` | `ids` | select objects |
| `shot` | `path`, `ms` | screenshot (what `editor-screenshot` uses) |
| `quit` | `force` | close the editor (refuses with unsaved edits unless `force: true`) |

Every answer includes the state. The editor also reloads by itself when `level.json` or a script
changes on disk and it has no unsaved edits, so you can edit files with any tool and watch the editor
follow.

## Environment variables

| variable | meaning |
|---|---|
| `SONOKINESIS_LIBRARY` | the script library folder |
| `SONOKINESIS_DOCS` | the docs folder |
| `SONOKINESIS_GAME_DIR` | the game folder (with `JSB.exe`) |
