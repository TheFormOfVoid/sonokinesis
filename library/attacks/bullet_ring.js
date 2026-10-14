// Bullet ring: a core warns, then fires rings of bullets outward, turning a bit each volley.
// A live attack: start() runs once when it spawns, update(dt) every 1/60 s, draw() every frame.
meta = {
  name: 'Bullet ring',
  description: 'A core that fires rings of bullets outward on the beat.',
  category: 'Projectiles',
  params: {
    volleys: { type: 'int', label: 'Volleys', default: 4, min: 1, max: 32 },
    every: { type: 'beats', label: 'Every (beats)', default: 1, min: 0.25, max: 8, step: 0.25 },
    count: { type: 'int', label: 'Bullets per ring', default: 12, min: 3, max: 64 },
    speed: { type: 'number', label: 'Speed', default: 300, min: 20, max: 2000, step: 10 },
    turn: { type: 'number', label: 'Turn per volley (°)', default: 15, min: -180, max: 180 },
    size: { type: 'number', label: 'Bullet size', default: 18, min: 4, max: 100 },
    warnBeats: { type: 'beats', label: 'Warning (beats)', default: 1, min: 0, max: 8, step: 0.25 },
  },
};

function start(p) {
  this.bullets = [];
  this.fired = 0;
  this.warn = p.warnBeats * beatMs;
}

function update(dt) {
  // Fire on schedule.
  var next = this.warn + this.fired * this.every * beatMs;
  if (this.fired < this.volleys && this.age >= next) {
    for (var i = 0; i < this.count; i++) {
      var a = rad(360 * i / this.count + this.fired * this.turn);
      this.bullets.push({ x: this.x, y: this.y, vx: Math.cos(a) * this.speed, vy: Math.sin(a) * this.speed });
    }
    this.fired++;
    camera.shake(4, 150);
  }
  // Move the bullets and forget those far off screen.
  for (var j = this.bullets.length - 1; j >= 0; j--) {
    var b = this.bullets[j];
    b.x += b.vx * dt / 1000;
    b.y += b.vy * dt / 1000;
    if (Math.abs(b.x) > 800 || Math.abs(b.y) > 500) this.bullets.splice(j, 1);
  }
  if (this.fired >= this.volleys && this.bullets.length === 0) kill();
}

function draw() {
  if (this.age < this.warn) {
    // Warning: the core grows in, harmless.
    var g = ease('outBack', this.age / this.warn);
    circle(this.x, this.y, 50 * g, { warning: true });
  } else if (this.fired < this.volleys) {
    var beatPulse = 1 + 0.15 * Math.max(0, 1 - ((this.age - this.warn) % (this.every * beatMs)) / 150);
    hexagon(this.x, this.y, 50 * beatPulse, { rot: this.age * 0.2 });
  }
  for (var i = 0; i < this.bullets.length; i++) circle(this.bullets[i].x, this.bullets[i].y, this.size);
}
