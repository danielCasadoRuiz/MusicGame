"""Stage 4 — copy validated masters into Unity + sidecars + credits + assignment list.

  - Only PASS renders are published (REVIEW_* / failures never reach Unity).
  - Master (32-bit float WAV, kept in Work/) → MusicGame/Assets/_Project/Audio/Music/Composers/<Composer>/<Name>.mp3
    (silence-trimmed 192 kbps CBR, see unity_mp3.py) with <Name>.json sidecar. An existing .mp3 that is not the
    one this script last built is NEVER overwritten (reported as a conflict). A previous <Name>.wav in Unity is
    replaced by the .mp3 keeping its .meta (same GUID → every opponent reference survives).
  - Docs/unity_assignments.json: {opponent displayName → [asset paths]} — which clips belong in each
    opponent's songs[] (every tier); the references are added to the Opponent_*.asset files directly.
  - Docs/composer_music_credits.md / .json: attribution + licence for every processed track.
Usage: python AssetsMaterial/AudioPipeline/Tools/publish.py [--dry-run]
"""
import argparse, json
from common import *
from render_qa import qa
import unity_mp3

LICENCE_NAMES = {"OK_PD": "Public Domain", "OK_CC0": "CC0 1.0", "OK_CC_BY": "CC BY", "OK_CC_BY_SA": "CC BY-SA"}


def credit_entry(e, rec):
    src = rec["source"]
    status = rec["status"]
    lic = src["licenceStatus"]
    d = dict(
        targetId=rec.get("targetId") or src["targetId"], composer=e["composer"], work=e["title"], catalog=e["catalog"],
        movement=e["target_movement"], source="Mutopia Project", sourceUrl=src["mutopiaPageUrl"], mutopiaId=src["mutopiaId"],
        sourceFiles=[d["url"] for d in src["downloads"] if d.get("role") == "midi"],
        sourceLicense=src["licence"], sourceLicenseClass=LICENCE_NAMES.get(lic, lic), sourceLicenseUrl=src.get("licenceUrl"),
        sourceAttribution=f"Typeset/transcribed by {src['attribution']} for the Mutopia Project" if src.get("attribution") else None,
        sourceEdition=src.get("edition"),
        renderedWith=("MuseScore Studio 4.7.5 + Muse Sounds" if rec.get("render", {}).get("profile") == "Muse Sounds" else "MuseScore Studio 4.7.5 (MuseScore Basic)") if rec.get("render") else None,
        modified=True,
        modificationNote="MIDI imported into MuseScore Studio and rendered to audio for MusicGame (no musical changes).",
        status=status,
    )
    fixes = (rec.get("render") or {}).get("instrumentCorrections") or []
    if fixes:
        d["instrumentCorrections"] = fixes
        d["modificationNote"] = ("MIDI imported into MuseScore Studio and rendered to audio for MusicGame. Instrument "
                                 "assignments of mis-imported parts corrected to the edition's own staff names ("
                                 + ", ".join(f"{f['part']} -> {f['corrected']}" for f in fixes) + "); no musical changes.")
    if lic == "OK_CC_BY_SA":
        d["shareAlike"] = (f"This audio is an adaptation of a {src['licence']} work and is distributed under the same "
                           f"licence ({src.get('licenceUrl')}). Attribution: {d['sourceAttribution']}; source {src['mutopiaPageUrl']}.")
    return d


# Variants the user listened to and approved (2026-10-02) for targets that cannot reach PASS with
# the free Muse Sounds library. Each one says exactly how it departs from the plain render.
APPROVED_VARIANTS = {
    "Bach_1": dict(
        wav="Work/Bach_1/PREVIEW_Catedral_RT4.5.wav",
        renderedWith="MuseScore Studio 4.7.5 (MuseScore Basic church organ — no free Muse Sounds pipe organ) + Tools/reverb.py",
        note="Synthetic cathedral reverb added after rendering (RT60 4.5 s, wet -4 dB, predelay 40 ms); no musical changes."),
    "Handel_1": dict(
        wav="Work/Handel_1/PREVIEW_VoicesAsImported_NoChoir.wav",
        renderedWith="MuseScore Studio 4.7.5 + Muse Sounds (11/12 parts; bass oboe on MuseScore Basic)",
        note="Instrumental version: the Soprano/Alto/Tenor/Bass choir lines are played by flute, bass oboe, clarinet and "
             "bassoon (the MIDI import's assignment); trumpets corrected to the edition's 'Trombe in C'. Notes unchanged."),
    "Brahms_2": dict(
        wav="Work/Brahms_2/Brahms_Op49No4_Wiegenlied_CompleteSong.wav",
        renderedWith="MuseScore Studio 4.7.5 + Muse Sounds",
        note="Instrumental version: the voice line is played by the piano together with the original accompaniment. Notes unchanged."),
}


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--dry-run", action="store_true"); args = ap.parse_args()
    manifest = {target_id(e): e for e in load_manifest()["entries"]}
    state = load_state()
    credits, assignments, conflicts, published = [], {}, [], 0
    for tid, rec in state.items():
        if "source" not in rec: continue
        e = manifest[tid]
        c = credit_entry(e, rec)
        variant = APPROVED_VARIANTS.get(tid)
        if variant:
            c.update(status="USER_APPROVED_VARIANT", renderedWith=variant["renderedWith"], variantNote=variant["note"],
                     modificationNote=f"MIDI imported into MuseScore Studio and rendered to audio for MusicGame. {variant['note']}")
        if rec["status"] == "PASS" or variant:
            name = target_filename(e)
            comp = composer_key(e["composer"])
            dest_dir = UNITY_MUSIC / comp
            dest = dest_dir / f"{name}.mp3"
            old_wav = dest_dir / f"{name}.wav"
            src_wav = PIPELINE / variant["wav"] if variant else ROOT / rec["render"]["wav"]
            track_qa = qa(src_wav)[0] if variant else rec["render"]["qa"]
            unity_path = dest.relative_to(UNITY).as_posix()
            built = rec.get("unityMp3") or {}
            stale = built.get("fromMaster") != src_wav.relative_to(ROOT).as_posix() or built.get("masterSha256") != sha256(src_wav)
            if dest.exists() and sha256(dest) != built.get("sha256"):
                conflicts.append(unity_path); c["status"] = "CONFLICT_EXISTING_FILE"
            elif not args.dry_run:
                if old_wav.exists():  # one-time WAV32 → MP3 swap, GUID preserved through the .meta
                    meta = old_wav.with_suffix(".wav.meta")
                    if meta.exists() and not dest.with_suffix(".mp3.meta").exists(): meta.rename(dest.with_suffix(".mp3.meta"))
                    old_wav.unlink()
                if not dest.exists() or stale:
                    built = dict(unity_mp3.build(src_wav, dest), masterSha256=sha256(src_wav))
                    rec["unityMp3"] = built
                sidecar = dict(c, unityAsset=unity_path, durationSeconds=built["durationSeconds"], unityFormat=built,
                               masterQa=track_qa, importedInstruments=rec["render"]["importedInstruments"])
                (dest_dir / f"{name}.json").write_text(json.dumps(sidecar, indent=2, ensure_ascii=False), encoding="utf-8")
                published += 1
            c["unityAsset"] = unity_path
            assignments.setdefault(comp, []).append(unity_path)
        credits.append(c)

    DOCS.mkdir(parents=True, exist_ok=True)
    if not args.dry_run:
        save_state(state)
        (DOCS / "unity_assignments.json").write_text(json.dumps(assignments, indent=2), encoding="utf-8")
        (DOCS / "composer_music_credits.json").write_text(json.dumps(credits, indent=2, ensure_ascii=False), encoding="utf-8")
        lines = ["# Composer music credits", "",
                 "Classical works are in the public domain. The scores/MIDI below come from the Mutopia Project",
                 "(https://www.mutopiaproject.org) and were imported into MuseScore Studio and rendered to audio for",
                 "MusicGame (no musical changes). Licences and attributions are those of each Mutopia source.", ""]
        for c in credits:
            lines += [f"## {c['composer']} — {c['work']} ({c['catalog']}), {c['movement']}",
                      f"- Status: {c['status']}" + (f" — `{c['unityAsset']}`" if c.get("unityAsset") else ""),
                      f"- Source: Mutopia Project, {c['mutopiaId']} — {c['sourceUrl']}",
                      f"- Licence: {c['sourceLicense']}" + (f" ({c['sourceLicenseUrl']})" if c.get("sourceLicenseUrl") else ""),
                      f"- Attribution: {c['sourceAttribution'] or '—'}",
                      f"- Rendered with: {c['renderedWith'] or 'not rendered yet'}; {c['modificationNote']}"]
            if c.get("shareAlike"): lines.append(f"- ShareAlike: {c['shareAlike']}")
            lines.append("")
        (DOCS / "composer_music_credits.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"published {published}, conflicts {len(conflicts)} {conflicts}, credits {len(credits)}")


if __name__ == "__main__":
    main()
