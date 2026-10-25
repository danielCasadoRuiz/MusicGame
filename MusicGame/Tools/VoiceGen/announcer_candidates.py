"""
Announcer CANDIDATES (production, for listening only): for each persona, VoiceDesign speaks the announcer reference
text once (the identity), then the clone model says the two audition phrases with that identity — exactly how the
final set would be produced. Writes <out>/<id>_tempo.wav / _fury.wav (+ _reference.wav) and candidates.json with the
measured median F0 (adult female speech ~165–255 Hz, adult male ~85–180 Hz; the rejected voice is measured too).

    .venv/Scripts/python announcer_candidates.py --out <folder> [--compare rejected.wav ...]
"""
import argparse, json, os, sys
import numpy as np, soundfile as sf
import voicegen

REF_TEXT = "Three! Two! One! Let's go! Welcome to the arena, where every beat hits like a punch. Are you ready? Here we go!"
PHRASES = {"tempo": "Three, two, one... Tempo!", "fury": "Fury!"}

CANDIDATES = [
    ("female_a", "Female, late thirties, charismatic arena announcer, full warm contralto with strong chest resonance, low speaking register, "
                 "confident punchy video-game delivery, natural and adult, never high-pitched, never childlike, no falsetto.", 4101),
    ("female_b", "Female, forties, sports broadcast and fighting-game announcer, deep husky alto voice with body and a little grit, strong breath support, "
                 "commanding hype energy, crisp consonants, grounded mature tone, no squeaky or girlish sound.", 4202),
    ("female_c", "Female, thirties, esports arena host, rich mezzo-contralto, powerful and rhythmic, big stadium energy, relaxed low pitch, "
                 "natural adult performance, not cartoonish, not shrill.", 4303),
    ("male_b",   "Male, forties, fighting-game announcer, deep resonant bass-baritone with a slight rasp, huge hype energy, rhythmic punchy delivery, "
                 "natural adult performance, not cartoonish.", 4404),
    ("male_c",   "Male, thirties, arena announcer, warm powerful baritone, bright energetic attack, rhythmic and bold, crisp consonants, "
                 "natural video-game hype, no exaggerated growl.", 4505),
]


def f0_median(y, sr):
    import librosa
    f0, voiced, _ = librosa.pyin(y.astype(np.float32), fmin=60, fmax=500, sr=sr, frame_length=1024)
    f = f0[~np.isnan(f0)]
    return round(float(np.median(f)), 1) if f.size else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--compare", nargs="*", default=[])
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    here = os.path.dirname(os.path.abspath(__file__))
    cfg = {"models": {"design": os.path.join(here, "models", "Qwen3-TTS-12Hz-1.7B-VoiceDesign"),
                      "clone": os.path.join(here, "models", "Qwen3-TTS-12Hz-1.7B-Base")}, "device": "cuda:0", "dtype": "bfloat16", "attn": "sdpa"}
    post = {"target_lufs": -18, "peak_db": -1, "max_lead_silence": 10, "keep_silence": 0.04, "tail_silence": 0.12, "min_duration": 0.15}
    eng = voicegen.Qwen3TTSEngine(cfg)
    voices = []
    for cid, desc, seed in CANDIDATES:                      # 1. identities (design model only)
        v = {"id": cid, "description": desc, "ref_text": REF_TEXT, "ref_language": "en", "seed": seed,
             "ref_wav": os.path.join(a.out, f"{cid}_reference.wav")}
        y, sr = eng.design(v)
        y, _ = voicegen.post_process(y, sr, post)
        sf.write(v["ref_wav"], y, sr, subtype="PCM_16")
        voices.append(v)
        print(json.dumps({"designed": cid})); sys.stdout.flush()
    report = []
    for v in voices:                                         # 2. the audition phrases with that identity (clone model)
        row = {"id": v["id"], "description": v["description"], "seed": v["seed"]}
        for key, text in PHRASES.items():
            y, sr = eng.speak(v, text, "en", v["seed"] + len(key))
            y, info = voicegen.post_process(y, sr, post)
            path = os.path.join(a.out, f"{v['id']}_{key}.wav")
            sf.write(path, y, sr, subtype="PCM_16")
            row[key] = {"file": os.path.basename(path), "duration": info["duration"]}
        ref, rsr = sf.read(v["ref_wav"])
        row["f0_median_hz"] = f0_median(ref, rsr)
        report.append(row)
        print(json.dumps({"sample": v["id"], "f0": row["f0_median_hz"]})); sys.stdout.flush()
    for c in a.compare:
        y, sr = sf.read(c)
        report.append({"id": "compare:" + os.path.basename(c), "f0_median_hz": f0_median(y, sr)})
    with open(os.path.join(a.out, "candidates.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1, ensure_ascii=False)
    print(json.dumps({"done": len(report)}))


if __name__ == "__main__":
    main()
