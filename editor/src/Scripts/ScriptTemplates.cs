namespace Beatform.Editor.Scripts;

/// <summary>
/// Starting points for new scripts. The code these write into a level is under MIT No Attribution, like
/// library/ (see library/LICENSE), so levels made from them carry no license conditions.
/// </summary>
public static class ScriptTemplates
{
    public static readonly string[] Kinds = { "attack", "effect", "pattern" };
    public static readonly string[] KindLabels = { "Attack (runs live in the game)", "Effect (camera, screen, players)", "Pattern (builds objects in the editor)" };

    public static string For(string kind, string name) => kind switch
    {
        "effect" => Effect(name),
        "pattern" => Pattern(name),
        _ => Attack(name),
    };

    private static string Attack(string name) => $@"// {name}: describe your attack here.
// start(p) runs once when it spawns (p holds the parameters, also available as this.NAME),
// update(dt) every 1/60 second (dt in ms), draw() every frame. this.x / this.y is where it was placed,
// this.age the time since it spawned (ms). See the Scripting guide (Help) for everything you can call.
meta = {{
  name: '{name}',
  description: 'A shape that grows in on the beat, then flies at the nearest player.',
  length: 8,
  params: {{
    size: {{ type: 'number', label: 'Size', default: 40, min: 4, max: 400 }},
    speed: {{ type: 'number', label: 'Speed', default: 500, min: 10, max: 3000, step: 10 }},
    warnBeats: {{ type: 'beats', label: 'Warning (beats)', default: 1, min: 0, max: 8, step: 0.25 }},
  }},
}};

function start(p) {{
  this.px = this.x;
  this.py = this.y;
  this.vx = 0;
  this.vy = 0;
  this.launched = false;
}}

function update(dt) {{
  if (!this.launched && this.age >= this.warnBeats * beatMs) {{
    this.launched = true;
    var target = nearestPlayer(this.px, this.py);
    var angle = target ? angleTo(this.px, this.py, target.x, target.y) : 180;
    var v = vec(angle, this.speed);
    this.vx = v.x;
    this.vy = v.y;
    camera.shake(5, 150);
  }}
  this.px += this.vx * dt / 1000;
  this.py += this.vy * dt / 1000;
  if (Math.abs(this.px) > 800 || Math.abs(this.py) > 500) kill();
}}

function draw() {{
  var warn = this.warnBeats * beatMs;
  if (this.age < warn) {{
    // While warning it grows in and cannot hurt.
    square(this.px, this.py, this.size * ease('outBack', this.age / warn), {{ warning: true, rot: 45 }});
  }} else {{
    square(this.px, this.py, this.size, {{ rot: 45 + this.age * 0.3 }});
  }}
}}
";

    private static string Effect(string name) => $@"// {name}: an effect on the camera, the screen or the players for as long as the object lasts.
// draw() runs every frame; camera.move / rotate / zoom, screen.flash (without a duration),
// screen.background and setPlayerSpeed apply to that frame only.
meta = {{
  name: '{name}',
  description: 'Tilts the camera back and forth on the beat.',
  category: 'Camera',     // 'Camera', 'Screen' or 'Effects' list it under Effects in the palette
  length: 8,
  params: {{
    tilt: {{ type: 'number', label: 'Tilt (°)', default: 3, min: 0, max: 45, step: 0.5 }},
    every: {{ type: 'beats', label: 'Every (beats)', default: 2, min: 0.25, max: 16, step: 0.25 }},
  }},
}};

function draw() {{
  var period = this.every * beatMs;
  var phase = (time % period) / period;
  camera.rotate(Math.sin(phase * Math.PI * 2) * this.tilt);
}}
";

    private static string Pattern(string name) => $@"// {name}: a pattern builds ordinary objects when you place it in the editor; they can then be edited
// like any other. p.hit is the song time it becomes dangerous (the playhead), p.x / p.y where you
// dropped it. add() takes the fields of a level.json object; x, y, rot, w, h and opacity can be a
// number or keys [[time, value, ease], ...] with time relative to start.
meta = {{
  name: '{name}',
  kind: 'pattern',
  description: 'A row of pillars that rise one after another.',
  params: {{
    count: {{ type: 'int', label: 'Pillars', default: 6, min: 1, max: 32 }},
    gap: {{ type: 'number', label: 'Spacing', default: 160, min: 20, max: 600 }},
    every: {{ type: 'beats', label: 'Every (beats)', default: 0.5, min: 0.0625, max: 4, step: 0.0625 }},
  }},
}};

function build(p) {{
  for (var i = 0; i < p.count; i++) {{
    var t = p.hit + i * p.every * beatMs;
    var x = p.x + (i - (p.count - 1) / 2) * p.gap;
    add({{
      name: 'pillar', shape: 'rect', start: t - beatMs, end: t + beatMs * 2, warn: beatMs, outro: 200,
      x: x, y: 360, w: 60,
      h: [[0, 40], [beatMs, 40], [beatMs + 150, 520, 'outBack']],
    }});
  }}
}}
";
}
