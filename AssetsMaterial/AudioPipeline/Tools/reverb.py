"""Room reverb for a rendered master (e.g. a dry organ that should sound like it is in a church).

Convolution with a synthetic, deterministic impulse response: a few early reflections + a stereo
decorrelated noise tail whose decay time is frequency dependent (lows ring longest, highs die first,
as in a large stone room). No other processing besides a final pure-gain -1 dBFS ceiling.
Usage: python reverb.py in.wav out.wav [--rt60 4.5] [--wet-db -4] [--predelay-ms 40]
"""
import argparse
import numpy as np
from scipy.signal import butter, sosfilt, oaconvolve
from render_qa import read_wav, write_float_wav, CEILING_DBFS


def impulse_response(rate, rt60, predelay_ms, seed=565):
    rng = np.random.default_rng(seed)
    n = int(rate * rt60 * 1.2)
    t = np.arange(n) / rate
    ir = np.zeros((n, 2))
    # Tail: three bands, each with its own decay (low 1.15x, mid 1x, high 0.55x of rt60).
    bands = [(butter(4, 300, "low", fs=rate, output="sos"), 1.15),
             (butter(4, [300, 3000], "band", fs=rate, output="sos"), 1.0),
             (butter(4, 3000, "high", fs=rate, output="sos"), 0.55)]
    onset = np.clip(t / 0.08, 0, 1)  # tail builds up over the first 80 ms
    for sos, k in bands:
        noise = sosfilt(sos, rng.standard_normal((n, 2)), axis=0)
        ir += noise * (np.exp(-6.91 * t / (rt60 * k)) * onset)[:, None]
    # Early reflections (walls/floor), slightly different per side.
    for ms, g in [(11, .5), (19, .42), (27, .35), (38, .3), (52, .25), (67, .2)]:
        for ch, off in ((0, 0), (1, 3)):
            i = int(rate * (ms + off) / 1000)
            ir[i, ch] += g * 3
    pre = int(rate * predelay_ms / 1000)
    ir = np.vstack([np.zeros((pre, 2)), ir])
    return ir / np.sqrt(np.sum(ir ** 2) / 2)  # unit energy per channel


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("inp"); ap.add_argument("out")
    ap.add_argument("--rt60", type=float, default=4.5)
    ap.add_argument("--wet-db", type=float, default=-4.0)
    ap.add_argument("--predelay-ms", type=float, default=40.0)
    a = ap.parse_args()
    dry, rate, ch, _, _ = read_wav(a.inp)
    dry = dry.astype(np.float64)
    if ch == 1: dry = np.repeat(dry, 2, axis=1)
    ir = impulse_response(rate, a.rt60, a.predelay_ms)
    wet = np.stack([oaconvolve(dry[:, c], ir[:, c]) for c in range(2)], axis=1)
    out = np.zeros_like(wet); out[:len(dry)] = dry
    out += wet * 10 ** (a.wet_db / 20)
    # Trim the tail once it has decayed below -70 dB of the peak.
    peak = np.max(np.abs(out)); env = np.max(np.abs(out), axis=1)
    last = np.nonzero(env > peak * 10 ** (-70 / 20))[0][-1]
    out = out[:last + 1]
    out *= 10 ** ((CEILING_DBFS - 20 * np.log10(np.max(np.abs(out)))) / 20)
    write_float_wav(a.out, out.astype(np.float32), rate)
    print(f"{a.out}: {len(out) / rate:.1f}s, rt60 {a.rt60}s, wet {a.wet_db} dB, peak {CEILING_DBFS} dBFS")


if __name__ == "__main__":
    main()
