# Vintage Story verlangt in einem Schluesselbild pro Kanal immer alle drei Achsen:
# wer offsetY setzt, muss auch offsetX und offsetZ angeben (sonst Absturz beim Ueberblenden).
# Dieses Werkzeug ergaenzt fehlende Achsen mit 0 - in allen Modellen des Mods und im Bären-Patch.
import glob, json, os

CHANNELS = [("offsetX", "offsetY", "offsetZ"), ("rotationX", "rotationY", "rotationZ"), ("stretchX", "stretchY", "stretchZ")]


def fix_animation(anim):
    changed = 0
    for keyframe in anim.get("keyframes", []):
        for element in keyframe.get("elements", {}).values():
            for group in CHANNELS:
                if any(k in element for k in group):
                    for k in group:
                        if k not in element:
                            element[k] = 0.0
                            changed += 1
    return changed


def fix_file(path, animations_getter, dump):
    data = json.load(open(path, encoding="utf-8"))
    changed = sum(fix_animation(a) for a in animations_getter(data))
    if changed:
        open(path, "w", encoding="utf-8").write(dump(data))
    print(os.path.basename(path), "->", changed, "Werte ergaenzt")


base = os.path.join(os.path.dirname(__file__), "..", "resources", "assets", "badluck")
for path in glob.glob(os.path.join(base, "shapes", "entity", "*.json")):
    fix_file(path, lambda d: d.get("animations", []), lambda d: json.dumps(d, indent="\t") + "\n")

# Der Baeren-Patch enthaelt die Animation als Patch-Wert
patch = os.path.join(base, "patches", "bear-opendoor-animation.json")
if os.path.exists(patch):
    fix_file(patch, lambda d: [op["value"] for op in d], lambda d: json.dumps(d, indent="\t") + "\n")
