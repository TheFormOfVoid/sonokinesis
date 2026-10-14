// Burst: a bomb grows during its warning, then splits into projectiles flying outward.
// A pattern script: build(p) adds ordinary objects to the level when you place it. p.hit is the song
// time it becomes dangerous (the playhead), p.x / p.y where you dropped it.
meta = {
  name: 'Burst',
  kind: 'pattern',
  description: 'A bomb grows during its warning, then splits into projectiles flying outward.',
  params: {
    count: { type: 'int', label: 'Projectiles', default: 10, min: 2, max: 64 },
    speed: { type: 'number', label: 'Speed', default: 520, min: 50, max: 2000, step: 10, tooltip: 'Units per second' },
    size: { type: 'number', label: 'Projectile size', default: 26, min: 4, max: 200 },
    bomb: { type: 'number', label: 'Bomb size', default: 70, min: 10, max: 400 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 1, min: 0, max: 8, step: 0.25 },
    travel: { type: 'number', label: 'Flight time (ms)', default: 1600, min: 200, max: 8000, step: 50 },
    spin: { type: 'number', label: 'Angle offset (°)', default: 0, min: -360, max: 360 },
  },
};

function build(p) {
  var warn = p.warnBeats * beatMs;
  add({
    name: 'burst bomb', shape: 'circle', start: p.hit - warn, end: p.hit + 120, warn: warn, outro: 120,
    x: p.x, y: p.y,
    w: [[0, p.bomb * 0.4], [warn, p.bomb, 'inBack']],
    h: [[0, p.bomb * 0.4], [warn, p.bomb, 'inBack']],
  });
  var dist = p.speed * p.travel / 1000;
  for (var i = 0; i < p.count; i++) {
    var a = 2 * Math.PI * i / p.count + rad(p.spin);
    add({
      name: 'burst shot', shape: 'circle', start: p.hit, end: p.hit + p.travel, outro: 200,
      x: [[0, p.x], [p.travel, p.x + Math.cos(a) * dist, 'outSine']],
      y: [[0, p.y], [p.travel, p.y + Math.sin(a) * dist, 'outSine']],
      w: p.size, h: p.size,
    });
  }
}
