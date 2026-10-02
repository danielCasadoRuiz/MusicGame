"""Inventory every audio asset in the Unity project and compare against the composer repertoire manifest.

Writes Docs/Audio/existing_audio_inventory.json + .md. Read-only for Unity assets.
Usage: python AssetsMaterial/AudioPipeline/Tools/inventory.py
"""
import datetime, json, os, re, struct, unicodedata
from pathlib import Path
from common import target_filename

ROOT = Path(__file__).resolve().parents[3]            # repo root (this file: AssetsMaterial/AudioPipeline/Tools)
UNITY = ROOT / "MusicGame"
ASSETS = UNITY / "Assets"
MANIFEST = ROOT / "AssetsMaterial" / "musicgame_composer_repertoire_v3.json"
OUT_DIR = ROOT / "AssetsMaterial" / "AudioPipeline" / "Docs"
AUDIO_EXT = {".wav", ".ogg", ".mp3", ".aif", ".aiff", ".flac", ".m4a"}
REF_EXT = {".asset", ".unity", ".prefab", ".controller", ".overrideController", ".playable", ".mat"}


def wav_info(path):
    """Duration / rate / channels from the RIFF header (PCM and IEEE float)."""
    try:
        with open(path, "rb") as f:
            if f.read(4) != b"RIFF": return None
            f.read(4)
            if f.read(4) != b"WAVE": return None
            fmt = None; data_size = None
            while True:
                hdr = f.read(8)
                if len(hdr) < 8: break
                cid, size = hdr[:4], struct.unpack("<I", hdr[4:])[0]
                if cid == b"fmt ":
                    raw = f.read(size)
                    audio_fmt, ch, rate, byte_rate, align, bits = struct.unpack("<HHIIHH", raw[:16])
                    fmt = dict(format=audio_fmt, channels=ch, sampleRate=rate, byteRate=byte_rate, bits=bits)
                elif cid == b"data":
                    data_size = size
                    break
                else:
                    f.seek(size + (size & 1), 1)
            if fmt and data_size is not None and fmt["byteRate"]:
                return dict(durationSeconds=round(data_size / fmt["byteRate"], 2), sampleRate=fmt["sampleRate"],
                            channels=fmt["channels"], bitsPerSample=fmt["bits"])
    except OSError:
        pass
    return None


def guid_of(path):
    meta = Path(str(path) + ".meta")
    if not meta.exists(): return None
    m = re.search(r"guid:\s*([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="ignore"))
    return m.group(1) if m else None


def norm(s):
    s = unicodedata.normalize("NFKD", s or "").encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", " ", s.lower()).strip()


def main():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    entries = manifest["entries"]
    audio = sorted(p for p in ASSETS.rglob("*") if p.suffix.lower() in AUDIO_EXT)

    # Index every text asset once for GUID references.
    ref_files = [p for p in ASSETS.rglob("*") if p.suffix in REF_EXT]
    texts = {}
    for p in ref_files:
        try: texts[p] = p.read_text(encoding="utf-8", errors="ignore")
        except OSError: pass

    composer_words = {e["composer"]: norm(e["composer"]).split()[-1] for e in entries}
    by_filename = {target_filename(e): e for e in entries}  # files published by this pipeline
    records = []
    for p in audio:
        rel = p.relative_to(UNITY).as_posix()
        guid = guid_of(p)
        refs = sorted(q.relative_to(UNITY).as_posix() for q, t in texts.items() if guid and guid in t)
        info = wav_info(p) if p.suffix.lower() == ".wav" else None
        stem = p.stem
        artist, title = (re.split(r"\s+[—-]\s+", stem, maxsplit=1) + [""])[:2] if re.search(r"\s[—-]\s", stem) else ("", stem)
        n = norm(stem)
        hits = [e for e in entries if composer_words[e["composer"]] in n.split()
                and any(w in n for w in norm(e["title"]).split() if len(w) > 4)]
        match = by_filename.get(stem) or (hits[0] if hits else None)
        records.append(dict(
            unityPath=rel, filename=p.name, extension=p.suffix.lower(), guid=guid,
            **(info or {"durationSeconds": None, "sampleRate": None, "channels": None}),
            referencedBy=refs,
            likelyArtistOrComposer=artist.strip() or None,
            likelyWork=title.strip() or stem,
            likelyMovement=None,
            category="Runner song (non-classical, original/library track)" if not match else "Composer repertoire candidate",
            confidence="high" if not match else "medium",
            matchesCanonicalTarget=(f"{match['composer']} #{match['rank']} {match['title']}" if match else None),
        ))

    existing_targets = {r["matchesCanonicalTarget"] for r in records if r["matchesCanonicalTarget"]}
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    (OUT_DIR / "existing_audio_inventory.json").write_text(json.dumps(dict(
        generated="2026-10-01", unityProject=str(UNITY), audioAssetCount=len(records),
        canonicalTargetsAlreadyPresent=sorted(existing_targets), assets=records), indent=2, ensure_ascii=False), encoding="utf-8")

    lines = ["# Existing audio inventory", "", f"{len(records)} audio assets found under `Assets/` (scanned {datetime.date.today().isoformat()}).", "",
             f"Canonical repertoire targets already present: **{len(existing_targets)}**" + (f" — {', '.join(sorted(existing_targets))}" if existing_targets else ""), "",
             "| Unity path | Duration | Rate / ch | Referenced by | Likely artist / work | Matches target |",
             "|---|---|---|---|---|---|"]
    for r in records:
        refs = "<br>".join(Path(x).name for x in r["referencedBy"]) or "—"
        rate = f"{r['sampleRate']} Hz / {r['channels']}" if r.get("sampleRate") else "—"
        dur = f"{r['durationSeconds']:.1f} s" if r.get("durationSeconds") else "—"
        lines.append(f"| `{r['unityPath']}` | {dur} | {rate} | {refs} | {r['likelyArtistOrComposer'] or '?'} — {r['likelyWork']} | {r['matchesCanonicalTarget'] or 'no'} |")
    (OUT_DIR / "existing_audio_inventory.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{len(records)} audio assets; {len(existing_targets)} canonical targets already present")
    for r in records:
        print(f"  {r['filename']:<55} {r.get('durationSeconds')}s {r.get('sampleRate')}Hz ch{r.get('channels')} refs={len(r['referencedBy'])} match={r['matchesCanonicalTarget']}")


if __name__ == "__main__":
    main()
