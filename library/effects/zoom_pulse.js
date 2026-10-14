// Zoom pulse: the camera punches in on every beat for as long as the object lasts.
meta = {
  name: 'Zoom pulse',
  description: 'The camera punches in on every beat while it lasts.',
  category: 'Camera',
  params: {
    amount: { type: 'number', label: 'Punch (×)', default: 1.06, min: 1, max: 2, step: 0.01 },
    every: { type: 'beats', label: 'Every (beats)', default: 1, min: 0.25, max: 8, step: 0.25 },
    ms: { type: 'number', label: 'Back in (ms)', default: 250, min: 30, max: 2000, step: 10 },
  },
};

function draw() {
  // Pure function of time: the punch starts on each beat (counted from the song start) and fades.
  var period = this.every * beatMs;
  var since = ((time % period) + period) % period;
  var p = clamp(since / this.ms, 0, 1);
  camera.zoom(1 + (this.amount - 1) * (1 - ease('outCubic', p)));
}
