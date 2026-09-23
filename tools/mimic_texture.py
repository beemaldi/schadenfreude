# Erzeugt die Mimic-Textur (32x32) als PNG, nur mit der Standardbibliothek.
import struct, zlib, random

W = H = 32
rnd = random.Random(11)
px = [[(0, 0, 0, 255)] * W for _ in range(H)]


def put(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[y][x] = c


def jitter(c, amount=8):
    return tuple(max(0, min(255, v + rnd.randint(-amount, amount))) for v in c[:3]) + (255,)


WOOD_A, WOOD_B, IRON, TOOTH, EYE, MOUTH = (124, 84, 48), (100, 66, 36), (70, 70, 78), (238, 236, 220), (226, 196, 60), (92, 30, 34)

# Holz mit Brettfugen (0..16, 0..16)
for y in range(16):
    for x in range(16):
        base = WOOD_B if y % 5 == 0 else WOOD_A
        put(x, y, jitter(base))
# Eisenbeschlag (16..32, 0..16): dunkle Baender mit Nieten
for y in range(16):
    for x in range(16, 32):
        c = IRON if (x - 16) % 8 < 3 else (WOOD_A if y % 5 else WOOD_B)
        put(x, y, jitter(c, 6))
for y in (3, 11):
    for x in (17, 25):
        put(x, y, (150, 150, 160, 255))
# Zaehne (0..8, 16..24): weiss mit dunklem Ansatz
for y in range(16, 24):
    for x in range(0, 8):
        put(x, y, jitter(TOOTH, 6) if y > 17 else jitter((190, 186, 170), 6))
# Augen (8..16, 16..24): gelb mit schwarzer Pupille
for y in range(16, 24):
    for x in range(8, 16):
        put(x, y, jitter(EYE, 10))
for y in range(19, 22):
    for x in range(11, 14):
        put(x, y, (20, 16, 12, 255))
# Maulinneres (16..32, 16..32): dunkelrot
for y in range(16, 32):
    for x in range(16, 32):
        put(x, y, jitter(MOUTH, 10))
# Restflaeche mit Holz fuellen
for y in range(24, 32):
    for x in range(0, 16):
        put(x, y, jitter(WOOD_A))

raw = b"".join(b"\x00" + b"".join(struct.pack("BBBB", *p) for p in row) for row in px)


def chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)


png = (b"\x89PNG\r\n\x1a\n"
       + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0))
       + chunk(b"IDAT", zlib.compress(raw, 9))
       + chunk(b"IEND", b""))
open("../resources/assets/badluck/textures/entity/mimic.png", "wb").write(png)
print("mimic.png", len(png), "bytes")
