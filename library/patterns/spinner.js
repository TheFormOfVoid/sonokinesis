// Spinner: a spiked hub with arms that turn around it. The arms are children of the hub.
meta = {
  name: 'Spinner',
  kind: 'pattern',
  description: 'A spiked hub with arms that turn around it. Arms are children of the hub.',
  params: {
    arms: { type: 'int', label: 'Arms', default: 3, min: 1, max: 12 },
    length: { type: 'number', label: 'Arm length', default: 330, min: 20, max: 1500, step: 5 },
    speed: { type: 'number', label: 'Speed (°/s)', default: 150, min: -1440, max: 1440, step: 5 },
    beats: { type: 'beats', label: 'Lasts (beats)', default: 16, min: 1, max: 256 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 2, min: 0, max: 8, step: 0.25 },
  },
};

function build(p) {
  var warn = p.warnBeats * beatMs, dur = p.beats * beatMs;
  var hub = add({
    name: 'spinner hub', shape: { type: 'spikes', spikes: 10, inner: 0.7 }, layer: 2,
    start: p.hit - warn, end: p.hit + dur, warn: warn, outro: 300,
    x: p.x, y: p.y, w: 70, h: 70,
    rot: [[0, 0], [warn + dur, p.speed * (warn + dur) / 1000]],
  });
  var grow = Math.min(beatMs, warn);
  for (var i = 0; i < p.arms; i++) {
    add({
      name: 'spinner arm', shape: 'rect', parent: hub, pivot: [-0.5, 0], layer: 1,
      start: p.hit - grow, end: p.hit + dur, warn: grow, outro: 300,
      rot: 360 * i / p.arms,
      w: [[0, 0], [Math.max(1, grow), p.length, 'outBack']], h: 22,
    });
  }
}
