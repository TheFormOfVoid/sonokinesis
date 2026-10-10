# Beatform

Custom levels for Just Shapes & Beats: a loader that plays them inside the real game, and an editor
to make them.

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

Open `Beatform Editor`. Click **New**, pick a song (`.ogg`, `.mp3` or `.wav`) and set its BPM in
**Level…** (the **Detect** button can guess it). Then:

- Drag shapes and patterns from the left onto the stage, or click them to add one at the playhead.
- Move, rotate and resize on the stage. When a value has two or more keyframes, changing it at the
  playhead adds a keyframe there, so you animate by scrubbing and moving.
- The timeline shows the song waveform, the beat grid, checkpoints, scene events and every object.
  Drag bars to move them in time and drag their edges to change their length.
- **Space** previews the level with its song. The editor has no player: levels are played in the
  game, so it cannot be used to play anything without owning the game.
- **Test in game** (F5) starts the level in the running game from the playhead.
- **Share → Export as .zip** makes a file other players drop into their `CustomLevels` folder.

The preview uses the same code as the in-game loader, so what you see in the editor is what the game
plays.

## How it works

- `loader/` is a BepInEx plugin. It creates a song slot for the custom level, gives the game's own
  level flow (checkpoints, rewinds, lives, results) a level made only of checkpoints, and draws the
  hazards itself in front of the players. When a hazard touches a player, the hit is handed to the
  game, so invincibility and dashing work as in official levels.
- `editor/` is a Godot 4 (.NET) app.
- `shared/` holds the level format code both use.
- `spec/level-format.md` documents the level format, and `spec/test-vectors.json` lets other tools
  check they read it the same way.

## Building

- Loader: .NET 8 SDK, a game folder with BepInEx 6 IL2CPP run once (it generates the reference
  assemblies). `cd loader/src && dotnet build -c Release`.
- Editor: Godot 4.7.2 .NET. `godot --path editor`.
- Release: `build/package.sh`.
