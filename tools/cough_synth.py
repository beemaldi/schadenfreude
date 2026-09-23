# Erzeugt Hustengeraeusche (Kehlkopf-Impuls + Rauschstoss) als WAV, nur mit der Standardbibliothek.
import math, random, struct, wave

SR = 44100


def lp(x, fc):
    a = math.exp(-2 * math.pi * fc / SR)
    y, out = 0.0, []
    for v in x:
        y = (1 - a) * v + a * y
        out.append(y)
    return out


def hp(x, fc):
    low = lp(x, fc)
    return [a - b for a, b in zip(x, low)]


def cough(seed, f0=130.0, dur=0.42, breathiness=0.55, bright=2200.0):
    rnd = random.Random(seed)
    n = int(dur * SR)

    # Stimmhafter Anteil: kurze Pulsfolge, die schnell tiefer und leiser wird
    voiced = []
    phase = 0.0
    for i in range(n):
        t = i / SR
        f = f0 * (1 - 0.35 * min(1.0, t / 0.25))
        phase += f / SR
        if phase >= 1.0:
            phase -= 1.0
        voiced.append(math.exp(-phase * 7.0) - 0.13)
    voiced = lp(voiced, bright)

    # Rauschanteil: harter Stoss am Anfang, danach Ausatmen
    noise = [rnd.uniform(-1, 1) for _ in range(n)]
    noise = hp(lp(noise, 6000), 500)

    out = []
    for i in range(n):
        t = i / SR
        burst = math.exp(-t / 0.035)                      # Anfangsstoss
        body = max(0.0, 1 - t / dur) ** 1.6               # Ausklingen
        gate = min(1.0, t / 0.008)
        v = voiced[i] * body * (1 - breathiness * 0.5)
        nz = noise[i] * (burst * 0.9 + body * breathiness * 0.35)
        out.append(math.tanh(2.4 * (v + nz) * gate))

    out = hp(out, 90)
    peak = max(abs(v) for v in out) or 1
    return [v / peak * 0.9 for v in out]


def write(path, data):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, v)) * 32767)) for v in data))


variants = [
    dict(seed=5, f0=145, dur=0.38, breathiness=0.5, bright=2400),
    dict(seed=17, f0=115, dur=0.48, breathiness=0.6, bright=1900),
    dict(seed=29, f0=100, dur=0.55, breathiness=0.7, bright=1600),
]
for i, v in enumerate(variants, 1):
    write("cough%d.wav" % i, cough(**v))
    print("cough%d.wav" % i, v["dur"], "s")
