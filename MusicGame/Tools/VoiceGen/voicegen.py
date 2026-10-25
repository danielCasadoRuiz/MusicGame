"""
MusicGame voice production — offline generator (PRODUCTION TOOL, never shipped with the game).

Driven by the Unity editor window (Tools > MusicGame > Audio > Voice Production), which writes a job JSON and
reads this script's stdout (one JSON object per line). It can also be run by hand:

    .venv/Scripts/python voicegen.py --job work/job.json
    .venv/Scripts/python voicegen.py --check            # environment / models / GPU report

Pipeline per voice (identity kept stable across lines and languages):
  1. DESIGN (once per voice definition): the engine's voice-design model speaks the definition's reference text
     with its persona description -> reference.wav (+ the reference text) = the voice's fixed identity.
     Regenerated only when the definition (description / reference text / seed / engine) changes.
  2. LINES: the clone model builds a reusable prompt from that reference and speaks every item (already
     translated text, its language) with it.
  3. POST: trim leading/trailing silence, loudness-normalise (EBU R128 integrated) with a peak ceiling, checks
     (empty, too short, NaN, leading silence), then write the 24-bit production MASTER and the 16-bit game copy.

Engines are a registry (ENGINES): a future engine for a language only adds a class with the same three methods.
Every finished item is reported at once, so a cancelled run keeps everything already done.
"""
import argparse, json, os, sys, time, traceback

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))


RESULTS = None  # --results file: every message is also appended there (a run is recoverable without Unity)


def emit(**kw):
    line = json.dumps(kw, ensure_ascii=False)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()
    if RESULTS:
        with open(RESULTS, "a", encoding="utf-8") as f:
            f.write(line + "\n")


# ── Engines ─────────────────────────────────────────────────────────────────────

class Qwen3TTSEngine:
    """Qwen3-TTS (Apache-2.0): 1.7B VoiceDesign for the identity, 1.7B Base (clone) for the lines."""
    id = "qwen3-tts"
    LANGUAGES = {"en": "English", "es": "Spanish", "fr": "French", "de": "German", "it": "Italian",
                 "pt": "Portuguese", "ru": "Russian", "ko": "Korean", "zh": "Chinese", "ja": "Japanese"}

    def __init__(self, cfg):
        self.cfg = cfg
        self._design = None
        self._clone = None
        self._prompts = {}

    def _load(self, path):
        import torch
        from qwen_tts import Qwen3TTSModel
        return Qwen3TTSModel.from_pretrained(path, device_map=self.cfg.get("device", "cuda:0"),
                                             dtype=getattr(torch, self.cfg.get("dtype", "bfloat16")),
                                             attn_implementation=self.cfg.get("attn", "sdpa"))

    def _free(self, attr):
        import gc, torch
        setattr(self, attr, None)
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

    def design(self, voice):
        import torch
        if self._clone is not None:
            self._free("_clone"); self._prompts.clear()
        if self._design is None:
            self._design = self._load(self.cfg["models"]["design"])
        torch.manual_seed(int(voice.get("seed", 0)))
        wavs, sr = self._design.generate_voice_design(text=voice["ref_text"], language=self.lang(voice.get("ref_language", "en")),
                                                     instruct=voice["description"])
        return np.asarray(wavs[0], dtype=np.float32), sr

    def speak(self, voice, text, language, seed):
        import torch
        if self._design is not None:
            self._free("_design")
        if self._clone is None:
            self._clone = self._load(self.cfg["models"]["clone"])
        if voice["id"] not in self._prompts:
            self._prompts[voice["id"]] = self._clone.create_voice_clone_prompt(ref_audio=voice["ref_wav"], ref_text=voice["ref_text"],
                                                                              x_vector_only_mode=False)
        torch.manual_seed(int(seed))
        wavs, sr = self._clone.generate_voice_clone(text=text, language=self.lang(language), voice_clone_prompt=self._prompts[voice["id"]])
        return np.asarray(wavs[0], dtype=np.float32), sr

    def lang(self, code):
        if code not in self.LANGUAGES:
            raise ValueError(f"language '{code}' is not supported by {self.id}")
        return self.LANGUAGES[code]


ENGINES = {Qwen3TTSEngine.id: Qwen3TTSEngine}


# ── Post-processing / checks ──────────────────────────────────────────────────────

def lead_silence(y, sr, thr_db=-45.0):
    thr = 10 ** (thr_db / 20.0)
    idx = np.flatnonzero(np.abs(y) > thr)
    return (idx[0] / sr) if idx.size else len(y) / sr, (idx[-1] / sr) if idx.size else 0.0


def post_process(y, sr, post):
    warnings = []
    if y.size == 0 or not np.all(np.isfinite(y)):
        raise RuntimeError("empty or invalid audio")
    lead, last = lead_silence(y, sr)
    if lead > post.get("max_lead_silence", 0.3):
        warnings.append(f"leading silence {lead:.2f}s (trimmed)")
    keep = post.get("keep_silence", 0.04)
    start = max(0, int((lead - keep) * sr))
    end = min(len(y), int((last + post.get("tail_silence", 0.12)) * sr) + 1)
    y = y[start:end]
    dur = len(y) / sr
    if dur < post.get("min_duration", 0.15):
        raise RuntimeError(f"too short ({dur:.2f}s)")
    if dur > post.get("max_duration", 30.0):
        warnings.append(f"long ({dur:.1f}s)")
    import pyloudnorm as pyln
    meter = pyln.Meter(sr, block_size=min(0.4, max(0.05, dur / 2)))
    try:
        loud = meter.integrated_loudness(y)
    except Exception:
        loud = float("-inf")
    target = post.get("target_lufs", -18.0)
    if np.isfinite(loud):
        y = y * (10 ** ((target - loud) / 20.0))
    peak_ceiling = 10 ** (post.get("peak_db", -1.0) / 20.0)
    peak = float(np.max(np.abs(y))) if y.size else 0.0
    if peak > peak_ceiling:
        y = y * (peak_ceiling / peak)
    y = y.astype(np.float32)
    fade = min(len(y) // 4, int(0.008 * sr))           # click-free edges
    if fade > 0:
        ramp = np.linspace(0.0, 1.0, fade, dtype=np.float32)
        y[:fade] *= ramp; y[-fade:] *= ramp[::-1]
    return y, {"duration": round(dur, 3), "lead_silence": round(lead, 3), "lufs_in": round(loud, 1) if np.isfinite(loud) else None,
               "peak": round(float(np.max(np.abs(y))), 3), "warnings": warnings}


def split_phrase(y, sr, count, thr_db=-32.0, min_gap=0.04):
    """Cuts a phrase said in one breath ("Three! Two! One! Tempo!") into `count` word clips at its `count - 1` longest
    pauses (cut in the middle of each pause). None when the phrase has fewer clear pauses than that."""
    if count <= 1:
        return [y]
    hop = int(0.01 * sr)
    frames = len(y) // hop
    if frames < 4:
        return None
    rms = np.sqrt(np.mean(np.square(y[: frames * hop].reshape(frames, hop)), axis=1))
    db = 20 * np.log10(rms / (rms.max() + 1e-12) + 1e-12)
    voiced = db > thr_db
    idx = np.flatnonzero(voiced)
    if idx.size == 0:
        return None
    gaps, i = [], idx[0]
    while i <= idx[-1]:
        if not voiced[i]:
            j = i
            while j <= idx[-1] and not voiced[j]:
                j += 1
            if (j - i) * 0.01 >= min_gap:
                gaps.append((j - i, (i + j) // 2))
            i = j
        else:
            i += 1
    if len(gaps) < count - 1:
        return None
    cuts = sorted(c for _, c in sorted(gaps, reverse=True)[: count - 1])
    bounds = [0] + [c * hop for c in cuts] + [len(y)]
    return [y[bounds[k]: bounds[k + 1]] for k in range(count)]


def dry_cut(y, sr, seconds, fade=0.06):
    """Keeps `seconds` from where the word gets loud (> -20 dB of its peak), then a quick exponential fade."""
    hop = int(0.01 * sr); n = len(y) // hop
    if n < 2:
        return y
    r = np.sqrt(np.mean(np.square(y[: n * hop].reshape(n, hop)), axis=1)); db = 20 * np.log10(r / (r.max() + 1e-12) + 1e-12)
    v = np.flatnonzero(db > -20)
    start = int(v[0] * hop) if v.size else 0
    end = min(len(y), start + int(seconds * sr))
    f = min(int(fade * sr), end)
    out = y[:end].copy()
    out[end - f:] *= np.exp(np.linspace(0.0, -5.0, f)).astype(out.dtype)
    return out


# ── Announcer FX ("hype" chain: body + power + a short arena tail) ───────────────────

ANNOUNCER_FX_VERSION = "hype1"   # part of the announcer lines' signature (C# VoiceProduction.AnnouncerFxVersion)


def _biquad(kind, f0, sr, gain_db=0.0, q=0.707):
    A = 10 ** (gain_db / 40.0); w = 2 * np.pi * f0 / sr; c = np.cos(w); al = np.sin(w) / (2 * q)
    if kind == "hp":
        b = [(1 + c) / 2, -(1 + c), (1 + c) / 2]; a = [1 + al, -2 * c, 1 - al]
    elif kind == "peak":
        b = [1 + al * A, -2 * c, 1 - al * A]; a = [1 + al / A, -2 * c, 1 - al / A]
    else:  # low shelf
        sq = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * c + sq), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sq)]
        a = [(A + 1) + (A - 1) * c + sq, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sq]
    return np.array(b) / a[0], np.array(a) / a[0]


def announcer_fx(y, sr, target_lufs=-14.0, peak_db=-1.0):
    """Arena PA announcer sound: chest (low shelf) + presence, fast compression, gentle saturation, slapback and a short
    hall tail, loud. The input is a post-processed clip; the output is ~0.35 s longer (the tail)."""
    from scipy.signal import lfilter
    x = y.astype(np.float64)
    for k, f, g, q in (("hp", 75, 0, 0.707), ("shelf", 190, 4.5, 0.707), ("peak", 2800, 3.5, 0.9), ("peak", 450, -1.5, 1.0)):
        b, a = _biquad(k, f, sr, g, q); x = lfilter(b, a, x)
    # compressor (feed-forward, peak envelope)
    env = np.abs(x); out = np.empty_like(env); e = 0.0
    att, rel = np.exp(-1 / (0.003 * sr)), np.exp(-1 / (0.09 * sr))
    for i, v in enumerate(env):
        e = att * e + (1 - att) * v if v > e else rel * e + (1 - rel) * v
        out[i] = e
    ref = np.max(out) + 1e-9
    lvl = 20 * np.log10(out / ref + 1e-9); thr, ratio = -16.0, 4.0
    gain = np.where(lvl > thr, (thr + (lvl - thr) / ratio) - lvl, 0.0)
    x = x * 10 ** (gain / 20)
    x = x / (np.max(np.abs(x)) + 1e-9)
    drive = 2.2
    x = 0.55 * x + 0.45 * np.tanh(drive * x) / np.tanh(drive)
    # slapback + short hall (deterministic noise IR, low-passed)
    tail = int(0.35 * sr)
    x = np.concatenate([x, np.zeros(tail)])
    d = int(0.085 * sr); slap = np.zeros_like(x); slap[d:] = x[:-d] * 0.18
    rng = np.random.default_rng(7)
    n = int(0.6 * sr); t = np.arange(n) / sr
    ir = rng.standard_normal(n) * np.exp(-t / 0.16); ir[: int(0.02 * sr)] = 0
    b, a = _biquad("peak", 5000, sr, -9, 0.5); ir = lfilter(b, a, ir); ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    from scipy.signal import fftconvolve
    hall = fftconvolve(x, ir)[: len(x)] * 0.16
    x = x + slap + hall
    import pyloudnorm as pyln
    try:
        loud = pyln.Meter(sr, block_size=min(0.4, max(0.05, len(x) / sr / 2))).integrated_loudness(x)
        if np.isfinite(loud): x = x * 10 ** ((target_lufs - loud) / 20)
    except Exception:
        pass
    ceil = 10 ** (peak_db / 20); pk = np.max(np.abs(x))
    if pk > ceil: x = x * ceil / pk
    x = x.astype(np.float32)
    f = int(0.03 * sr); x[-f:] *= np.linspace(1, 0, f, dtype=np.float32)
    return x


def write_wav(path, y, sr, subtype):
    import soundfile as sf
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp.wav"
    sf.write(tmp, y, sr, subtype=subtype)
    os.replace(tmp, path)                                # never a half-written file


# ── Runner ──────────────────────────────────────────────────────────────────────

def run_job(job):
    engines = {}
    def engine(eid):
        if eid not in engines:
            if eid not in ENGINES:
                raise ValueError(f"unknown engine '{eid}'")
            engines[eid] = ENGINES[eid](job["engines"][eid])
        return engines[eid]

    voices = {v["id"]: v for v in job.get("voices", [])}
    post = job.get("post", {})
    # 1. identities first (design model), so the clone model is loaded only once afterwards
    for v in voices.values():
        if os.path.exists(v["ref_wav"]) and not v.get("redesign"):
            continue
        emit(event="voice", id=v["id"], status="designing")
        t = time.time()
        y, sr = engine(v["engine"]).design(v)
        y, info = post_process(y, sr, dict(post, max_lead_silence=10))
        write_wav(v["ref_wav"], y, sr, "PCM_24")
        if v.get("ref_preview"):
            write_wav(v["ref_preview"], y, sr, "PCM_16")       # editor preview copy (never shipped)
        emit(event="voice", id=v["id"], status="ready", ref_wav=v["ref_wav"], seconds=round(time.time() - t, 1), **info)

    items = job.get("items", [])
    phrases = {}
    for i, it in enumerate(items):
        emit(event="progress", done=i, total=len(items), id=it["id"])
        t = time.time()
        try:
            v = voices[it["voice"]]
            if it.get("phrase"):
                # A word said within a whole phrase (announcer countdown): the phrase once per (voice, phrase, language,
                # seed), cut at its pauses, this item = its word `segment`.
                pk = (it["voice"], it["phrase"], it["language"], it.get("seed", 0))
                if pk not in phrases:
                    py, psr = engine(v["engine"]).speak(v, it["phrase"], it["language"], it.get("seed", 0))
                    phrases[pk] = (split_phrase(py, psr, int(it["segments"])), psr)
                segs, sr = phrases[pk]
                if segs is None:
                    raise RuntimeError(f"phrase '{it['phrase']}' has fewer than {it['segments']} clear words")
                y = segs[int(it["segment"])]
            else:
                y, sr = engine(v["engine"]).speak(v, it["text"], it["language"], it.get("seed", 0))
            y, info = post_process(y, sr, post)
            if float(it.get("max_seconds", 0) or 0) > 0:
                y = dry_cut(y, sr, float(it["max_seconds"]))     # a drawn-out vowel made as dry as the other words
            if it.get("announcer"):
                y = announcer_fx(y, sr)                      # arena PA sound (announcer words only)
                info["duration"] = round(len(y) / sr, 3)
            write_wav(it["master"], y, sr, "PCM_24")
            write_wav(it["game"], y, sr, "PCM_16")
            emit(event="result", id=it["id"], ok=True, seconds=round(time.time() - t, 1), sample_rate=sr, **info)
        except Exception as e:
            emit(event="result", id=it["id"], ok=False, error=f"{type(e).__name__}: {e}")
    emit(event="progress", done=len(items), total=len(items), id="")
    emit(event="finished")


def check(models_dir):
    report = {"python": sys.version.split()[0]}
    try:
        import torch
        report["torch"] = torch.__version__
        report["cuda"] = torch.cuda.is_available()
        if torch.cuda.is_available():
            p = torch.cuda.get_device_properties(0)
            report["gpu"] = p.name
            report["vram_gb"] = round(p.total_memory / 2**30, 1)
    except Exception as e:
        report["torch_error"] = str(e)
    try:
        import qwen_tts  # noqa
        report["qwen_tts"] = True
    except Exception as e:
        report["qwen_tts"] = False; report["qwen_tts_error"] = str(e)
    report["models"] = {m: os.path.isfile(os.path.join(models_dir, m, "config.json"))
                        for m in ("Qwen3-TTS-12Hz-1.7B-VoiceDesign", "Qwen3-TTS-12Hz-1.7B-Base", "Qwen3-TTS-Tokenizer-12Hz")}
    import shutil
    report["free_disk_gb"] = round(shutil.disk_usage(HERE).free / 2**30, 1)
    emit(event="check", **report)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--job")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--models", default=os.path.join(HERE, "models"))
    ap.add_argument("--results")
    a = ap.parse_args()
    RESULTS = a.results
    try:
        if a.check:
            check(a.models)
        elif a.job:
            with open(a.job, encoding="utf-8") as f:
                run_job(json.load(f))
        else:
            ap.print_help()
    except Exception as e:
        emit(event="fatal", error=f"{type(e).__name__}: {e}", trace=traceback.format_exc())
        sys.exit(1)
