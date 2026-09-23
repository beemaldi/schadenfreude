# Erzeugt ein Zischen (gefiltertes Rauschen mit Huellkurve) als WAV.
import math, random, struct, wave
SR = 44100
def lp(x, fc):
    a = math.exp(-2 * math.pi * fc / SR); y = 0.0; out = []
    for v in x:
        y = (1 - a) * v + a * y; out.append(y)
    return out
rnd = random.Random(3)
dur = 0.75
n = int(dur * SR)
noise = [rnd.uniform(-1, 1) for _ in range(n)]
band = [a - b for a, b in zip(lp(noise, 7500), lp(noise, 2800))]
out = []
for i, v in enumerate(band):
    t = i / SR
    env = min(1, t / 0.04) * max(0.0, 1 - max(0, t - 0.15) / (dur - 0.15)) ** 1.5
    env *= 1 + 0.25 * math.sin(2 * math.pi * 9 * t)
    out.append(v * env)
peak = max(abs(v) for v in out)
with wave.open('hiss.wav', 'wb') as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes(b''.join(struct.pack('<h', int(v / peak * 0.85 * 32767)) for v in out))
print('hiss.wav')
