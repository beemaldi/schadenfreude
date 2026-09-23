# Erzeugt die Schlangen-Textur (32x16) als PNG, nur mit der Standardbibliothek.
import struct, zlib, random

W, H = 32, 16
rnd = random.Random(7)
px = [[(0, 0, 0, 255)] * W for _ in range(H)]

def put(x, y, c):
    px[y][x] = c

def jitter(c, amount=10):
    return tuple(max(0, min(255, v + rnd.randint(-amount, amount))) for v in c[:3]) + (255,)

GREEN, DARK, BELLY, HEAD = (70, 110, 45), (40, 55, 25), (190, 185, 120), (60, 95, 40)

# Ruecken (0..16, 0..8): gruen mit dunklen Rauten
for y in range(8):
    for x in range(16):
        d = abs((x % 8) - 4) + abs(y - 4)
        put(x, y, jitter(DARK if d <= 2 else GREEN))
# Bauch (16..32, 0..8): hell mit Querschuppen
for y in range(8):
    for x in range(16, 32):
        put(x, y, jitter((160, 155, 100) if y % 2 == 0 else BELLY, 6))
# Augen (0..4, 8..12): schwarz mit Glanzpunkt
for y in range(8, 12):
    for x in range(0, 4):
        put(x, y, (15, 15, 10, 255))
put(1, 9, (230, 220, 120, 255))
# Zunge (4..8, 8..12): rot
for y in range(8, 12):
    for x in range(4, 8):
        put(x, y, jitter((170, 30, 35), 8))
# Kopf (8..16, 8..16): etwas dunkler, feine Schuppen
for y in range(8, 16):
    for x in range(8, 16):
        put(x, y, jitter(DARK if (x + y) % 5 == 0 else HEAD))
# Rest (0..8, 12..16 und 16..32, 8..16): Ruecken-Wiederholung
for y in range(8, 16):
    for x in list(range(0, 8)) + list(range(16, 32)):
        if y < 12 and x < 8:
            continue
        put(x, y, jitter(GREEN))

raw = b''.join(b'\x00' + b''.join(struct.pack('BBBB', *p) for p in row) for row in px)
def chunk(tag, data):
    return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', W, H, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
open('../resources/assets/schadenfreude/textures/entity/snake.png', 'wb').write(png)
print('snake.png', len(png), 'bytes')
