# Beatform level format, version 2

A Beatform level is a folder. The editor writes it, the loader reads it, and both must draw and
collide it identically, so every rule that affects what the player sees or touches is defined here.

```
My Level/
  level.json      required
  song.ogg        the audio file named by meta.audio (.ogg, .wav or .mp3)
  cover.png       optional, shown in the level list
  scripts/        optional: the level's scripts (*.js), see Scripts
```

A level may also be shipped as a `.zip` of that folder; the loader treats the zip the same way.

## Space and time

- **Coordinates** use the play area of a 1280×720 screen. The origin is the screen center, `x` grows
  to the right and `y` grows **down**. The visible area at rest is `x ∈ [-640, 640]`, `y ∈ [-360, 360]`.
- **Sizes** are in the same units.
- **Angles** are degrees. Because `y` points down, a positive angle turns **clockwise** on screen.
- **Time** is milliseconds from the start of the audio file, after `meta.offsetMs` is applied
  (see below). All times are numbers and may be fractional.

## level.json

```json
{
  "format": "beatform.level",
  "formatVersion": 2,
  "meta": { ... },
  "theme": { ... },
  "settings": { ... },
  "checkpoints": [0, 30000],
  "objects": [ ... ],
  "events": [ ... ],
  "editor": { }
}
```

Unknown fields are ignored, so newer editors can add data without breaking older loaders. The
`editor` block is for editor-only state (pattern generator inputs, layer names, view position) and is
never read by the loader.

### meta

| field | type | default | meaning |
|---|---|---|---|
| `id` | string | required | Stable unique id. The editor generates it once. Used for save data and the test bridge. |
| `title` | string | required | Song title shown in game. |
| `artist` | string | `""` | Song artist. |
| `author` | string | `""` | Level creator. |
| `description` | string | `""` | Free text. |
| `audio` | string | required | File name of the song, relative to the level folder. |
| `bpm` | number | `120` | Tempo. Beat `k` is at song time `k · 60000 / bpm` (set `offsetMs` so song time 0 is a beat). Used by the editor grid and by imposters that dash on the beat. |
| `beatsPerBar` | int | `4` | Editor grid only. |
| `offsetMs` | number | `0` | Song time `0` is this many ms into the audio file. Lets creators trim silence. |
| `lengthMs` | number | audio length | The level ends at this song time. |
| `previewStartMs` | number | `0` | Where the level list preview starts. |
| `difficulty` | int 1–5 | `3` | Shown in the level list only. |

### theme

Colors are `#rrggbb` or `#rrggbbaa`.

| field | default | meaning |
|---|---|---|
| `background` | `#0b0d12` | Background color at time 0. |
| `hazard` | `#ff2a6d` | Default object color. |
| `flash` | `#ffffff` | Color objects flash on activation. |

### settings

| field | default | meaning |
|---|---|---|
| `hitboxScale` | `0.9` | Every hitbox is scaled by this around the object's center, so near misses feel fair. |

### checkpoints

Sorted song times in ms. The first entry must be `0`. When every player dies the game rewinds the
music to the last checkpoint reached; Beatform's drawing and collision are pure functions of song
time, so they rewind with it automatically.

The game finishes a level when its last checkpoint is completed, so the loader adds one more
checkpoint at the level end (`meta.lengthMs`, or the end of the audio) and cuts the audio there.

## Objects

An object is one shape with an animated transform. Everything a level does is built from objects.

```json
{
  "id": "o12",
  "name": "left wall",
  "shape": { "type": "rect" },
  "start": 1000,
  "warn": 600,
  "end": 3000,
  "outro": 250,
  "hitbox": true,
  "hitboxScale": 1,
  "color": "#ff2a6d",
  "layer": 0,
  "parent": "o5",
  "pivot": [0, 0],
  "tracks": {
    "x":   [[0, -300], [600, 300, "outCubic"]],
    "y":   [[0, 0]],
    "rot": [[0, 0], [2000, 360]],
    "w":   [[0, 80]],
    "h":   [[0, 80]],
    "opacity": [[0, 1]]
  }
}
```

| field | default | meaning |
|---|---|---|
| `id` | required | Unique within the level. |
| `name` | `""` | Editor label. |
| `shape` | required | See *Shapes*. |
| `start` | required | Song time the object appears. |
| `warn` | `0` | Length of the warning phase, from `start`. |
| `end` | required | Song time the object is gone. Must be greater than `start`. |
| `outro` | `0` | Length of the fade out, ending at `end`. |
| `hitbox` | `true` | `false` makes the object decoration only. |
| `hitboxScale` | `1` | Multiplied with `settings.hitboxScale`. |
| `color` | `theme.hazard` | Object color, or `"@player"` for the colour of the player it follows (player 1 if it follows none). *(v2)* |
| `layer` | `0` | Draw order. Higher layers draw on top. Ties keep file order. |
| `parent` | none | Id of another object, or a player: `"@player"` (player 1), `"@player2"` … `"@player4"`. See *Transform* and *Players*. |
| `imposter` | none | Makes the object move like a player. See *Imposters*. *(v2)* |
| `random` | none | Random offsets picked again on every play. See *Random each play*. *(v2)* |
| `choice` | none | The object exists only on some rolls of random groups. See *Random each play*. *(v2)* |
| `script` | none | Runs a script instead of drawing its shape. See *Scripts*. *(v2)* |
| `pivot` | `[0, 0]` | The point that `x`/`y` place and that rotation and scale turn around, in fractions of the object's size: `[-0.5, 0]` is the middle of the left edge. |

### Phases

With `a = start + warn` and `b = end - outro`:

| song time | phase | drawn as | collides |
|---|---|---|---|
| `[start, a)` | warning | warning style | no |
| `[a, b)` | active | solid | yes, if `hitbox` |
| `[b, end)` | outro | fading | no |
| otherwise | absent | nothing | no |

If `warn + outro` exceeds the lifetime, the active phase is empty.

### Tracks

A track is a list of keyframes `[t, value]` or `[t, value, ease]`, where `t` is milliseconds
**relative to `start`**. Keyframes are sorted by `t`. Track values:

| track | default | meaning |
|---|---|---|
| `x`, `y` | `0` | Position of the pivot. |
| `rot` | `0` | Rotation in degrees. |
| `w`, `h` | `100` | Size. |
| `opacity` | `1` | Multiplies the phase alpha. Clamped to `[0, 1]`. |

Evaluating a track at local time `t`:

1. No keyframes: the default.
2. `t` before the first keyframe: the first value. After the last: the last value.
3. Otherwise, between keys `k0` and `k1`: `p = (t - k0.t) / (k1.t - k0.t)`, then
   `value = k0.value + (k1.value - k0.value) * ease(p)`, using the ease of **`k1`** (default `linear`).
   Keys with equal `t` jump: the later key wins at that `t`.

### Eases

Names follow the usual easing set. `p` is in `[0, 1]`, `π` is pi, `c1 = 1.70158`, `c2 = c1 * 1.525`,
`c3 = c1 + 1`, `c4 = 2π/3`, `c5 = 2π/4.5`.

| name | formula |
|---|---|
| `linear` | `p` |
| `step` | `0` if `p < 1`, else `1` (holds the previous value until the key) |
| `inSine` | `1 - cos(p·π/2)` |
| `outSine` | `sin(p·π/2)` |
| `inOutSine` | `-(cos(π·p) - 1) / 2` |
| `inQuad` / `outQuad` / `inOutQuad` | `p²` / `1 - (1-p)²` / `p<.5 ? 2p² : 1 - (-2p+2)²/2` |
| `inCubic` / `outCubic` / `inOutCubic` | `p³` / `1 - (1-p)³` / `p<.5 ? 4p³ : 1 - (-2p+2)³/2` |
| `inQuart` / `outQuart` / `inOutQuart` | `p⁴` / `1 - (1-p)⁴` / `p<.5 ? 8p⁴ : 1 - (-2p+2)⁴/2` |
| `inQuint` / `outQuint` / `inOutQuint` | `p⁵` / `1 - (1-p)⁵` / `p<.5 ? 16p⁵ : 1 - (-2p+2)⁵/2` |
| `inExpo` | `p=0 ? 0 : 2^(10p-10)` |
| `outExpo` | `p=1 ? 1 : 1 - 2^(-10p)` |
| `inOutExpo` | `p=0 ? 0 : p=1 ? 1 : p<.5 ? 2^(20p-10)/2 : (2 - 2^(-20p+10))/2` |
| `inCirc` / `outCirc` | `1 - sqrt(1 - p²)` / `sqrt(1 - (p-1)²)` |
| `inOutCirc` | `p<.5 ? (1 - sqrt(1 - (2p)²))/2 : (sqrt(1 - (-2p+2)²) + 1)/2` |
| `inBack` / `outBack` | `c3·p³ - c1·p²` / `1 + c3·(p-1)³ + c1·(p-1)²` |
| `inOutBack` | `p<.5 ? ((2p)²·((c2+1)·2p - c2))/2 : ((2p-2)²·((c2+1)·(2p-2) + c2) + 2)/2` |
| `inElastic` | `p=0 ? 0 : p=1 ? 1 : -2^(10p-10)·sin((10p-10.75)·c4)` |
| `outElastic` | `p=0 ? 0 : p=1 ? 1 : 2^(-10p)·sin((10p-0.75)·c4) + 1` |
| `inOutElastic` | `p=0 ? 0 : p=1 ? 1 : p<.5 ? -(2^(20p-10)·sin((20p-11.125)·c5))/2 : (2^(-20p+10)·sin((20p-11.125)·c5))/2 + 1` |
| `outBounce` | `n1=7.5625, d1=2.75`: `p<1/d1 ? n1p² : p<2/d1 ? n1(p-1.5/d1)²+.75 : p<2.5/d1 ? n1(p-2.25/d1)²+.9375 : n1(p-2.625/d1)²+.984375` |
| `inBounce` | `1 - outBounce(1-p)` |
| `inOutBounce` | `p<.5 ? (1 - outBounce(1-2p))/2 : (1 + outBounce(2p-1))/2` |

Unknown names behave as `linear`.

### Shapes

Every shape is defined on a unit square from `-0.5` to `0.5` on both axes and then stretched to
`w × h`. The outline points below are that unit-space outline; the loader and the editor draw the
same polygons. Circles use 48 segments, the first one at angle 0 (pointing to `+x`).

| `type` | options | outline |
|---|---|---|
| `rect` | — | the unit square |
| `circle` | — | ellipse with radius 0.5 |
| `polygon` | `sides` (3–12, default 3) | regular polygon inscribed in the circle, first vertex pointing up (`-y`) |
| `star` | `points` (3–12, default 5), `inner` (0.1–0.95, default 0.5) | alternating outer radius 0.5 and inner radius `0.5·inner`, first outer vertex pointing up |
| `ring` | `thickness` (0.02–0.5, default 0.12) | annulus between radius 0.5 and `0.5 - thickness` |
| `spikes` | `spikes` (4–24, default 12), `inner` (0.4–0.95, default 0.75) | spike ball: like `star` with `points = spikes` |
| `text` | `text` (string, default `""`), `bold` (bool, default `true`) *(v2)* | the unit square (see *Text* below) |

The `rect` is filled corner to corner. `circle`, `polygon`, `star` and `spikes` are filled as one
polygon. `ring` fills the area between its two circles.

#### Text

`text` draws one line of filled letters in Montserrat: Montserrat Bold, or Montserrat Medium with
`bold: false` (SIL Open Font License 1.1, `shared/Fonts/OFL.txt`). The glyph outlines are flattened
(curves within 2.5 font units of the real ones) and filled with the nonzero rule ahead of time by
`tools/bake_font.py`; the result in `shared/Core/TextFontData.cs` is the reference. It covers ASCII
32–126 and `‘ ’ “ ” … – — ×`; other characters draw nothing and take the width of a space.

Glyphs are laid out with their advance widths and the font's pair kerning. In font units (1000 per
em, y down, baseline at 0, capitals reaching −700) the line's ink runs from `inkMin` (the leftmost
point of any letter) to `inkMax`. Each point maps to the unit square as
`x' = (x − inkMin) / (inkMax − inkMin) − 0.5`, `y' = y / 700 + 0.5`: capitals fill the box height
with the baseline on its bottom edge (descenders hang below it, accents and tall lowercase letters
reach above), and the ink fills the box width. `width = (inkMax − inkMin) / 700` (0.5 when nothing
is drawn), so an object with `w = h · width` shows undistorted letters.

For collision a text object is its whole box (the unit square). Most text is decoration and has
`hitbox: false`. Version 1 files have no text shapes; the stroke-font `weight` of early version 2
files is ignored.

### Transform

For an object at song time `T`, with local time `t = T - start`:

1. Evaluate `x, y, rot, w, h, opacity` from its tracks.
2. A unit-space point `u` maps to the object's local frame as
   `L = ((u.x - pivot.x) · w, (u.y - pivot.y) · h)`.
3. Rotate `L` by `rot` (clockwise on screen) and add `(x, y)`.
4. If the object has a parent, apply the parent's step 3 to the result, then the parent's parent's,
   and so on. A parent contributes position and rotation only; its `w`, `h`, opacity and phase do
   not affect children. Parents are evaluated at the same song time `T`, with their own local time
   clamped to their keyframes as usual, even when the parent itself is absent. Cycles are invalid; the
   loader stops after 16 levels.

### Drawing

The drawn color is the object's color with this alpha:

| phase | alpha | extra |
|---|---|---|
| warning | `0.25 + 0.1·sin(2π·(T - start)/240)` | — |
| active | `1` | for the first 120 ms, the color goes linearly from `theme.flash` to the object color |
| outro | `1 - (T - b)/outro` | — |

Then the alpha is multiplied by `opacity`. Higher `layer` draws later; equal layers keep file order.

*(v2)* Decoration (`hitbox: false`) on `layer` −10 or lower is **backdrop**: it is drawn behind the
players and the game's own effects, and it does not flash when it becomes active. Everything else is
drawn just behind the players, and screen flashes in front of everything.

### Players *(v2)*

An object whose `parent` is `"@player"` … `"@player4"` is placed relative to that player: step 4 of
*Transform* ends by adding the player's position (the player contributes position only). While that
player is not in the game, or is dead, the object is not drawn and cannot hurt. Players are numbered
as the game numbers them; a solo player is player 1. Tools without players (the editor) use a
stand-in player 1.

### Imposters *(v2)*

An object with an `imposter` block moves like a player instead of following its `x`/`y` tracks. Its
tracks still give its spawn point (`x`, `y` at the end of the warning), its size (`w`, `h`) and its
`opacity`; `rot` and `pivot` are ignored. A `parent` that is a player makes the spawn point relative
to that player: during the warning the imposter keeps that offset from the player (kept inside the
screen when `bounds` is on), and it starts from there; without that player the offset counts from
the centre of the screen. A parent that is an object makes the spawn point relative to where that
object is (its position through its own parents; its rotation is not applied to the offset), during
the warning and at activation, so an imposter can come out of a moving shape. Other objects can use
an imposter as their `parent`; they then follow its position only.

```json
"imposter": { "mode": "chase", "speed": 383, "dashSpeed": 1467, "dashEvery": 550, "leaveAt": 10000 }
```

| field | default | meaning |
|---|---|---|
| `mode` | `"chase"` | `"chase"`: always heads for the nearest living player. `"straight"`: keeps `angle`. |
| `angle` | `180` | Heading in degrees, `0` right, `90` down. Chasers use it while no player is alive. |
| `speed` | `383` | Cruising speed, units per second. |
| `dashSpeed` | `1467` | Speed at the start of a dash. |
| `dashDrag` | `0.9` | While faster than `speed`, velocity is multiplied by this every step. |
| `dashTime` | `450` | Length of a dash in ms. |
| `dashEvery` | `550` | Cruising time between dashes in ms. |
| `dashBeats` *(v2)* | `0` | Above 0 (0.25–64): dash on the beat instead, one every `dashBeats` beats (see `meta.bpm`); `dashEvery` is ignored. |
| `firstDash` | `dashEvery` | Cruising time before the first dash; `0` dashes on activation. With `dashBeats`, the first dash is on the first beat at or after activation + `max(0, firstDash)`. |
| `stopAt`, `stopFor` | never, `0` | After `stopAt` ms it brakes and stands for `stopFor` ms, then dashes. |
| `stopDash` *(v2)* | `true` | `false`: when the stop ends it cruises on instead of dashing. |
| `leaveAt` | never | After `leaveAt` ms it dashes away on its heading at `dashSpeed` and no longer hurts. |
| `arriveFrom`, `arriveAt`, `arriveEase` *(v2)* | never, never, `"inQuad"` | A chaser closes in on the nearest living player: from `arriveFrom` to `arriveAt` (ms after activation) its position goes from where it was at `arriveFrom` to the player's position with `arriveEase`, reaching them exactly at `arriveAt`, and stays on them after. Its velocity is what that movement works out to (so it stretches and trails), and `bounds` does not apply meanwhile. |
| `bounds` | `true` for chase | Keeps it inside the screen while hunting. |
| `stretchSpeed`, `stretchMax` | `2000`, `0.4` | How speed stretches it. |
| `effects` | `true` | Trail, dash bursts, dash rings and drop shadow. |
| `dashPulse` | `"ring"` | How a dash is marked: `"ring"` (below) or `"disk"`: a disk that grows from `1.3·w` to `2.2·w` over 250 ms while its `ring` thickness falls from 0.5 to 0.08 (rounded to 0.02), fading out over its last 45 %. |

**Warning phase.** It sits at its spawn point, scaled by `outBack(t / warn)`, drawn in warning style.
A chaser faces the nearest living player.

**Simulation.** From the end of the warning the imposter runs in fixed steps of `1000/60` ms. It
starts with heading `angle` (a chaser: towards the nearest living player), velocity 0, in the
*cruise* state, and throws a burst (below); with `firstDash: 0` it dashes at once. With `dashBeats`,
its next dash time is the first beat at or after activation + `max(0, firstDash)`; if that is within
half a step of activation it dashes at once and the next dash time moves on by `dashBeats` beats. Each step, with
`dt` the step length in seconds and `life` the time since activation:

1. If `life ≥ leaveAt`, it switches to *leave* once: remembers its heading, warp 0.8, adds a ring
   and a burst. If `life ≥ stopAt`, it switches to *stop* once (unless leaving).
2. A chaser with a living player sets heading towards the nearest one (in *cruise* and *dash*).
3. *Cruise*: velocity = `speed` along the heading; after `firstDash` (first time) or `dashEvery` ms
   in this state it dashes. With `dashBeats` it instead dashes, in *cruise* or *dash*, at the first
   step with `life ≥ next dash time − half a step`, and the next dash time moves on by `dashBeats`
   beats until it is later than that (also when a *stop* ends, after its dash). *Dash*: a chaser turns its velocity to the heading keeping its speed;
   if faster than `speed` the velocity is multiplied by `dashDrag`, otherwise set to `speed` along
   the heading; after `dashTime` ms it returns to *cruise*. *Stop*: velocity × 0.8; after `stopFor`
   ms it dashes. *Leave*: velocity = `dashSpeed` along the remembered heading.
   Dashing means: velocity = `dashSpeed` along the heading, warp 0.8, add a ring and a burst.
4. Position += velocity · `dt`. With `bounds` (and not leaving) it is kept within
   `|x| ≤ 620, |y| ≤ 340`; a velocity component that hits the edge is multiplied by −0.5. An
   imposter that starts outside that area is only kept inside once it has entered it, so it can
   come in from off screen.
5. Draw angle moves 20 % of the way to the heading (in *stop*, to the heading rounded to 90°).
   Stretch moves 30 % of the way to `min(|velocity| / stretchSpeed, stretchMax)`; warp moves 4 % of
   the way to 0.

Between steps it is drawn at the last step's position plus velocity times the time since. With
`s = stretch + warp`, it is drawn rotated by the draw angle and scaled to `w·(1+s)` by
`h·max(0.25, 1 − 0.6·s)`. It hurts like an object of size `w × h` at that position and angle
(unstretched) during its active phase, except while leaving. Going back in time (a seek or a rewind)
restarts the run; chasers then follow where the players are now.

Polygons, stars and spike balls point their first vertex (unit-space "up") along the heading, so a
triangle leads with its tip; the stretch stays along the heading.

**Effects** (decoration, the object's colour, never hurting):

- *Trail*: while faster than 67 units/s, every 35 ms a copy of its shape is left at the position
  plus `(sin(life·45)·4, cos(life·32)·4)`, rotated `heading + sin(life·60)·90`, size
  `0.25 + sin(life·80)·0.1` (life in ms, sines in radians). Trail copies of a `circle` are drawn as
  a 10-sided `polygon`.
- *Burst*: 12 copies, for `c = 0…11`: `r1 = sin(life·113 + 45c)`, `r2 = cos(life·277 + 83c)`,
  `r3 = sin(life·341 + 67c)`; direction `heading + 180 + 55·r1` degrees, speed `133 + 533·|r2|`,
  size `0.08 + 0.12·|r3|`, starting life 1.2.
- Every copy loses 2.5 life per second (drawn with alpha `0.7·min(1, life)` at `size·life` of the
  object's `w × h`) and its velocity is multiplied by 0.88 every step.
- *Ring*: a `ring` of thickness 0.155 at the dash point, diameter `2.8·scale·w`, scale starting at
  0.2 and growing 8 per second, alpha `0.8·a` with `a` falling from 1 by 3.5 per second.
- *Shadow*: the stretched body in black at alpha 0.3, offset by (4, 4).

Effects are drawn under the body, in the order rings, trail, shadow.

### Random each play *(v2)*

A level can differ on every attempt. Each play has a seed: tools use 0, the loader picks a new one
when the level starts and whenever playback has gone back (a checkpoint restart) and runs again.

```json
"random": { "x": [-20, 20], "y": [-340, 340], "rot": [0, 360], "seed": "cube12" },
"choice": [{ "group": "ambush", "count": 9, "show": [3] }, { "group": "shape 4", "count": 4, "hide": [0] }]
```

`random.x`, `random.y` and `random.rot` are `[min, max]` ranges. For each one present, the play adds
`min + (max − min) · u` to every value of that track (and to what the object contributes to its
children as a parent), with `u = R(seed, name + "/x")` (or `"/y"`, `"/rot"`) and `name` =
`random.seed`, or the object's `id` without it; objects with the same `random.seed` get the same
offsets.

`choice` is one condition or a list of them, all of which must hold. A group rolls
`r = min(count − 1, ⌊R(seed, "choice/" + group) · count⌋)` once per play; every condition naming it
sees the same `r` (its own `count` is used for the roll it computes, so give every condition of a
group the same `count`). The condition holds when `r` is in `show` (if given) and not in `hide`. An
object whose conditions do not hold does not exist in that play: it is not drawn and does not hurt.
It still works as a parent; its children carry their own conditions.

`R(seed, key)` is a number in `[0, 1)`: FNV-1a 64 over the key's UTF-16 code units (offset basis
14695981039346656037, prime 1099511628211), xored with `seed · 0x9E3779B97F4A7C15` (64-bit, seed
as unsigned 32-bit), then splitmix64's finaliser (add `0x9E3779B97F4A7C15`; `h = (h ^ h >> 30) ·
0xBF58476D1CE4E5B9`; `h = (h ^ h >> 27) · 0x94D049BB133111EB`; `h ^= h >> 31`), and `(h >> 11) / 2^53`.

### Collision

A player is a circle (the game's own player hitbox, radius 8). During its active phase an object with
`hitbox: true` hurts a player whose circle overlaps the object's shape scaled by
`settings.hitboxScale · hitboxScale` around the object's own center (unit point `(0, 0)` after
transform). For `ring` only the band between the two circles counts.

The loader decides the overlap and then hands the hit to the game, so invincibility after a hit and
while dashing work exactly as in official levels.

### Scripts *(v2)*

An object with a `script` block runs one of the level's scripts (`scripts/NAME.js`) while it exists,
instead of drawing its shape:

```json
{ "id": "o7", "shape": { "type": "rect" }, "start": 4000, "end": 8000,
  "script": { "name": "bullet_ring", "params": { "count": 16, "speed": 340 } },
  "tracks": { "x": [[0, 0]], "y": [[0, -100]] } }
```

`params` are the script's settings (numbers, strings, booleans); missing ones take the defaults the
script declares in `meta.params`. The object's `x`, `y` and `rot` at `start` (through its parents) are
where the script starts; its shape, `w`, `h`, `opacity`, `warn`, `outro`, `hitbox`, `color` and
`layer` are not used. The script draws what it wants each frame; those shapes hurt like objects
(collision as above) unless the script marks them as decoration, warnings or front shapes.

Scripts are JavaScript (as run by the Jint engine, ECMAScript 2023 and later features) with the API in
`docs/scripting.md`, run in a sandbox with
no clock, files or network. Each script object is a world of *instances*, started with one instance
at `start` and stepped in fixed steps of `1000/60` ms from `start` (`update`), drawn at the exact
song time (`draw`). Random numbers come from a splitmix64 generator whose state starts at
`⌊R(seed, "script/" + id) · 2^64⌋` (see *Random each play*), so
a play is the same every time it reaches the same song time: going back in time restarts the world
from `start` and steps it forward again. Outside `[start, end)` the script draws nothing and has no
effect. Camera, screen and player-speed requests from scripts combine with the events: camera shifts
and rotations add, zooms multiply, player speed and turn take the minimum, a script background
replaces the event background for that frame, script flashes add to event flashes.

A level runs only the scripts in its own `scripts/` folder. A script with `kind: 'pattern'` in its
`meta` is an editor tool that builds objects; it cannot be used in a `script` block.

## Events

Events change the whole scene at a song time.

```json
{ "t": 12000, "type": "shake", "duration": 400, "intensity": 10 }
{ "t": 12000, "type": "flash", "duration": 250, "color": "#ffffff", "opacity": 0.7 }
{ "t": 12000, "type": "background", "duration": 600, "color": "#1a0010", "ease": "outQuad" }
```

| type | fields | effect |
|---|---|---|
| `shake` | `duration`, `intensity` (units) | Camera shake. Uses the game's camera kick so players and hazards move together. |
| `flash` | `duration`, `color`, `opacity`, `hold` *(v2, default 0)* | Full screen color over the hazards, held at `opacity` for `hold` ms, then fading linearly to 0 over `duration`. While a flash is fully opaque the loader hides the players too (the game draws them over the flash). |
| `background` | `duration`, `color`, `ease` | Background color transition from the current color, starting at `t`. |
| `sway` *(v2)* | `rotation` (degrees), `x`, `y` (units), `period`, `periodX`, `periodY` (ms; defaults `2000`, `period·1.08`, `period·1.21`), `duration`, `ease` | From `t` until the next sway, the whole scene (players included) turns by `rotation·sin(2π·T/period)` around the screen centre and moves by `x·sin(2π·T/periodX)`, `y·sin(2π·T/periodY)`, with `T` the song time. Over `duration` the amplitudes blend from the previous sway's (or 0). |
| `sound` *(v2)* | `sound`, `volume` (0–1) | Plays one of the game's own sounds when playback passes `t` (not after seeks or rewinds): `playerDeath`, `playerHit`, `playerRespawn`, `explosion`. Tools without the game stay silent. |
| `playerSpeed` *(v2)* | `speed` (0–2, default 1), `turn` (0.001–1, default 1), `duration`, `ease` | From `t` on the players keep `speed` times each step's movement, dashes included, until the next `playerSpeed` event. Their velocity is unchanged, so the game still stretches and trails them: near 0 they are rooted and warp in place. Each frame they turn `turn` of the way to the direction they move in (1: at once). Before the first one both are 1; each one blends from the previous values over `duration` with `ease`. Applied by the loader every frame; editors do not simulate it. |
| `zoom` *(v2)* | `scale` (0.25–4, default 1), `x`, `y` (units from the centre) or `focus` (`"@player"` … `"@player4"`), `duration`, `ease` | From `t` on everything (players included) is drawn `scale` times bigger around the point `x`, `y`, or around that player's position at each moment (the centre if absent), until the next zoom. Before the first zoom the scale is 1; each one blends scale and point from the previous one's over `duration` with `ease`. |
| `credits` *(v2)* | — | The game's own title card (`meta.title` and `meta.artist`) shows at `t` instead of at the start: the game's wait before it is lengthened by `t` minus the song time the level was launched from. Launched from past `t`, the card keeps the game's timing. Use one per level. |

## Compatibility

- The loader rejects files whose `formatVersion` is greater than it supports and says so in the level
  list.
- The camera's sway and zoom move the game's gameplay container: rotation and scale by their change
  each frame, position by a shift added after the game places its camera every frame.
- Version 1 levels will always load in later loaders. Version 2 adds players (`"@player"` parents and
  colours), imposters, text, random each play, scripts and the newer events; the editor writes
  version 2.
