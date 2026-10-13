"""Stage 4 — copy validated masters into Unity + sidecars + credits + assignment list.

  - Only PASS renders are published (REVIEW_* / failures never reach Unity).
  - Master (32-bit float WAV, kept in Work/) → MusicGame/Assets/_Project/Audio/Composers/<Composer>/<Name>.mp3
    (silence-trimmed 192 kbps CBR, see unity_mp3.py) with <Name>.json sidecar. An existing .mp3 that is not the
    one this script last built is NEVER overwritten (reported as a conflict). A previous <Name>.wav in Unity is
    replaced by the .mp3 keeping its .meta (same GUID → every opponent reference survives).
  - a new .mp3 gets its .meta (fresh GUID, same import settings as the other composer tracks) and is added to
    every songs[] list (all tiers + default) of its Opponent_<Composer>.asset — only while Unity is closed
    (Unity would overwrite the edited asset); otherwise it is reported and done on the next run.
  - Docs/composer_music_credits.md / .json: attribution + licence for every processed track.
Usage: python AssetsMaterial/AudioPipeline/Tools/publish.py [--dry-run]
"""
import argparse, json, re, subprocess, uuid
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
        movement=e["target_movement"], source=src.get("source") or "Mutopia Project", sourceUrl=src["mutopiaPageUrl"], mutopiaId=src["mutopiaId"],
        sourceFiles=[d["url"] for d in src["downloads"] if d.get("role") == "midi"],
        sourceLicense=src["licence"], sourceLicenseClass=LICENCE_NAMES.get(lic, lic), sourceLicenseUrl=src.get("licenceUrl"),
        sourceAttribution=(f"Typeset/transcribed by {src['attribution']} for the Mutopia Project" if (src.get("source") or "Mutopia Project") == "Mutopia Project"
                           else src["attribution"]) if src.get("attribution") else None,
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


def approved_variant(tid):
    """APPROVED_VARIANTS (decided 2026-10-02) + the user's later decisions in Sources/approvals.json."""
    if tid in APPROVED_VARIANTS:
        return APPROVED_VARIANTS[tid]
    a = load_json_file(APPROVALS, {}).get(tid)
    if a and a.get("approved"):
        return dict(wav=a["wav"], renderedWith=a.get("renderedWith") or "MuseScore Studio 4.7.5", note=a.get("note") or "Approved by the user after listening.")
    return None


def unity_running():
    r = subprocess.run(["powershell", "-NoProfile", "-Command",
                        "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | ForEach-Object { $_.CommandLine }"],
                       capture_output=True, text=True)
    project = str(UNITY).replace("/", "\\").lower()
    return any(project in (line or "").replace("/", "\\").lower() for line in r.stdout.splitlines())


META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 1
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 1
  loadInBackground: 1
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""
FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def ensure_meta(mp3):
    """GUID of the clip; a new clip gets the same import settings as the published composer tracks."""
    folder_meta = mp3.parent.with_suffix(".meta")
    if not folder_meta.exists():
        folder_meta.write_text(FOLDER_META.format(guid=uuid.uuid4().hex), encoding="utf-8")
    meta = mp3.with_suffix(".mp3.meta")
    if not meta.exists():
        meta.write_text(META_TEMPLATE.format(guid=uuid.uuid4().hex), encoding="utf-8")
    return re.search(r"^guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8"), re.M).group(1)


def assign_to_opponent(composer, guid, write=True):
    """Adds the clip to every songs[] list (each tier + defaultConfig) of Opponent_<Composer>.asset.
    Returns None when it is already in all of them, "+N" when lists were (or, with write=False, would be) added."""
    asset = OPPONENTS / f"Opponent_{composer}.asset"
    if not asset.exists():
        return f"no {asset.name}"
    text = asset.read_text(encoding="utf-8")
    ref = f"{{fileID: 8300000, guid: {guid}, type: 3}}"
    out, i, added = [], 0, 0
    lines = text.split("\n")
    while i < len(lines):
        line = lines[i]
        m = re.match(r"^(\s*)songs:(\s*\[\])?\s*$", line)
        if not m:
            out.append(line); i += 1; continue
        indent = m.group(1)
        out.append(f"{indent}songs:")
        i += 1
        items = []
        while i < len(lines) and lines[i].startswith(f"{indent}- "):
            items.append(lines[i]); i += 1
        if not any(guid in it for it in items):
            items.append(f"{indent}- {ref}"); added += 1
        out.extend(items)
    if added and write:
        asset.write_text("\n".join(out), encoding="utf-8")
    return f"{asset.name}: +{added} list(s)" if added else None


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--dry-run", action="store_true"); args = ap.parse_args()
    manifest = {target_id(e): e for e in load_manifest()["entries"]}
    state = load_state()
    credits, conflicts, published, new_clips = [], [], 0, []
    for tid, rec in state.items():
        if "source" not in rec or tid not in manifest: continue
        e = manifest[tid]
        c = credit_entry(e, rec)
        variant = approved_variant(tid)
        if variant:
            c.update(status="USER_APPROVED_VARIANT", renderedWith=variant["renderedWith"], variantNote=variant["note"],
                     modificationNote=f"MIDI imported into MuseScore Studio and rendered to audio for MusicGame. {variant['note']}")
        if rec["status"] in ("PASS", "DONE") or variant:
            name = target_filename(e)
            comp = composer_key(e["composer"])
            dest_dir = UNITY_MUSIC / comp
            dest = dest_dir / f"{name}.mp3"
            src_wav = PIPELINE / variant["wav"] if variant else ROOT / rec["render"]["wav"]
            unity_path = dest.relative_to(UNITY).as_posix()
            built = rec.get("unityMp3") or {}
            sidecar_path = dest_dir / f"{name}.json"
            if dest.exists() and sha256(dest) != built.get("sha256"):
                conflicts.append(unity_path); c["status"] = "CONFLICT_EXISTING_FILE"
            elif not args.dry_run:
                if src_wav.exists():
                    stale = built.get("fromMaster") != src_wav.relative_to(ROOT).as_posix() or built.get("masterSha256") != sha256(src_wav)
                    if not dest.exists() or stale:
                        built = dict(unity_mp3.build(src_wav, dest), masterSha256=sha256(src_wav))
                        rec["unityMp3"] = built
                    track_qa = qa(src_wav)[0] if variant else rec["render"]["qa"]
                elif dest.exists():          # master already cleaned (published + archived): keep the MP3 as it is
                    track_qa = (load_json_file(sidecar_path, {}) or {}).get("masterQa")
                else:
                    print(f"[{tid}] master missing and no MP3: render it again (status reset)")
                    rec["status"] = "SELECTED"; continue
                sidecar = dict(c, unityAsset=unity_path, durationSeconds=built["durationSeconds"], unityFormat=built,
                               masterQa=track_qa, importedInstruments=rec["render"]["importedInstruments"])
                dest_dir.mkdir(parents=True, exist_ok=True)
                sidecar_path.write_text(json.dumps(sidecar, indent=2, ensure_ascii=False), encoding="utf-8")
                rec["unityGuid"] = ensure_meta(dest)
                if not rec.get("assigned"):
                    new_clips.append((tid, comp, rec))
                published += 1
            c["unityAsset"] = unity_path
        credits.append(c)

    if new_clips and not args.dry_run:
        open_unity = unity_running()   # Unity keeps its own copy of the asset and would overwrite an edit
        waiting = []
        for tid, comp, rec in new_clips:
            note = assign_to_opponent(comp, rec["unityGuid"], write=not open_unity)
            if note is None:
                rec["assigned"] = True; print(f"[{tid}] opponent {comp}: already assigned")
            elif "+" in note and not open_unity:
                rec["assigned"] = True; print(f"[{tid}] opponent {comp}: {note}")
            else:
                waiting.append(tid)
        if waiting:
            print(f"Unity is open: {len(waiting)} clip(s) not assigned to their opponent yet ({', '.join(waiting)}). "
                  f"Close Unity and run again.")

    DOCS.mkdir(parents=True, exist_ok=True)
    if not args.dry_run:
        save_state(state)
        (DOCS / "composer_music_credits.json").write_text(json.dumps(credits, indent=2, ensure_ascii=False), encoding="utf-8")
        lines = ["# Composer music credits", "",
                 "Classical works are in the public domain. The scores/MIDI below come from the Mutopia Project",
                 "(https://www.mutopiaproject.org) or the source named in each entry, and were imported into MuseScore",
                 "Studio and rendered to audio for MusicGame. Licences and attributions are those of each source.", ""]
        for c in credits:
            lines += [f"## {c['composer']} — {c['work']} ({c['catalog']}), {c['movement']}",
                      f"- Status: {c['status']}" + (f" — `{c['unityAsset']}`" if c.get("unityAsset") else ""),
                      f"- Source: {c['source']}, {c['mutopiaId']} — {c['sourceUrl']}",
                      f"- Licence: {c['sourceLicense']}" + (f" ({c['sourceLicenseUrl']})" if c.get("sourceLicenseUrl") else ""),
                      f"- Attribution: {c['sourceAttribution'] or '—'}",
                      f"- Rendered with: {c['renderedWith'] or 'not rendered yet'}; {c['modificationNote']}"]
            if c.get("shareAlike"): lines.append(f"- ShareAlike: {c['shareAlike']}")
            lines.append("")
        (DOCS / "composer_music_credits.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"published {published}, conflicts {len(conflicts)} {conflicts}, credits {len(credits)}")


if __name__ == "__main__":
    main()
