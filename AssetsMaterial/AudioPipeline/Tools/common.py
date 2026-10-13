"""Shared paths/helpers for the MusicGame composer-audio pipeline."""
import hashlib, json, re, unicodedata
from pathlib import Path

# Everything of this pipeline lives OUTSIDE the Unity project, in AssetsMaterial/AudioPipeline/.
PIPELINE = Path(__file__).resolve().parents[1]          # AssetsMaterial/AudioPipeline
ROOT = PIPELINE.parents[1]                              # repo root
UNITY = ROOT / "MusicGame"
MANIFEST = ROOT / "AssetsMaterial" / "musicgame_composer_repertoire_v3.json"
SOURCES = PIPELINE / "Sources" / "Mutopia"              # untouched downloads + source.json
WORK = PIPELINE / "Work"                                # .mscz working files, renders before import
DOCS = PIPELINE / "Docs"
# Composer / opponent music. NOT under Audio/Music: everything there is a playable Runner song
# (PlayableSongScope.MusicRoot) — composer tracks are referenced only by OpponentDefinition.songs.
UNITY_MUSIC = UNITY / "Assets" / "_Project" / "Audio" / "Composers"
OPPONENTS = UNITY / "Assets" / "_Project" / "Configs" / "Fight" / "Opponents"
STATE = PIPELINE / "Sources" / "pipeline_state.json"
# Sources found after the manifest's own pass (the manifest is never edited). Per target id:
#   {"provider": "mutopia", "source_page", "mutopia_id", "license_status", "listed_instrumentation", "note"}
#   {"provider": "direct", "page_url", "midi_url", "licence", "licence_url", "license_status",
#    "attribution", "edition", "listed_instrumentation", "source_name", "note"}
# optional "midi_member": the MIDI to use inside a multi-file download.
EXTRA_SOURCES = PIPELINE / "Sources" / "extra_sources.json"
# The user's decisions on REVIEW_* renders: {target: {"approved": true|false, "wav": rel path, "note"}}.
APPROVALS = PIPELINE / "Sources" / "approvals.json"

# OK_CPDL: the Choral Public Domain Library licence (GPL-like copyleft: free use, performance, recording and
# modification, share-alike) — approved by the user on 2026-10-10 alongside the four CC/PD classes.
ACCEPTED_LICENCES = ("OK_PD", "OK_CC0", "OK_CC_BY", "OK_CC_BY_SA", "OK_CPDL")
# Score/performance files MuseScore Studio imports directly (a full score in MusicXML/MSCZ keeps the
# instruments, dynamics and articulations a MIDI often loses).
SCORE_EXTS = (".mid", ".midi", ".musicxml", ".mxl", ".xml", ".mscz")


def is_score_file(name):
    return str(name).lower().endswith(SCORE_EXTS)

# Manifest composer → the project's OpponentDefinition.displayName / folder name.
COMPOSER_KEY = {
    "bach": "Bach", "beethoven": "Beethoven", "brahms": "Brahms", "chopin": "Chopin", "handel": "Handel",
    "haydn": "Haydn", "monteverdi": "Monteverdi", "mozart": "Mozart", "pachelbel": "Pachelbel",
    "tchaikovsky": "Tchaikovsky", "vivaldi": "Vivaldi", "wagner": "Wagner",
}


def ascii_fold(s):
    s = (s or "").replace("–", "-").replace("—", "-")
    return unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()


def norm(s):
    return re.sub(r"[^a-z0-9]+", " ", ascii_fold(s).lower()).strip()


def composer_key(name):
    n = norm(name)
    for k, v in COMPOSER_KEY.items():
        if k in n.split() or n.endswith(k):
            return v
    return re.sub(r"[^A-Za-z]", "", ascii_fold(name).split()[-1])


def slug(s):
    words = re.findall(r"[A-Za-z0-9]+", ascii_fold(s))
    return "".join(w[:1].upper() + w[1:] for w in words)


def target_id(entry):
    return f"{composer_key(entry['composer'])}_{entry['rank']}"


def target_filename(entry):
    """Deterministic Unity filename: Mozart_K525_EineKleineNachtmusik_I_Allegro."""
    cat = slug(entry["catalog"]).replace("Op", "Op").replace("No", "No")
    title = slug(re.sub(r"\(.*?\)", "", entry["title"]).split("-")[0].split("�")[0])
    mov = "_".join(p for p in (slug(x) for x in re.split(r"[.:;,]", entry["target_movement"])) if p)
    return f"{composer_key(entry['composer'])}_{cat}_{title}_{mov}"


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_manifest():
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def load_json_file(path, default):
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else default


def save_json_file(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def effective_source(entry):
    """The approved source of a manifest entry: the manifest's own Mutopia data when it passes the four
    criteria, else an entry of Sources/extra_sources.json (same criteria, verified when fetched), else None."""
    mu = entry.get("mutopia") or {}
    if mu.get("passes_four_criteria") and mu.get("license_status") in ACCEPTED_LICENCES and mu.get("source_page"):
        return dict(mu, provider="mutopia")
    extra = extra_for(target_id(entry))
    if extra and extra.get("license_status") in ACCEPTED_LICENCES:
        return extra
    return None


MANUAL_DIR = PIPELINE / "Sources" / "MuseScore_manual"   # <TargetId>.mscz files the user downloaded from musescore.com


def extra_for(tid):
    """The extra_sources.json entry of a target; when the user has dropped Sources/MuseScore_manual/<tid>.mscz
    and the entry has a "musescore" block (the verified musescore.com score), that block replaces it."""
    extra = load_json_file(EXTRA_SOURCES, {}).get(tid) or {}
    manual = MANUAL_DIR / f"{tid}.mscz"
    if extra.get("musescore") and manual.exists():
        return dict(extra["musescore"], provider="direct", source_name="MuseScore.com", local_file=str(manual),
                    file_name=f"{tid}.mscz", midi_url=extra["musescore"]["page_url"])
    return extra


def load_state():
    return json.loads(STATE.read_text(encoding="utf-8")) if STATE.exists() else {}


def save_state(state):
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(state, indent=2, ensure_ascii=False), encoding="utf-8")


def roman_to_int(s):
    m = re.match(r"^\s*([IVX]+)\.", s or "")
    if not m: return None
    vals = {"I": 1, "V": 5, "X": 10}; total = 0; prev = 0
    for ch in reversed(m.group(1)):
        v = vals[ch]; total = total - v if v < prev else total + v; prev = max(prev, v)
    return total
