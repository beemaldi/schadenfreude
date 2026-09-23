# Erzeugt Furzgeraeusche rein rechnerisch (Pulsfolge + Luftrauschen) als WAV.
import math, random, struct, wave, sys

SR = 44100

def one_pole_lp(x, cutoff):
    a = math.exp(-2 * math.pi * cutoff / SR)
    y, out = 0.0, []
    for v in x:
        y = (1 - a) * v + a * y
        out.append(y)
    return out

def one_pole_hp(x, cutoff):
    lp = one_pole_lp(x, cutoff)
    return [a - b for a, b in zip(x, lp)]

def synth(seed, dur, f0_start, f0_end, wobble_hz, wobble_depth, jitter, noise_mix, bright,
          sputters=0, gap=0.06, attack=0.012, release=0.12, squeak=0.0):
    rnd = random.Random(seed)
    n = int(dur * SR)
    # Lautstaerke-Huellkurve mit Stotter-Luecken
    env = []
    gaps = []
    for i in range(sputters):
        c = rnd.uniform(0.2, 0.85) * dur
        gaps.append((c, c + gap * rnd.uniform(0.7, 1.4)))
    for i in range(n):
        t = i / SR
        e = min(1.0, t / attack) * min(1.0, (dur - t) / release)
        e *= 0.75 + 0.25 * math.sin(2 * math.pi * rnd.uniform(0, 1) * 0 + t * 2 * math.pi * 1.7) ** 2
        for g0, g1 in gaps:
            if g0 <= t <= g1:
                d = min(t - g0, g1 - t)
                e *= max(0.0, 1 - math.exp(-d * 180)) * 0.05
        env.append(max(0.0, e))
    # Pulsfolge mit wackelnder Grundfrequenz (Schwingung der "Klappe")
    out = []
    phase = 0.0
    period_jit = 1.0
    amp_jit = 1.0
    for i in range(n):
        t = i / SR
        k = t / dur
        f0 = f0_start + (f0_end - f0_start) * k
        f0 *= 1 + wobble_depth * math.sin(2 * math.pi * wobble_hz * t + seed)
        f0 *= 1 + squeak * math.sin(2 * math.pi * 23 * t) * k
        f0 *= period_jit
        phase += f0 / SR
        if phase >= 1.0:
            phase -= 1.0
            period_jit = 1 + rnd.uniform(-jitter, jitter)
            amp_jit = 1 + rnd.uniform(-0.35, 0.35)
        # asymmetrischer Puls: kurzes Oeffnen, langsames Ausklingen
        p = phase
        v = (math.exp(-p * 9.0) - 0.11) * amp_jit
        out.append(v)
    out = one_pole_lp(out, bright)
    # Luft-/Feuchtrauschen, im Takt der Pulse moduliert
    noise = [rnd.uniform(-1, 1) for _ in range(n)]
    noise = one_pole_hp(one_pole_lp(noise, 2600), 250)
    mix = []
    for i in range(n):
        s = out[i] * (1 - noise_mix) + noise[i] * noise_mix * (0.6 + 0.8 * abs(out[i]))
        mix.append(math.tanh(2.2 * s * env[i]))
    mix = one_pole_hp(mix, 35)
    peak = max(abs(v) for v in mix) or 1
    return [v / peak * 0.9 for v in mix]

def write(path, data):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, v)) * 32767)) for v in data))

variants = [
    # kurz und hoch (frisch)
    dict(seed=11, dur=0.35, f0_start=150, f0_end=120, wobble_hz=9, wobble_depth=0.08, jitter=0.10, noise_mix=0.45, bright=1800, attack=0.006, release=0.08),
    dict(seed=23, dur=0.55, f0_start=210, f0_end=290, wobble_hz=6, wobble_depth=0.05, jitter=0.06, noise_mix=0.25, bright=2400, attack=0.01, release=0.1, squeak=0.12),
    # mittel
    dict(seed=37, dur=0.95, f0_start=95, f0_end=75, wobble_hz=4.5, wobble_depth=0.14, jitter=0.12, noise_mix=0.3, bright=1400, sputters=1),
    dict(seed=41, dur=1.15, f0_start=120, f0_end=85, wobble_hz=7, wobble_depth=0.1, jitter=0.14, noise_mix=0.35, bright=1500, sputters=3, gap=0.05),
    # lang und tief (uralt)
    dict(seed=53, dur=1.8, f0_start=70, f0_end=48, wobble_hz=3.2, wobble_depth=0.18, jitter=0.16, noise_mix=0.3, bright=1100, sputters=2, release=0.25),
    dict(seed=67, dur=2.4, f0_start=62, f0_end=90, wobble_hz=2.6, wobble_depth=0.22, jitter=0.18, noise_mix=0.38, bright=1200, sputters=4, gap=0.07, release=0.3),
]
for i, v in enumerate(variants, 1):
    write(f'fart{i}.wav', synth(**v))
    print('fart%d.wav' % i, v['dur'], 's')
