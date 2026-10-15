# Making a level with an AI

This guide is for an AI agent (or a person working the same way) building a Sonokinesis level with the
`sonokinesis` tool. It covers the workflow, how the format works in practice, what makes a level good to
play, and how to check your work. Read [level-format](../spec/level-format.md) for every field and
[scripting](scripting.md) for custom attacks; [ai-tools](ai-tools.md) lists the commands.

## The loop

1. **Start from the song.** `new_level` (folder, song, title, artist) copies the song and detects the
   tempo and first beat. Then `analyze_song` on the folder: note the BPM, the sections and their feel,
   the strongest hits. Plan the level around them before writing anything.
2. **Plan sections.** One idea per section, matching the music: calm intro, build, drop, breakdown,
   second drop, ending. Write the plan down (bar ranges, attacks, intensity). Put a checkpoint at the
   start of each section after the first.
3. **Build a section at a time.** Use `edit_level` to add objects and events, `place_script` for live
   attacks and effects, `add_pattern` for laid-out groups, `write_script` for anything custom.
4. **Check it.** `validate_level` after every change; fix every error and read the warnings.
5. **Look at it.** `render_sheet` over the section (12–16 frames) and `render_frames` at the moments
   that matter (just before and at each hit). Check that warnings come before hits, that there is
   always somewhere safe to stand, and that the shapes line up with what you planned.
6. **Play it.** If the game is available, `play_in_game` from the section's start. A person playing it
   is the real test: ask them what felt unfair, late, early, too easy or too busy, and change that.
7. Repeat for the next section. Keep `level_info` handy to see what is already there.

If the Sonokinesis Editor is open on the same folder, it reloads whenever you change `level.json` or a
script (as long as it has no unsaved edits of its own), so the person can watch the level grow.

## The format in practice

- The screen is 1280×720 with `(0, 0)` in the centre; `x` right, `y` **down**; visible area
  x −640…640, y −360…360. Angles in degrees, clockwise. Objects are sized by `w`, `h` and placed by
  their centre (the pivot).
- Level times are **song times** in ms. With `meta.offsetMs` set (new_level does it), beat `k` is at
  `k × 60000 / bpm`. `beat_times` lists them. Snap hits to beats or to half/quarter beats.
- An object exists from `start` to `end`. The first `warn` ms it is a harmless warning (pulsing,
  see-through); then it hurts; the last `outro` ms it fades out harmlessly. So the moment it becomes
  dangerous is `start + warn`: put **that** on the beat.
- Track keys are `[t, value]` or `[t, value, ease]` with `t` **relative to start**. Values hold before
  the first key and after the last. The ease of a key shapes the motion arriving at it.
- `hitbox: false` makes decoration; decoration on `layer` ≤ −10 is backdrop (behind the players).
- `parent: "o5"` moves an object with another (spinning arms around a hub); `parent: "@player"`
  attaches it to player 1 (speech bubbles, a ring around the player).
- `imposter: {...}` makes an object move like a player and chase the nearest one.
- `random` and `choice` make each attempt different.
- Events change the whole scene: `shake`, `flash`, `background`, `sway`, `zoom`, `playerSpeed`,
  `sound`, `credits` (the game's title card).

## What makes a level good

**Telegraph everything.** Every hazard should be visible before it can hurt: a warning phase, a thin
line before a beam, a shape growing in, a marker where something will land. At normal speed a warning
of one beat (about 400–600 ms) is comfortable; half a beat is hard; less than 250 ms feels unfair
unless the attack is obviously aimed and slow.

**Ride the music.** Hits land on beats and on the strongest accents (`strongHits`). Fast sections use
half and quarter beats; calm sections use whole beats or bars, and fewer hazards. Big moments (the
drop, the last note) deserve a camera effect: a `shake`, a `zoom` punch, a `flash`.

**Always leave a way out.** Never cover the whole screen. A wall needs a gap big enough to pass (the
player is about 22 units across; give gaps of 150+ units), lasers need time between them, bullet rings
need spacing. Avoid trapping the player in a corner with no exit. Check renders: if you can't see a
safe path in a frame, neither can the player.

**Escalate and rest.** Intensity should follow the song: build up, peak at drops, breathe in breakdowns.
A short calm stretch after a hard part feels good. Don't keep the same attack going for more than a few
bars; vary between sweeps, aimed shots, area denial, chasers and patterns.

**Mix attack kinds.** Things that sweep the screen (walls, beams), things that fill space (bursts,
rings, rain), things that target the player (homing orbs, chasers, aimed shards), and things that
change the arena (zoom, rotation, slowing the players). The most memorable levels have a few
distinctive ideas that come back with variations.

**Be readable.** Hazards in the hazard colour (pink by default), decoration in other colours or on the
backdrop layer. Keep the screen from getting cluttered: a few clear threats beat many small ones.

**Checkpoints.** Every 20–40 seconds, at section starts. The level ends when the song ends (or at
`meta.lengthMs`).

## Useful building blocks

A wall sweeping left to right with its warning on the edge:

```json
{ "name": "sweep", "shape": { "type": "rect" }, "start": 7031, "warn": 469, "end": 9375, "outro": 100,
  "tracks": { "x": [[0, -610], [469, -610], [2300, 700, "inQuad"]], "y": [[0, 0]], "w": [[0, 60]], "h": [[0, 720]] } }
```

A beam: thin warning line first, then the full beam (two objects, or one whose `h` grows at the end of
its warning):

```json
{ "name": "beam", "shape": { "type": "rect" }, "start": 9375, "warn": 938, "end": 10781, "outro": 200,
  "tracks": { "x": [[0, 0]], "y": [[0, -120]], "w": [[0, 1400]], "h": [[0, 6], [938, 6], [1038, 90, "outBack"]] } }
```

A spinning cross: a hub that turns, arms parented to it:

```json
{ "id": "hub", "shape": { "type": "circle" }, "start": 12000, "warn": 938, "end": 19500, "outro": 300,
  "tracks": { "x": [[0, 0]], "y": [[0, 0]], "w": [[0, 60]], "h": [[0, 60]], "rot": [[938, 0], [7500, 360]] } },
{ "parent": "hub", "shape": { "type": "rect" }, "start": 12000, "warn": 938, "end": 19500, "outro": 300,
  "tracks": { "x": [[0, 0]], "y": [[0, 0]], "w": [[0, 900]], "h": [[0, 40]] } },
{ "parent": "hub", "shape": { "type": "rect" }, "start": 12000, "warn": 938, "end": 19500, "outro": 300,
  "tracks": { "x": [[0, 0]], "y": [[0, 0]], "w": [[0, 40]], "h": [[0, 900]] } }
```

A chaser that dashes on the beat and leaves after 8 beats:

```json
{ "name": "hunter", "shape": { "type": "polygon", "sides": 3 }, "start": 20000, "warn": 938, "end": 26000,
  "imposter": { "mode": "chase", "dashBeats": 1, "leaveAt": 4000 },
  "tracks": { "x": [[0, 700]], "y": [[0, 0]], "w": [[0, 40]], "h": [[0, 40]] } }
```

Text (decoration; `h` is the capital-letter height; leave out `w` and `edit_level` gives it the width
the letters need):

```json
{ "name": "title", "shape": { "type": "text", "text": "READY?" }, "hitbox": false, "start": 0, "end": 2000, "outro": 300,
  "color": "#ffffff", "tracks": { "x": [[0, 0]], "y": [[0, 0]], "h": [[0, 60]] } }
```

From the library (see `list_scripts` for every setting):

| attacks (place_script) | effects (place_script) | patterns (add_pattern) |
|---|---|---|
| `bullet_ring` rings of bullets on the beat | `camera_kick` a jolt | `burst` a bomb that splits |
| `homing_orb` chases, then bursts into shards | `zoom_pulse` zoom punch every beat | `shockwave` an expanding ring |
| `laser_sweep` a beam that pivots | `custom_sway` rocking camera | `wall` a sweeping wall with a gap |
| `bouncing_balls` gravity balls | `screen_shake` | `laser` warning line, then beam |
| | `beat_bump` camera dips on a beat pattern | |
| `shard` a flying triangle | `beat_flash` a flash every beat | `spinner` arms around a hub |
| | `vhs_rewind` tape-rewind look, players frozen | `spiral`, `rain`, `bouncer`, `pulse` |

When nothing fits, write a script (`write_script`, then `check_script`, then `place_script`). See
[scripting](scripting.md).

## Checking your work

- `validate_level` must report `ok: true`. Warnings are worth reading (a hazard that never hurts, an
  object after the level ends, too many shapes at once).
- Render around every hit: one frame a little before (the warning should be visible), one at the hit,
  one just after. `render_sheet` with `count` 16 over 8 bars gives a quick feel for density.
- Move the stand-in player (`playerX`, `playerY`) to see how chasers and aimed attacks behave from
  different spots. Use a few `seed` values if the level uses `random`/`choice`.
- In the game, `play_in_game --from <section start>`; with the developer console on,
  `game_screenshot` shows what the player sees.

## Working with a person

The person can't see your plan unless you share it, and you can't feel the level: they can. Describe
what you're about to build in a few lines (section, bars, attacks), then build it, then ask them to play
that section and tell you what felt off. Timing feedback is usually about warnings (too short or
missing) and sync (hits early or late against the music); density feedback is about escape routes.
Change one thing at a time when tuning.
