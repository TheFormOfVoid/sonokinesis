// Camera kick: jolts the camera one way, then it springs back.
meta = {
  name: 'Camera kick',
  description: 'Jolts the camera in a direction; it eases back.',
  category: 'Camera',
  params: {
    kickX: { type: 'number', label: 'Kick x', default: 0, min: -500, max: 500 },
    kickY: { type: 'number', label: 'Kick y', default: -25, min: -500, max: 500, tooltip: 'Negative is up' },
    ms: { type: 'number', label: 'Back in (ms)', default: 300, min: 30, max: 5000, step: 10 },
    springEase: { type: 'ease', label: 'Ease', default: 'outQuad' },
  },
};

function start(p) {
  camera.kick(p.kickX, p.kickY, p.ms, p.springEase);
  kill();
}
