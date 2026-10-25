"""
Intelligibility check of generated lines (PRODUCTION): transcribes each clip with Whisper (openai/whisper-small, Apache-2.0)
and compares it with the text it should say (word error rate). Flags mispronounced / garbled / truncated lines.

    .venv/Scripts/python verify.py --manifest ../../../AssetsMaterial/VoiceMasters/manifest.json --voices beethoven,mozart --languages en
Writes a report next to the manifest (verify_report.txt) and prints a summary.
"""
import argparse, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
LANG = {"en": "english", "es": "spanish", "fr": "french", "de": "german", "it": "italian", "pt": "portuguese",
        "ru": "russian", "ko": "korean", "zh": "chinese", "ja": "japanese"}


def words(s):
    s = s.lower().replace("’", "'")
    return re.findall(r"[a-zà-ÿ0-9']+", s)


def wer(ref, hyp):
    r, h = words(ref), words(hyp)
    d = [[0] * (len(h) + 1) for _ in range(len(r) + 1)]
    for i in range(len(r) + 1): d[i][0] = i
    for j in range(len(h) + 1): d[0][j] = j
    for i in range(1, len(r) + 1):
        for j in range(1, len(h) + 1):
            d[i][j] = min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + (r[i - 1] != h[j - 1]))
    return d[len(r)][len(h)] / max(1, len(r))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", required=True)
    ap.add_argument("--voices", default="")
    ap.add_argument("--languages", default="")
    ap.add_argument("--threshold", type=float, default=0.25)
    a = ap.parse_args()
    import torch
    from transformers import pipeline
    from huggingface_hub import snapshot_download
    model_dir = os.path.join(HERE, "models", "whisper-small")
    if not os.path.isfile(os.path.join(model_dir, "config.json")):
        snapshot_download("openai/whisper-small", local_dir=model_dir, allow_patterns=["*.json", "*.txt", "model.safetensors"])  # one weights format
    asr = pipeline("automatic-speech-recognition", model=model_dir, device=0 if torch.cuda.is_available() else -1)
    with open(a.manifest, encoding="utf-8-sig") as f:
        items = json.load(f)["items"]
    voices = set(filter(None, a.voices.split(","))); langs = set(filter(None, a.languages.split(",")))
    root = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))  # project root (MusicGame/MusicGame)
    rows, flagged = [], 0
    for it in items:
        if not it.get("ok") or it["language"] not in LANG: continue
        if voices and it["voice"] not in voices: continue
        if langs and it["language"] not in langs: continue
        path = it["master"] if os.path.exists(it["master"]) else os.path.join(root, it["game"])
        out = asr(path, generate_kwargs={"language": LANG[it["language"]], "task": "transcribe"})
        e = wer(it["text"], out["text"])
        bad = e > a.threshold
        flagged += bad
        rows.append(f"{'FLAG' if bad else 'ok  '} WER {e:4.2f}  {it['id']}\n      text: {it['text']}\n      heard: {out['text'].strip()}")
    report = os.path.join(os.path.dirname(a.manifest), "verify_report.txt")
    with open(report, "w", encoding="utf-8") as f:
        f.write("\n".join(rows))
    print(json.dumps({"event": "verify", "checked": len(rows), "flagged": flagged, "report": report}))


if __name__ == "__main__":
    main()
