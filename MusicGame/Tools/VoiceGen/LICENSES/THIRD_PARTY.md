# Third-party components of the voice generator (production tool — NOT distributed with the game)

| Component | Version used | Licence | Source |
|---|---|---|---|
| Qwen3-TTS code (`qwen-tts`) | PyPI latest (2026-10-23) | Apache-2.0 | https://github.com/QwenLM/Qwen3-TTS |
| Qwen3-TTS-12Hz-1.7B-VoiceDesign | HF snapshot 2026-10-23 | Apache-2.0 (model card) | https://huggingface.co/Qwen/Qwen3-TTS-12Hz-1.7B-VoiceDesign |
| Qwen3-TTS-12Hz-1.7B-Base | HF snapshot 2026-10-23 | Apache-2.0 (model card) | https://huggingface.co/Qwen/Qwen3-TTS-12Hz-1.7B-Base |
| Qwen3-TTS-Tokenizer-12Hz | HF snapshot 2026-10-23 | Apache-2.0 (model card) | https://huggingface.co/Qwen/Qwen3-TTS-Tokenizer-12Hz |
| openai/whisper-small (verify.py: intelligibility check only) | HF snapshot | Apache-2.0 (model card) | https://huggingface.co/openai/whisper-small |
| PyTorch / torchaudio | 2.11.0 (cu128) | BSD-3-Clause | https://pytorch.org |
| transformers 4.57.3, accelerate 1.12.0, huggingface_hub | — | Apache-2.0 | https://github.com/huggingface |
| librosa | — | ISC | https://librosa.org |
| soundfile | — | BSD-3-Clause | https://github.com/bastibe/python-soundfile |
| pyloudnorm | — | MIT | https://github.com/csteinmetz1/pyloudnorm |
| onnxruntime | — | MIT | https://onnxruntime.ai |
| einops | — | MIT | https://github.com/arogozhnikov/einops |
| gradio (pulled by qwen-tts, unused) | — | Apache-2.0 | https://gradio.app |

All are free with commercial use allowed. No paid service, account or subscription is used. The full Apache-2.0
text is in `Apache-2.0.txt` next to this file (required notice for the Qwen3-TTS code / models we run).

The generated audio is our own output; the Apache-2.0 licence places no restriction on it. The voices are fictional
personas designed from text descriptions — no real person's recording is used as a reference.
