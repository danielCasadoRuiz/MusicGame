"""
Original synthesized sound effects for MusicGame (PRODUCTION TOOL). Everything here is generated from scratch
(no samples), so the output is our own work, released as CC0 with the rest of the generated sfx.

    python synth_sfx.py <output folder>

  countdown_tick_{0,1,2}.wav  short orchestral-triangle strikes (free-bar inharmonic partials + beater click),
                              choked after ~0.35 s so a tick never rings into the next number
  countdown_go_{0,1}.wav      a triangle ROLL (accelerating strikes) opening into a long ring with a soft low
                              bell under it — clearly different from the ticks
"""
import os, sys
import numpy as np
from scipy.io import wavfile

SR = 44100
RATIOS = [1.0, 2.76, 5.40, 8.93, 13.34, 19.9]          # free-free bar modes (a bent steel rod ≈ triangle)
AMPS   = [0.55, 0.85, 0.75, 0.55, 0.35, 0.2]
DECAYS = [1.6, 1.3, 1.0, 0.75, 0.5, 0.3]              # seconds to -60 dB (higher modes die faster)


def strike(f0, dur, choke=None, seed=0, gain=1.0):
    rng = np.random.default_rng(seed)
    t = np.arange(int(dur * SR)) / SR
    y = np.zeros_like(t)
    for r, a, d in zip(RATIOS, AMPS, DECAYS):
        f = f0 * r
        if f > SR / 2.2: continue
        for detune in (-0.0015, 0.0015):               # two close modes per partial → the triangle's shimmer
            ph = rng.uniform(0, 2 * np.pi)
            y += a * 0.5 * np.sin(2 * np.pi * f * (1 + detune) * t + ph) * np.exp(-6.9 * t / d)
    click = rng.standard_normal(int(0.004 * SR))       # metal beater contact
    click = np.diff(click, prepend=0.0) * np.exp(-np.arange(click.size) / (0.0008 * SR))
    y[:click.size] += 0.35 * click
    if choke:                                          # hand damping: a short, musical tick
        k = int(choke * SR)
        fade = np.exp(-np.arange(y.size - k) / (0.05 * SR))
        y[k:] *= fade
    return gain * y


def normalize(y, peak_db=-3.0):
    y = y / max(1e-9, np.max(np.abs(y)))
    return (y * 10 ** (peak_db / 20)).astype(np.float32)


def write(folder, name, y):
    wavfile.write(os.path.join(folder, name), SR, (np.clip(y, -1, 1) * 32767).astype(np.int16))


def main(folder):
    os.makedirs(folder, exist_ok=True)
    for i, f0 in enumerate((1180.0, 1240.0, 1310.0)):  # three variants, a touch apart
        write(folder, f"countdown_tick_{i}.wav", normalize(strike(f0, 0.6, choke=0.33, seed=i), -6.0))
    for i, f0 in enumerate((1240.0, 1180.0)):
        dur = 1.9
        y = np.zeros(int(dur * SR))
        t_hit = 0.0
        gaps = [0.075, 0.062, 0.052, 0.045, 0.04, 0.036]  # accelerating roll
        for k, g in enumerate(gaps):
            s = strike(f0 * (1 + 0.004 * (k % 2)), 0.5, choke=0.12, seed=10 + k + i * 7, gain=0.35 + 0.08 * k)
            a = int(t_hit * SR); y[a:a + s.size] += s[:max(0, y.size - a)]
            t_hit += g
        ring = strike(f0, dur - t_hit, seed=99 + i, gain=1.2)
        a = int(t_hit * SR); y[a:a + ring.size] += ring[:y.size - a]
        tt = np.arange(y.size - a) / SR                   # soft low bell under the final ring
        y[a:] += 0.25 * np.sin(2 * np.pi * (f0 / 2) * tt) * np.exp(-6.9 * tt / 1.4)
        write(folder, f"countdown_go_{i}.wav", normalize(y, -3.0))
    print("ok", folder)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "out")
