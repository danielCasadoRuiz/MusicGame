"""
Original synthesized runner pickup sounds for MusicGame (PRODUCTION TOOL). Generated from scratch (no samples) —
our own work, CC0 with the rest of the generated sfx.

    python synth_pickups.py <output folder>      (the game uses Assets/_Project/Audio/Sfx/Synth)

  bonus_pickup_{0,1,2}.wav   a soft breath of filtered white noise ("fff"), its band rising a little — no tone, no
                             click: confirms the pickup without competing with the song (user: closer to soft noise)
  piano_slam_{0,1,2}.wav     both hands slammed on the low keys of a piano (a dissonant low cluster: inharmonic string
                             partials, three detuned strings per note, felt-hammer thump) — the chaser catching you
"""
import os, sys
import numpy as np
from scipy.io import wavfile

SR = 44100


def env(t, attack, decay):
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    return a * np.exp(-6.9 * t / decay)


def pling(f0, seed):
    rng = np.random.default_rng(seed)
    dur = 0.32
    t = np.arange(int(dur * SR)) / SR
    y = np.zeros_like(t)
    # Bell-like partials: fundamental, octave + a soft fifth above it; the upper ones die faster.
    for r, a, d in ((1.0, 0.8, 0.26), (2.0, 0.35, 0.14), (3.0, 0.12, 0.08), (4.2, 0.05, 0.05)):
        ph = rng.uniform(0, 2 * np.pi)
        y += a * np.sin(2 * np.pi * f0 * r * t + ph) * env(t, 0.003, d)
    # A tiny upward glide on the onset (the "got it" lift), 20 ms.
    glide = np.sin(2 * np.pi * np.cumsum(np.interp(t, [0, 0.02, dur], [f0 * 0.94, f0, f0])) / SR)
    y += 0.25 * glide * env(t, 0.002, 0.05)
    air = rng.standard_normal(t.size) * env(t, 0.001, 0.015)
    air = np.convolve(air, np.ones(4) / 4, mode="same")
    y += 0.05 * air
    return y


def ting(f0, seed):
    rng = np.random.default_rng(seed)
    dur = 0.26
    t = np.arange(int(dur * SR)) / SR
    y = np.zeros_like(t)
    # Thin circular plate modes (inharmonic) → a coin-like metallic ring.
    for r, a, d in ((1.0, 0.7, 0.20), (1.59, 0.55, 0.16), (2.14, 0.4, 0.12), (2.65, 0.3, 0.09), (3.16, 0.2, 0.07), (4.06, 0.12, 0.05)):
        f = f0 * r
        if f > SR / 2.3: continue
        for det in (-0.002, 0.002):
            y += a * 0.5 * np.sin(2 * np.pi * f * (1 + det) * t + rng.uniform(0, 2 * np.pi)) * env(t, 0.0008, d)
    click = rng.standard_normal(int(0.003 * SR))
    click = np.diff(click, prepend=0.0) * np.exp(-np.arange(click.size) / (0.0006 * SR))
    y[:click.size] += 0.5 * click
    return y


def normalize(y, peak_db):
    y = y / max(1e-9, np.max(np.abs(y)))
    fade = int(0.01 * SR)
    y[-fade:] *= np.linspace(1, 0, fade)
    return y * 10 ** (peak_db / 20)


def write(folder, name, y):
    wavfile.write(os.path.join(folder, name), SR, (np.clip(y, -1, 1) * 32767).astype(np.int16))


def breath(seed, dur=0.2, f_lo=1800.0, f_hi=5200.0):
    from scipy.signal import butter, sosfilt
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    t = np.arange(n) / SR
    noise = rng.standard_normal(n)
    # Band sweeping upward, processed in short blocks (a gentle "shff" lift).
    y = np.zeros(n)
    blk = 256
    for i in range(0, n, blk):
        k = i / n
        lo = f_lo * (1 + 0.6 * k); hi = f_hi * (1 + 0.5 * k)
        sos = butter(2, [lo, min(hi, SR / 2.2)], btype="band", fs=SR, output="sos")
        seg = noise[max(0, i - 2048):i + blk]
        y[i:i + blk] = sosfilt(sos, seg)[-min(blk, n - i):]
    e = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 1.5 * np.exp(-2.5 * t / dur)   # soft swell in and out
    return y * e


def piano_note(f0, dur, seed, vel=1.0):
    rng = np.random.default_rng(seed)
    t = np.arange(int(dur * SR)) / SR
    B = 0.0004                                    # string stiffness → stretched partials
    y = np.zeros_like(t)
    for n_ in range(1, 26):
        f = f0 * n_ * np.sqrt(1 + B * n_ * n_)
        if f > SR / 2.3: break
        amp = vel / n_ ** 0.9 * (1.0 if n_ < 8 else 0.6)
        d1 = 2.8 / (1 + 0.18 * n_)               # two-stage decay: fast prompt sound, slower aftersound
        for det in (-0.0012, 0.0, 0.0013):
            ph = rng.uniform(0, 2 * np.pi)
            y += amp / 3 * np.sin(2 * np.pi * f * (1 + det) * t + ph) * (0.7 * np.exp(-6.9 * t / (d1 * 0.25)) + 0.3 * np.exp(-6.9 * t / d1))
    return y


def piano_slam(seed, root):
    rng = np.random.default_rng(seed)
    dur = 2.2
    midi = [root + o for o in (0, 1, 3, 6, 7, 12, 13, 18)]           # a dissonant two-hand smash on the low keys
    y = sum(piano_note(440 * 2 ** ((m - 69) / 12), dur, seed * 31 + i, vel=rng.uniform(0.8, 1.0)) *
            (1.0 if i < 5 else 0.7) for i, m in enumerate(midi))
    t = np.arange(y.size) / SR
    thump = rng.standard_normal(y.size) * np.exp(-t / 0.012)          # felt hammers + key bed knock
    from scipy.signal import butter, sosfilt
    thump = sosfilt(butter(2, 900, fs=SR, output="sos"), thump)
    y += 3.0 * thump
    y = np.tanh(1.6 * y / np.max(np.abs(y))) # hit hard: a touch of saturation
    return y


def main(folder):
    os.makedirs(folder, exist_ok=True)
    for i, (lo, hi) in enumerate(((1700.0, 5000.0), (1900.0, 5600.0), (1600.0, 4700.0))):
        write(folder, f"bonus_pickup_{i}.wav", normalize(breath(10 + i, 0.2, lo, hi), -12.0))
    for i, root in enumerate((31, 29, 33)):                              # around G1 / F1 / A1
        write(folder, f"piano_slam_{i}.wav", normalize(piano_slam(40 + i, root), -3.0))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "out")
