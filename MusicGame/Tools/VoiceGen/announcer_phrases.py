"""
Announcer PHRASE takes: the clone model says the whole countdown in one breath ("Three! Two! One! Tempo!") — with the
announcer's real flow and rising energy, and no reference leaking into a lone word — then the phrase is cut at its
pauses into one clip per word (voicegen.split_phrase, the same cut production uses). Each seed is kept only if Whisper
hears every word in its segment; the most energetic valid seed wins per (voice, phrase).

    .venv/Scripts/python announcer_phrases.py --out <folder> --voice id=<reference.wav> [...] [--seeds 11,101,...]
"""
import argparse, json, os, re, sys
import numpy as np, soundfile as sf
import voicegen
from announcer_takes import ACCEPT
ACCEPT = {**ACCEPT, "Announcer.One": ACCEPT["Announcer.One"] | {"uu", "uuu", "uh", "uuh", "uuuh", "ooh", "oooh"}}
from announcer_candidates import REF_TEXT as OLD_REF
from announcer_hype import REF_TEXT as HYPE_REF

# (phrase id, text, engine language, whisper language, keys of its words in order)
PHRASES = [
    ("en_tempo", "Three! Two! One! Tempo!", "en", "en", ["Announcer.Three", "Announcer.Two", "Announcer.One", "Announcer.Tempo"]),
    ("en_fury", "Three! Two! One! Fyury!", "en", "en", ["Announcer.Three", "Announcer.Two", "Announcer.One", "Announcer.Fury"]),
    ("ca_count", "¡Tres! ¡Dos! ¡U!", "es", "ca", ["Announcer.Three", "Announcer.Two", "Announcer.One"]),
    ("en_perfect", "Perfect!", "en", "en", ["Announcer.Perfect"]),
]


def voiced_shape(y, sr):
    """Length of the loud part (> -20 dB of the word's peak) and pitch glide start→end in semitones: a dry countdown
    word is short and flat; a drawn-out sliding vowel sounds like a moan."""
    import librosa
    hop = int(0.01 * sr); n = len(y) // hop
    r = np.sqrt(np.mean(np.square(y[: n * hop].reshape(n, hop)), axis=1)); db = 20 * np.log10(r / (r.max() + 1e-12) + 1e-12)
    v = np.flatnonzero(db > -20); dur = (v[-1] - v[0] + 1) * 0.01 if v.size else 0.0
    f0, _, _ = librosa.pyin(y.astype(np.float32), fmin=60, fmax=600, sr=sr, frame_length=1024)
    f = f0[~np.isnan(f0)]
    glide = float(12 * np.log2(f[-1] / f[0])) if f.size > 3 else 0.0
    return round(float(dur), 2), round(glide, 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--voice", action="append", required=True)
    ap.add_argument("--seeds", default="11,101,202,303,404,505")
    ap.add_argument("--ref-text", default="hype", choices=["hype", "old"])
    ap.add_argument("--phrase", action="append", default=[], help="id|text|engine lang|whisper lang|key,key,...")
    ap.add_argument("--report", default="phrases.json")
    a = ap.parse_args()
    phrases = [(p[0], p[1], p[2], p[3], p[4].split(",")) for p in (x.split("|") for x in a.phrase)] if a.phrase else PHRASES
    os.makedirs(a.out, exist_ok=True)
    seeds = [int(s) for s in a.seeds.split(",")]
    here = os.path.dirname(os.path.abspath(__file__))
    cfg = {"models": {"design": os.path.join(here, "models", "Qwen3-TTS-12Hz-1.7B-VoiceDesign"),
                      "clone": os.path.join(here, "models", "Qwen3-TTS-12Hz-1.7B-Base")}, "device": "cuda:0", "dtype": "bfloat16", "attn": "sdpa"}
    post = {"target_lufs": -18, "peak_db": -1, "max_lead_silence": 10, "keep_silence": 0.04, "tail_silence": 0.12, "min_duration": 0.15}
    eng = voicegen.Qwen3TTSEngine(cfg)
    takes = []
    for spec in a.voice:
        vid, ref = spec.split("=", 1)
        v = {"id": vid, "ref_wav": ref, "ref_text": HYPE_REF if a.ref_text == "hype" else OLD_REF}
        for pid, text, elang, alang, keys in phrases:
            for seed in seeds:
                y, sr = eng.speak(v, text, elang, seed)
                rms = float(np.sqrt(np.mean(np.square(y)))) if y.size else 0.0
                whole = os.path.join(a.out, f"{vid}_{pid}_s{seed}.wav")
                sf.write(whole, y, sr, subtype="PCM_16")
                segs = voicegen.split_phrase(y, sr, len(keys)) if len(y) / sr < 8 else None
                row = {"voice": vid, "phrase": pid, "text": text, "seed": seed, "file": whole, "asr": alang, "keys": keys,
                       "duration": round(len(y) / sr, 3), "raw_rms_db": round(20 * np.log10(max(rms, 1e-9)), 2), "segments": []}
                if segs is not None:
                    for k, seg in zip(keys, segs):
                        try:
                            p, info = voicegen.post_process(seg, sr, post)
                        except RuntimeError:
                            row["segments"].append({"key": k, "file": None}); continue
                        f = os.path.join(a.out, f"{vid}_{pid}_s{seed}_{k.split('.')[1].lower()}.wav")
                        sf.write(f, voicegen.announcer_fx(p, sr), sr, subtype="PCM_16")
                        vd, gl = voiced_shape(p, sr)
                        row["segments"].append({"key": k, "file": f, "duration": info["duration"], "voiced": vd, "glide_st": gl})
                takes.append(row)
                print(json.dumps({"take": os.path.basename(whole), "segments": len(row["segments"])})); sys.stdout.flush()
    del eng
    import torch, gc
    gc.collect(); torch.cuda.empty_cache()
    from transformers import pipeline
    asr = pipeline("automatic-speech-recognition", model=os.path.join(here, "models", "whisper-small"), device=0)
    for t in takes:
        ok = len(t["segments"]) == len(t["keys"])
        for s in t["segments"]:
            if s["file"] is None:
                ok = False; continue
            hyp = asr(s["file"], generate_kwargs={"language": t["asr"]})["text"].strip()
            s["heard"] = hyp
            words = re.findall(r"[a-z0-9]+", hyp.lower())
            s["ok"] = 1 <= len(words) <= 2 and bool(set(words) & ACCEPT[s["key"]]) and "?" not in hyp
            ok = ok and s["ok"] and s["duration"] <= 1.0
        t["ok"] = ok
    best = {}
    for vid in {t["voice"] for t in takes}:
        for pid, *_ in phrases:
            group = [t for t in takes if t["voice"] == vid and t["phrase"] == pid and t["ok"]]
            if group:
                b = max(group, key=lambda t: t["raw_rms_db"])
                best[f"{vid}/{pid}"] = {"seed": b["seed"], "heard": [s["heard"] for s in b["segments"]], "file": b["file"]}
    with open(os.path.join(a.out, a.report), "w", encoding="utf-8") as f:
        json.dump({"takes": takes, "best": best}, f, indent=1, ensure_ascii=False)
    print(json.dumps({"done": len(takes), "best": len(best)}))


if __name__ == "__main__":
    main()
