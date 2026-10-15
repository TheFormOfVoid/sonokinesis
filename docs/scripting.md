# Scripting guide

Sonokinesis levels can carry their own code. Anything the built-in tools don't do, you can write: a new
attack, a camera effect, a screen effect, a way of moving the players, or a pattern that lays out
objects for you. Scripts are JavaScript files in the level's `scripts/` folder, and they run in the
game exactly as they run in the editor preview.

Every built-in attack, effect and pattern in the editor's palette is a script too (the files in
`library/`). Open any of them in the code editor to see how it works, copy it into your level and
change it.

- [Kinds of scripts](#kinds-of-scripts)
- [Your first attack](#your-first-attack)
- [How a script runs](#how-a-script-runs)
- [meta: name, settings, length](#meta)
- [Drawing](#drawing)
- [Players](#players)
- [Camera and screen](#camera-and-screen)
- [Time and beats](#time-and-beats)
- [Spawning more instances](#spawning)
- [Random numbers](#random-numbers)
- [Math helpers](#math-helpers)
- [Sound and logging](#sound-and-logging)
- [Patterns](#patterns)
- [Rules and limits](#rules-and-limits)
- [Recipes](#recipes)
- [Reference card](#reference-card)

## Kinds of scripts

| kind | runs | used for |
|---|---|---|
| **attack** (the default) | live, in the game, every frame while its object lasts | hazards that move, chase, spawn, react to the players; also effects on the camera, the screen and the players |
| **pattern** (`kind: 'pattern'`) | once, in the editor, when you place it | building ordinary objects (walls, bursts, spirals) that you can then edit by hand |

The editor's palette shows attack scripts under **Attacks** and **Effects** (effects are just attacks
that mostly touch the camera or screen; put them in `library/effects/` or give them
`category: 'Camera'`, `'Screen'` or `'Effects'`) and pattern scripts under **Patterns**.

A level only ever runs **its own** scripts, the files in `LEVEL/scripts/`. When you place a library
attack, the editor copies it into the level first (with any scripts it spawns), so the level keeps
working on any computer and you can change your copy without touching the library.

## Your first attack

In the editor: save the level, click **+ New script** in the palette, name it `my_attack`, pick
**Attack**. The code editor opens with a small working example:

```js
meta = {
  name: 'my_attack',
  description: 'A shape that grows in on the beat, then flies at the nearest player.',
  length: 8,
  params: {
    size: { type: 'number', label: 'Size', default: 40, min: 4, max: 400 },
    speed: { type: 'number', label: 'Speed', default: 500, min: 10, max: 3000, step: 10 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 1, min: 0, max: 8, step: 0.25 },
  },
};

function start(p) {
  this.px = this.x;          // where the object was placed
  this.py = this.y;
  this.vx = 0;
  this.vy = 0;
  this.launched = false;
}

function update(dt) {
  if (!this.launched && this.age >= this.warnBeats * beatMs) {
    this.launched = true;
    var target = nearestPlayer(this.px, this.py);
    var angle = target ? angleTo(this.px, this.py, target.x, target.y) : 180;
    var v = vec(angle, this.speed);
    this.vx = v.x;
    this.vy = v.y;
    camera.shake(5, 150);
  }
  this.px += this.vx * dt / 1000;
  this.py += this.vy * dt / 1000;
  if (Math.abs(this.px) > 800 || Math.abs(this.py) > 500) kill();
}

function draw() {
  var warn = this.warnBeats * beatMs;
  if (this.age < warn) {
    square(this.px, this.py, this.size * ease('outBack', this.age / warn), { warning: true, rot: 45 });
  } else {
    square(this.px, this.py, this.size, { rot: 45 + this.age * 0.3 });
  }
}
```

Click **Place** (or drag it from the palette onto the stage) and scrub the timeline: the preview runs
your code. Change a number, press **Ctrl+S**, and the preview updates at once. The inspector shows the
`params` as fields, so each placed copy can have its own size and speed.

## How a script runs

Each placed object that runs a script is a little world:

1. When the song reaches the object's **start** time, one **instance** is created at the object's
   position. `this.x`, `this.y` and `this.rot` are where the object is at that moment (its x, y and
   rotation keys, through its parents). Its settings are copied onto `this` (`this.size`) and are
   also in `this.params` and the `p` argument.
2. `start(p)` runs once.
3. `update(dt)` runs in **fixed steps of 1/60 s** (`dt` is the step in milliseconds, 16.67). Keep all
   moving state here: positions, velocities, timers.
4. `draw()` runs for every frame shown. It describes what is on screen right now; it must not change
   state (it can run several times per step, or be skipped).
5. At the object's **end** time everything it drew disappears and the world stops.

Inside these functions, `this` is the instance. `this.age` is the time since the instance was
created, in ms. You can store anything on `this`.

**Determinism.** A level must play the same way every time the music is at the same point, because
the game rewinds to checkpoints and the editor scrubs back and forth. Sonokinesis does this for you:
going back in time restarts the world from the object's start and replays the steps. That only works
because scripts cannot see the clock (`Date` does not exist) and random numbers come from
`random()`, which repeats within a play. Don't keep state outside `this` (in global variables) that
changes during play.

**Many instances, one file.** All instances of a script share the same code; each keeps its own
`this`. `spawn()` creates more instances inside the same world (bullets, shards, children).

## meta

`meta` describes the script to the editor. All fields are optional.

```js
meta = {
  name: 'Bullet ring',            // shown in the palette and the inspector
  description: 'A core that fires rings of bullets outward on the beat.',
  kind: 'pattern',                // only for patterns
  category: 'Projectiles',        // 'Camera', 'Screen' or 'Effects' put it under Effects
  length: 8,                      // default length in beats when placed (attacks)
  uses: ['shard'],                // other scripts it spawns, copied into the level with it
  params: {
    count: { type: 'int', label: 'Bullets', default: 12, min: 3, max: 64 },
    speed: { type: 'number', label: 'Speed', default: 300, min: 20, max: 2000, step: 10, tooltip: 'Units per second' },
    every: { type: 'beats', label: 'Every', default: 1, min: 0.25, max: 8, step: 0.25 },
    label: 'HELLO',               // a bare default: text
    big: false,                   // a bare default: on/off
  },
};
```

Setting types and the field the editor shows:

| type | value | field |
|---|---|---|
| `number` | any number | number box (`min`, `max`, `step`) |
| `int` | whole number | number box |
| `beats` | a number of beats (multiply by `beatMs` for ms) | number box marked "beats" |
| `text` | string | text box |
| `bool` | true / false | check box |
| `choice` | one of `options: ['left', 'right']` | drop-down |
| `color` | `'#rrggbb'` | colour picker |
| `ease` | an ease name (see the level format) | drop-down of eases |
| `shape` | `'rect'`, `'circle'`, `'polygon'`, `'star'`, `'ring'` or `'spikes'` | drop-down |

`spawn()` also sees `uses`: when the editor copies a script into a level it brings everything it
spawns (it also finds `spawn('name', ...)` calls by itself).

## Drawing

Call these in `draw()`. Coordinates are the game's: the screen is 1280×720, `(0, 0)` is the centre,
`x` grows right and `y` grows **down**; angles are degrees, clockwise. Sizes are full widths (a
`circle(0, 0, 100)` is 100 units across).

| function | draws |
|---|---|
| `rect(x, y, w, h, opts)` | a rectangle centred on x, y |
| `square(x, y, size, opts)` | a square |
| `ellipse(x, y, w, h, opts)` | an ellipse |
| `circle(x, y, size, opts)` | a circle |
| `polygon(x, y, size, sides, opts)` | a regular polygon (first corner pointing up) |
| `triangle(x, y, size, opts)` | `polygon` with 3 sides |
| `hexagon(x, y, size, opts)` | `polygon` with 6 sides |
| `star(x, y, size, points, inner, opts)` | a star; `inner` is the inner radius (0.05–0.99, default 0.5) |
| `ring(x, y, size, thickness, opts)` | a ring; `thickness` is a fraction of the size (default 0.12). Only the band hurts |
| `spikes(x, y, size, count, inner, opts)` | a spike ball |
| `line(x1, y1, x2, y2, width, opts)` | a thick line (a rotated rectangle) |
| `shape(points, opts)` | any polygon: `[[x, y], ...]` or `[{x, y}, ...]`, in screen coordinates |
| `text(str, x, y, size, opts)` | one line of text, `size` = capital-letter height. Centred on x unless `align: 'left'` / `'right'`. Never hurts unless `hurts: true` |
| `textWidth(str, size, bold)` | how wide `text()` would be |

Options (`opts`, all optional):

| option | default | meaning |
|---|---|---|
| `rot` | `0` | rotation in degrees |
| `color` | the level's hazard colour | `'#rrggbb'`, `'#rrggbbaa'`, or `'player'` for player 1's colour |
| `alpha` | `1` | opacity 0–1 |
| `hurts` | `true` (text: `false`) | `false` makes it decoration |
| `warning` | `false` | draw in the warning style (pulsing, see-through) and never hurt: use it to telegraph |
| `front` | `false` | draw in front of the players; never hurts |
| `layer` | `0` | draw order: higher on top |
| `align` | centre | text only: `'left'` or `'right'` |
| `bold` | `true` | text only: `false` for the lighter weight |

Anything that hurts uses the game's own hit rules (invincibility after a hit, dashing through), and
the level's hitbox scale.

## Players

| | |
|---|---|
| `players` | the players in the game: `[{ number, x, y, alive, color }]` (number is 1–4). In the editor, one stand-in player you can drag |
| `nearestPlayer(x, y)` | the closest living player, or `null` |
| `hitPlayer(p)` | hurt a player (a player object or a number) as if touched |
| `movePlayer(p, x, y)` | teleport a player |
| `setPlayerSpeed(speed, turn)` | this frame, players move at `speed` × normal (0 roots them, 2 doubles) and turn `turn` of the way to their direction each frame (1 = at once, 0.05 = slowly). Call it in `draw()`; several calls keep the slowest |

`hitPlayer` and `movePlayer` happen at the step they are called in (call them in `update()`); the
editor has no real player, so they only matter in the game.

## Camera and screen

Two kinds of calls. **Per-frame** calls describe this frame, so call them in `draw()` every frame you
want them:

| | |
|---|---|
| `camera.move(x, y)` | shift everything by x, y |
| `camera.rotate(deg)` | turn everything around the screen centre |
| `camera.zoom(scale)` / `camera.zoom(scale, x, y)` | zoom around the centre, or around the point x, y |
| `screen.background(color)` | the background colour |
| `screen.flash(color, alpha)` | a full-screen colour over everything, this frame |
| `setPlayerSpeed(speed, turn)` | see above |

**Timed** calls start an effect that plays out on its own; call them once, in `start()` or
`update()`:

| | |
|---|---|
| `camera.kick(x, y, ms, ease)` | jolt the camera by x, y; it eases back over `ms` (default 300, ease `outQuad`) |
| `camera.kickRotate(deg, ms, ease)` | the same for rotation |
| `camera.kickZoom(scale, ms, ease)` | punch the zoom to `scale` (1.1 = 10 % closer) and ease back |
| `camera.shake(intensity, ms)` | shake, fading over `ms` |
| `screen.flash(color, alpha, ms, hold)` | a flash at `alpha`, held for `hold` ms, then fading over `ms` |

`screen.width`, `screen.height` (1280, 720), `screen.left`, `screen.right`, `screen.top`,
`screen.bottom` (±640, ±360) describe the visible area.

The camera moves the players together with the hazards, like the game's own camera. Script camera
calls add to the level's sway and zoom events.

Calling one of these in the wrong function does nothing, and the console says so once (for example
"camera.move ... call them in draw()").

## Time and beats

| | |
|---|---|
| `time` | song time in ms (in `update()`, the time of the step; in `draw()`, the frame's) |
| `dt` | the step length in ms (1000/60) |
| `bpm` | the level's tempo |
| `beatMs` | one beat in ms |
| `beat(n)` | `n` beats in ms |
| `beatAt(t)` | the beat number at song time `t` (beat 0 is song time 0) |
| `this.age` | ms since this instance was created |

To do something on every beat:

```js
function update(dt) {
  var b = Math.floor(beatAt(time));
  if (b !== this.lastBeat) { this.lastBeat = b; camera.kickZoom(1.05, 200); }
}
```

## Spawning

| | |
|---|---|
| `spawn(name, params, x, y)` | create an instance of another attack script (or this one) in the same world. `x`, `y` default to the spawner's `this.x`, `this.y`. `params` override the spawned script's defaults |
| `kill()` | remove this instance (it stops drawing and updating) |

Call both in `start()` or `update()`. A world holds at most 4000 instances; past that, `spawn` does
nothing. List spawned scripts in `meta.uses` so the editor copies them with yours.

```js
// In a turret's update(): fire a shard toward the player every half beat.
if (this.age >= this.next) {
  this.next += beat(0.5);
  var t = nearestPlayer(this.x, this.y);
  spawn('shard', { angle: t ? angleTo(this.x, this.y, t.x, t.y) : 180, speed: 500 });
}
```

## Random numbers

| | |
|---|---|
| `random()` | 0 ≤ r < 1 |
| `random(max)` / `random(min, max)` | a number in the range |
| `randomInt(min, max)` | a whole number, both ends included |
| `pick(array)` | one element |
| `chance(p)` | true with probability `p` |

`Math.random()` is the same generator. Numbers come in the same order every time a play reaches the
same point, so rewinds replay exactly; every new attempt in the game gets new numbers (the editor
always shows attempt 0; **Roll again** in the inspector shows another).

## Math helpers

`lerp(a, b, t)`, `clamp(v, min, max)`, `ease(name, t)` (t from 0 to 1, any ease name),
`dist(x1, y1, x2, y2)`, `angleTo(x1, y1, x2, y2)` (degrees), `rad(deg)`, `deg(rad)`,
`vec(angle, length)` → `{x, y}`, `rgb(r, g, b, a)` → `'#rrggbb'` (0–255, alpha 0–1). Everything in
`Math` works too.

## Sound and logging

| | |
|---|---|
| `sound(name, volume)` | play one of the game's sounds when playback passes this step: `'playerDeath'`, `'playerHit'`, `'playerRespawn'`, `'explosion'`. Not after seeks or rewinds. The editor is silent |
| `log(...)` | print to the code editor's console (and the game's log), with the script name and time |

## Patterns

A pattern runs once, in the editor, when you place it. Its `build(p)` calls `add()` for each object
it wants; the objects are ordinary level objects, grouped so **Ctrl+G** selects them all. `p` holds
the settings plus:

| | |
|---|---|
| `p.hit` | the song time the pattern should become dangerous (the playhead when placed) |
| `p.x`, `p.y` | where it was dropped (0, 0 when clicked) |

`add(spec)` takes a level.json object (see the level format), with shortcuts: `shape` can be a name
(`'circle'`), and `x`, `y`, `rot`, `w`, `h`, `opacity` can be a number or keys
`[[t, value, ease], ...]` (t relative to `start`) instead of going inside `tracks`. It returns the new
object's id, so later objects can use it as their `parent`.

```js
meta = {
  name: 'Pillars',
  kind: 'pattern',
  params: {
    count: { type: 'int', label: 'Pillars', default: 6, min: 1, max: 32 },
    gap: { type: 'number', label: 'Spacing', default: 160 },
    every: { type: 'beats', label: 'Every', default: 0.5 },
  },
};

function build(p) {
  for (var i = 0; i < p.count; i++) {
    var t = p.hit + i * p.every * beatMs;
    add({
      name: 'pillar', shape: 'rect', start: t - beatMs, end: t + beatMs * 2, warn: beatMs, outro: 200,
      x: p.x + (i - (p.count - 1) / 2) * p.gap, y: 360, w: 60,
      h: [[0, 40], [beatMs, 40], [beatMs + 150, 520, 'outBack']],
    });
  }
}
```

`random()` works in patterns too (the same numbers each time you build with the same settings). Things
that only make sense live (drawing, camera, players, spawn) do nothing in `build()`.

Patterns or attacks? A pattern gives you objects you can still drag around and fine-tune, and costs
nothing at play time. An attack can react to the players and to time, and can do things objects
can't (chasing, bouncing, spawning, camera and screen effects).

## Rules and limits

- Scripts run in a sandbox: no files, no network, no clock, no access to the game or the computer.
- A call that runs too long (an endless loop) is stopped after 2 seconds or 20 million statements;
  recursion is limited to 200 levels, memory to 256 MB. The script is reported and stops; the rest of
  the level keeps going.
- Errors show in the code editor's console (with the line highlighted), in the inspector of the
  object, in `sonokinesis validate-level`, and in the game's BepInEx log. An instance that throws stops.
- At most 50 000 shapes per frame and 4000 instances per object.
- Keep `draw()` light: it runs every frame. Do the work in `update()`.
- Script names (file names) use letters, digits, `_` and `-`.

## Recipes

**Telegraph, then strike** — draw with `warning: true` first, then for real:

```js
function draw() {
  var warn = beat(1);
  if (this.age < warn) rect(0, this.y, 1280, 60, { warning: true });
  else if (this.age < warn + beat(1)) rect(0, this.y, 1280, 60);
}
```

**Zoom punch on every beat** (an effect):

```js
meta = { name: 'Beat zoom', category: 'Camera', params: { amount: { type: 'number', default: 1.06 } } };
function update(dt) {
  var b = Math.floor(beatAt(time));
  if (b !== this.last) { this.last = b; camera.kickZoom(this.amount, 250, 'outQuad'); }
}
```

**Slow time** (players crawl while it lasts, turning slowly):

```js
meta = { name: 'Slow time', category: 'Effects', params: { speed: 0.2, turn: 0.3 } };
function draw() { setPlayerSpeed(this.speed, this.turn); screen.background('#1a1030'); }
```

**A homing missile** — keep a heading and turn it a little each step:

```js
function start(p) { this.px = this.x; this.py = this.y; this.heading = 180; }
function update(dt) {
  var t = nearestPlayer(this.px, this.py);
  if (t) {
    var want = angleTo(this.px, this.py, t.x, t.y);
    var diff = ((want - this.heading + 540) % 360) - 180;
    this.heading += clamp(diff, -180 * dt / 1000, 180 * dt / 1000);
  }
  var v = vec(this.heading, 300);
  this.px += v.x * dt / 1000;
  this.py += v.y * dt / 1000;
}
function draw() { triangle(this.px, this.py, 30, { rot: this.heading + 90 }); }
```

**Text that types itself out**:

```js
meta = { name: 'Typewriter', params: { message: 'HELLO THERE', size: 40 } };
function draw() {
  var n = Math.min(this.message.length, Math.floor(this.age / 60));
  text(this.message.slice(0, n), this.x, this.y, this.size, { color: '#ffffff', align: 'left' });
}
```

Look in `library/` for more: `bullet_ring`, `homing_orb` + `shard`, `laser_sweep`,
`bouncing_balls`, and the effects `camera_kick`, `zoom_pulse`, `custom_sway`, `screen_shake`,
`beat_flash`, `vhs_rewind`.

## Reference card

```
lifecycle   start(p)  update(dt)  draw()  build(p)        this.x this.y this.rot this.age this.params
draw        rect square ellipse circle polygon triangle hexagon star ring spikes line shape text textWidth
            opts: rot color alpha hurts warning front layer align bold
players     players  nearestPlayer(x,y)  hitPlayer(p)  movePlayer(p,x,y)  setPlayerSpeed(speed,turn)
camera      move(x,y) rotate(deg) zoom(s[,x,y])                          ← per frame, in draw()
            kick(x,y,ms,ease) kickRotate(deg,ms,ease) kickZoom(s,ms,ease) shake(i,ms)   ← timed
screen      background(c) flash(c,a)  ← per frame;  flash(c,a,ms,hold) ← timed;  width height left right top bottom
time        time dt bpm beatMs beat(n) beatAt(t)
instances   spawn(name,params,x,y)  kill()
random      random([a[,b]]) randomInt(a,b) pick(arr) chance(p)
math        lerp clamp ease(name,t) dist angleTo rad deg vec(angle,len) rgb(r,g,b[,a])
other       sound(name,volume)  log(...)  add(spec) (patterns)
```
