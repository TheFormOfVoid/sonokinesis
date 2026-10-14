// VHS rewind: darkens the screen and draws drifting tape bands, flickering scan lines, tape skips and a
// blinking rewind sign over everything, for as long as the object lasts. Optionally freezes the players.
meta = {
  name: 'VHS rewind',
  description: 'A tape-rewind look over the whole screen while it lasts.',
  category: 'Screen',
  params: {
    dim: { type: 'number', label: 'Darken', default: 0.45, min: 0, max: 1, step: 0.05 },
    bands: { type: 'int', label: 'Bands', default: 3, min: 0, max: 8 },
    lines: { type: 'int', label: 'Scan lines', default: 15, min: 0, max: 60 },
    freeze: { type: 'bool', label: 'Freeze the players', default: true },
    turn: { type: 'number', label: 'Their turning', default: 0.5, min: 0.001, max: 1, step: 0.05, tooltip: 'While frozen, how quickly they still turn' },
  },
};

var W = 1400, H = 800;

// Smooth pseudo-random values from the time, so the look is the same every time it plays.
function wobble(t, k) { return Math.sin(t * 0.07 + k * 137) * Math.cos(t * 0.04 + k * 19); }

function skipAt(t) {
  var n = Math.sin(t * 0.023) * Math.cos(t * 0.047) * Math.sin(t * 0.089);
  if (n > 0.88) return { y: 60, x: 12, on: true };
  if (n < -0.88) return { y: -45, x: -12, on: true };
  return { y: 0, x: 0, on: false };
}

function draw() {
  var t = this.age, front = { front: true };
  if (this.freeze) setPlayerSpeed(0, this.turn);
  rect(0, 0, W, H, { front: true, color: '#000000', alpha: this.dim });
  // Grey lift that flickers like tape noise, brighter while the tape skips.
  var skip = skipAt(t);
  rect(0, 0, W, H, { front: true, color: '#ffffff', alpha: 0.085 + 0.015 * Math.abs(Math.sin(t * 0.061)) + (skip.on ? 0.03 : 0) });
  // Scan lines jumping around.
  var step = Math.floor(t / 33);
  for (var k = 0; k < this.lines; k++) {
    var y = Math.sin(step * 0.7 + k * 137) * 400 + skip.y;
    rect(0, y, W, 1 + Math.abs(Math.sin(k * 33)) * 3, { front: true, color: '#ffffff', alpha: 0.01 + Math.abs(Math.cos(step * 0.4 + k * 19)) * 0.04 });
  }
  // Bands drifting on a parabola, the middle one the other way, tilting when the tape skips.
  for (var c = 0; c < this.bands; c++) {
    var inv = c % 2 === 1 ? -1 : 1;
    var sway = (1 - Math.pow(((t / 1800) % 2) - 1, 2)) * 120 - skip.y;
    var by = -260 + c * (520 / Math.max(1, this.bands - 1)) - sway * inv;
    var rot = skip.on ? inv * (2 + c) : 0;
    [[2.8, 0.15], [1.6, 0.35], [1, 1]].forEach(function (s) {
      rect(skip.x, by, 2600, 27 * s[0], { front: true, color: '#ffffff', alpha: (0.1 + c * 0.03) * s[1], rot: rot });
    });
  }
  // The blinking rewind sign, bottom left, with red and blue fringes.
  var blink = 0.4 + Math.abs(Math.sin(t * 0.005)) * 0.4;
  [[-2, '#ff3030', 0.8], [2, '#3050ff', 0.8], [0, '#ffffff', 1]].forEach(function (f) {
    triangle(-560 + f[0], 290, 40, { front: true, rot: -90, color: f[1], alpha: blink * f[2] });
    triangle(-526 + f[0], 290, 40, { front: true, rot: -90, color: f[1], alpha: blink * f[2] });
  });
}
