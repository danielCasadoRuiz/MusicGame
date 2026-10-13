"""Stage 2 — choose the exact MIDI (work + movement) inside each verified Mutopia download.

Identification uses only what the source itself states: the file names and the LilyPond headers
(title / subtitle / piece / opus, and the \\include list of the combined score) — the reviewed decision
for each target is in SELECTION below together with its evidence. LilyPond names the MIDI of the
1st/2nd/3rd \\score of a file "x.mid", "x-1.mid", "x-2.mid", which is how movements are told apart.
The chosen file is copied untouched to Work/<target>/source.mid (the original download stays as-is).
Usage: python AssetsMaterial/AudioPipeline/Tools/select_sources.py
"""
import io, json, os, re, shutil, subprocess, zipfile
import excerpt
from common import *


LILYPOND = Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft/WinGet/Packages/LilyPond.LilyPond_Microsoft.Winget.Source_8wekyb3d8bbwe/lilypond-2.24.4/bin/lilypond.exe"


def scale_musicxml_tempo(path, factor):
    """Multiplies every <sound tempo="..."> of a MusicXML/MXL file (a source whose tempo mark was written
    for the wrong beat unit, e.g. quarter = 116 where the edition means eighth = 116)."""
    fix = lambda s: re.sub(r'(<sound[^>]*tempo=")([\d.]+)', lambda m: f"{m.group(1)}{float(m.group(2)) * factor:g}", s)
    if path.suffix.lower() != ".mxl":
        path.write_text(fix(path.read_text(encoding="utf-8")), encoding="utf-8"); return
    buf = io.BytesIO()
    with zipfile.ZipFile(path) as src, zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as out:
        for item in src.infolist():
            data = src.read(item)
            if item.filename.lower().endswith((".xml", ".musicxml")) and not item.filename.startswith("META-INF"):
                data = fix(data.decode("utf-8")).encode("utf-8")
            out.writestr(item, data)
    path.write_bytes(buf.getvalue())

# target → (archive or file, member, evidence). Rule: full/combined score > parts; exact movement.
SELECTION = {
    "Bach_1":      ("ToccataFugue.mid", None, "single-work page; ToccataFugue.ly title 'Toccata and Fugue in D Minor', subtitle 'BWV 565'"),
    "Bach_3":      ("bach-air-mids.zip", "bach-air-score.mid", "full score MIDI (others are single parts); notes.ly: title 'Air', opus 'BWV 1068, Mvmt. 2'"),
    "Beethoven_3": ("moonlight-mids.zip", "moonlight1.mid", "moonlight1/2/3 = the three movements; target I. Adagio sostenuto = moonlight1"),
    "Beethoven_4": ("fur_Elise_WoO59.mid", None, "single-work page; ly title 'Für Elise', subtitle 'WoO 59'"),
    "Beethoven_5": ("Symphony7_2.mid", None, "page covers only the 2nd movement; ly subtitle 'Allegretto'; full orchestral score"),
    "Brahms_2":    ("Wiegenlied-mids.zip", "Wiegenlied.mid", "Wiegenlied.ly = voice staff + piano (original); Wiegenlied_piano_SATB is an SATB arrangement — not used"),
    "Chopin_2":    ("chopin_fantaisie-impromptu.mid", None, "single-work page; ly title 'Fantaisie-Impromptu', opus 'Op. 66'"),
    "Handel_1":    ("hallelujah-mids.zip", "combined-score.mid", "combined-score.ly includes oboe, bassoon, trumpet ×2, timpani, strings, SATB = original scoring; "
                                                                   "Clarinet/Trombone MIDIs are separate add-on parts, NOT in the combined score"),
    "Haydn_3":     ("op76-n3-mids.zip", "score-1.mid", "page = Op. 76 No. 3 (lys defs: 'String Quartet in C major (Emperor)'); score/-1/-2/-3 = movements I–IV; target II = score-1"),
    "Mozart_1":    ("MozartWA-KV525-mids.zip", "MozartWA-KV525-mov-01.mid", "mov-01..04 = lys folders 01_allegro / 02_andante / 03_allegretto / 04_allegro; target I. Allegro = mov-01"),
    "Mozart_3":    ("KV331_3_RondoAllaTurca.mid", None, "page covers only the 3rd movement; ly title 'Rondo Alla Turca'"),
    "Pachelbel_1": ("Canon_per_3_Violini_e_Basso-mids.zip", "canon_per_3_violini_e_basso.mid", "full score MIDI (others are single parts); ly title 'Canon per 3 Violini e Basso'"),
    "Vivaldi_1":   ("spring-mids.zip", "spring-score.mid", "spring-score.ly pieces in order: Allegro, Largo, Danza Pastorale; 1st \\score = spring-score.mid"),
    "Vivaldi_3":   ("autumn-mids.zip", "autumn-score.mid", "autumn-score.ly pieces: Allegro, Adagio Molto, Allegro; 1st \\score = autumn-score.mid"),
    "Vivaldi_4":   ("winter-mids.zip", "winter-score.mid", "winter-score.ly pieces: Allegro non Molto, Largo, Allegro; 1st \\score = winter-score.mid"),
}


def prepare_score(tid, dest, extra):
    """Optional steps from extra_sources.json, in order:
      "omr": {"sheets": "2-6", "movement": 1}   a PDF score read by Audiveris -> Work/<id>/source.mxl
                                               (+ source.omr, the Audiveris project, to fix it by hand)
      "measures": "93-205"                     only those measures (MIDI/MSCZ are converted to MusicXML first)
    Returns the file MuseScore will import and notes for the evidence trail."""
    work, notes = dest.parent, []
    ly = extra.get("lilypond")                   # {"score": "094-score.ly", "piece": "II"}: LilyPond engraving source
    if ly and dest.suffix.lower() == ".zip":
        src_dir = work / "ly"
        shutil.rmtree(src_dir, ignore_errors=True)
        with zipfile.ZipFile(dest) as z: z.extractall(src_dir)
        text = (src_dir / ly["score"]).read_text(encoding="utf-8")
        start = text.index("\\keepWithTag", text.index(f'piece = "{ly["piece"]}"'))
        body = text[start:text.index("\n    }\n", start)]
        head = text[:text.index("\\book")]       # \version, N = .., \include of the edition
        (src_dir / "midi_build.ly").write_text(head + "\\score {\n" + body + "\n  \\midi { }\n}\n", encoding="utf-8")
        subprocess.run([str(LILYPOND), "-o", "midi_build", "midi_build.ly"], cwd=src_dir,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=1800)
        built = sorted(src_dir.glob("*.mid*"), key=lambda p: p.stat().st_mtime)
        if not built: raise RuntimeError("LilyPond produced no MIDI")
        out = work / "source.mid"
        shutil.copyfile(built[-1], out)
        shutil.rmtree(src_dir, ignore_errors=True)
        dest.unlink(); dest = out
        notes.append(f"MIDI built with LilyPond from {ly['score']}, piece {ly['piece']}")
    if dest.suffix.lower() == ".mscz":           # user-made scores: bars MuseScore's batch mode refuses to open
        fixed = excerpt.repair_mscz(dest, work)
        if fixed: notes.append(f"repaired bars (filled with rests): {', '.join(fixed)}")
    omr_opts = extra.get("omr")
    if omr_opts is not None:
        if dest.suffix.lower() != ".pdf":
            raise RuntimeError(f"'omr' needs a PDF source, got {dest.name}")
        marker, prev_mxl = work / "source.omr.json", work / "source.mxl"
        key = {"pdf_sha256": sha256(dest), "sheets": omr_opts.get("sheets"), "movement": omr_opts.get("movement", 1)}
        if prev_mxl.exists() and marker.exists() and load_json_file(marker, {}) == key:
            dest.unlink()                          # same PDF + pages: reuse the OMR result (it takes long)
            dest = prev_mxl
            notes.append("OMR (Audiveris) result reused")
            omr_opts = dict(omr_opts, _reused=True)
    if omr_opts is not None and not omr_opts.get("_reused"):
        tmp = work / "omr_tmp"
        shutil.rmtree(tmp, ignore_errors=True)
        (work / "omr.log").unlink(missing_ok=True)
        found = excerpt.omr(dest, tmp, omr_opts.get("sheets"), log=work / "omr.log")
        movement = int(omr_opts.get("movement", 1))
        chosen = found[min(movement, len(found)) - 1]
        out = work / f"source{chosen.suffix.lower()}"
        shutil.copyfile(chosen, out)
        for book in tmp.glob("*.omr"):
            shutil.copyfile(book, work / "source.omr")
        shutil.rmtree(tmp, ignore_errors=True)
        dest.unlink()                              # the PDF itself stays in Sources/
        dest = out
        notes.append(f"OMR (Audiveris) of sheets {omr_opts.get('sheets') or 'all'}, movement {movement} of {len(found)}")
        save_json_file(marker, key)
    if extra.get("measures") and dest.suffix.lower() == ".mscz":
        notes.append(f"measures {extra['measures']}: the audio is cut at those bars after rendering (render_qa)")
    elif extra.get("measures"):
        first, last = (int(x) for x in str(extra["measures"]).split("-"))
        src = dest
        if dest.suffix.lower() in (".mid", ".midi", ".mscz"):
            src = work / "full.musicxml"
            excerpt.to_musicxml(dest, src, work / "convert.log")
        out = work / "source.musicxml"
        n = excerpt.cut_measures(src, out, first, last)
        for f in {dest, src} - {out}:
            f.unlink(missing_ok=True)
        dest = out
        notes.append(f"measures {first}-{last} ({n} bars) cut by excerpt.py")
    if omr_opts is not None:                     # MuseScore refuses OMR scores with inexact bars: play it via MIDI
        mid = work / "source.mid"
        parts, quarters = excerpt.score_to_midi(dest, mid, omr_opts.get("tempo"))
        dest = mid
        notes.append(f"OMR score -> MIDI with music21 ({parts} parts, {quarters:.0f} quarter notes"
                     + (f", tempo {omr_opts['tempo']} per quarter)" if omr_opts.get("tempo") else ")"))
    return dest, notes


def automatic_choice(tid, rec):
    """(archive, member, evidence) without a reviewed SELECTION entry, or None when it is ambiguous:
    the extra source's explicit "midi_member", or the only MIDI the source provides."""
    extra = extra_for(tid)
    member = extra.get("midi_member")
    if member:
        archive, _, inner = member.partition("::")
        return archive, inner or None, f"extra_sources.json midi_member: {extra.get('note', '')}".strip()
    midis = [d["file"] for d in rec.get("downloads", []) if d.get("role") == "midi"]
    if len(midis) != 1:
        return None
    folder = ROOT / rec["folder"]
    name = midis[0]
    if is_score_file(name) or name.lower().endswith(".pdf") or (name.lower().endswith(".zip") and extra.get("lilypond")):
        return name, None, "the source provides a single score/MIDI/PDF file"
    if name.lower().endswith(".zip"):
        with zipfile.ZipFile(folder / name) as z:
            inner = [n for n in z.namelist() if is_score_file(n)]
        if len(inner) == 1:
            return name, inner[0], "the source's archive holds a single score/MIDI file"
    return None


def main():
    state = load_state()
    done = review = 0
    for tid, rec in state.items():
        if rec.get("status") not in ("SOURCE_OK", "SELECTED", "REVIEW_MOVEMENT"): continue
        folder = ROOT / rec["folder"]
        choice = SELECTION.get(tid) or automatic_choice(tid, rec)
        if choice is None:
            rec.update(status="REVIEW_MOVEMENT", reason="several MIDI files and no selection: add 'midi_member' "
                                                        "(archive::file) for this target in Sources/extra_sources.json"); review += 1
            print(f"[{tid}] REVIEW_MOVEMENT — {rec['reason']}"); continue
        archive, member, evidence = choice
        extra = extra_for(tid)
        options = {k: extra[k] for k in ("omr", "measures", "musicxml_tempo_scale", "lilypond") if k in extra}
        prev = rec.get("selection") or {}
        if options and prev.get("options") == options and prev.get("archive") == archive \
                and (ROOT / prev.get("workMidi", "-")).exists():
            print(f"[{tid}] up to date ({Path(prev['workMidi']).name})"); continue   # OMR / excerpt already made
        src = folder / archive
        ext = Path(member or archive).suffix.lower()
        dest = WORK / tid / f"source{'.mid' if ext == '.midi' else ext}"   # MuseScore imports it as it is
        dest.parent.mkdir(parents=True, exist_ok=True)
        try:
            if member:
                with zipfile.ZipFile(src) as z:
                    dest.write_bytes(z.read(member))
            else:
                shutil.copyfile(src, dest)
        except (KeyError, FileNotFoundError) as ex:
            rec.update(status="SOURCE_FAILED", reason=f"selected file missing: {ex}"); review += 1
            print(f"[{tid}] SOURCE_FAILED {ex}"); continue
        try:
            dest, notes = prepare_score(tid, dest, extra)
        except Exception as ex:
            rec.update(status="SOURCE_FAILED", reason=f"{type(ex).__name__}: {ex}"); review += 1
            print(f"[{tid}] SOURCE_FAILED {rec['reason']}"); continue
        if notes: evidence += " | " + "; ".join(notes)
        factor = extra.get("musicxml_tempo_scale")
        if factor and dest.suffix.lower() in (".mxl", ".musicxml", ".xml"):
            scale_musicxml_tempo(dest, factor)
            evidence += f" | tempo x{factor} (extra_sources.json musicxml_tempo_scale)"
        sel = dict(archive=archive, member=member, evidence=evidence, workMidi=str(dest.relative_to(ROOT)), sha256=sha256(dest),
                   options=options)
        rec.update(status="SELECTED", selection=sel)
        # keep the source sidecar complete
        sj = folder / "source.json"
        data = json.loads(sj.read_text(encoding="utf-8")); data["selection"] = sel
        sj.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
        done += 1
        print(f"[{tid}] SELECTED {member or archive}")
    save_state(state)
    print(f"selected {done}, needs review {review}")


if __name__ == "__main__":
    main()
