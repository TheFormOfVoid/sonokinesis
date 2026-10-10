"""Generates the Beatform demo level: an original synthesized 128 BPM track and a level that uses
every shape, parenting, eases, checkpoints and scene events. Nothing here comes from any game.

    python make_demo.py <output folder>

Writes level.json and song.wav into the folder (convert song.wav to .ogg with ffmpeg if you like
and update meta.audio)."""
import array
import json
import math
import os
import random
import sys
import wave

BPM = 128
BEAT = 60000 / BPM  # ms
BAR = BEAT * 4
BARS = 32
RATE = 44100
PINK = "#ff2a6d"
CYAN = "#33e5ff"
GOLD = "#ffd23f"


def b(bar, beat=0.0):
    """Song time in ms of a bar (1-based) and beat offset (0-based, may be fractional)."""
    return (bar - 1) * BAR + beat * BEAT


# ----------------------------------------------------------------------------------------------
# Audio
# ----------------------------------------------------------------------------------------------

def synth(path):
    length = int((b(BARS + 1) + 1500) / 1000 * RATE)
    out = array.array("f", [0.0]) * length
    rnd = random.Random(7)

    def add(start_ms, samples):
        i0 = int(start_ms / 1000 * RATE)
        for k, v in enumerate(samples):
            j = i0 + k
            if j >= length:
                break
            out[j] += v

    def kick():
        n = int(0.22 * RATE)
        res, ph = [], 0.0
        for k in range(n):
            t = k / RATE
            f = 45 + 95 * math.exp(-t * 28)
            ph += 2 * math.pi * f / RATE
            res.append(math.sin(ph) * math.exp(-t * 11) * 0.9)
        return res

    def noise(dur, decay, gain, hp=False):
        n = int(dur * RATE)
        res, prev = [], 0.0
        for k in range(n):
            v = rnd.uniform(-1, 1)
            if hp:
                v, prev = v - prev, v
            res.append(v * math.exp(-k / RATE * decay) * gain)
        return res

    def tone(freq, dur, gain, harmonics=(1.0, 0.5, 0.33, 0.25), decay=3.0, attack=0.005):
        n = int(dur * RATE)
        res = []
        for k in range(n):
            t = k / RATE
            env = min(1.0, t / attack) * math.exp(-t * decay)
            v = sum(a * math.sin(2 * math.pi * freq * (h + 1) * t) for h, a in enumerate(harmonics))
            res.append(v * env * gain)
        return res

    kick_s = kick()
    clap_s = noise(0.16, 22, 0.35)
    hat_s = noise(0.05, 70, 0.18, hp=True)
    # A minor progression: Am F C G, one chord per bar.
    roots = [110.0, 87.31, 130.81, 98.0]
    arp = {110.0: [220, 261.63, 329.63, 440], 87.31: [174.61, 220, 261.63, 349.23],
           130.81: [261.63, 329.63, 392, 523.25], 98.0: [196, 246.94, 293.66, 392]}
    bass_cache, lead_cache, pad_cache = {}, {}, {}

    for bar in range(1, BARS + 1):
        root = roots[(bar - 1) % 4]
        breakdown = 25 <= bar <= 28
        for beat in range(4):
            t0 = b(bar, beat)
            if not breakdown:
                add(t0, kick_s)
            if bar >= 9 and not breakdown and beat in (1, 3):
                add(t0, clap_s)
            if bar >= 5 and not breakdown:
                add(t0 + BEAT / 2, hat_s)
        if bar >= 9 and not breakdown:
            for e in range(8):
                key = root
                if key not in bass_cache:
                    bass_cache[key] = tone(key, BEAT / 2000 * 0.95, 0.22, (1, 0.6, 0.4, 0.3, 0.2), decay=6)
                add(b(bar, e / 2), bass_cache[key])
        if bar >= 17 and not breakdown:
            notes = arp[root]
            for s in range(16):
                f = notes[s % 4] * (2 if s % 8 >= 4 else 1)
                if f not in lead_cache:
                    lead_cache[f] = tone(f, BEAT / 4000 * 0.9, 0.06, (1, 0, 0.33, 0, 0.2), decay=12)
                add(b(bar, s / 4), lead_cache[f])
        if bar >= 21:
            key = root
            if key not in pad_cache:
                pad_cache[key] = [sum(math.sin(2 * math.pi * f * k / RATE) for f in (key * 2, key * 2.5, key * 3)) *
                                  0.025 * min(1, k / (0.4 * RATE)) * min(1, (int(BAR / 1000 * RATE) - k) / (0.3 * RATE))
                                  for k in range(int(BAR / 1000 * RATE))]
            add(b(bar), pad_cache[key])

    peak = max(1e-6, max(abs(v) for v in out))
    pcm = array.array("h", (int(max(-1, min(1, v / peak * 0.9)) * 32767) for v in out))
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


# ----------------------------------------------------------------------------------------------
# Level
# ----------------------------------------------------------------------------------------------

objects = []
events = []
rnd = random.Random(42)


def obj(shape, start, end, warn=0, outro=0, hitbox=True, color=None, layer=0, parent=None, pivot=None, name="", **tracks):
    o = {"id": f"o{len(objects)}", "shape": shape, "start": round(start, 3), "end": round(end, 3)}
    if name:
        o["name"] = name
    if warn:
        o["warn"] = round(warn, 3)
    if outro:
        o["outro"] = round(outro, 3)
    if not hitbox:
        o["hitbox"] = False
    if color:
        o["color"] = color
    if layer:
        o["layer"] = layer
    if parent:
        o["parent"] = parent
    if pivot:
        o["pivot"] = pivot
    o["tracks"] = {k: [[round(x, 3) if isinstance(x, float) else x for x in key] for key in v] for k, v in tracks.items()}
    objects.append(o)
    return o["id"]


RECT = {"type": "rect"}
CIRCLE = {"type": "circle"}


def ring(th=0.12):
    return {"type": "ring", "thickness": th}


def poly(n):
    return {"type": "polygon", "sides": n}


def star(n=5, inner=0.5):
    return {"type": "star", "points": n, "inner": inner}


def spikes(n=12, inner=0.75):
    return {"type": "spikes", "spikes": n, "inner": inner}


def pulse_ring(t, size=240, color=CYAN):
    obj(ring(0.04), t, t + BEAT, hitbox=False, color=color, outro=BEAT * 0.8,
        w=[[0, size * 0.6], [BEAT, size, "outCubic"]], h=[[0, size * 0.6], [BEAT, size, "outCubic"]],
        opacity=[[0, 0.5]])


def falling_square(t, x):
    size = 46
    obj(RECT, t - BEAT, t + BEAT * 3, warn=BEAT, outro=150,
        x=[[0, x]], y=[[0, -330], [BEAT, -330], [BEAT * 4, 420, "inQuad"]],
        rot=[[BEAT, 0], [BEAT * 4, 180]], w=[[0, size]], h=[[0, size]])


def burst(t, x, y, n=8, speed=520, size=26, color=None, spin=0):
    """A bomb that grows during the warning, then splits into n projectiles flying outward."""
    obj(CIRCLE, t - BEAT, t + 120, warn=BEAT, outro=120, color=color,
        x=[[0, x]], y=[[0, y]], w=[[0, 30], [BEAT, 70, "inBack"]], h=[[0, 30], [BEAT, 70, "inBack"]])
    dur = 1600
    for i in range(n):
        a = 2 * math.pi * i / n + spin
        dx, dy = math.cos(a) * speed * dur / 1000, math.sin(a) * speed * dur / 1000
        obj(CIRCLE, t, t + dur, outro=200, color=color,
            x=[[0, x], [dur, x + dx, "outSine"]], y=[[0, y], [dur, y + dy, "outSine"]],
            w=[[0, size]], h=[[0, size]])


def wall(t, side, gap_y, gap=170, dur=BEAT * 4):
    """A vertical wall sweeping across the screen with a gap to slip through."""
    x0, x1 = (-700, 700) if side == "left" else (700, -700)
    top_h = gap_y - gap / 2 + 360
    bot_h = 360 - (gap_y + gap / 2)
    for (cy, h) in ((-360 + top_h / 2, top_h), (gap_y + gap / 2 + bot_h / 2, bot_h)):
        if h <= 0:
            continue
        obj(RECT, t - BEAT * 2, t + dur, warn=BEAT * 2, outro=100,
            x=[[0, x0 * 0.92], [BEAT * 2, x0 * 0.92], [BEAT * 2 + dur, x1, "inOutSine"]],
            y=[[0, cy]], w=[[0, 60]], h=[[0, h]])


def spinner(t0, t1, x, y, arms=3, length=330, speed=180, color=GOLD):
    hub = obj(spikes(10, 0.7), t0 - BEAT * 2, t1, warn=BEAT * 2, outro=300, color=color, layer=2, name="hub",
              x=[[0, x]], y=[[0, y]], w=[[0, 70]], h=[[0, 70]],
              rot=[[0, 0], [t1 - t0 + BEAT * 2, speed * (t1 - t0 + BEAT * 2) / 1000]])
    for i in range(arms):
        obj(RECT, t0 - BEAT, t1, warn=BEAT, outro=300, color=color, layer=1, parent=hub, pivot=[-0.5, 0],
            name="arm", rot=[[0, 360 * i / arms]], x=[[0, 0]],
            w=[[0, 0], [BEAT, length, "outBack"]], h=[[0, 22]])
    return hub


def laser(t, y=None, x=None, thick=70, dur=BEAT * 2):
    """A horizontal (y given) or vertical (x given) beam: thin warning line, then a full beam."""
    horizontal = y is not None
    tracks = dict(
        x=[[0, 0 if horizontal else x]], y=[[0, y if horizontal else 0]],
        w=[[0, 1400 if horizontal else 4], [BEAT * 2, 1400 if horizontal else 4], [BEAT * 2 + 90, 1400 if horizontal else thick, "outExpo"]],
        h=[[0, 4 if horizontal else 800], [BEAT * 2, 4 if horizontal else 800], [BEAT * 2 + 90, thick if horizontal else 800, "outExpo"]],
    )
    obj(RECT, t - BEAT * 2, t + dur, warn=BEAT * 2, outro=180, **tracks)


def shockwave(t, x, y, max_size=900, dur=BEAT * 3):
    obj(ring(0.06), t - BEAT, t + dur, warn=BEAT, outro=250,
        x=[[0, x]], y=[[0, y]],
        w=[[0, 60], [BEAT, 60], [BEAT + dur, max_size, "outQuad"]], h=[[0, 60], [BEAT, 60], [BEAT + dur, max_size, "outQuad"]])


def drifter(t, x0, y0, x1, y1, size, dur, shape, hitbox=True, color=None, rot=90):
    obj(shape, t, t + dur, warn=BEAT if hitbox else 0, outro=400, hitbox=hitbox, color=color,
        x=[[0, x0], [dur, x1, "inOutSine"]], y=[[0, y0], [dur, y1, "inOutSine"]],
        rot=[[0, 0], [dur, rot]], w=[[0, size]], h=[[0, size]], opacity=[[0, 1 if hitbox else 0.35]])


def build_level():
    # Intro: center pulses on the beat, then squares raining from the top.
    for bar in range(1, 5):
        for beat in range(4):
            pulse_ring(b(bar, beat), 220 if beat else 320)
    for bar in range(3, 5):
        for beat in range(4):
            falling_square(b(bar, beat), rnd.uniform(-560, 560))
    # Bars 5-8: bursts on every bar, alternating sides, plus off-beat squares.
    for i, bar in enumerate(range(5, 9)):
        burst(b(bar), -380 if i % 2 == 0 else 380, rnd.uniform(-160, 160), n=10, spin=i * 0.3)
        burst(b(bar, 2), 0, -200 if i % 2 else 200, n=6, color=CYAN, size=22)
        for beat in (1, 3):
            falling_square(b(bar, beat + 0.5), rnd.uniform(-560, 560))
    # Bars 9-12: sweeping walls with gaps, lasers on the back beats.
    for i, bar in enumerate(range(9, 13)):
        wall(b(bar), "left" if i % 2 == 0 else "right", rnd.uniform(-200, 200))
        laser(b(bar, 2), y=rnd.choice([-250, 250]))
    events.append({"t": b(9), "type": "flash", "duration": 350, "color": "#ffffff", "opacity": 0.6})
    events.append({"t": b(9), "type": "shake", "duration": 400, "intensity": 12})
    # Bars 13-16: a spinner in the middle with bursts around it.
    spinner(b(13), b(17), 0, 0, arms=3, speed=150)
    for bar in range(13, 17):
        burst(b(bar, 1), rnd.choice([-500, 500]), rnd.uniform(-250, 250), n=8, color=CYAN, size=20)
        laser(b(bar, 3), x=rnd.choice([-450, 450]), thick=60)
    # Bars 17-20: star rain and bouncing spike balls.
    for bar in range(17, 21):
        for s in range(8):
            t = b(bar, s / 2)
            x = rnd.uniform(-600, 600)
            obj(star(5, 0.45), t - BEAT, t + 2200, warn=BEAT, outro=200,
                x=[[0, x], [BEAT, x], [BEAT + 2200, x + rnd.uniform(-120, 120)]],
                y=[[0, -330], [BEAT, -330], [BEAT + 2200, 420, "inQuad"]],
                rot=[[0, 0], [BEAT + 2200, rnd.choice([-1, 1]) * 360]], w=[[0, 50]], h=[[0, 50]])
        bx = rnd.choice([-1, 1])
        obj(spikes(12, 0.72), b(bar) - BEAT, b(bar) + BAR, warn=BEAT, outro=200, layer=1,
            x=[[0, bx * 700], [BEAT, bx * 700], [BEAT + BAR, -bx * 700]],
            y=[[0, 250], [BEAT, 250], [BEAT + BAR * 0.25, -120, "outQuad"], [BEAT + BAR * 0.5, 250, "inQuad"],
               [BEAT + BAR * 0.75, -120, "outQuad"], [BEAT + BAR, 250, "inQuad"]],
            rot=[[0, 0], [BEAT + BAR, -bx * 540]], w=[[0, 90]], h=[[0, 90]])
    events.append({"t": b(17), "type": "background", "duration": BAR, "color": "#14081c", "ease": "inOutSine"})
    events.append({"t": b(17), "type": "flash", "duration": 300, "color": CYAN, "opacity": 0.35})
    # Bars 21-24: shockwaves, triangles from the sides.
    for i, bar in enumerate(range(21, 25)):
        shockwave(b(bar), rnd.uniform(-400, 400), rnd.uniform(-200, 200))
        for k in range(3):
            y = -240 + k * 240
            side = 1 if (i + k) % 2 else -1
            obj(poly(3), b(bar, 2) - BEAT, b(bar, 2) + 1800, warn=BEAT, outro=150, color=GOLD,
                x=[[0, side * 600], [BEAT, side * 600], [BEAT + 1800, -side * 760, "inCubic"]],
                y=[[0, y]], rot=[[0, -90 * side], [BEAT + 1800, -90 * side + 720 * side]], w=[[0, 70]], h=[[0, 70]])
    # Bars 25-28: breakdown. Quiet background drift and a few slow hazards.
    events.append({"t": b(25), "type": "background", "duration": BAR * 2, "color": "#060912", "ease": "outSine"})
    for k in range(10):
        drifter(b(25) + k * BEAT * 1.5, rnd.uniform(-640, 640), 420, rnd.uniform(-640, 640), -420,
                rnd.uniform(80, 220), BAR * 2, poly(rnd.choice([3, 4, 5, 6])), hitbox=False, color=CYAN, rot=rnd.choice([-180, 180]))
    for k in range(4):
        drifter(b(25, k * 4), -720, rnd.uniform(-250, 250), 720, rnd.uniform(-250, 250), 120, BAR * 1.5, poly(6), rot=360)
    # Bars 29-32: the drop. Everything at once.
    events.append({"t": b(29), "type": "background", "duration": 200, "color": "#1c0410"})
    for bar in range(29, 33):
        events.append({"t": b(bar), "type": "shake", "duration": 350, "intensity": 14})
        events.append({"t": b(bar), "type": "flash", "duration": 250, "color": "#ffffff", "opacity": 0.45})
        burst(b(bar), rnd.uniform(-450, 450), rnd.uniform(-200, 200), n=12, spin=bar)
        wall(b(bar, 1), "left" if bar % 2 else "right", rnd.uniform(-180, 180), gap=190, dur=BEAT * 3)
        laser(b(bar, 2), y=rnd.choice([-280, 0, 280]), thick=60)
        shockwave(b(bar, 3), 0, 0, max_size=1000, dur=BEAT * 2)
    spinner(b(29), b(33), 0, 0, arms=4, length=360, speed=-220, color=PINK)
    events.append({"t": b(33), "type": "flash", "duration": 900, "color": "#ffffff", "opacity": 0.9})
    events.append({"t": b(33), "type": "background", "duration": 900, "color": "#0b0d12"})


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "demo"
    os.makedirs(out, exist_ok=True)
    build_level()
    level = {
        "format": "beatform.level",
        "formatVersion": 1,
        "meta": {
            "id": "beatform-demo-128",
            "title": "Neon Rehearsal",
            "artist": "Beatform (synthesized)",
            "author": "Beatform",
            "description": "The built-in demo: every shape, parenting, eases, checkpoints and scene events, on an original 128 BPM track.",
            "audio": "song.ogg",
            "bpm": BPM,
            "beatsPerBar": 4,
            "offsetMs": 0,
            "lengthMs": round(b(BARS + 1) + 1200, 3),
            "previewStartMs": round(b(29)),
            "difficulty": 3,
        },
        "theme": {"background": "#0b0d12", "hazard": PINK, "flash": "#ffffff"},
        "settings": {"hitboxScale": 0.9},
        "checkpoints": [0, round(b(9), 3), round(b(17), 3), round(b(25), 3)],
        "objects": objects,
        "events": sorted(events, key=lambda e: e["t"]),
        "editor": {"generator": "spec/examples/make_demo.py"},
    }
    with open(os.path.join(out, "level.json"), "w", encoding="utf-8") as f:
        json.dump(level, f, indent=1)
    synth(os.path.join(out, "song.wav"))
    print(f"wrote {len(objects)} objects, {len(events)} events to {out}")


if __name__ == "__main__":
    main()
