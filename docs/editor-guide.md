# Beatform Editor guide

The editor makes levels: you lay hazards out on a stage over the song's timeline, preview them with
the music, and test them in the game. Everything the game can do is reachable from here, including
writing your own attacks and effects in code.

- [The window](#the-window)
- [Starting a level](#starting-a-level)
- [Adding things](#adding-things)
- [The stage](#the-stage)
- [The timeline](#the-timeline)
- [The inspector](#the-inspector)
- [Animating](#animating)
- [Scene events](#scene-events)
- [Imposters, players and randomness](#imposters-players-and-randomness)
- [Scripts: your own attacks and effects](#scripts)
- [Testing in the game](#testing-in-the-game)
- [Sharing](#sharing)
- [Shortcuts](#shortcuts)

## The window

- **Top bar**: New, Open (with recent levels), Save, Level… (settings), Share, the level name (a dot
  means unsaved changes), the play controls, the playhead time and bar.beat, the snap grid, the
  preview speed, and **Test in game**.
- **Palette** (left): shapes, attacks, effects, patterns and scene events to add.
- **Stage** (middle): the 1280×720 screen at the playhead. The dashed cyan square is a stand-in player.
- **Inspector** (right): the properties of whatever is selected, or the level when nothing is.
- **Timeline** (bottom): the song's waveform, the beat grid, checkpoints, scene events and one bar per
  object.

## Starting a level

Click **New**, pick a song (`.ogg`, `.mp3` or `.wav`) and a folder. Then open **Level…**:

- **Song**: title, artist, your name, description, difficulty (1–5, shown in the level list), and
  the song file.
- **Timing**: BPM (**Detect** guesses it from the song), beats per bar, and the song offset (where
  the first beat is in the audio file; song time 0 starts there, so the grid lines up with the
  music). **Level ends** cuts the level short; **Preview from** is where the level list's preview starts.
- **Look**: background colour, hazard colour (the default for objects), the flash colour hazards show
  for a moment when they turn dangerous, and the hitbox scale (below 1 is more forgiving).
- **Editor**: the game folder (for Test in game) and where new levels go.

A level is a folder: `level.json`, the song, and `scripts/` for its code. Unsaved changes are
autosaved every two minutes next to the level (`level.autosave.json`, never over it); if the editor
closes unexpectedly, opening the level offers to restore it.

## Adding things

Click an item in the palette to add it at the playhead in the middle of the stage, or drag it onto
the stage to place it there.

- **Shapes**: square, circle, triangle, hexagon, star, ring, spike ball, text, and an imposter.
- **Attacks** and **Effects**: scripted attacks that run live in the game (bullet rings, homing orbs,
  laser sweeps…) and effects on the camera, the screen and the players (zoom pulses, sway, a VHS
  rewind…). See [Scripts](#scripts).
- **Patterns**: generators that build ordinary objects from a few settings (bursts, shockwaves, walls,
  spinners, spirals, rain…). Clicking one opens its settings; dragging uses the last settings. The
  objects are grouped (**Ctrl+G** selects the whole group) and can be edited like any others.
- **Scene**: checkpoint, camera shake, screen flash, background colour, camera sway, game sound,
  player speed, title card and camera zoom.

## The stage

- Click an object to select it; Shift+click adds to the selection; drag on empty space to select with
  a box. Click on a scripted object's gold marker (its spawn point) to select it.
- Drag to move (Shift locks to one axis, Ctrl snaps to 10 units, Alt moves the whole animation path).
  Drag a white corner to resize (Shift keeps the proportions). Drag the gold handle to rotate (Shift
  snaps to 15°).
- Arrow keys nudge by 1 unit (Shift: 10).
- The mouse wheel moves the playhead by one grid step; Ctrl+wheel zooms the stage; middle-drag pans.
- Right-click for the object menu: copy, paste, duplicate, delete, key all tracks, select the pattern,
  mirror, repeat, stretch time, bring to front, send to back.
- Drag the stand-in player to see how chasers and player-following objects behave.
- While the song plays, the stage shows the camera like the game does (sway, zoom, script camera).

## The timeline

- Click or drag on the ruler to move the playhead (Alt: without snapping). Space plays from the
  playhead; the speed menu plays at 25–100 %.
- Each object is a bar from its start to its end; the darker parts are its warning and fade out. Drag a
  bar to move it in time (Ctrl+drag duplicates it), drag its edges to change its length.
- Checkpoints and scene events have their own rows; click one to edit it in the inspector.
- The wheel scrolls, Ctrl+wheel zooms, + and − zoom too. The snap menu sets the grid (1 beat to 1/16
  beat, or off).

## The inspector

With one object selected:

- **Shape**: its type and the shape's options (sides, points, inner radius, ring thickness, the text
  and whether it is bold).
- **Timing**: when it appears, how long it warns (harmless and pulsing), when it is gone, and how long
  it fades out. "Appear here" and "Hit here" move it so it appears, or becomes dangerous, at the
  playhead.
- **Collision and look**: whether it hurts, its hitbox size, its colour (the theme's, the player's,
  or its own) and its layer (higher draws on top; decoration on layer −10 or lower is drawn behind
  the players).
- **Moves like a player**: turns it into an imposter (below).
- **Random each play**: offsets and conditions rolled again on every attempt.
- **Parent**: make it follow another object or a player, and set its pivot.
- **Animation at the playhead**: the value of each track (X, Y, rotation, width, height, opacity) at
  the playhead, with its keys.

With several objects selected you can change them all at once; with a scene event or a checkpoint
selected, its fields; with nothing selected, the level and a list of shortcuts.

## Animating

Every object has six tracks: X, Y, Rotation, Width, Height and Opacity. While a track has one key,
changing it (on the stage or in the inspector) changes the whole object. Once it has two or more,
changing it writes a key at the playhead. So to animate: add a key with ◆ (or press **K** to key every
track), move the playhead, then move or resize the object. Each key has an ease (how the motion
arrives at it). Hold **R** while the song plays to record a path with the mouse.

## Scene events

Scene events change the whole screen at a moment: **shake**, **flash** (with an optional hold at full
strength), **background** colour fades, **sway** (the scene rocks and drifts from then on),
**sound** (one of the game's sounds), **player speed** (slow or root the players, and how quickly they
turn), **title card** (show the game's song card here instead of at the start) and **zoom** (on a
point or following a player). Add them from the palette's Scene section; they appear on the timeline's
event row.

## Imposters, players and randomness

- **Imposters** move like a player: they cruise, dash (on a timer or on the beat), stretch and leave a
  trail. Chasers hunt the nearest player; straight ones keep a heading. They can stop, leave, or arrive
  at the player exactly on a beat. Turn any object into one under *Moves like a player*.
- **Follow a player**: set *Follows* to Player 1–4 to keep an object next to that player (speech
  bubbles, rings around them). It disappears when that player isn't playing.
- **Random each play**: random offsets for position and rotation, and *conditions* that make an object
  exist only on some rolls of a group (for example, one of four shapes is the real threat). The stage
  shows one roll; **Roll again** shows another.

## Scripts

Attacks, effects and patterns are JavaScript. The palette lists the level's own scripts and the
built-in library; placing a library script copies it into the level's `scripts/` folder, where you can
change it.

- **+ New script** creates one from a working example: an attack, an effect or a pattern.
- **{ } Code** (or **Edit code** in the inspector) opens the code editor: highlighting, completion of
  every scripting function, a live check that marks syntax errors as you type, and a console showing
  `log()` lines and errors from the preview. **Ctrl+S** saves, and the preview and the game pick up the
  change at once. **Ctrl+/** comments lines out. **Place** adds the script at the playhead. Library
  scripts open read-only: **Copy into level** makes the level's own copy.
- The inspector shows a scripted object's settings (from its `meta.params`), its errors, and where it
  spawns. Drag its gold marker on the stage to move the spawn point.
- Scripts can also be changed outside the editor (in any text editor, or by an AI with the `beatform`
  tool); the editor reloads them when the file changes.

The full reference is the [scripting guide](scripting.md).

## Testing in the game

**Test in game** (F5) starts the level in the running game from the playhead (the game needs the
Beatform loader, installed by the Beatform installer). It saves the level first (the game plays the saved
files), and the game opens it as soon as it is on a menu.

## Sharing

**Share → Export as .zip** makes one file other players drop into their `CustomLevels` folder.
**Copy into the game's CustomLevels folder** installs it on your own game; **Open level folder** shows
the files.

## Shortcuts

| key | does |
|---|---|
| Space | play / pause from the playhead |
| Ctrl+S, Ctrl+O, Ctrl+N | save, open, new |
| Ctrl+Z, Ctrl+Y (Ctrl+Shift+Z) | undo, redo |
| Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+D | copy, paste at the playhead, cut, duplicate |
| Ctrl+A, Ctrl+G, Esc | select everything, select the whole pattern, select nothing |
| Delete / Backspace | delete the selection |
| K | key every track of the selection at the playhead |
| , and . | step the playhead back / forward one grid step |
| Home, End | go to the start / the last object |
| + and − | zoom the timeline |
| arrows (Shift) | nudge by 1 (10) |
| F5 | test in game |
| R (hold, while playing) | record the mouse as a path |
