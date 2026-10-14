// Beat pulse: decorative rings that pulse on every beat. They do not hurt.
meta = {
  name: 'Beat pulse',
  kind: 'pattern',
  description: 'Decorative rings that pulse on every beat. They do not hurt.',
  params: {
    beats: { type: 'int', label: 'Beats', default: 8, min: 1, max: 256 },
    size: { type: 'number', label: 'Size', default: 260, min: 20, max: 1500, step: 5 },
    accent: { type: 'int', label: 'Accent every', default: 4, min: 1, max: 16, tooltip: 'Bigger pulse every N beats' },
    color: { type: 'color', label: 'Color', default: '#33e5ff' },
  },
};

function build(p) {
  for (var i = 0; i < p.beats; i++) {
    var t = p.hit + i * beatMs;
    var s = i % p.accent === 0 ? p.size * 1.4 : p.size;
    add({
      name: 'pulse', shape: { type: 'ring', thickness: 0.04 }, hitbox: false, color: p.color,
      start: t, end: t + beatMs, outro: beatMs * 0.8,
      x: p.x, y: p.y, opacity: 0.5,
      w: [[0, s * 0.6], [beatMs, s, 'outCubic']], h: [[0, s * 0.6], [beatMs, s, 'outCubic']],
    });
  }
}
