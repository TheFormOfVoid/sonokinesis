// Screen shake: shakes the camera, fading out.
meta = {
  name: 'Screen shake',
  description: 'Shakes the camera, fading out over its length.',
  category: 'Camera',
  params: {
    intensity: { type: 'number', label: 'Strength', default: 12, min: 1, max: 200 },
    ms: { type: 'number', label: 'Length (ms)', default: 400, min: 30, max: 10000, step: 10 },
  },
};

function start(p) {
  camera.shake(p.intensity, p.ms);
  kill();
}
