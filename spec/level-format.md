# Beatform level format, version 1

A Beatform level is a folder. The editor writes it, the loader reads it, and both must draw and
collide it identically, so every rule that affects what the player sees or touches is defined here.

```
My Level/
  level.json      required
  song.ogg        the audio file named by meta.audio (.ogg, .wav or .mp3)
  cover.png       optional, shown in the level list
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
  "formatVersion": 1,
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
| `bpm` | number | `120` | Tempo. Used by the editor grid only. |
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
| `color` | `theme.hazard` | Object color. |
| `layer` | `0` | Draw order. Higher layers draw on top. Ties keep file order. |
| `parent` | none | Id of another object. See *Parenting*. |
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

The `rect` is filled corner to corner. `circle`, `polygon`, `star` and `spikes` are filled as one
polygon. `ring` fills the area between its two circles.

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

### Collision

A player is a circle (the game's own player hitbox, radius 8). During its active phase an object with
`hitbox: true` hurts a player whose circle overlaps the object's shape scaled by
`settings.hitboxScale · hitboxScale` around the object's own center (unit point `(0, 0)` after
transform). For `ring` only the band between the two circles counts.

The loader decides the overlap and then hands the hit to the game, so invincibility after a hit and
while dashing work exactly as in official levels.

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
| `flash` | `duration`, `color`, `opacity` | Full screen color over the hazards, fading linearly from `opacity` to 0. |
| `background` | `duration`, `color`, `ease` | Background color transition from the current color, starting at `t`. |

## Compatibility

- The loader rejects files whose `formatVersion` is greater than it supports and says so in the level
  list.
- Version 1 levels will always load in later loaders.
