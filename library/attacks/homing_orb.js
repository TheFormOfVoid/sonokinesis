// Homing orb: chases the nearest player, turning a little at a time, then bursts into shards.
meta = {
  name: 'Homing orb',
  description: 'An orb that chases the nearest player, then bursts into shards.',
  category: 'Chasers',
  uses: ['shard'],
  params: {
    speed: { type: 'number', label: 'Speed', default: 260, min: 20, max: 1500, step: 10 },
    turn: { type: 'number', label: 'Turn speed (°/s)', default: 140, min: 10, max: 2000, step: 10 },
    chase: { type: 'beats', label: 'Chases for (beats)', default: 6, min: 1, max: 64 },
    shards: { type: 'int', label: 'Shards', default: 8, min: 0, max: 32 },
    size: { type: 'number', label: 'Size', default: 40, min: 8, max: 200 },
  },
};

function start(p) {
  this.px = this.x;
  this.py = this.y;
  var target = nearestPlayer(this.x, this.y);
  this.heading = target ? angleTo(this.x, this.y, target.x, target.y) : 180;
  this.burst = false;
}

function update(dt) {
  var s = dt / 1000;
  if (!this.burst) {
    var target = nearestPlayer(this.px, this.py);
    if (target) {
      // Turn towards the player, at most turn degrees per second.
      var want = angleTo(this.px, this.py, target.x, target.y);
      var diff = ((want - this.heading) % 360 + 540) % 360 - 180;
      this.heading += clamp(diff, -this.turn * s, this.turn * s);
    }
    var v = vec(this.heading, this.speed);
    this.px += v.x * s;
    this.py += v.y * s;
    if (this.age >= this.chase * beatMs) {
      this.burst = true;
      for (var i = 0; i < this.shards; i++) {
        spawn('shard', { angle: 360 * i / this.shards, speed: this.speed * 1.6, size: this.size * 0.4 }, this.px, this.py);
      }
      sound('explosion', 0.6);
      screen.flash('#ffffff', 0.3, 200);
      kill();
    }
  }
}

function draw() {
  var pulse = 1 + 0.08 * Math.sin(this.age / 60);
  circle(this.px, this.py, this.size * pulse);
  ring(this.px, this.py, this.size * 1.8 * pulse, 0.05, { hurts: false, alpha: 0.4 });
}
