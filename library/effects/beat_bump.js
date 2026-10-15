// Beat bump: the camera dips and springs back, on a rhythm you choose. Each step of the pattern is
// one beat (or "every" beats): X is a full bump, x a small one, . nothing. A short object with the
// pattern "X" gives a single bump.
meta = {
  name: 'Beat bump',
  description: 'The camera dips and springs back on the beat, following a pattern (X bump, x small, . none).',
  category: 'Camera',
  length: 8,
  params: {
    amount: { type: 'number', label: 'Bump (units)', default: 9, min: 0, max: 200, step: 0.5 },
    small: { type: 'number', label: 'Small bump (units)', default: 4, min: 0, max: 200, step: 0.5 },
    angle: { type: 'number', label: 'Direction (°)', default: 90, min: -360, max: 360, tooltip: '90 pushes the scene down, -90 up, 0 right' },
    every: { type: 'beats', label: 'Step every', default: 1, min: 0.0625, max: 16, step: 0.0625 },
    pattern: { type: 'text', label: 'Pattern', default: 'X', tooltip: 'Repeats: X bump, x small bump, . nothing' },
    rise: { type: 'number', label: 'Dip time (ms)', default: 100, min: 10, max: 1000 },
    fall: { type: 'number', label: 'Return time (ms)', default: 70, min: 10, max: 1000 },
    bounce: { type: 'number', label: 'Spring', default: 0, min: 0, max: 1, step: 0.05, tooltip: 'Wobbles after the return (0: none)' },
  },
};

function bumpAt(t, size) {
  // Eases out to the full dip, eases back, then an optional damped wobble.
  if (t < 0) return 0;
  if (t < this.rise) return size * Math.sin(Math.PI / 2 * t / this.rise);
  var u = t - this.rise;
  if (u < this.fall) return size * Math.cos(Math.PI / 2 * u / this.fall);
  u -= this.fall;
  if (this.bounce <= 0 || u > this.fall * 5) return 0;
  return size * this.bounce * Math.sin(Math.PI * u / this.fall) * Math.exp(-u / (this.fall * 1.2));
}

function draw() {
  var step = this.every * beatMs;
  var pattern = String(this.pattern || 'X');
  var d = 0;
  // The current step and the one before (a bump can outlast its step when steps are short).
  var n = Math.floor(this.age / step);
  for (var k = Math.max(0, n - 1); k <= n; k++) {
    var c = pattern.charAt(k % pattern.length);
    var size = c === 'X' ? this.amount : c === 'x' ? this.small : 0;
    if (size > 0) d += bumpAt.call(this, this.age - k * step, size);
  }
  if (d !== 0) {
    var a = rad(this.angle);
    camera.move(Math.cos(a) * d, Math.sin(a) * d);
  }
}
