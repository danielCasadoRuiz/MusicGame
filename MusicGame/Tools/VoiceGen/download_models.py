"""Downloads ONLY the three models the voice pipeline uses into ./models/<name> (skips the ones already present).
    .venv/Scripts/python download_models.py
"""
import os, sys
from huggingface_hub import snapshot_download

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = ["Qwen3-TTS-Tokenizer-12Hz", "Qwen3-TTS-12Hz-1.7B-VoiceDesign", "Qwen3-TTS-12Hz-1.7B-Base"]

for m in MODELS:
    target = os.path.join(HERE, "models", m)
    if os.path.isfile(os.path.join(target, "config.json")):
        print(f"{m}: already present"); continue
    print(f"{m}: downloading ..."); sys.stdout.flush()
    snapshot_download(f"Qwen/{m}", local_dir=target)
    print(f"{m}: ok")
