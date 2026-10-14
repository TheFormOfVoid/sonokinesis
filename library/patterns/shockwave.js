// Shockwave: a ring that expands from a point. Its hole is safe.
meta = {
  name: 'Shockwave',
  kind: 'pattern',
  description: 'A ring that expands from a point. Its hole is safe.',
  params: {
    max: { type: 'number', label: 'Final size', default: 900, min: 50, max: 3000, step: 10 },
    duration: { type: 'number', label: 'Expand time (ms)', default: 1400, min: 100, max: 10000, step: 50 },
    thickness: { type: 'number', label: 'Thickness', default: 0.06, min: 0.02, max: 0.5, step: 0.01 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 1, min: 0, max: 8, step: 0.25 },
  },
};

function build(p) {
  var warn = p.warnBeats * beatMs;
  var size = [[0, 60], [warn, 60], [warn + p.duration, p.max, 'outQuad']];
  add({
    name: 'shockwave', shape: { type: 'ring', thickness: p.thickness },
    start: p.hit - warn, end: p.hit + p.duration, warn: warn, outro: 250,
    x: p.x, y: p.y, w: size, h: size,
  });
}
