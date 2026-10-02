"""Stage 2 — choose the exact MIDI (work + movement) inside each verified Mutopia download.

Identification uses only what the source itself states: the file names and the LilyPond headers
(title / subtitle / piece / opus, and the \\include list of the combined score) — the reviewed decision
for each target is in SELECTION below together with its evidence. LilyPond names the MIDI of the
1st/2nd/3rd \\score of a file "x.mid", "x-1.mid", "x-2.mid", which is how movements are told apart.
The chosen file is copied untouched to Work/<target>/source.mid (the original download stays as-is).
Usage: python AssetsMaterial/AudioPipeline/Tools/select_sources.py
"""
import json, shutil, zipfile
from common import *

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


def main():
    state = load_state()
    done = review = 0
    for tid, rec in state.items():
        if rec.get("status") not in ("SOURCE_OK", "SELECTED"): continue
        folder = ROOT / rec["folder"]
        if tid not in SELECTION:
            rec.update(status="REVIEW_MOVEMENT", reason="no reviewed selection for this source"); review += 1
            print(f"[{tid}] REVIEW_MOVEMENT"); continue
        archive, member, evidence = SELECTION[tid]
        src = folder / archive
        dest = WORK / tid / "source.mid"
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
        sel = dict(archive=archive, member=member, evidence=evidence, workMidi=str(dest.relative_to(ROOT)), sha256=sha256(dest))
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
