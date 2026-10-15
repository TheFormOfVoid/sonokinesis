# Beatform

Custom levels for Just Shapes & Beats: a loader that plays them inside the real game, an editor to
make them, a scripting system to code your own attacks and effects, and a command-line tool (also an
MCP server) so an AI can build levels too.

Beatform is a fan project. It is not made, endorsed or supported by Berzerk Studio. It contains no
code, art, sound, music, fonts or level data from the game: hazards are drawn with Beatform's own
shapes, levels bring their own songs, and the loader only talks to the game you already own.

## For players

1. Download the Beatform release and unzip it anywhere.
2. Close the game and double-click `Install-Beatform.bat`. It finds the game through Steam, adds the
   BepInEx mod loader and the Beatform plugin next to the game files (nothing is replaced), and
   creates a `CustomLevels` folder with a demo level.
3. Start the game. The first start takes a minute or two while BepInEx prepares itself.
4. On the main menu (or the playlist screen) press **F1** to open the custom level list.

Put levels in `Just Shapes & Beats/CustomLevels`, either as a folder or as a `.zip`.

Custom levels never change your save file or Steam achievements: saving is paused while a custom level
runs, and your progress is put back exactly as it was afterwards. Custom levels work in solo and local
co-op; they are not available in online lobbies.

To remove everything, close the game and run `Uninstall-Beatform.bat`. Your levels are kept.

## For level makers

Open `Beatform Editor`, click **New**, pick a song and set its BPM in **Level…** (**Detect** guesses
it). Drag shapes, attacks, effects and patterns onto the stage, animate them over the timeline,
preview with **Space**, and press **F5** to play the level in the game from the playhead.

- Shapes with keyframed position, rotation, size and opacity, warnings and fade outs, parents, and
  objects that follow a player.
- Imposters that move like a player: cruise, dash on the beat, chase, stop, leave, arrive on time.
- Scene events: shake, flash, background fades, camera sway, zoom, player speed, game sounds and the
  game's title card.
- Random each play: offsets and conditions rolled on every attempt.
- **Scripts**: every attack, effect and pattern in the palette is a JavaScript file you can open, copy
  and change, and you can write new ones. Scripts run live in the game exactly as in the preview: they
  draw, hurt, chase, spawn, move the camera, flash the screen and slow the players. The editor has a
  code editor with completion, live error checking and a console.
- **Share → Export as .zip** makes a file other players drop into their `CustomLevels` folder.

The editor has no player of its own: levels are played in the game, so it can't be used to play
anything without owning the game. The preview uses the same code as the in-game loader, so what you
see is what the game plays.

## For AI agents

`beatform` (in `tools/`) does everything the editor does, from the command line: create a level from a
song (with tempo and beat detection), edit it, write and test scripts, validate it by running it,
render frames to PNG, play it in the game and drive the editor. `beatform mcp` serves all of it as MCP
tools, with renders returned as images, so an AI client such as Claude Code can build a level end to
end and look at its work. See [docs/ai-tools.md](docs/ai-tools.md) and [docs/ai-guide.md](docs/ai-guide.md);
the companion repository [beatform-ai](https://github.com/TheFormOfVoid/beatform-ai) has ready-made
instructions, setup and an example level for agents.

## Documentation

| | |
|---|---|
| [Editor guide](docs/editor-guide.md) | every part of the editor, and the shortcuts |
| [Scripting guide](docs/scripting.md) | writing attacks, effects and patterns in JavaScript |
| [Level format](spec/level-format.md) | `level.json`, exactly as the loader reads it |
| [AI guide](docs/ai-guide.md) | how to build a good level step by step with the tool |
| [Tool reference](docs/ai-tools.md) | the `beatform` commands and MCP tools |

## How it works

- `loader/` is a BepInEx plugin. It creates a song slot for the custom level, gives the game's own
  level flow (checkpoints, rewinds, lives, results) a level made only of checkpoints, and draws the
  hazards itself just behind the players. When a hazard touches a player, the hit is handed to the
  game, so invincibility and dashing work as in official levels.
- `editor/` is a Godot 4 (.NET) app.
- `shared/` holds the level format, evaluation, scripting and audio code used by the loader, the
  editor and the tool alike.
- `library/` holds the built-in scripts (attacks, effects, patterns).
- `tools/cli/` is the `beatform` command-line tool and MCP server.
- `spec/level-format.md` documents the level format, and `spec/test-vectors.json` lets other tools
  check they read it the same way.

## Building

- Loader: .NET 8 SDK and a game folder with BepInEx 6 IL2CPP run once (it generates the reference
  assemblies). `cd loader/src && dotnet build -c Release`.
- Tests: `dotnet run -c Release --project loader/tests/CoreTests`.
- Editor: Godot 4.7.2 .NET. `godot --path editor`.
- Tool: `dotnet build -c Release tools/cli`.
- Release: `build/package.sh`.

## License

Beatform is free software under the GNU General Public License version 3 ([LICENSE](LICENSE)): you
may use, study, share and change it, and anything you distribute that is based on it must stay under
the same license with its source available. Levels you make with Beatform are yours; the license
covers Beatform's code, not your levels or songs. The built-in scripts in `library/` and the starting
code of new scripts are under MIT No Attribution ([library/LICENSE](library/LICENSE)), so copying them
into your levels puts no conditions on your levels. Third-party components keep their own licenses
([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

Just Shapes & Beats is a trademark of Berzerk Studio. Beatform is an unofficial fan project and is not
affiliated with or endorsed by Berzerk Studio. Only use songs you have the right to use in levels you
share.
