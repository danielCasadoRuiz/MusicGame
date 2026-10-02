"""Lossless 16-bit archive of every published composer master + size report vs the Unity MP3s.

For each published master (32-bit float WAV in Work/, see publish.py — the same file the Unity MP3 is built from):
  - Optimized/WAV16/<Composer>/<name>.wav : 44.1 kHz stereo 16-bit PCM (TPDF dither, no gain change)
  - its Unity <name>.json sidecar is copied next to it with an added "derivedFormat" block.
Gain is applied ONLY if 16-bit would otherwise clip (reported per track). Every output is decoded back and
checked (decodable, same sample count, peak / clipping, residual vs the master); the Unity MP3 is decoded too.
Writes Docs/optimized_formats_report.json/.md. Idempotent (skips outputs newer than their source; --force).
Usage: python AssetsMaterial/AudioPipeline/Tools/optimize.py [--force]
"""
import argparse, datetime, json, os, struct, subprocess
import numpy as np
from common import *
from render_qa import read_wav

OUT = PIPELINE / "Optimized"
FFMPEG = Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/ffmpeg.exe"
# LAME encoder (576) + decoder (529) delay: what a decoder that ignores gapless info (like Unity's) plays first.
MP3_CODEC_DELAY = 1105


def write_pcm16(path, data, rate):
    data = np.ascontiguousarray(data, dtype="<i2")
    ch = data.shape[1]
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + data.nbytes) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 1, ch, rate, rate * ch * 2, ch * 2, 16))
        f.write(b"data" + struct.pack("<I", data.nbytes)); f.write(data.tobytes())


def to_pcm16(audio, seed):
    peak = float(np.max(np.abs(audio)))
    gain = 1.0
    if peak * 32768 + 1 > 32767:  # would clip after dither → minimal pure gain
        gain = 32766 / 32768 / peak
    rng = np.random.default_rng(seed)
    tpdf = (rng.random(audio.shape) - rng.random(audio.shape))  # ±1 LSB triangular
    q = np.round(audio.astype(np.float64) * gain * 32768 + tpdf)
    return np.clip(q, -32768, 32767).astype(np.int16), gain


def decode(path):
    """Decode any file with FFmpeg to float32 stereo; raises on decoder errors."""
    r = subprocess.run([str(FFMPEG), "-v", "error", "-i", str(path), "-f", "f32le", "-ac", "2", "-ar", "44100", "-"],
                       capture_output=True)
    if r.returncode != 0 or r.stderr.strip():
        raise RuntimeError(f"decode error: {r.stderr.decode(errors='replace')[:300]}")
    return np.frombuffer(r.stdout, dtype="<f4").reshape(-1, 2)


def compare(orig, dec):
    n = min(len(orig), len(dec))
    res = dec[:n].astype(np.float64) - orig[:n]
    sig = np.sqrt(np.mean(orig[:n].astype(np.float64) ** 2))
    err = np.sqrt(np.mean(res ** 2))
    db = lambda x: round(float(20 * np.log10(x)), 2) if x > 0 else None
    return dict(samples=len(dec), sampleDelta=len(dec) - len(orig), durationSeconds=round(len(dec) / 44100, 3),
                peakDbfs=db(np.max(np.abs(dec))), clippedSamples=int(np.count_nonzero(np.abs(dec) >= 1.0)),
                residualDbfs=db(err), snrDb=round(float(20 * np.log10(sig / err)), 1) if err > 0 else None)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--force", action="store_true"); args = ap.parse_args()
    if not FFMPEG.exists(): raise SystemExit(f"FFmpeg not found at {FFMPEG}")
    rows = []
    for mp3 in sorted(UNITY_MUSIC.glob("*/*.mp3")):
        comp, name = mp3.parent.name, mp3.stem
        sidecar = mp3.with_suffix(".json")
        meta = json.loads(sidecar.read_text(encoding="utf-8"))
        src = ROOT / meta["unityFormat"]["fromMaster"]
        audio, rate, ch, bits, code = read_wav(src)
        row = dict(composer=comp, name=name, source=src.relative_to(ROOT).as_posix(), sourceBytes=src.stat().st_size,
                   sourceFormat=f"{rate} Hz, {ch} ch, {bits}-bit {'float' if code == 3 else 'PCM'}",
                   sourceSamples=len(audio), sourceSha256=sha256(src))
        for fmt, ext in (("WAV16", ".wav"), ("MP3", ".mp3")):
            if fmt == "MP3":  # the Unity asset itself (built by publish.py) — measured, never rewritten here
                lead = meta["unityFormat"]["trimmedLeadSamples"]; n = meta["unityFormat"]["encodedSamples"]
                row[fmt] = dict(format=fmt, path=mp3.relative_to(ROOT).as_posix(), bytes=mp3.stat().st_size, gainApplied=1.0,
                                **compare(audio[lead:lead + n], decode(mp3)[MP3_CODEC_DELAY:]), codecDelaySamples=MP3_CODEC_DELAY)
                continue
            dest = OUT / fmt / comp / f"{name}{ext}"
            dest.parent.mkdir(parents=True, exist_ok=True)
            fresh = dest.exists() and dest.stat().st_mtime >= src.stat().st_mtime and not args.force
            gain = 1.0
            if not fresh:
                if fmt == "WAV16":
                    pcm, gain = to_pcm16(audio, seed=len(audio))
                    write_pcm16(dest, pcm, rate)
            check = compare(audio, decode(dest))
            info = dict(format=fmt, path=dest.relative_to(ROOT).as_posix(), bytes=dest.stat().st_size,
                        gainApplied=round(gain, 6), **check)
            row[fmt] = info
            if meta:
                derived = dict(meta, derivedFormat=dict(
                    format="WAV 44.1 kHz stereo 16-bit PCM (TPDF dither)", fromMaster=row["source"], fromSha256=row["sourceSha256"], gainApplied=round(gain, 6),
                    convertedAt=datetime.date.today().isoformat()))
                (dest.parent / f"{name}.json").write_text(json.dumps(derived, indent=2, ensure_ascii=False), encoding="utf-8")
        rows.append(row)
        print(f"{comp}/{name}: {row['sourceBytes']/1e6:.1f} MB -> WAV16 {row['WAV16']['bytes']/1e6:.1f} MB "
              f"(Δ{row['WAV16']['sampleDelta']} smp, SNR {row['WAV16']['snrDb']} dB) | MP3 {row['MP3']['bytes']/1e6:.1f} MB "
              f"(Δ{row['MP3']['sampleDelta']} smp, SNR {row['MP3']['snrDb']} dB, peak {row['MP3']['peakDbfs']})")

    tot = {k: sum(r[k]["bytes"] if k != "sourceBytes" else r[k] for r in rows) for k in ("sourceBytes", "WAV16", "MP3")}
    report = dict(generatedAt=datetime.datetime.now().isoformat(timespec="seconds"), tracks=rows, totals=tot)
    DOCS.mkdir(parents=True, exist_ok=True)
    (DOCS / "optimized_formats_report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    mb = lambda b: f"{b / 1e6:.1f}"
    lines = ["# Optimized formats report", "", f"Generated {report['generatedAt']}. Originals untouched.", "",
             "| Track | WAV32 master MB | WAV16 archive MB | Unity MP3 MB | WAV16 Δsamples / SNR | MP3 Δsamples / SNR / peak | gain |", "|---|---|---|---|---|---|---|"]
    for r in rows:
        w, m = r["WAV16"], r["MP3"]
        lines.append(f"| {r['composer']}/{r['name']} | {mb(r['sourceBytes'])} | {mb(w['bytes'])} | {mb(m['bytes'])} | "
                     f"{w['sampleDelta']} / {w['snrDb']} dB | {m['sampleDelta']} / {m['snrDb']} dB / {m['peakDbfs']} dBFS | "
                     f"{w['gainApplied']} / {m['gainApplied']} |")
    s = tot["sourceBytes"]
    lines += ["", f"**Totals:** WAV32 {mb(s)} MB · WAV16 {mb(tot['WAV16'])} MB (−{100 * (1 - tot['WAV16'] / s):.1f}%) · "
                  f"MP3 {mb(tot['MP3'])} MB (−{100 * (1 - tot['MP3'] / s):.1f}%)"]
    (DOCS / "optimized_formats_report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n" + lines[-1])


if __name__ == "__main__":
    main()
