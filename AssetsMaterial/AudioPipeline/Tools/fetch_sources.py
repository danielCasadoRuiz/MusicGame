"""Stage 1 — exact-source fetch + verification for eligible manifest targets (Mutopia only).

For every entry with mutopia.passes_four_criteria == true and an accepted licence status:
  - opens EXACTLY mutopia.source_page (no searching),
  - parses composer / title / opus / instrumentation / licence / Mutopia ID / maintainer / links,
  - verifies them against the manifest (mismatch → SOURCE_FAILED with the reason),
  - downloads the linked "MIDI file(s)" (and the LilyPond sources, used only to identify movements),
  - stores the untouched downloads + source.json (SHA-256, URLs, licence, attribution, date).
Idempotent: an existing download with a recorded hash is reused (no re-download).
Usage: python AssetsMaterial/AudioPipeline/Tools/fetch_sources.py [--dry-run] [--only Mozart_1,Bach_3]
"""
import argparse, datetime, html, json, re, sys, urllib.request
from pathlib import Path
from common import *

UA = {"User-Agent": "MusicGame-audio-pipeline/1.0 (contact: project maintainer)"}


def get(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def field(lines, label):
    for i, l in enumerate(lines):
        if l.rstrip(":").strip().lower() == label.lower() and i + 1 < len(lines):
            return lines[i + 1]
    return None


def parse_page(raw):
    s = raw.decode("utf-8", errors="replace")
    t = re.sub(r"<script.*?</script>", "", s, flags=re.S)
    t = html.unescape(re.sub(r"<[^>]+>", "\n", t))
    lines = [l.strip() for l in t.split("\n") if l.strip()]
    links = [(html.unescape(m.group(1)), html.unescape(m.group(2)).strip())
             for m in re.finditer(r'href="([^"]+)"[^>]*>([^<]*)<', s)]
    title_line = lines[0] if lines else ""
    m = re.match(r"(.*?),\s*by\s*(.*?)\s*\(", title_line)
    licence_url = None
    cm = re.search(r"Copyright:.*?<a[^>]+href=\"([^\"]+)\"", s, flags=re.S)
    if cm: licence_url = html.unescape(cm.group(1))
    lic_links = [h for h, _ in links if "creativecommons.org" in h]
    if lic_links: licence_url = lic_links[0] if not licence_url or licence_url.startswith("..") else licence_url
    return dict(
        pageTitle=m.group(1) if m else title_line, pageComposer=m.group(2) if m else None,
        instrumentation=field(lines, "Instrument(s)"), opus=field(lines, "Opus"), style=field(lines, "Style"),
        edition=field(lines, "Source"), licence=field(lines, "Copyright"), licenceUrl=licence_url,
        mutopiaId=field(lines, "Music ID Number"), maintainer=field(lines, "Maintainer"),
        lastUpdated=field(lines, "Last updated"),
        midiLinks=[h for h, txt in links if re.match(r"MIDI file", txt, re.I)],
        lilypondLinks=[h for h, txt in links if re.match(r"LilyPond file", txt, re.I)],
    )


LICENCE_MAP = [
    (r"public domain", "OK_PD"), (r"cc0|publicdomain/zero", "OK_CC0"),
    (r"attribution[- ]sharealike|by-sa", "OK_CC_BY_SA"),
    (r"attribution[- ]noncommercial|by-nc", "REJECT_NC"), (r"noderiv|by-nd", "REJECT_ND"),
    (r"creative commons attribution|/by/", "OK_CC_BY"),
]


def classify_licence(text, url):
    t = f"{text or ''} {url or ''}".lower()
    if "noncommercial" in t or "by-nc" in t: return "REJECT_NC"
    if "noderiv" in t or "by-nd" in t: return "REJECT_ND"
    for pat, status in LICENCE_MAP:
        if re.search(pat, t): return status
    return "UNVERIFIED"


KEYBOARD = {"piano", "organ", "harpsichord", "guitar", "pianoforte"}


def instrument_kind(text):
    """'solo-keyboard' (piano/organ/harpsichord/guitar alone), 'voice+keyboard', or 'ensemble'."""
    words = set(norm(text).split())
    if not words: return "unknown"
    if words & {"orchestra", "strings", "string", "violin", "violins", "quartet", "choir", "satb", "ensemble",
                "cello", "viola", "oboes", "trumpets", "continuo", "flutes"}:
        return "ensemble"
    if words & {"voice", "soprano", "song"} and words & KEYBOARD: return "voice+keyboard"
    if words & KEYBOARD: return "solo-keyboard"
    return "unknown"


def verify(entry, page):
    """Returns a list of problems (empty = verified)."""
    problems = []
    mu = entry["mutopia"]
    if composer_key(page["pageComposer"] or "") != composer_key(entry["composer"]):
        problems.append(f"composer '{page['pageComposer']}' ≠ '{entry['composer']}'")
    if page["mutopiaId"] != mu.get("mutopia_id"):
        problems.append(f"Mutopia ID '{page['mutopiaId']}' ≠ manifest '{mu.get('mutopia_id')}'")
    lic = classify_licence(page["licence"], page["licenceUrl"])
    if lic not in ACCEPTED_LICENCES:
        problems.append(f"licence '{page['licence']}' → {lic}")
    elif lic != mu.get("license_status"):
        problems.append(f"licence class {lic} ≠ manifest {mu.get('license_status')}")
    # Instrumentation: the page's own listing must be compatible with the TARGET instrumentation —
    # an ensemble/orchestral target never accepts a keyboard/guitar-only source (a reduction).
    src_kind, tgt_kind = instrument_kind(page["instrumentation"]), instrument_kind(entry["target_instrumentation"])
    if tgt_kind == "ensemble" and src_kind == "solo-keyboard":
        problems.append(f"REVIEW_BAD_INSTRUMENTATION: target '{entry['target_instrumentation']}' but source is '{page['instrumentation']}'")
    if tgt_kind == "solo-keyboard" and src_kind != "solo-keyboard":
        problems.append(f"instrumentation '{page['instrumentation']}' is not the solo keyboard target '{entry['target_instrumentation']}'")
    if not page["midiLinks"]:
        problems.append("no MIDI link on the page")
    return problems, lic


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    only = {x.strip() for x in args.only.split(",") if x.strip()}

    manifest = load_manifest()
    state = load_state()
    today = datetime.date.today().isoformat()
    summary = {"downloaded": 0, "reused": 0, "failed": 0, "pending": 0}

    for e in manifest["entries"]:
        tid = target_id(e)
        mu = e["mutopia"]
        if not (mu.get("passes_four_criteria") and mu.get("license_status") in ACCEPTED_LICENCES and mu.get("source_page")):
            state.setdefault(tid, {})["status"] = state.get(tid, {}).get("status") if state.get(tid, {}).get("status") == "EXISTING_REVIEW" else "PENDING_NO_APPROVED_SOURCE"
            summary["pending"] += 1
            continue
        if only and tid not in only: continue

        rec = state.setdefault(tid, {})
        folder = SOURCES / composer_key(e["composer"]) / slug(f"{e['catalog']} {e['title']}")[:60]
        try:
            page = parse_page(get(mu["source_page"]))
            problems, lic = verify(e, page)
            if problems:
                rec.update(status="SOURCE_FAILED", reason="; ".join(problems))
                summary["failed"] += 1
                print(f"[{tid}] SOURCE_FAILED: {rec['reason']}")
                continue
            downloads = []
            for url in page["midiLinks"] + page["lilypondLinks"]:
                name = url.rsplit("/", 1)[-1]
                dest = folder / name
                prev = next((d for d in rec.get("downloads", []) if d["url"] == url), None)
                if dest.exists() and prev and prev.get("sha256") == sha256(dest):
                    downloads.append(prev); summary["reused"] += 1
                    continue
                if args.dry_run:
                    print(f"[{tid}] would download {url}"); continue
                folder.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(get(url))
                downloads.append(dict(url=url, file=name, sha256=sha256(dest), role="midi" if url in page["midiLinks"] else "lilypond-source"))
                summary["downloaded"] += 1
            source = dict(
                targetId=tid, composer=e["composer"], canonicalTitle=ascii_fold(e["title"]), catalog=e["catalog"],
                targetMovement=e["target_movement"], targetInstrumentation=e["target_instrumentation"],
                source="Mutopia Project", mutopiaPageUrl=mu["source_page"], mutopiaId=page["mutopiaId"],
                pageTitle=page["pageTitle"], pageComposer=page["pageComposer"], opus=page["opus"],
                sourceInstrumentation=page["instrumentation"], edition=page["edition"],
                licence=page["licence"], licenceStatus=lic, licenceUrl=page["licenceUrl"],
                attribution=page["maintainer"], lastUpdated=page["lastUpdated"],
                downloads=downloads, downloadDate=rec.get("source", {}).get("downloadDate") or today,
            )
            if not args.dry_run:
                (folder / "source.json").write_text(json.dumps(source, indent=2, ensure_ascii=False), encoding="utf-8")
            rec.update(status="SOURCE_OK", folder=str(folder.relative_to(ROOT)), source=source, downloads=downloads)
            print(f"[{tid}] SOURCE_OK  {page['pageTitle']} | {page['opus']} | {page['instrumentation']} | {page['licence']} | {len(downloads)} file(s)")
        except Exception as ex:  # one failed target never aborts the batch
            rec.update(status="SOURCE_FAILED", reason=f"{type(ex).__name__}: {ex}")
            summary["failed"] += 1
            print(f"[{tid}] SOURCE_FAILED: {rec['reason']}")

    if not args.dry_run: save_state(state)
    print("summary:", summary)


if __name__ == "__main__":
    main()
