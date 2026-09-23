# Erzeugt das Mimic-Modell (Kiste mit Maul) als Vintage-Story-Shape-JSON.
# Vorne = -X (dort sitzen Augen und Zaehne), Deckelscharnier hinten bei x = 14.
import json

WOOD = [0, 0, 12, 12]
IRON = [16, 0, 28, 12]
TOOTH = [0, 16, 4, 20]
EYE = [8, 16, 12, 20]
MOUTH = [16, 16, 28, 28]


def faces(side, top=None, bottom=None, front=None):
    top = top or side
    bottom = bottom or side
    front = front or side
    return {
        "north": {"texture": "#skin", "uv": side},
        "south": {"texture": "#skin", "uv": side},
        "east": {"texture": "#skin", "uv": side},
        "west": {"texture": "#skin", "uv": front},
        "up": {"texture": "#skin", "uv": top},
        "down": {"texture": "#skin", "uv": bottom},
    }


def box(name, frm, to, uv, origin=None, children=None):
    e = {"name": name, "from": frm, "to": to, "faces": faces(uv)}
    if origin: e["rotationOrigin"] = origin
    if children: e["children"] = children
    return e


def tooth(name, x, z, up):
    # Zaehne haengen vom Deckel nach unten bzw. stehen vom Unterteil nach oben
    y0, y1 = (0.0, 1.6) if up else (-1.6, 0.0)
    return {"name": name, "from": [x, y0, z], "to": [x + 1.4, y1, z + 1.4],
            "faces": {f: {"texture": "#skin", "uv": TOOTH} for f in ("north", "south", "east", "west", "up", "down")}}


# Deckel (Kind des Unterteils; Koordinaten relativ zu dessen "from" [2,0,2])
lid_children = [
    # Unterseite des Deckels = Maulinneres
    {"name": "Gums", "from": [0.2, -0.6, 0.2], "to": [11.8, 0.0, 11.8],
     "faces": {f: {"texture": "#skin", "uv": MOUTH} for f in ("north", "south", "east", "west", "up", "down")}},
    {"name": "EyeL", "from": [-0.1, 2.0, 2.0], "to": [0.0, 3.6, 3.6],
     "faces": {f: {"texture": "#skin", "uv": EYE} for f in ("north", "south", "east", "west", "up", "down")}},
    {"name": "EyeR", "from": [-0.1, 2.0, 8.4], "to": [0.0, 3.6, 10.0],
     "faces": {f: {"texture": "#skin", "uv": EYE} for f in ("north", "south", "east", "west", "up", "down")}},
]
for i in range(4):
    lid_children.append(tooth("ToothTop%d" % (i + 1), 0.1, 1.2 + i * 2.6, up=False))

lid = {"name": "Lid", "from": [0.0, 8.0, 0.0], "to": [12.0, 13.0, 12.0],
       "rotationOrigin": [12.0, 8.0, 6.0], "faces": faces(WOOD, IRON, MOUTH, IRON), "children": lid_children}

body_children = [lid]
for i in range(4):
    body_children.append(tooth("ToothBottom%d" % (i + 1), 0.1, 2.5 + i * 2.6, up=True))
# Zaehne des Unterteils sitzen auf der Oberkante
for e in body_children[1:]:
    e["from"][1] += 8.0
    e["to"][1] += 8.0

body = {"name": "Body", "from": [2.0, 0.0, 2.0], "to": [14.0, 8.0, 14.0],
        "rotationOrigin": [8.0, 0.0, 8.0], "faces": faces(WOOD, IRON, WOOD, IRON), "children": body_children}

animations = [
    {"name": "Idle", "code": "idle", "quantityframes": 60, "onActivityStopped": "EaseOut", "onAnimationEnd": "Repeat",
     "keyframes": [
         {"frame": 0, "elements": {"Lid": {"rotationZ": 0.0}, "Body": {"offsetY": 0.0}}},
         {"frame": 30, "elements": {"Lid": {"rotationZ": -5.0}, "Body": {"offsetY": 0.2}}},
     ]},
    # Huepfen: Koerper springt, Deckel klappert
    {"name": "Hop", "code": "walk", "quantityframes": 20, "onActivityStopped": "EaseOut", "onAnimationEnd": "Repeat",
     "keyframes": [
         {"frame": 0, "elements": {"Body": {"offsetY": 0.0, "rotationZ": 0.0}, "Lid": {"rotationZ": -4.0}}},
         {"frame": 5, "elements": {"Body": {"offsetY": 3.5, "rotationZ": -8.0}, "Lid": {"rotationZ": -28.0}}},
         {"frame": 10, "elements": {"Body": {"offsetY": 0.0, "rotationZ": 0.0}, "Lid": {"rotationZ": -2.0}}},
         {"frame": 15, "elements": {"Body": {"offsetY": -0.8, "rotationZ": 4.0}, "Lid": {"rotationZ": -10.0}}},
     ]},
    # Zuschnappen
    {"name": "Bite", "code": "attack", "quantityframes": 16, "onActivityStopped": "EaseOut", "onAnimationEnd": "Stop",
     "keyframes": [
         {"frame": 0, "elements": {"Lid": {"rotationZ": 0.0}, "Body": {"offsetX": 0.0}}},
         {"frame": 5, "elements": {"Lid": {"rotationZ": -62.0}, "Body": {"offsetX": 0.6}}},
         {"frame": 9, "elements": {"Lid": {"rotationZ": -4.0}, "Body": {"offsetX": -1.6}}},
         {"frame": 15, "elements": {"Lid": {"rotationZ": 0.0}, "Body": {"offsetX": 0.0}}},
     ]},
    {"name": "Die", "code": "die", "quantityframes": 14, "onActivityStopped": "Stop", "onAnimationEnd": "Hold",
     "keyframes": [
         {"frame": 0, "elements": {"Body": {"rotationX": 0.0, "offsetY": 0.0}, "Lid": {"rotationZ": 0.0}}},
         {"frame": 13, "elements": {"Body": {"rotationX": 80.0, "offsetY": 0.0}, "Lid": {"rotationZ": -18.0}}},
     ]},
]

shape = {
    "editor": {"allAngles": True},
    "textureWidth": 32, "textureHeight": 32,
    "textures": {"skin": "schadenfreude:entity/mimic"},
    "elements": [body],
    "animations": animations,
}
open("../resources/assets/schadenfreude/shapes/entity/mimic.json", "w", encoding="utf-8").write(json.dumps(shape, indent="\t") + "\n")
print("mimic.json written")
