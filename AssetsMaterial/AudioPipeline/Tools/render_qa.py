"""Stage 3 — MuseScore Studio import + render + audio QA (CLI only, no UI automation).

For each SELECTED target:
  1. MIDI → Work/<target>/<name>.mscz  (MuseScore CLI import),
  2. sets the score's sound profile to "Muse Sounds" (audiosettings.json inside the .mscz) when the
     Muse Sampler is installed — otherwise it stays "MuseScore Basic" and the track is REVIEW_MUSESOUNDS.
     Proof of use comes from MuseScore's own session log: every part must be rendered by the Muse Sampler,
     else REVIEW_MUSESOUNDS (fallbackTracks = parts left on MuseScore Basic),
  3. compares the imported instruments with the expected scoring (missing section → REVIEW_BAD_INSTRUMENTATION),
  4. renders the WAV master (CLI) and QAs it: readable, duration, not silent, peak / clipping, rate / channels,
     RMS level. A conservative output ceiling (-1 dBFS, pure gain, no compression) is applied only if needed,
  5. writes <name>.json sidecar beside the master.
Idempotent: skips a target whose source MIDI hash + profile match the last successful render (--force to redo).
Usage: python AssetsMaterial/AudioPipeline/Tools/render_qa.py [--only Mozart_1,...] [--force] [--dry-run]
"""
import argparse, datetime, json, os, re, struct, subprocess, zipfile
import numpy as np
from common import *

MUSESCORE = Path(r"C:\Program Files\MuseScore 4\bin\MuseScore4.exe")
CEILING_DBFS = -1.0

# Expected scoring families → MuseScore instrument-id fragments that satisfy them.
FAMILIES = {
    "violin": ["violin"], "viola": ["viola"], "cello": ["violoncello", "cello"], "bass": ["contrabass", "double-bass"],
    "flute": ["flute"], "oboe": ["oboe"], "clarinet": ["clarinet"], "bassoon": ["bassoon"], "horn": ["horn"],
    "trumpet": ["trumpet"], "timpani": ["timpani"], "piano": ["piano"], "organ": ["organ"],
    "voice": ["voice", "soprano", "alto", "tenor", "baritone", "bass"], "choir": ["soprano", "alto", "tenor", "bass", "voice", "choir"],
}
WORDS = {"violins": "violin", "violin": "violin", "viola": "viola", "cello": "cello", "violoncello": "cello", "bass": "bass",
         "flutes": "flute", "oboes": "oboe", "clarinets": "clarinet", "bassoons": "bassoon", "horns": "horn",
         "trumpet": "trumpet", "trumpets": "trumpet", "timpani": "timpani", "piano": "piano", "organ": "organ",
         "voice": "voice", "satb": "choir", "strings": "violin"}


VOCAL = {"voice", "choir"}


def family_match(family, instrument_id, fragment):
    # Voices must match the whole id (so "bassoon"/"contrabass" never count as a bass voice).
    return instrument_id == fragment if family in VOCAL else fragment in instrument_id


# Parts the MIDI import mis-identifies, corrected from the EDITION's own staff names (LilyPond
# \set Staff.instrument / instrumentName in the Mutopia .ly files). Keyed by the imported part's
# trackName (= the MIDI track name LilyPond wrote). Only the instrument identity changes — notes,
# pitches (concert) and dynamics are untouched.
HORN    = ("horn", "brass.french-horn", "Horn", "Hn.", 60)
TRUMPET = ("c-trumpet", "brass.trumpet", "Trumpet", "Tpt.", 56)
VOICES  = {v: (v, f"voice.{v}", v.title(), v[0].upper() + ".", 52) for v in ("soprano", "alto", "tenor", "bass")}
INSTRUMENT_FIXES = {
    # Symphony 7 .ly: "Clarinetti in A", "Corni in E", "Trombe in D" — the edition gave horns AND
    # trumpets midiInstrument "english horn" (parts five/six) and the import read the clarinets as piccolo.
    # Muse Woodwinds has no A clarinet → the Bb clarinet sample (same instrument; concert pitches unchanged).
    # "Violoncello II e Basso" → the orchestral contrabass SECTION (Muse Strings ships no solo contrabass).
    "Beethoven_5": {"Piccolo Clarinet, three": ("bb-clarinet", "wind.reed.clarinet.bflat", "Clarinet", "Cl.", 71),
                    "English Horn, five": HORN, "Baroque Oboe, six": TRUMPET,
                    "Contrabass, twelve": ("contrabasses", "strings.contrabass", "Contrabasses", "Cb.", 43)},
    # Messiah .ly: "Trombe in C" + choir staves "Soprano/Alto/Tenore/Basso" (no midiInstrument set,
    # so the import guessed winds for the voices).
    "Handel_1": {"Cornet, Trumpets:one": TRUMPET, "Flute, :Sop": VOICES["soprano"], "Bass Oboe, :Alto": VOICES["alto"],
                 "Clarinet, :Tenor": VOICES["tenor"], "Bassoon, :Bass": VOICES["bass"]},
    # Magic Flute No. 14: the page lists a Viola and the MIDI zip ships viola.mid, but the score MIDI's
    # viola track has no instrument set, so the import made it a piano (the aria has no keyboard).
    "Mozart_5": {"Piano": ("viola", "strings.viola", "Viola", "Va.", 41)},
}


def fix_instruments(mscz, fixes):
    """Rewrite the identity of mis-imported parts inside the .mscz; returns what changed."""
    if not fixes: return []
    with zipfile.ZipFile(mscz) as z:
        x = next(n for n in z.namelist() if n.endswith(".mscx"))
        t = z.read(x).decode("utf-8")
    done = []
    def repl(m):
        part = m.group(0)
        track = re.search(r"<trackName>(.*?)</trackName>", part).group(1)
        if track not in fixes: return part
        iid, sound, long_, short, prog = fixes[track]
        old = re.search(r'<Instrument id="([^"]+)"', part).group(1)
        part = re.sub(r'<Instrument id="[^"]+"', f'<Instrument id="{iid}"', part)
        part = re.sub(r"<instrumentId>[^<]*</instrumentId>", f"<instrumentId>{sound}</instrumentId>", part)
        part = re.sub(r"<trackName>[^<]*</trackName>", f"<trackName>{long_}</trackName>", part)
        part = re.sub(r"<longName>[^<]*</longName>", f"<longName>{long_}</longName>", part)
        part = re.sub(r"<shortName>[^<]*</shortName>", f"<shortName>{short}</shortName>", part)
        part = re.sub(r'<program value="\d+"/>', f'<program value="{prog}"/>', part, count=1)
        done.append(dict(part=track, imported=old, corrected=iid, sound=sound))
        return part
    t = re.sub(r"<Part[ >].*?</Part>", repl, t, flags=re.S)
    missing = set(fixes) - {d["part"] for d in done}
    if missing: raise RuntimeError(f"instrument fix: parts not found {sorted(missing)}")
    tmp = mscz.with_suffix(".tmp")
    with zipfile.ZipFile(mscz) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            zout.writestr(item, t.encode("utf-8") if item.filename == x else zin.read(item.filename))
    tmp.replace(mscz)
    return done


# Sounds the free Muse Sounds packs lack → the closest free Muse instrument (same register, concert
# pitches untouched; only the part's sound identity changes). Voices go to winds: the user prefers that
# to Muse Choir. Keyed by the imported part's instrumentId; "*" in the key = prefix match.
SOUND_SUBSTITUTES_VERSION = 3
SOUND_SUBSTITUTES = {
    "brass.natural-horn":       HORN,
    "wind.reed.clarinet.a":     ("bb-clarinet", "wind.reed.clarinet.bflat", "Clarinet", "Cl.", 71),
    "strings.contrabass":       ("contrabasses", "strings.contrabass", "Contrabasses", "Cb.", 43),
    "voice.vocals":             ("flute", "wind.flutes.flute", "Flute (voice)", "Fl.", 73),
    "voice.soprano":            ("flute", "wind.flutes.flute", "Flute (soprano)", "Fl.", 73),
    "voice.mezzo-soprano":      ("oboe", "wind.reed.oboe", "Oboe (mezzo)", "Ob.", 68),
    "voice.alto":               ("english-horn", "wind.reed.english-horn", "English Horn (alto)", "E.H.", 69),
    "voice.tenor":              ("bb-clarinet", "wind.reed.clarinet.bflat", "Clarinet (tenor)", "Cl.", 71),
    "voice.baritone":           ("bassoon", "wind.reed.bassoon", "Bassoon (baritone)", "Bsn.", 70),
    "voice.bass":               ("bassoon", "wind.reed.bassoon", "Bassoon (bass)", "Bsn.", 70),
    "voice.choir*":             ("flute", "wind.flutes.flute", "Flute (choir)", "Fl.", 73),
    "brass.euphonium":          HORN,           # Wagner tubas: horn players' instrument, horn-like tone
    "brass.trumpet.bass":       ("trombone", "brass.trombone", "Trombone (bass trumpet)", "Tbn.", 57),
    "id:soprano-trombone":      TRUMPET,        # cornet/soprano brass line
    "id:alto-trombone":         ("trombone", "brass.trombone", "Trombone (alto)", "Tbn.", 57),
    "id:tenor-trombone":        ("trombone", "brass.trombone", "Trombone (tenor)", "Tbn.", 57),
    "strings.viol.violone":     ("contrabasses", "strings.contrabass", "Contrabasses (violone)", "Cb.", 43),
}
# No free Muse instrument comes close: these stay on MuseScore Basic (a real sampled harp / pipe organ)
# and do not make a render REVIEW_MUSESOUNDS.
BASIC_ALLOWED = ("pluck.harp", "keyboard.organ")
ORGAN = ("organ", "keyboard.organ", "Organ", "Org.", 19)
# extra_sources.json "part_instruments": {"<text in the part name>": "<key>"} — for sources whose MIDI
# programs / OMR part names do not say the real instrument.
NAMED_INSTRUMENTS = {
    "violin": ("violin", "strings.violin", "Violin", "Vln.", 40), "viola": ("viola", "strings.viola", "Viola", "Va.", 41),
    "cello": ("violoncello", "strings.cello", "Violoncello", "Vc.", 42),
    "contrabass": ("contrabasses", "strings.contrabass", "Contrabasses", "Cb.", 43),
    "flute": ("flute", "wind.flutes.flute", "Flute", "Fl.", 73), "oboe": ("oboe", "wind.reed.oboe", "Oboe", "Ob.", 68),
    "clarinet": ("bb-clarinet", "wind.reed.clarinet.bflat", "Clarinet", "Cl.", 71),
    "bassoon": ("bassoon", "wind.reed.bassoon", "Bassoon", "Bsn.", 70), "horn": HORN, "trumpet": TRUMPET,
    "trombone": ("trombone", "brass.trombone", "Trombone", "Tbn.", 57), "tuba": ("tuba", "brass.tuba", "Tuba", "Tba.", 58),
    "timpani": ("timpani", "drum.timpani", "Timpani", "Timp.", 47), "harp": ("harp", "pluck.harp", "Harp", "Hp.", 46),
    "organ": ORGAN, "harpsichord": ("harpsichord", "keyboard.harpsichord", "Harpsichord", "Hch.", 6),
    "piano": ("piano", "keyboard.piano", "Piano", "Pno.", 0),
    **VOICES,
}


def substitute_sounds(mscz, force=None):
    """Applies SOUND_SUBSTITUTES to every matching part of the .mscz (or `force` = one instrument for
    every part, e.g. an organ piece whose MIDI uses a different program per stop); returns what changed."""
    with zipfile.ZipFile(mscz) as z:
        x = next(n for n in z.namelist() if n.endswith(".mscx"))
        t = z.read(x).decode("utf-8")
    done = []
    def target(sound, iid):
        for k, v in SOUND_SUBSTITUTES.items():
            if k.startswith("id:"):
                if iid == k[3:]: return v
            elif sound == k or (k.endswith("*") and sound.startswith(k[:-1])):
                return v
    def repl(m):
        part = m.group(0)
        sound = (re.search(r"<instrumentId>([^<]*)</instrumentId>", part) or [None, ""])[1]
        iid_old = re.search(r'<Instrument id="([^"]+)"', part).group(1)
        new = force or target(sound, iid_old)
        if not new or (new[0] == iid_old and new[1] == sound): return part
        iid, snd, long_, short, prog = new
        part = re.sub(r'<Instrument id="[^"]+"', f'<Instrument id="{iid}"', part)
        part = re.sub(r"<instrumentId>[^<]*</instrumentId>", f"<instrumentId>{snd}</instrumentId>", part)
        part = re.sub(r"<longName>[^<]*</longName>", f"<longName>{long_}</longName>", part)
        part = re.sub(r"<shortName>[^<]*</shortName>", f"<shortName>{short}</shortName>", part)
        part = re.sub(r'<program value="\d+"/>', f'<program value="{prog}"/>', part, count=1)
        done.append(dict(imported=iid_old, sound=sound, substitute=iid))
        return part
    t = re.sub(r"<Part[ >].*?</Part>", repl, t, flags=re.S)
    if done:
        tmp = mscz.with_suffix(".tmp")
        with zipfile.ZipFile(mscz) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
            for item in zin.infolist():
                zout.writestr(item, t.encode("utf-8") if item.filename == x else zin.read(item.filename))
        tmp.replace(mscz)
    return done


# OMR part names are OCR of the score's staff labels (any language, abbreviated, often misread):
# first match wins; anything unrecognised keeps MuseScore's own guess.
OMR_NAME_PATTERNS = [
    (r"picc|kl.*fl", "flute"), (r"^f[l1i]|fl[oöa]t|flûte", "flute"), (r"^ob|^oh|^oh\.|hautb|hoboe", "oboe"),
    (r"^cl|^kl|clar", "clarinet"), (r"^b[s5][s5n1]|^fag|bass?oon|basson", "bassoon"),
    (r"^h[no1]|^h[r1][n.]|^cor|horn|hörn|^cr", "horn"), (r"^tp|^tr[pu]|^tr\.|tromb[ae]\b|tromp|trump", "trumpet"),
    (r"^tb|^th[un]|^pos|trombon|posaun", "trombone"), (r"tuba", "tuba"), (r"^ti|^pk|pauk|timp", "timpani"),
    (r"harp|^hp|harfe|arpa", "harp"), (r"^vla|^br|viola|bratsch", "viola"),
    (r"^v[lnc1i]*\.?\s*[i1l]{1,2}\b|^vn|^vl|viol(?!a|on?c)|violin|geig", "violin"),
    (r"^vc|^vlc|cell|^vcl", "cello"), (r"^cb|^kb|^db|contrab|kontrab|^bass[io]?\.?$", "contrabass"),
    (r"^s\.|sopr", "soprano"), (r"^a\.|^alto|^alt\b", "alto"), (r"^t\.|tenor", "tenor"),
    (r"^b\.|basso\b|^bas\b", "bass"),
]


def omr_part_mapping(mscz):
    """part_instruments-style {track name: key} from the OMR'd staff labels."""
    with zipfile.ZipFile(mscz) as z:
        t = z.read(next(n for n in z.namelist() if n.endswith(".mscx"))).decode("utf-8")
    mapping = {}
    for name in set(re.findall(r"<trackName>(.*?)</trackName>", t)):
        label = name.split(",", 1)[-1].strip().lower()     # MuseScore prefixes its own guess: "Voice, Oboe I."
        for pat, key in OMR_NAME_PATTERNS:
            if re.search(pat, label):
                mapping[name] = key; break
    return mapping


def assign_named_instruments(mscz, mapping):
    """extra_sources.json part_instruments: {"<text in the part name>": "<key>"} (first match wins), or a list
    of keys in part order for sources whose parts are all called the same (e.g. "MusicXML Part")."""
    if not mapping: return []
    with zipfile.ZipFile(mscz) as z:
        x = next(n for n in z.namelist() if n.endswith(".mscx"))
        t = z.read(x).decode("utf-8")
    done, index = [], iter(range(10 ** 6))
    def repl(m):
        part, i = m.group(0), next(index)
        name = (re.search(r"<trackName>(.*?)</trackName>", part) or [None, ""])[1]
        if isinstance(mapping, list):
            key = mapping[i] if i < len(mapping) else None
        else:
            key = next((k for text, k in mapping.items() if text.lower() in name.lower()), None)
        if not key: return part
        iid, sound, long_, short, prog = NAMED_INSTRUMENTS[key]
        old = re.search(r'<Instrument id="([^"]+)"', part).group(1)
        part = re.sub(r'<Instrument id="[^"]+"', f'<Instrument id="{iid}"', part)
        part = re.sub(r"<instrumentId>[^<]*</instrumentId>", f"<instrumentId>{sound}</instrumentId>", part)
        part = re.sub(r"<trackName>[^<]*</trackName>", f"<trackName>{long_}</trackName>", part)
        part = re.sub(r"<longName>[^<]*</longName>", f"<longName>{long_}</longName>", part)
        part = re.sub(r"<shortName>[^<]*</shortName>", f"<shortName>{short}</shortName>", part)
        part = re.sub(r'<program value="\d+"/>', f'<program value="{prog}"/>', part, count=1)
        done.append(dict(part=name, imported=old, corrected=iid, sound=sound))
        return part
    t = re.sub(r"<Part[ >].*?</Part>", repl, t, flags=re.S)
    tmp = mscz.with_suffix(".tmp")
    with zipfile.ZipFile(mscz) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            zout.writestr(item, t.encode("utf-8") if item.filename == x else zin.read(item.filename))
    tmp.replace(mscz)
    return done


def cut_audio_at_measures(mscz, wav, measures, work):
    """Keeps only bars a..b of a rendered score: MuseScore's measure-position export (.mpos: the start time
    of every bar in ms) gives the exact cut points; 20 ms fade-in, 1.5 s fade-out (unless b is the last bar)."""
    first, last = (int(x) for x in str(measures).split("-"))
    mpos = work / "measures.mpos"
    if run(["-o", str(mpos), str(mscz)], work / "mpos.log") != 0 or not mpos.exists():
        raise RuntimeError("measure positions export failed (see mpos.log)")
    starts = [int(p) for _, p in sorted((int(i), p) for i, p in re.findall(r'<event elid="(\d+)" position="(\d+)"', mpos.read_text(encoding="utf-8")))]
    mpos.unlink()
    audio, rate = read_wav(wav)[:2]
    a = starts[first - 1] * rate // 1000
    b = starts[last] * rate // 1000 if last < len(starts) else len(audio)
    clip = audio[a:b].copy()
    fade_in, fade_out = int(0.02 * rate), (int(1.5 * rate) if last < len(starts) else 0)
    clip[:fade_in] *= np.linspace(0, 1, fade_in)[:, None]
    if fade_out:
        clip[-fade_out:] *= np.linspace(1, 0, fade_out)[:, None]
    write_float_wav(wav, clip, rate)
    return dict(measures=f"{first}-{last}", fromSec=round(a / rate, 2), toSec=round(b / rate, 2))


def mscz_basic_allowed(mscz):
    """Parts expected to be rendered by MuseScore Basic: a BASIC_ALLOWED sound, or a part with no notes at
    all (e.g. the percussion of a symphony excerpt in which it does not play)."""
    with zipfile.ZipFile(mscz) as z:
        t = z.read(next(n for n in z.namelist() if n.endswith(".mscx"))).decode("utf-8")
    parts = re.findall(r"<Part[ >].*?</Part>", t, flags=re.S)
    music = t[t.rfind("</Part>"):]
    has_notes = {sid for sid, body in re.findall(r'<Staff id="(\d+)">(.*?)</Staff>', music, flags=re.S) if "<Chord>" in body}
    count = 0
    for p in parts:
        sound = (re.search(r"<instrumentId>([^<]*)</instrumentId>", p) or [None, ""])[1]
        staves = set(re.findall(r'<Staff id="(\d+)"', p))
        if sound.startswith(BASIC_ALLOWED) or not (staves & has_notes):
            count += 1
    return count


def muse_sounds_installed():
    lib = Path(os.environ.get("LOCALAPPDATA", "")) / "MuseSampler" / "lib"
    return lib.exists() and any(lib.glob("*.dll"))


def run(args, log):
    r = subprocess.run([str(MUSESCORE)] + args, capture_output=True, text=True, timeout=1800)
    log.write_text((r.stdout or "") + (r.stderr or ""), encoding="utf-8")
    if r.returncode != 0:  # the work folder is cleaned later: show the tool's own output on the console now
        tail = ((r.stdout or "") + (r.stderr or "")).strip().splitlines()[-25:]
        print("    MuseScore:\n      " + "\n      ".join(tail))
    return r.returncode


def expected_families(text):
    fam = []
    for w in norm(text).split():
        f = WORDS.get(w)
        if f and f not in fam: fam.append(f)
    return fam


def mscz_instruments(mscz):
    with zipfile.ZipFile(mscz) as z:
        x = next(n for n in z.namelist() if n.endswith(".mscx"))
        t = z.read(x).decode("utf-8", "replace")
    return re.findall(r'<Instrument id="([^"]+)"', t)


def set_profile(mscz, profile):
    """Rewrite audiosettings.json inside the .mscz (file-based, deterministic)."""
    tmp = mscz.with_suffix(".tmp")
    with zipfile.ZipFile(mscz) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == "audiosettings.json":
                js = json.loads(data.decode("utf-8")); js["activeSoundProfile"] = profile
                data = json.dumps(js, indent=4).encode("utf-8")
            zout.writestr(item, data)
    tmp.replace(mscz)


def mscz_part_count(mscz):
    with zipfile.ZipFile(mscz) as z:
        x = next(n for n in z.namelist() if n.endswith(".mscx"))
        return len(re.findall(r"<Part[ >]", z.read(x).decode("utf-8", "replace")))


LOGS = Path(os.environ.get("LOCALAPPDATA", "")) / "MuseScore" / "MuseScore4" / "logs"


def render_engine_evidence(wav, since):
    """MuseScore's own session log for this render: how many tracks the Muse Sampler rendered offline
    (one 'Start offline mode' per Muse Sounds track; MuseScore Basic/FluidSynth tracks log none)."""
    target = "out: " + wav.resolve().as_posix()
    for log in sorted(LOGS.glob("MuseScore_*.log"), key=lambda f: f.stat().st_mtime, reverse=True):
        if log.stat().st_mtime < since: break
        t = log.read_text(encoding="utf-8", errors="replace")
        if target in t:
            return dict(log=log.name, museSamplerTracks=t.count("MuseSamplerWrapper::setOutputSpec | Start offline mode"),
                        museSamplerVersion=(re.search(r"MuseSampler successfully inited: .*?version: ([\d.]+)", t) or [None, None])[1],
                        synthErrors=re.findall(r"SynthResolver::resolveSynth \| (.*)", t),
                        saved="Successfully saved sound track" in t)
    return dict(log=None, museSamplerTracks=0, museSamplerVersion=None, synthErrors=[], saved=False)


def read_wav(path):
    with open(path, "rb") as f:
        assert f.read(4) == b"RIFF"; f.read(4); assert f.read(4) == b"WAVE"
        fmt = None
        while True:
            h = f.read(8)
            if len(h) < 8: raise ValueError("no data chunk")
            cid, size = h[:4], struct.unpack("<I", h[4:])[0]
            if cid == b"fmt ":
                raw = f.read(size); fmt = struct.unpack("<HHIIHH", raw[:16])
            elif cid == b"data":
                buf = f.read(size); break
            else:
                f.seek(size + (size & 1), 1)
    code, ch, rate, _, _, bits = fmt
    if code == 3 and bits == 32: a = np.frombuffer(buf, dtype="<f4")
    elif code == 1 and bits == 16: a = np.frombuffer(buf, dtype="<i2").astype(np.float32) / 32768.0
    elif code == 1 and bits == 24:
        b = np.frombuffer(buf, dtype=np.uint8).reshape(-1, 3)
        a = ((b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16)) << 8 >> 8).astype(np.float32) / 8388608.0
    else: raise ValueError(f"unsupported WAV format {code}/{bits}")
    return a.reshape(-1, ch), rate, ch, bits, code


def write_float_wav(path, data, rate):
    data = np.ascontiguousarray(data, dtype="<f4")
    ch = data.shape[1]
    with open(path, "wb") as f:
        size = data.nbytes
        f.write(b"RIFF" + struct.pack("<I", 36 + size) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 3, ch, rate, rate * ch * 4, ch * 4, 32))
        f.write(b"data" + struct.pack("<I", size)); f.write(data.tobytes())


def qa(path):
    a, rate, ch, bits, code = read_wav(path)
    bad = int(a.size - np.count_nonzero(np.isfinite(a)))
    if bad:                                   # the Muse Sampler occasionally emits NaN/inf samples
        a = np.nan_to_num(a, nan=0.0, posinf=0.0, neginf=0.0)
    dur = a.shape[0] / rate
    peak = float(np.max(np.abs(a))) if a.size else 0.0
    rms = float(np.sqrt(np.mean(a.astype(np.float64) ** 2))) if a.size else 0.0
    db = lambda x: round(20 * np.log10(x), 2) if x > 0 else None
    clipped = int(np.count_nonzero(np.abs(a) >= 0.999))
    return dict(durationSeconds=round(dur, 2), sampleRate=rate, channels=ch, bitsPerSample=bits,
                sampleFormat="float" if code == 3 else "pcm", peakDbfs=db(peak), rmsDbfs=db(rms),
                clippedSamples=clipped, nonFiniteSamples=bad, silent=peak < 1e-4), a, rate


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    only = {x.strip() for x in args.only.split(",") if x.strip()}
    manifest = {target_id(e): e for e in load_manifest()["entries"]}
    state = load_state()
    muse = muse_sounds_installed()
    profile = "Muse Sounds" if muse else "MuseScore Basic"
    print(f"MuseScore: {MUSESCORE} | Muse Sampler installed: {muse} → profile '{profile}'")
    counts = {}
    for tid, rec in state.items():
        if rec.get("status") not in ("SELECTED", "PASS", "REVIEW_MUSESOUNDS", "RENDER_FAILED", "REVIEW_BAD_INSTRUMENTATION", "REVIEW_OMR"): continue
        if only and tid not in only: continue
        e = manifest[tid]
        sel = rec["selection"]
        name = target_filename(e)
        work = WORK / tid
        mid = ROOT / sel["workMidi"]
        prev = rec.get("render") or {}
        fixes_now = [dict(part=k, corrected=v[0], sound=v[1]) for k, v in (INSTRUMENT_FIXES.get(tid) or {}).items()]
        fixes_prev = [dict(part=c["part"], corrected=c["corrected"], sound=c["sound"]) for c in prev.get("instrumentCorrections") or []]
        if not args.force and prev.get("midiSha256") == sel["sha256"] and prev.get("profile") == profile                 and fixes_now == fixes_prev and (work / f"{name}.wav").exists()                 and (rec["status"] == "PASS" or prev.get("substitutesVersion") == SOUND_SUBSTITUTES_VERSION):
            print(f"[{tid}] up to date ({rec['status']})"); counts[rec["status"]] = counts.get(rec["status"], 0) + 1; continue
        if args.dry_run: print(f"[{tid}] would render {name}"); continue
        try:
            mscz = work / f"{name}.mscz"
            if run(["-o", str(mscz), str(mid)], work / "import.log") != 0 or not mscz.exists():
                raise RuntimeError("MIDI import failed (see import.log)")
            corrections = fix_instruments(mscz, INSTRUMENT_FIXES.get(tid))
            extra = extra_for(tid)
            mapping = extra.get("part_instruments")
            if mapping is None and extra.get("omr") is not None:
                mapping = omr_part_mapping(mscz)       # exact track names, so only those parts change
            named = assign_named_instruments(mscz, mapping)
            instruments = mscz_instruments(mscz)        # the scoring as written (before sound substitutes)
            force = {"organ": ORGAN}.get(extra.get("force_instrument"))
            substitutes = substitute_sounds(mscz, force) if muse or force else []
            basic_parts = mscz_basic_allowed(mscz)
            set_profile(mscz, profile)
            expected = expected_families(extra.get("listed_instrumentation") or e["mutopia"].get("listed_instrumentation")
                                        or e["target_instrumentation"])
            missing = [f for f in expected if not any(family_match(f, i, k) for i in instruments for k in FAMILIES[f])]
            wav = work / f"{name}.wav"
            for attempt in range(2):                  # NaN blocks from the Muse Sampler are not repeatable: retry once
                started = datetime.datetime.now().timestamp() - 2
                if run(["-o", str(wav), str(mscz)], work / "render.log") != 0 or not wav.exists():
                    raise RuntimeError("render failed (see render.log)")
                report, audio, rate = qa(wav)
                if not report["nonFiniteSamples"]: break
                print(f"[{tid}] {report['nonFiniteSamples']} NaN samples in the render" + (", rendering again" if not attempt else ""))
            gain_db = 0.0
            if report["nonFiniteSamples"]:            # write the zeroed samples back before anything else
                write_float_wav(wav, audio, rate)
            cut = None
            if extra.get("measures") and mid.suffix.lower() == ".mscz":   # excerpt of a MuseScore score
                cut = cut_audio_at_measures(mscz, wav, extra["measures"], work)
                report, audio, rate = qa(wav)
            if report["peakDbfs"] is not None and report["peakDbfs"] > CEILING_DBFS:
                gain_db = CEILING_DBFS - report["peakDbfs"]
                write_float_wav(wav, audio * (10 ** (gain_db / 20)), rate)
                report, _, _ = qa(wav)
            report["ceilingGainDb"] = round(gain_db, 2)
            parts = mscz_part_count(mscz)
            engine = render_engine_evidence(wav, started)
            fallback = max(0, parts - engine["museSamplerTracks"])
            fallback_unexpected = max(0, fallback - basic_parts)
            if report["silent"] or report["durationSeconds"] <= 0: status = "RENDER_FAILED"
            elif missing: status = "REVIEW_BAD_INSTRUMENTATION"
            elif not muse or fallback_unexpected or (not engine["museSamplerTracks"] and not basic_parts): status = "REVIEW_MUSESOUNDS"
            elif extra.get("omr") is not None: status = "REVIEW_OMR"   # optical recognition: always listened to first
            else: status = "PASS"
            rec.update(status=status, render=dict(
                midiSha256=sel["sha256"], profile=profile, parts=parts, instrumentCorrections=corrections + named, soundSubstitutes=substitutes,
                substitutesVersion=SOUND_SUBSTITUTES_VERSION, engineEvidence=engine, excerpt=cut,
                museSoundsInstalled=muse, fallbackTracks=fallback, basicAllowedParts=basic_parts, importedInstruments=instruments,
                expectedFamilies=expected, missingFamilies=missing, mscz=str(mscz.relative_to(ROOT)),
                wav=str(wav.relative_to(ROOT)), qa=report, renderedAt=datetime.datetime.now().isoformat(timespec="seconds"),
                museScore="MuseScore Studio 4.7.5"))
            print(f"[{tid}] {status:<26} {report['durationSeconds']:>7.1f}s peak {report['peakDbfs']} dBFS rms {report['rmsDbfs']} | "
                  f"instr {sorted(set(instruments))} | missing {missing} | MuseSampler {engine['museSamplerTracks']}/{parts} parts")
        except Exception as ex:
            rec.update(status="RENDER_FAILED", reason=f"{type(ex).__name__}: {ex}")
            print(f"[{tid}] RENDER_FAILED {ex}")
            for lg in ("import.log", "render.log"):
                f = work / lg
                if f.exists() and f.stat().st_size:
                    print(f"    {lg}:\n      " + "\n      ".join(f.read_text(encoding="utf-8", errors="replace").strip().splitlines()[-15:]))
        counts[rec["status"]] = counts.get(rec["status"], 0) + 1
        save_state(state)
    save_state(state)
    print("summary:", counts)


if __name__ == "__main__":
    main()
