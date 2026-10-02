"""Compare the game's AudioPreAnalyzer output (dumped by the temporary Unity tool AudioFormatComparison)
across source formats WAV32 / WAV16 / MP3, each imported as Vorbis (production) and PCM.
Reference = WAV32 with the same import mode. Writes Docs/format_analysis_comparison.json/.md.
Usage: python AssetsMaterial/AudioPipeline/Tools/compare_analysis.py
"""
import json
import numpy as np
from common import *

DUMPS = UNITY / "Logs" / "FormatComparison"
TOL = 0.025  # onset match tolerance (s) ≈ 2 analysis hops


def f1(ref, test, tol=TOL):
    ref, test = np.asarray(ref), np.asarray(test)
    if len(ref) == 0 and len(test) == 0: return 1.0
    if len(ref) == 0 or len(test) == 0: return 0.0
    used, hits = np.zeros(len(test), bool), 0
    for t in ref:
        i = np.searchsorted(test, t)
        for j in (i - 1, i):
            if 0 <= j < len(test) and not used[j] and abs(test[j] - t) <= tol:
                used[j] = True; hits += 1; break
    p, r = hits / len(test), hits / len(ref)
    return 2 * p * r / (p + r) if p + r else 0.0


def env(a, b):
    a, b = np.asarray(a, float), np.asarray(b, float)
    n = min(len(a), len(b)); a, b = a[:n], b[:n]
    r = float(np.corrcoef(a, b)[0, 1]) if a.std() > 0 and b.std() > 0 else 1.0
    rel = float(np.mean(np.abs(a - b)) / (np.mean(np.abs(a)) + 1e-12))
    return round(r, 5), round(100 * rel, 3)


def compare(ref, x):
    out = dict(sampleDelta=int(x["samples"] - ref["samples"]), durationDelta=round(x["duration"] - ref["duration"], 4),
               bpm=(ref["bpm"], x["bpm"]), onsets=(len(ref["onsetTimes"]), len(x["onsetTimes"])),
               onsetF1=round(f1(ref["onsetTimes"], x["onsetTimes"]), 4),
               impactF1=round(f1(ref["impactTimes"], x["impactTimes"], 0.05), 4),
               drops=(len(ref["dropTimes"]), len(x["dropTimes"])),
               energy=env(ref["energyEnvelope"], x["energyEnvelope"]), intensity=env(ref["intensity"], x["intensity"]),
               centroid=env(ref["spectralCentroid"], x["spectralCentroid"]),
               bands={f"band{b}": env(ref[f"band{b}"], x[f"band{b}"]) for b in range(6)},
               segmentAgreementPct=round(100 * float(np.mean(np.asarray(ref["segmentLevels"]) == np.asarray(x["segmentLevels"][:len(ref["segmentLevels"])]))), 1),
               key=(ref["estimatedKey"], x["estimatedKey"], ref["isMajor"], x["isMajor"]),
               style=(ref["style"], x["style"]),
               top5TagOverlap=len({t["tag"] for t in ref["tags"][:5]} & {t["tag"] for t in x["tags"][:5]}),
               maxTagScoreDelta=round(max([abs(a["score"] - next((b["score"] for b in x["tags"] if b["tag"] == a["tag"]), 0)) for a in ref["tags"][:5]] or [0]), 4))
    for k in ("danceability", "complexity", "tempoStability", "beatConfidence", "modeConfidence"):
        out[k + "Delta"] = round(x[k] - ref[k], 4)
    return out


def main():
    d = {}
    for f in DUMPS.glob("*__*__*.json"):
        j = json.loads(f.read_text(encoding="utf-8")); d[(j["track"], j["format"], j["import"])] = j
    tracks = sorted({k[0] for k in d})
    res = {}
    for t in tracks:
        for mode in ("Vorbis", "PCM"):
            ref = d[(t, "WAV32", mode)]
            for fmt in ("WAV16", "MP3"):
                if (t, fmt, mode) in d: res[f"{t} {fmt} vs WAV32 ({mode})"] = compare(ref, d[(t, fmt, mode)])
        # How much does Unity's own Vorbis import already change things (today's production path)?
        res[f"{t} WAV32 Vorbis vs WAV32 PCM"] = compare(d[(t, "WAV32", "PCM")], d[(t, "WAV32", "Vorbis")])
    (DOCS / "format_analysis_comparison.json").write_text(json.dumps(res, indent=2), encoding="utf-8")
    lines = ["# Analysis comparison across source formats", "",
             "Game's AudioPreAnalyzer on each version. Envelopes: (Pearson r, mean abs diff % of mean). Onset/impact: F1 vs reference.", "",
             "| Comparison | Δsamples | BPM | onsets | onset F1 | impact F1 | energy | intensity | treble band | segments % | key | style | top-5 tags |",
             "|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for k, v in res.items():
        lines.append(f"| {k} | {v['sampleDelta']} | {v['bpm'][0]:.0f}→{v['bpm'][1]:.0f} | {v['onsets'][0]}→{v['onsets'][1]} | {v['onsetF1']} | {v['impactF1']} | "
                     f"{v['energy'][0]:.4f} / {v['energy'][1]}% | {v['intensity'][0]:.4f} / {v['intensity'][1]}% | "
                     f"{v['bands']['band5'][0]:.4f} / {v['bands']['band5'][1]}% | {v['segmentAgreementPct']} | "
                     f"{'=' if v['key'][0] == v['key'][1] and v['key'][2] == v['key'][3] else 'DIFF'} | {'=' if v['style'][0] == v['style'][1] else v['style']} | {v['top5TagOverlap']}/5 |")
    (DOCS / "format_analysis_comparison.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
