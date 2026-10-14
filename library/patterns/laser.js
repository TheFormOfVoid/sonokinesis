// Laser: a thin warning line, then a full-width beam through the drop point.
meta = {
  name: 'Laser',
  kind: 'pattern',
  description: 'A thin warning line, then a full-width beam through the drop point.',
  params: {
    dir: { type: 'choice', label: 'Orientation', default: 'horizontal', options: ['horizontal', 'vertical', 'diagonal ↘', 'diagonal ↗'] },
    thickness: { type: 'number', label: 'Thickness', default: 70, min: 6, max: 600 },
    beats: { type: 'beats', label: 'Beam time (beats)', default: 2, min: 0.25, max: 32, step: 0.25 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 2, min: 0, max: 8, step: 0.25 },
  },
};

function build(p) {
  var warn = p.warnBeats * beatMs, dur = p.beats * beatMs;
  var rot = { 'horizontal': 0, 'vertical': 90, 'diagonal ↘': 30, 'diagonal ↗': -30 }[p.dir] || 0;
  add({
    name: 'laser', shape: 'rect', start: p.hit - warn, end: p.hit + dur, warn: warn, outro: 180,
    x: p.x, y: p.y, rot: rot, w: 2800,
    h: [[0, 4], [warn, 4], [warn + 90, p.thickness, 'outExpo']],
  });
}
