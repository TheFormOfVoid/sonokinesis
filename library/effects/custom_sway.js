// Custom sway: the whole scene rocks and drifts while the object lasts. Like the built-in sway event,
// but you can change the motion however you like (here: a figure-eight drift and a slow roll).
meta = {
  name: 'Custom sway',
  description: 'Rocks and drifts the camera while it lasts; edit the formula to make your own.',
  category: 'Camera',
  params: {
    roll: { type: 'number', label: 'Roll (°)', default: 1.5, min: 0, max: 45, step: 0.1 },
    drift: { type: 'number', label: 'Drift', default: 12, min: 0, max: 300 },
    period: { type: 'number', label: 'Period (ms)', default: 4000, min: 200, max: 60000, step: 50 },
    fade: { type: 'number', label: 'Fade in/out (ms)', default: 800, min: 0, max: 10000, step: 50 },
  },
};

function draw() {
  var t = this.age / this.period * 2 * Math.PI;
  // Fade in at the start; it stops when the object ends.
  var k = this.fade > 0 ? clamp(this.age / this.fade, 0, 1) : 1;
  camera.rotate(Math.sin(t) * this.roll * k);
  camera.move(Math.sin(t) * this.drift * k, Math.sin(2 * t) * this.drift * 0.5 * k);
}
