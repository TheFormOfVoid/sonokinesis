// Wall with gap: a wall sweeps across the screen. Slip through the gap.
meta = {
  name: 'Wall with gap',
  kind: 'pattern',
  description: 'A wall sweeps across the screen. Slip through the gap.',
  params: {
    dir: { type: 'choice', label: 'Direction', default: 'left → right', options: ['left → right', 'right → left', 'top → bottom', 'bottom → top'] },
    gap: { type: 'number', label: 'Gap position', default: 0, min: -300, max: 300, step: 5, tooltip: 'Across the wall, from the center' },
    gapSize: { type: 'number', label: 'Gap size', default: 170, min: 40, max: 600, step: 5 },
    thickness: { type: 'number', label: 'Thickness', default: 60, min: 10, max: 400 },
    beats: { type: 'beats', label: 'Sweep (beats)', default: 4, min: 0.5, max: 32, step: 0.25 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 2, min: 0, max: 8, step: 0.25 },
  },
};

function build(p) {
  var dir = ['left → right', 'right → left', 'top → bottom', 'bottom → top'].indexOf(p.dir);
  if (dir < 0) dir = 0;
  var horizontalMove = dir < 2;
  var warn = p.warnBeats * beatMs, dur = p.beats * beatMs;
  var extent = horizontalMove ? 360 : 640;
  var from = horizontalMove ? (dir === 0 ? -700 : 700) : (dir === 2 ? -420 : 420);
  var edge = from * 0.92;
  // One piece from the near edge to the gap, one from the gap to the far edge.
  [[-extent, p.gap - p.gapSize / 2], [p.gap + p.gapSize / 2, extent]].forEach(function (piece) {
    var lo = piece[0], hi = piece[1];
    if (hi - lo <= 1) return;
    var o = { name: 'wall', shape: 'rect', start: p.hit - warn, end: p.hit + dur, warn: warn, outro: 100 };
    var sweep = [[0, edge], [warn, edge], [warn + dur, -from, 'inOutSine']];
    if (horizontalMove) { o.x = sweep; o.y = (lo + hi) / 2; o.w = p.thickness; o.h = hi - lo; }
    else { o.y = sweep; o.x = (lo + hi) / 2; o.h = p.thickness; o.w = hi - lo; }
    add(o);
  });
}
