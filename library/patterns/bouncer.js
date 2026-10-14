// Bouncer: a spike ball bounces across the screen on the beat.
meta = {
  name: 'Bouncer',
  kind: 'pattern',
  description: 'A spike ball bounces across the screen on the beat.',
  params: {
    dir: { type: 'choice', label: 'From', default: 'left', options: ['left', 'right'] },
    bounces: { type: 'int', label: 'Bounces', default: 4, min: 1, max: 32 },
    beatsPer: { type: 'beats', label: 'Beats per bounce', default: 1, min: 0.25, max: 8, step: 0.25 },
    height: { type: 'number', label: 'Bounce height', default: 370, min: 20, max: 700, step: 5 },
    size: { type: 'number', label: 'Size', default: 90, min: 10, max: 400 },
  },
};

function build(p) {
  var per = p.beatsPer * beatMs, dur = per * p.bounces, side = p.dir === 'right' ? 1 : -1;
  var ys = [[0, 250], [beatMs, 250]];
  for (var i = 0; i < p.bounces; i++) {
    ys.push([beatMs + per * (i + 0.5), 250 - p.height, 'outQuad']);
    ys.push([beatMs + per * (i + 1), 250, 'inQuad']);
  }
  add({
    name: 'bouncer', shape: { type: 'spikes', spikes: 12, inner: 0.72 }, layer: 1,
    start: p.hit - beatMs, end: p.hit + dur, warn: beatMs, outro: 200,
    x: [[0, side * 700], [beatMs, side * 700], [beatMs + dur, -side * 700]],
    y: ys,
    rot: [[0, 0], [beatMs + dur, -side * 180 * p.bounces]],
    w: p.size, h: p.size,
  });
}
