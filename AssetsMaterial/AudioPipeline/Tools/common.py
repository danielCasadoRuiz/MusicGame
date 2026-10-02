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
UNITY_MUSIC = UNITY / "Assets" / "_Project" / "Audio" / "Music" / "Composers"
STATE = PIPELINE / "Sources" / "pipeline_state.json"

ACCEPTED_LICENCES = ("OK_PD", "OK_CC0", "OK_CC_BY", "OK_CC_BY_SA")

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
