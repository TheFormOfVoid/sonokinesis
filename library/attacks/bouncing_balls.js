// Bouncing balls: balls thrown out with gravity that bounce off the floor and walls, losing energy.
meta = {
  name: 'Bouncing balls',
  description: 'Balls with gravity that bounce around the screen, then fall away.',
  category: 'Physics',
  params: {
    count: { type: 'int', label: 'Balls', default: 6, min: 1, max: 40 },
    power: { type: 'number', label: 'Throw speed', default: 700, min: 50, max: 2500, step: 10 },
    gravity: { type: 'number', label: 'Gravity', default: 1400, min: 0, max: 5000, step: 50 },
    bounce: { type: 'number', label: 'Bounciness', default: 0.8, min: 0, max: 1, step: 0.05 },
    bounces: { type: 'int', label: 'Bounces before falling', default: 4, min: 1, max: 30 },
    size: { type: 'number', label: 'Size', default: 36, min: 6, max: 200 },
  },
};

function start(p) {
  this.balls = [];
  for (var i = 0; i < this.count; i++) {
    var v = vec(random(-150, -30), this.power * random(0.6, 1));
    this.balls.push({ x: this.x, y: this.y, vx: v.x, vy: v.y, hits: 0 });
  }
}

function update(dt) {
  var s = dt / 1000, alive = 0, r = this.size / 2;
  for (var i = 0; i < this.balls.length; i++) {
    var b = this.balls[i];
    b.vy += this.gravity * s;
    b.x += b.vx * s;
    b.y += b.vy * s;
    var done = b.hits >= this.bounces;
    if (!done && b.y > screen.bottom - r && b.vy > 0) { b.y = screen.bottom - r; b.vy *= -this.bounce; b.hits++; }
    if (!done && (b.x < screen.left + r && b.vx < 0 || b.x > screen.right - r && b.vx > 0)) b.vx *= -this.bounce;
    if (b.y < 500) alive++;
  }
  if (alive === 0) kill();
}

function draw() {
  for (var i = 0; i < this.balls.length; i++) {
    var b = this.balls[i];
    var squash = clamp(1 - Math.abs(b.vy) / 4000, 0.7, 1);
    ellipse(b.x, b.y, this.size / squash, this.size * squash);
  }
}
