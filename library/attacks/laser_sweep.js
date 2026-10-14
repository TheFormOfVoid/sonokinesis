// Laser sweep: a beam fixed at one end swings across an angle, after a warning line shows its path.
meta = {
  name: 'Laser sweep',
  description: 'A beam pivots around a point and sweeps across the screen.',
  category: 'Lasers',
  params: {
    from: { type: 'number', label: 'Start angle (°)', default: -30, min: -360, max: 360 },
    to: { type: 'number', label: 'End angle (°)', default: 210, min: -360, max: 360 },
    beats: { type: 'beats', label: 'Sweep (beats)', default: 4, min: 0.25, max: 32, step: 0.25 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 1, min: 0, max: 8, step: 0.25 },
    width: { type: 'number', label: 'Width', default: 40, min: 4, max: 400 },
    sweepEase: { type: 'ease', label: 'Ease', default: 'inOutSine' },
  },
};

function draw() {
  var warn = this.warnBeats * beatMs, dur = this.beats * beatMs, len = 1600;
  var t = this.age - warn;
  if (t > dur + 200) return;
  var p = clamp(t / dur, 0, 1);
  var a = lerp(this.from, this.to, ease(this.sweepEase, p));
  var end = vec(a, len);
  circle(this.x, this.y, this.width * 1.6, { warning: t < 0 });
  if (t < 0) {
    // The warning: a thin line where the beam will start.
    line(this.x, this.y, this.x + end.x, this.y + end.y, 4, { warning: true });
    return;
  }
  var w = t < 80 ? lerp(4, this.width, ease('outExpo', t / 80)) : t > dur ? lerp(this.width, 0, (t - dur) / 200) : this.width;
  line(this.x, this.y, this.x + end.x, this.y + end.y, w);
}
