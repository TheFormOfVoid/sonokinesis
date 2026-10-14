// Shard: a small triangle that flies in a straight line and fades out. Spawned by other attacks
// (homing_orb uses it), but you can place it on its own too.
meta = {
  name: 'Shard',
  description: 'A triangle flying in a straight line. Other attacks spawn it.',
  category: 'Projectiles',
  params: {
    angle: { type: 'number', label: 'Direction (°)', default: 0, min: -360, max: 360 },
    speed: { type: 'number', label: 'Speed', default: 400, min: 10, max: 3000, step: 10 },
    size: { type: 'number', label: 'Size', default: 18, min: 4, max: 200 },
    life: { type: 'number', label: 'Lifetime (ms)', default: 2500, min: 100, max: 20000, step: 50 },
  },
};

function draw() {
  // Everything follows from this.age, so this attack needs no update() and scrubs instantly.
  var d = this.speed * this.age / 1000;
  var v = vec(this.angle, d);
  var fade = clamp((this.life - this.age) / 300, 0, 1);
  if (fade <= 0) return;
  triangle(this.x + v.x, this.y + v.y, this.size, { rot: this.angle + 90 + this.age * 0.5, alpha: fade, hurts: fade > 0.5 });
}
