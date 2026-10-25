"""
Announcer TAKES: with a chosen identity (an approved candidate reference, never redesigned) the clone model says every
announcer line with several seeds; each take is checked with Whisper (must say the word) and measured for energy
(raw loudness before normalisation + pitch: excited shouting is louder and higher). The best intelligible take per
line wins; its seed goes into the VoiceDefinition (ExtraLine.seed) so production reproduces it exactly.

    .venv/Scripts/python announcer_takes.py --out <folder> --voice id=<reference.wav> [--voice ...] [--seeds 11,101,...]
"""
import argparse, json, os, re, sys
import numpy as np, soundfile as sf
import voicegen
from announcer_candidates import REF_TEXT, f0_median

LINES = {"Announcer.Three": "Three!", "Announcer.Two": "Two!", "Announcer.One": "One!",
         "Announcer.Tempo": "Tempo!", "Announcer.Fury": "Fury!", "Announcer.Perfect": "Perfect!"}
ACCEPT = {"Announcer.Three": {"three", "3", "tres"}, "Announcer.Two": {"two", "2", "too", "to", "dos"}, "Announcer.One": {"one", "1", "won", "u", "un", "hu"},
          "Announcer.Tempo": {"tempo"}, "Announcer.Fury": {"fury"}, "Announcer.Perfect": {"perfect"}}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--voice", action="append", required=True)
    ap.add_argument("--seeds", default="11,101,202,303,404,505")
    ap.add_argument("--report", default="takes.json")
    ap.add_argument("--engine-language", default="en", help="language the TTS speaks the lines in (es for the Catalan numbers)")
    ap.add_argument("--asr-language", default="en", help="language Whisper listens for (ca for the Catalan numbers)")
    ap.add_argument("--line", action="append", default=[], help="only these lines, KEY=TEXT (e.g. Announcer.Fury=Fyury!)")
    a = ap.parse_args()
    lines = [tuple(l.split("=", 1)) for l in a.line] if a.line else list(LINES.items())   # a key may have several spellings
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
        v = {"id": vid, "ref_wav": ref, "ref_text": REF_TEXT}
        for key, text in lines:
            for seed in seeds:
                y, sr = eng.speak(v, text, a.engine_language, seed)
                rms = float(np.sqrt(np.mean(np.square(y)))) if y.size else 0.0
                try:
                    p, info = voicegen.post_process(y, sr, post)
                except RuntimeError as e:                       # a broken take (too short / empty): skip it
                    print(json.dumps({"skipped": f"{vid} {text} s{seed}", "why": str(e)})); continue
                path = os.path.join(a.out, f"{vid}_{key.split('.')[1].lower()}_{re.sub('[^a-z]', '', text.lower())}_s{seed}.wav")
                sf.write(path, p, sr, subtype="PCM_16")
                takes.append({"voice": vid, "key": key, "text": text, "seed": seed, "file": path, "duration": info["duration"],
                              "raw_rms_db": round(20 * np.log10(max(rms, 1e-9)), 2), "f0": f0_median(p, sr)})
                print(json.dumps({"take": os.path.basename(path)})); sys.stdout.flush()
    del eng
    import torch, gc
    gc.collect(); torch.cuda.empty_cache()
    from transformers import pipeline
    asr = pipeline("automatic-speech-recognition", model=os.path.join(here, "models", "whisper-small"), device=0)
    for t in takes:
        if t["duration"] > 5:                                   # a runaway take (the model kept going): never usable
            t["heard"], t["ok"] = "(runaway take)", False
            continue
        hyp = asr(t["file"], generate_kwargs={"language": a.asr_language})["text"]
        t["heard"] = hyp.strip()
        t["ok"] = bool(set(re.findall(r"[a-z0-9]+", hyp.lower())) & ACCEPT[t["key"]])
    # Energy score within each (voice, line): louder raw take + higher pitch = more excited delivery.
    best = {}
    for vid in {t["voice"] for t in takes}:
        for key in dict(lines):
            group = [t for t in takes if t["voice"] == vid and t["key"] == key]
            def z(vals):
                vals = np.array(vals, dtype=float); s = vals.std() or 1.0
                return (vals - vals.mean()) / s
            zl = z([t["raw_rms_db"] for t in group]); zf = z([t["f0"] or 0 for t in group])
            for t, l, f in zip(group, zl, zf):
                t["energy"] = round(float(l + f), 3)
            ok = [t for t in group if t["ok"] and "?" not in t["heard"] and 0.2 <= t["duration"] <= 1.6] or [t for t in group if t["ok"]]
            if ok:
                b = max(ok, key=lambda t: t["energy"])
                best[f"{vid}/{key}"] = {"seed": b["seed"], "text": b["text"], "file": b["file"], "heard": b["heard"], "energy": b["energy"]}
    with open(os.path.join(a.out, a.report), "w", encoding="utf-8") as f:
        json.dump({"takes": takes, "best": best}, f, indent=1, ensure_ascii=False)
    print(json.dumps({"done": len(takes), "best": len(best)}))


if __name__ == "__main__":
    main()
