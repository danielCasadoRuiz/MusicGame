"""
Objective listening aid for candidate vocalisation clips (PRODUCTION): for each WAV (or each segment of a long WAV)
reports duration, loudness, pitch (median F0 + contour), attack, and what Whisper hears (a vocalisation must not
contain words). Used to pick hurt / effort / defeat clips; the final choice still needs a human ear.

    <VoiceGen .venv>/python analyze_vocalisations.py <folder or wav> [...] --out report.json [--split]
--split cuts long files into separate takes at silences (≥ 0.25 s below -40 dBFS).
"""
import argparse, json, os, sys
import numpy as np
import soundfile as sf
import librosa


def segments(y, sr, thr_db=-40.0, min_gap=0.25, min_len=0.12):
    thr = 10 ** (thr_db / 20)
    env = np.convolve(np.abs(y), np.ones(int(0.01 * sr)) / int(0.01 * sr), mode="same")
    on = env > thr
    out, start, gap = [], None, 0
    for i, v in enumerate(on):
        if v:
            if start is None: start = i
            gap = 0
        elif start is not None:
            gap += 1
            if gap > min_gap * sr:
                end = i - gap
                if (end - start) / sr >= min_len: out.append((start, end))
                start, gap = None, 0
    if start is not None and (len(y) - start) / sr >= min_len: out.append((start, len(y)))
    return out


def describe(y, sr):
    dur = len(y) / sr
    rms = float(np.sqrt(np.mean(y ** 2)) + 1e-9)
    peak = float(np.max(np.abs(y)) + 1e-9)
    f0, voiced, _ = librosa.pyin(y, fmin=70, fmax=700, sr=sr, frame_length=1024)
    f = f0[~np.isnan(f0)]
    contour = 0.0
    if f.size >= 4:
        q = max(1, f.size // 4)
        contour = float(12 * np.log2(np.median(f[-q:]) / np.median(f[:q])))
    env = np.abs(y)
    attack = float(np.argmax(env > 0.5 * peak) / sr)
    return {"duration": round(dur, 3), "rms_db": round(20 * np.log10(rms), 1), "peak_db": round(20 * np.log10(peak), 1),
            "f0_median": round(float(np.median(f)), 1) if f.size else None, "voiced_ratio": round(float(np.mean(voiced)), 2),
            "contour_st": round(contour, 1), "attack_s": round(attack, 3)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("inputs", nargs="+")
    ap.add_argument("--out", required=True)
    ap.add_argument("--split", action="store_true")
    ap.add_argument("--segments_dir")
    a = ap.parse_args()
    import torch
    from transformers import pipeline
    here = os.path.dirname(os.path.abspath(__file__))
    whisper = os.path.join(here, "..", "VoiceGen", "models", "whisper-small")
    asr = pipeline("automatic-speech-recognition", model=whisper, device=0 if torch.cuda.is_available() else -1)
    files = []
    for p in a.inputs:
        if os.path.isdir(p):
            files += [os.path.join(r, f) for r, _, fs in os.walk(p) for f in fs if f.lower().endswith(".wav")]
        else:
            files.append(p)
    rows = []
    for f in sorted(files):
        y, sr = sf.read(f, always_2d=True)
        y = y.mean(axis=1).astype(np.float32)
        parts = segments(y, sr) if a.split else [(0, len(y))]
        for k, (s, e) in enumerate(parts):
            seg = y[s:e]
            info = describe(seg, sr)
            text = asr({"raw": seg, "sampling_rate": sr}, generate_kwargs={"language": "english", "task": "transcribe"})["text"].strip()
            info.update(file=os.path.basename(f), take=k, start=round(s / sr, 3), heard=text, sr=sr)
            if a.split and a.segments_dir:
                os.makedirs(a.segments_dir, exist_ok=True)
                name = f"{os.path.splitext(os.path.basename(f))[0]}_take{k:02d}.wav"
                sf.write(os.path.join(a.segments_dir, name), seg, sr, subtype="PCM_16")
                info["segment"] = name
            rows.append(info)
            print(json.dumps(info, ensure_ascii=False)); sys.stdout.flush()
    with open(a.out, "w", encoding="utf-8") as fh:
        json.dump(rows, fh, indent=1, ensure_ascii=False)


if __name__ == "__main__":
    main()
