# Erzeugt das Schlangenmodell (Vintage-Story-Shape-JSON) samt Animationen.
# Vorne = -X. Kinder-Koordinaten sind relativ zum "from" des Elternteils.
import json, math

def faces(uv_side, uv_top, uv_bottom):
    return {
        "north": {"texture": "#skin", "uv": uv_side}, "south": {"texture": "#skin", "uv": uv_side},
        "east": {"texture": "#skin", "uv": uv_side}, "west": {"texture": "#skin", "uv": uv_side},
        "up": {"texture": "#skin", "uv": uv_top}, "down": {"texture": "#skin", "uv": uv_bottom},
    }

BACK, BELLY = [0, 0, 5, 4], [16, 0, 21, 4]

def segment(name, frm, to, origin, child=None):
    e = {"name": name, "from": frm, "to": to, "rotationOrigin": origin, "faces": faces(BACK, BACK, BELLY)}
    if child: e["children"] = [child]
    return e

# Schwanz von hinten nach vorn aufbauen
seg7 = segment("Seg7", [2.5, 0.3, 0.3], [4.5, 1.1, 1.1], [2.5, 0.7, 0.7])
seg6 = segment("Seg6", [2.5, 0.2, 0.3], [5.0, 1.6, 1.7], [2.5, 0.8, 1.0], seg7)
seg5 = segment("Seg5", [2.5, 0.0, 0.0], [5.0, 1.8, 2.0], [2.5, 0.9, 1.0], seg6)
seg4 = segment("Seg4", [2.5, 0.0, 0.0], [5.0, 1.8, 2.0], [2.5, 0.9, 1.0], seg5)
seg3 = segment("Seg3", [2.5, 0.0, 0.0], [5.0, 1.8, 2.0], [2.5, 0.9, 1.0], seg4)
seg2 = segment("Seg2", [2.5, 0.0, 0.0], [5.0, 1.8, 2.0], [2.5, 0.9, 1.0], seg3)
seg1 = segment("Seg1", [3.0, 0.1, 0.25], [5.5, 1.9, 2.25], [3.0, 1.0, 1.25], seg2)

eye = lambda name, z0, z1: {"name": name, "from": [0.6, 1.1, z0], "to": [1.4, 1.6, z1],
    "faces": {f: {"texture": "#skin", "uv": [0, 8, 2, 10]} for f in ("north", "south", "east", "west", "up", "down")}}
tongue = {"name": "Tongue", "from": [0.2, 0.6, 1.1], "to": [1.2, 0.8, 1.4], "rotationOrigin": [1.2, 0.7, 1.25],
    "faces": {f: {"texture": "#skin", "uv": [4, 8, 6, 10]} for f in ("north", "south", "east", "west", "up", "down")}}

head = {
    "name": "Head", "from": [2.0, 0.0, 6.75], "to": [5.0, 1.8, 9.25], "rotationOrigin": [3.5, 0.9, 8.0],
    "faces": faces([8, 8, 11, 10], [8, 8, 11, 11], [16, 0, 19, 3]),
    "children": [eye("EyeA", -0.1, 0.0), eye("EyeB", 2.5, 2.6), tongue, seg1],
}

segs = ["Seg1", "Seg2", "Seg3", "Seg4", "Seg5", "Seg6", "Seg7"]

def slither(frames=20, amp=28.0, phase=0.9, keys=(0, 5, 10, 15)):
    kfs = []
    for f in keys:
        els = {}
        for i, s in enumerate(segs):
            a = amp * math.sin(2 * math.pi * f / frames - i * phase)
            els[s] = {"rotationY": round(a * (0.6 if i == 0 else 1.0), 1)}
        els["Head"] = {"rotationY": round(-0.5 * amp * math.sin(2 * math.pi * f / frames), 1), "offsetX": 0.0, "offsetY": 0.0}
        kfs.append({"frame": f, "elements": els})
    return kfs

animations = [
    {"name": "Idle", "code": "idle", "quantityframes": 60, "onActivityStopped": "EaseOut", "onAnimationEnd": "Repeat",
     "keyframes": [
        {"frame": 0, "elements": {"Head": {"rotationY": -6.0, "rotationZ": 0.0}, "Seg1": {"rotationY": 8.0}, "Seg3": {"rotationY": -10.0}, "Seg5": {"rotationY": 12.0}}},
        {"frame": 30, "elements": {"Head": {"rotationY": 6.0, "rotationZ": 4.0}, "Seg1": {"rotationY": -4.0}, "Seg3": {"rotationY": 6.0}, "Seg5": {"rotationY": -8.0}}},
     ]},
    {"name": "Slither", "code": "walk", "quantityframes": 20, "onActivityStopped": "EaseOut", "onAnimationEnd": "Repeat",
     "keyframes": slither()},
    {"name": "Bite", "code": "bite", "quantityframes": 18, "onActivityStopped": "EaseOut", "onAnimationEnd": "Stop",
     "keyframes": [
        {"frame": 0, "elements": {"Head": {"offsetX": 0.0, "offsetY": 0.0, "rotationZ": 0.0}, "Tongue": {"offsetX": 0.0}, "Seg1": {"rotationZ": 0.0}}},
        {"frame": 3, "elements": {"Head": {"offsetX": 0.8, "offsetY": 1.2, "rotationZ": -25.0}, "Tongue": {"offsetX": 0.0}, "Seg1": {"rotationZ": 20.0}}},
        {"frame": 7, "elements": {"Head": {"offsetX": -2.0, "offsetY": 0.6, "rotationZ": 10.0}, "Tongue": {"offsetX": -1.4}, "Seg1": {"rotationZ": -10.0}}},
        {"frame": 12, "elements": {"Head": {"offsetX": -1.6, "offsetY": 0.3, "rotationZ": 5.0}, "Tongue": {"offsetX": -1.4}, "Seg1": {"rotationZ": -5.0}}},
        {"frame": 17, "elements": {"Head": {"offsetX": 0.0, "offsetY": 0.0, "rotationZ": 0.0}, "Tongue": {"offsetX": 0.0}, "Seg1": {"rotationZ": 0.0}}},
     ]},
    {"name": "Die", "code": "die", "quantityframes": 12, "onActivityStopped": "Stop", "onAnimationEnd": "Hold",
     "keyframes": [
        {"frame": 0, "elements": {"Head": {"rotationX": 0.0, "offsetY": 0.0}}},
        {"frame": 11, "elements": {"Head": {"rotationX": 180.0, "offsetY": 1.8}}},
     ]},
]

shape = {
    "editor": {"allAngles": True},
    "textureWidth": 32, "textureHeight": 16,
    "textures": {"skin": "badluck:entity/snake"},
    "elements": [head],
    "animations": animations,
}
open("../resources/assets/badluck/shapes/entity/snake.json", "w", encoding="utf-8").write(json.dumps(shape, indent="\t") + "\n")
print("snake.json written")
