// Spiral: a turret fires projectiles one after another while turning.
meta = {
  name: 'Spiral',
  kind: 'pattern',
  description: 'A turret fires projectiles one after another while turning.',
  params: {
    count: { type: 'int', label: 'Shots', default: 24, min: 2, max: 256 },
    interval: { type: 'number', label: 'Every (ms)', default: 80, min: 10, max: 2000, step: 5 },
    turn: { type: 'number', label: 'Turn per shot (°)', default: 23, min: -180, max: 180 },
    arms: { type: 'int', label: 'Streams', default: 2, min: 1, max: 8 },
    speed: { type: 'number', label: 'Speed', default: 420, min: 50, max: 2000, step: 10 },
    size: { type: 'number', label: 'Shot size', default: 22, min: 4, max: 200 },
  },
};

function build(p) {
  var travel = 2200, dist = p.speed * travel / 1000;
  add({
    name: 'spiral core', shape: { type: 'polygon', sides: 6 },
    start: p.hit - beatMs, end: p.hit + p.count * p.interval + 200, warn: beatMs, outro: 200,
    x: p.x, y: p.y, w: 54, h: 54,
    rot: [[0, 0], [beatMs + p.count * p.interval, p.turn * p.count]],
  });
  for (var i = 0; i < p.count; i++) {
    for (var a = 0; a < p.arms; a++) {
      var ang = rad(p.turn * i + 360 * a / p.arms);
      var t = p.hit + i * p.interval;
      add({
        name: 'spiral shot', shape: 'circle', start: t, end: t + travel, outro: 200,
        x: [[0, p.x], [travel, p.x + Math.cos(ang) * dist]],
        y: [[0, p.y], [travel, p.y + Math.sin(ang) * dist]],
        w: p.size, h: p.size,
      });
    }
  }
}
