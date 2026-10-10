"""Ask the running game (with the Beatform loader) to play a level folder: bridge-play.py <folder> [startMs] [hard]"""
import json, os, sys, time
GAME = os.environ.get("JSAB_DIR", r"G:\Steam\steamapps\common\Just Shapes & Beats")
bridge = os.path.join(GAME, "BepInEx", "beatform", "bridge")
os.makedirs(bridge, exist_ok=True)
req = {"folder": os.path.abspath(sys.argv[1]), "startMs": float(sys.argv[2]) if len(sys.argv) > 2 else 0, "hard": len(sys.argv) > 3 and sys.argv[3] == "hard"}
with open(os.path.join(bridge, "play.json"), "w", encoding="utf-8") as f:
    json.dump(req, f)
status = os.path.join(bridge, "status.json")
for _ in range(120):
    time.sleep(1)
    try:
        s = json.load(open(status, encoding="utf-8"))
    except Exception:
        continue
    if s["state"] in ("launching", "error"):
        print(s)
        break
