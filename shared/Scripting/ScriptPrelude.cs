namespace Beatform.Core;

/// <summary>
/// The JavaScript side of the scripting API, run in every script engine before the script itself. It
/// turns friendly calls (rect(x, y, w, h, {rot, color})) into a few native calls with plain numbers and
/// strings (the __ functions in ScriptApi). Documented in docs/scripting.md.
/// </summary>
internal static class ScriptPrelude
{
    public const string Source = @"
'use strict';
var __frameCache = -1, __playersCache = [];
var time = 0, dt = 1000 / 60;
var screen = { width: 1280, height: 720, left: -640, right: 640, top: -360, bottom: 360 };

function __flags(o, hurtsDefault) {
  var f = 0;
  var hurts = o && o.hurts !== undefined ? !!o.hurts : hurtsDefault;
  if (hurts) f |= 1;
  if (o && o.front) f |= 2;
  if (o && o.warning) f |= 4;
  if (o && o.align === 'left') f |= 8;
  if (o && o.align === 'right') f |= 16;
  if (o && o.bold === false) f |= 32;
  return f;
}
function __col(o) { return o && o.color !== undefined ? String(o.color) : ''; }
function __alpha(o) { return o && o.alpha !== undefined ? Number(o.alpha) : 1; }
function __rot(o) { return o && o.rot !== undefined ? Number(o.rot) : 0; }
function __layer(o) { return o && o.layer !== undefined ? (o.layer | 0) : 0; }
function __d(kind, x, y, w, h, o, a, b, text, hurts) {
  __draw(kind, +x, +y, +w, +h, __rot(o), __col(o), __alpha(o), __flags(o, hurts === undefined ? true : hurts), __layer(o), a || 0, b || 0, text || '');
}

// ---- drawing (call these in draw) ----
function rect(x, y, w, h, o) { __d(0, x, y, w, h, o); }
function square(x, y, size, o) { __d(0, x, y, size, size, o); }
function ellipse(x, y, w, h, o) { __d(1, x, y, w, h, o); }
function circle(x, y, size, o) { __d(1, x, y, size, size, o); }
function polygon(x, y, size, sides, o) { __d(2, x, y, size, size, o, sides || 3); }
function triangle(x, y, size, o) { __d(2, x, y, size, size, o, 3); }
function hexagon(x, y, size, o) { __d(2, x, y, size, size, o, 6); }
function star(x, y, size, points, inner, o) { __d(3, x, y, size, size, o, points || 5, inner === undefined ? 0.5 : inner); }
function ring(x, y, size, thickness, o) { __d(4, x, y, size, size, o, thickness === undefined ? 0.12 : thickness); }
function spikes(x, y, size, count, inner, o) { __d(5, x, y, size, size, o, count || 12, inner === undefined ? 0.75 : inner); }
function text(str, x, y, size, o) { __d(6, x, y, 0, size, o, 0, 0, String(str), false); }
function line(x1, y1, x2, y2, width, o) {
  var dx = x2 - x1, dy = y2 - y1, len = Math.sqrt(dx * dx + dy * dy);
  var q = Object.assign({}, o || {});
  q.rot = Math.atan2(dy, dx) * 180 / Math.PI;
  __d(0, (x1 + x2) / 2, (y1 + y2) / 2, len, width === undefined ? 4 : width, q);
}
function shape(points, o) {
  var flat = [];
  for (var i = 0; i < points.length; i++) {
    var p = points[i];
    if (Array.isArray(p)) { flat.push(+p[0], +p[1]); } else if (typeof p === 'object') { flat.push(+p.x, +p.y); } else flat.push(+p);
  }
  __polygon(flat.join(','), __col(o), __alpha(o), __flags(o, true), __layer(o));
}
function textWidth(str, size, bold) { return __textWidth(String(str), +size, bold === false ? 0 : 1); }

// ---- players ----
function __players() {
  var f = __frame();
  if (f !== __frameCache) { __frameCache = f; __playersCache = JSON.parse(__playersJson()); }
  return __playersCache;
}
Object.defineProperty(globalThis, 'players', { get: function () { return __players(); }, configurable: true });
function nearestPlayer(x, y) {
  var best = null, bd = Infinity, ps = __players();
  for (var i = 0; i < ps.length; i++) {
    if (!ps[i].alive) continue;
    var dx = ps[i].x - x, dy = ps[i].y - y, d = dx * dx + dy * dy;
    if (d < bd) { bd = d; best = ps[i]; }
  }
  return best;
}
function __num(p) { return typeof p === 'object' && p ? p.number : (p | 0); }
function hitPlayer(p) { __hit(__num(p)); }
function movePlayer(p, x, y) { __move(__num(p), +x, +y); }
function setPlayerSpeed(speed, turn) { __speed(+speed, turn === undefined ? 1 : +turn); }

// ---- camera and screen ----
var camera = {
  move: function (x, y) { __cam(0, +x, +y || 0, 0); },
  rotate: function (deg) { __cam(1, +deg, 0, 0); },
  zoom: function (scale, x, y) { __cam(x === undefined ? 2 : 3, +scale, +x || 0, +y || 0); },
  kick: function (x, y, ms, e) { __effect(0, +x, +y, 0, ms === undefined ? 300 : +ms, e || 'outQuad', ''); },
  kickRotate: function (deg, ms, e) { __effect(1, +deg, 0, 0, ms === undefined ? 300 : +ms, e || 'outQuad', ''); },
  kickZoom: function (scale, ms, e) { __effect(2, +scale, 0, 0, ms === undefined ? 300 : +ms, e || 'outQuad', ''); },
  shake: function (intensity, ms) { __effect(3, +intensity, 0, 0, ms === undefined ? 300 : +ms, 'linear', ''); },
};
screen.flash = function (color, alpha, ms, hold) {
  __effect(4, alpha === undefined ? 1 : +alpha, hold || 0, 0, ms === undefined ? -1 : +ms, 'linear', String(color || '#ffffff'));
};
screen.background = function (color) { __background(String(color)); };

// ---- time and beats ----
function beat(n) { return n * beatMs; }
function beatAt(t) { return t / beatMs; }

// ---- instances ----
function spawn(name, params, x, y) { __spawn(String(name), JSON.stringify(params || {}), x === undefined ? NaN : +x, y === undefined ? NaN : +y); }
function kill() { __kill(); }

// ---- random (the same every time for the same play, so rewinds repeat it) ----
function random(a, b) {
  var r = __random();
  if (a === undefined) return r;
  if (b === undefined) return r * a;
  return a + r * (b - a);
}
function randomInt(a, b) { return Math.floor(random(a, b + 1)); }
function pick(arr) { return arr[Math.floor(__random() * arr.length)]; }
function chance(p) { return __random() < p; }
Math.random = function () { return __random(); };

// ---- math ----
function lerp(a, b, t) { return a + (b - a) * t; }
function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
function ease(name, t) { return __ease(String(name), +t); }
function dist(x1, y1, x2, y2) { var dx = x2 - x1, dy = y2 - y1; return Math.sqrt(dx * dx + dy * dy); }
function angleTo(x1, y1, x2, y2) { return Math.atan2(y2 - y1, x2 - x1) * 180 / Math.PI; }
function rad(d) { return d * Math.PI / 180; }
function deg(r) { return r * 180 / Math.PI; }
function vec(angle, length) { var a = angle * Math.PI / 180; return { x: Math.cos(a) * length, y: Math.sin(a) * length }; }
function rgb(r, g, b, a) {
  function h(v) { v = Math.max(0, Math.min(255, Math.round(v))); return (v < 16 ? '0' : '') + v.toString(16); }
  return '#' + h(r) + h(g) + h(b) + (a === undefined ? '' : h(a * 255));
}

// ---- other ----
function sound(name, volume) { __sound(String(name), volume === undefined ? 1 : +volume); }
function log() { var s = []; for (var i = 0; i < arguments.length; i++) s.push(typeof arguments[i] === 'object' ? JSON.stringify(arguments[i]) : String(arguments[i])); __log(s.join(' ')); }

// Patterns: add an object to the level. The fields are those of level.json, with shortcuts:
// shape can be a name ('circle'), and x, y, rot, w, h, opacity can be given directly as a number or as
// keys [[t, value, ease], ...] (t relative to start) instead of inside tracks.
function add(spec) {
  var o = Object.assign({}, spec);
  if (typeof o.shape === 'string') o.shape = { type: o.shape };
  var tracks = Object.assign({}, o.tracks || {});
  ['x', 'y', 'rot', 'w', 'h', 'opacity'].forEach(function (k) {
    if (o[k] === undefined) return;
    tracks[k] = typeof o[k] === 'number' ? [[0, o[k]]] : o[k];
    delete o[k];
  });
  o.tracks = tracks;
  return __add(JSON.stringify(o));
}

// Called by the host.
function __new() { return {}; }
function __start(self, paramsJson, x, y, rot, t) {
  var p = JSON.parse(paramsJson);
  self.x = x; self.y = y; self.rot = rot; self.age = 0; time = t;
  for (var k in p) if (!(k in self)) self[k] = p[k];
  self.params = p;
  if (typeof start === 'function') start.call(self, p);
}
function __update(self, age, t) { self.age = age; time = t; if (typeof update === 'function') update.call(self, dt); }
function __drawCall(self, age, t) { self.age = age; time = t; if (typeof draw === 'function') draw.call(self); }
function __build(paramsJson) { if (typeof build === 'function') build.call({}, JSON.parse(paramsJson)); }
";
}
