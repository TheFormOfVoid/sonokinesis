// Rain: shapes fall from the top on the beat, at random positions.
meta = {
  name: 'Rain',
  kind: 'pattern',
  description: 'Shapes fall from the top on the beat, at random positions.',
  params: {
    count: { type: 'int', label: 'Drops', default: 8, min: 1, max: 128 },
    interval: { type: 'beats', label: 'Every (beats)', default: 0.5, min: 0.0625, max: 8, step: 0.0625 },
    shape: { type: 'choice', label: 'Shape', default: 'square', options: ['square', 'star', 'triangle', 'circle', 'spike ball'] },
    size: { type: 'number', label: 'Size', default: 46, min: 6, max: 300 },
    fall: { type: 'number', label: 'Fall time (ms)', default: 1500, min: 200, max: 8000, step: 50 },
    seed: { type: 'int', label: 'Random seed', default: 1, min: 0, max: 9999, tooltip: 'Change it for other positions' },
  },
};

// A small seeded random generator, so the same seed gives the same rain every time you build it.
function seeded(seed) {
  var s = (seed * 2654435761) >>> 0 || 1;
  return function () { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}

function build(p) {
  var r = seeded(p.seed);
  var shapes = { 'square': { type: 'rect' }, 'star': { type: 'star', inner: 0.45 }, 'triangle': { type: 'polygon', sides: 3 },
                 'circle': { type: 'circle' }, 'spike ball': { type: 'spikes' } };
  for (var i = 0; i < p.count; i++) {
    var t = p.hit + i * p.interval * beatMs;
    var x = r() * 1160 - 580;
    add({
      name: 'rain drop', shape: shapes[p.shape] || shapes.square,
      start: t - beatMs, end: t + p.fall, warn: beatMs, outro: 150,
      x: [[0, x], [beatMs + p.fall, x + r() * 160 - 80]],
      y: [[0, -330], [beatMs, -330], [beatMs + p.fall, 430, 'inQuad']],
      rot: [[beatMs, 0], [beatMs + p.fall, r() < 0.5 ? -270 : 270]],
      w: p.size, h: p.size,
    });
  }
}
