"""
Announcer HYPE candidates (NBA All-Star arena style): VoiceDesign creates each identity from a booming persona and a hype
reference text (the clone model copies the reference's energy and flow), then the clone says the announcer words with
it. Each candidate is measured for body and power (median pitch, low-mid energy share, loudness, pitch movement) and
ranked; the countdown words are written raw AND through the announcer FX chain (voicegen.announcer_fx).

    .venv/Scripts/python announcer_hype.py --out <folder> [--only id,id]
"""
import argparse, json, os, sys
import numpy as np, soundfile as sf
import voicegen
from announcer_candidates import f0_median

REF_TEXT = ("Ladies and gentlemen, get on your feet and make some noise! It's showtime! Three! Two! One! "
            "Let's goooo! Are you ready? I can't hear you! Louder! Here we go, baby!")
WORDS = {"three": "Three!", "two": "Two!", "one": "One!", "tempo": "Tempo!", "fury": "Fury!", "perfect": "Perfect!"}

CANDIDATES = [
    ("m1", "Male, forties, legendary NBA All-Star arena announcer, huge booming deep bass-baritone, full chest voice, massive "
           "projection, stretched hype vowels, rhythmic swagger and flow, a little rasp, fills the whole stadium, never thin, never soft.", 7101),
    ("m2", "Male, thirties, basketball arena hype man and PA announcer, deep powerful baritone with thick chest resonance, loud and "
           "commanding, street swagger, punchy rhythmic flow, elongated vowels, electrifying crowd energy, never nasal, never soft.", 7202),
    ("m3", "Male, fifties, iconic stadium ring announcer, enormous deep resonant voice, slow booming build-up then explosive "
           "shouts, gravel and power, stretched dramatic vowels, epic hype, larger than life.", 7303),
    ("m4", "Male, thirties, energetic NBA game host, strong dark baritone, very loud shouted delivery, rhythmic hip-hop flow, "
           "bold attack on every word, booming bass, pumping the crowd, never cartoonish.", 7404),
    ("f1", "Female, thirties, NBA All-Star arena host, powerful deep husky contralto, strong low chest voice, gritty and bold, "
           "massive projection, rhythmic swagger and flow, stretched hype vowels, fills the stadium, never high-pitched, never soft, never cute.", 8101),
    ("f2", "Female, forties, fierce stadium hype woman, very low raspy alto, thick full voice with body, loud commanding shouts, "
           "street swagger, punchy hip-hop rhythm, tough and confident, never girlish, never thin.", 8202),
    ("f3", "Female, thirties, basketball PA announcer, dark smoky contralto, big booming chest resonance, explosive loud delivery, "
           "elongated vowels, rhythmic flow, electrifying crowd energy, mature and strong, no falsetto.", 8303),
    ("f4", "Female, late thirties, powerful arena MC, deep resonant low voice, a little gravel, shouting with full power from the "
           "chest, bold rhythmic delivery, hype and swagger, commanding like a coach, never high, never sweet.", 8404),
]


def body_metrics(y, sr):
    import librosa
    S = np.abs(librosa.stft(y.astype(np.float32), n_fft=2048)) ** 2
    f = librosa.fft_frequencies(sr=sr, n_fft=2048)
    tot = S.sum() + 1e-12
    low = S[(f >= 80) & (f < 500)].sum() / tot
    cent = float((S.sum(axis=1) * f).sum() / tot)
    f0, _, _ = librosa.pyin(y.astype(np.float32), fmin=60, fmax=500, sr=sr, frame_length=1024)
    f0 = f0[~np.isnan(f0)]
    move = float(np.std(12 * np.log2(f0 / np.median(f0)))) if f0.size > 4 else 0.0
    return round(float(low), 3), round(cent), round(move, 2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    only = set(x for x in a.only.split(",") if x)
    cands = [c for c in CANDIDATES if not only or c[0] in only]
    here = os.path.dirname(os.path.abspath(__file__))
    cfg = {"models": {"design": os.path.join(here, "models", "Qwen3-TTS-12Hz-1.7B-VoiceDesign"),
                      "clone": os.path.join(here, "models", "Qwen3-TTS-12Hz-1.7B-Base")}, "device": "cuda:0", "dtype": "bfloat16", "attn": "sdpa"}
    post = {"target_lufs": -18, "peak_db": -1, "max_lead_silence": 10, "keep_silence": 0.04, "tail_silence": 0.12, "min_duration": 0.15}
    eng = voicegen.Qwen3TTSEngine(cfg)
    voices = []
    for cid, desc, seed in cands:
        v = {"id": cid, "description": desc, "ref_text": REF_TEXT, "ref_language": "en", "seed": seed,
             "ref_wav": os.path.join(a.out, f"{cid}_reference.wav")}
        y, sr = eng.design(v)
        rms = float(np.sqrt(np.mean(np.square(y))))
        y, _ = voicegen.post_process(y, sr, post)
        sf.write(v["ref_wav"], y, sr, subtype="PCM_16")
        v["raw_rms_db"] = round(20 * np.log10(max(rms, 1e-9)), 2)
        voices.append(v)
        print(json.dumps({"designed": cid})); sys.stdout.flush()
    report = []
    for v in voices:
        ref, rsr = sf.read(v["ref_wav"])
        low, cent, move = body_metrics(ref, rsr)
        row = {"id": v["id"], "description": v["description"], "seed": v["seed"], "f0": f0_median(ref, rsr), "low_share": low,
               "centroid": cent, "pitch_move_st": move, "ref_raw_rms_db": v["raw_rms_db"]}
        seq = []
        for key, text in WORDS.items():
            y, sr = eng.speak(v, text, "en", v["seed"] + len(key))
            y, info = voicegen.post_process(y, sr, post)
            sf.write(os.path.join(a.out, f"{v['id']}_{key}.wav"), y, sr, subtype="PCM_16")
            fx = voicegen.announcer_fx(y, sr)
            sf.write(os.path.join(a.out, f"{v['id']}_{key}_fx.wav"), fx, sr, subtype="PCM_16")
            row[key] = info["duration"]
            seq.append((fx, sr))
        # The race countdown as heard in game: a word every 0.8 s, Tempo! on the 4th beat.
        sr = seq[0][1]; step = int(0.8 * sr)
        order = [seq[0], seq[1], seq[2], seq[3]]
        mix = np.zeros(step * 4 + max(len(x) for x, _ in order))
        for i, (x, _) in enumerate(order):
            mix[i * step:i * step + len(x)] += x
        sf.write(os.path.join(a.out, f"{v['id']}_COUNTDOWN_fx.wav"), np.clip(mix, -1, 1), sr, subtype="PCM_16")
        report.append(row)
        print(json.dumps({"sample": v["id"], "f0": row["f0"], "low": low})); sys.stdout.flush()
    # Body/power rank inside each gender: lower pitch, more low-mid energy, darker centroid, louder raw reference.
    for g in ("m", "f"):
        rows = [r for r in report if r["id"].startswith(g)]
        if not rows: continue
        def z(k, sign):
            v = np.array([r[k] or 0 for r in rows], float); s = v.std() or 1.0
            return sign * (v - v.mean()) / s
        score = z("f0", -1) + z("low_share", 1) + z("centroid", -1) + z("ref_raw_rms_db", 1) + 0.5 * z("pitch_move_st", 1)
        for r, s in zip(rows, score): r["body_score"] = round(float(s), 2)
    with open(os.path.join(a.out, "hype.json"), "w", encoding="utf-8") as f:
        json.dump(sorted(report, key=lambda r: -r.get("body_score", 0)), f, indent=1, ensure_ascii=False)
    print(json.dumps({"done": len(report)}))


if __name__ == "__main__":
    main()
