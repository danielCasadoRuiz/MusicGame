"""
Ranks phrase takes of a 3-word countdown (announcer_phrases.py report) for a final word that MATCHES the others: same
pitch as words 1-2 (semitones), flat (pitch glide), a natural length (not clipped, not drawn out), a real /u/ (first
formant clearly below the /o/ of "Dos"), and a clean cut before it (pause length at the last cut).

    .venv/Scripts/python rank_countdown.py <report.json> [--top 10]
"""
import argparse, json, re
import numpy as np, soundfile as sf, librosa


def f0_median(path):
    y, sr = sf.read(path)
    f0, _, _ = librosa.pyin(y.astype(np.float32), fmin=60, fmax=600, sr=sr, frame_length=1024)
    f0 = f0[~np.isnan(f0)]
    return float(np.median(f0)) if f0.size else None


def first_formant(path, tail=0.35):
    y, sr = sf.read(path)
    y = librosa.resample(y.astype(float), orig_sr=sr, target_sr=10000); sr = 10000
    y = y[:max(int(0.4 * sr), len(y) - int(tail * sr))]                 # skip the FX hall tail
    rms = librosa.feature.rms(y=y, frame_length=400, hop_length=100)[0]
    F = []
    for i in np.where(rms > rms.max() * 0.5)[0]:
        fr = y[i * 100:i * 100 + 400]
        if len(fr) < 400: continue
        fr = fr * np.hamming(400); fr = np.append(fr[0], fr[1:] - 0.63 * fr[:-1])
        r = [z for z in np.roots(librosa.lpc(fr, order=12)) if np.imag(z) > 0]
        f = sorted(np.angle(r) * sr / (2 * np.pi)); f = [x for x in f if x > 200]
        if f: F.append(f[0])
    return float(np.median(F)) if F else None


def last_pause(path, thr_db=-32.0):
    """Length of the pause the last cut falls in (the shortest of the 2 longest pauses of the phrase)."""
    y, sr = sf.read(path)
    hop = int(0.01 * sr); n = len(y) // hop
    r = np.sqrt(np.mean(np.square(y[:n * hop].reshape(n, hop)), axis=1)); db = 20 * np.log10(r / (r.max() + 1e-12) + 1e-12)
    v = db > thr_db; idx = np.flatnonzero(v)
    gaps, i = [], idx[0]
    while i <= idx[-1]:
        if not v[i]:
            j = i
            while j <= idx[-1] and not v[j]: j += 1
            gaps.append(j - i); i = j
        else:
            i += 1
    gaps = sorted(gaps, reverse=True)[:2]
    return (min(gaps) * 0.01) if len(gaps) == 2 else 0.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("report")
    ap.add_argument("--top", type=int, default=10)
    a = ap.parse_args()
    d = json.load(open(a.report, encoding="utf-8"))
    rows = []
    for t in d["takes"]:
        sg = t["segments"]
        if len(sg) != 3 or any(s["file"] is None for s in sg): continue
        h = [s.get("heard", "") for s in sg]
        if not ("tres" in h[0].lower() and re.search(r"do[sz]", h[1].lower())) or "..." in h[0] + h[1]: continue
        f = [f0_median(s["file"]) for s in sg]
        if None in f: continue
        u1, o1 = first_formant(sg[2]["file"]), first_formant(sg[1]["file"])
        if not u1 or not o1: continue
        pitch = 12 * np.log2(f[2] / ((f[0] + f[1]) / 2))
        ratio = u1 / o1
        uv, ref = sg[2]["voiced"], max(sg[0]["voiced"], sg[1]["voiced"])
        pause = last_pause(t["file"])
        score = (abs(pitch) + 0.5 * abs(sg[2]["glide_st"]) + 8 * max(0, ratio - 0.75)
                 + 6 * max(0, 0.28 - uv) + 3 * max(0, uv - max(0.5, ref + 0.1)) + 10 * max(0, 0.08 - pause))
        rows.append({"score": round(float(score), 2), "phrase": t["phrase"], "seed": t["seed"], "heard": h,
                     "f0": [round(x) for x in f], "pitch_st": round(float(pitch), 1), "glide_st": sg[2]["glide_st"],
                     "voiced": [s["voiced"] for s in sg], "u_o_F1": round(ratio, 2), "pause": round(pause, 2)})
    for r in sorted(rows, key=lambda r: r["score"])[:a.top]:
        print(json.dumps(r, ensure_ascii=False))


if __name__ == "__main__":
    main()
