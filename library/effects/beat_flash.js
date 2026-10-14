// Beat flash: the screen flashes on every beat while it lasts.
meta = {
  name: 'Beat flash',
  description: 'A screen flash on every beat while it lasts.',
  category: 'Screen',
  params: {
    color: { type: 'color', label: 'Color', default: '#ffffff' },
    alpha: { type: 'number', label: 'Strength', default: 0.25, min: 0, max: 1, step: 0.05 },
    every: { type: 'beats', label: 'Every (beats)', default: 1, min: 0.25, max: 8, step: 0.25 },
    ms: { type: 'number', label: 'Fade (ms)', default: 200, min: 10, max: 2000, step: 10 },
  },
};

function draw() {
  var period = this.every * beatMs;
  var since = ((time % period) + period) % period;
  if (since < this.ms) screen.flash(this.color, this.alpha * (1 - since / this.ms));
}
