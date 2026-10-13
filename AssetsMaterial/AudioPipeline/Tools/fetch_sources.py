"""Stage 1 — exact-source fetch + verification for every target with an approved source.

Approved source = the manifest's own Mutopia data (passes_four_criteria + accepted licence), or an entry of
Sources/extra_sources.json (see common.EXTRA_SOURCES): provider "mutopia" (same checks as below) or "direct"
(a MIDI URL whose licence/attribution were verified on its page; the licence class is re-checked here).
For Mutopia sources:
  - opens EXACTLY mutopia.source_page (no searching),
  - parses composer / title / opus / instrumentation / licence / Mutopia ID / maintainer / links,
  - verifies them against the manifest (mismatch → SOURCE_FAILED with the reason),
  - downloads the linked "MIDI file(s)" (and the LilyPond sources, used only to identify movements),
  - stores the untouched downloads + source.json (SHA-256, URLs, licence, attribution, date).
Idempotent: an existing download with a recorded hash is reused (no re-download).
Usage: python AssetsMaterial/AudioPipeline/Tools/fetch_sources.py [--dry-run] [--only Mozart_1,Bach_3]
"""
import argparse, datetime, html, json, re, shutil, sys, urllib.request
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
    (r"cpdl licen[cs]e|choral public domain library licen[cs]e", "OK_CPDL"),
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


def verify(entry, page, mu):
    """Returns a list of problems (empty = verified)."""
    problems = []
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


def browser_download(url, dest):
    """Downloads through the installed Chrome (Playwright), accepting IMSLP's copyright disclaimer."""
    from playwright.sync_api import sync_playwright
    with sync_playwright() as pw:
        browser = pw.chromium.launch(channel="chrome", headless=True)
        try:
            page = browser.new_context(accept_downloads=True, locale="en-US").new_page()
            downloads = []
            page.on("download", lambda d: downloads.append(d))
            try:
                page.goto(url, wait_until="domcontentloaded")
            except Exception:
                pass                                         # a direct file link aborts the navigation
            agree = page.locator("a", has_text=re.compile(r"I understand|agree", re.I))
            if not downloads and agree.count():
                agree.first.click()
            clicked = False
            for _ in range(90):                              # a file arrives as a download, or (PDF) opens inline
                if downloads or "/files/" in page.url:
                    break
                cont = page.locator("a", has_text=re.compile(r"continue your download", re.I))
                if not clicked and cont.count():             # IMSLP's free-download waiting page, once it has waited
                    href = cont.first.get_attribute("href") or ""
                    if "/files/" in href:                    # the link is the file itself
                        r = page.request.get(href, timeout=600000)
                        if not r.ok: raise RuntimeError(f"HTTP {r.status} for {href}")
                        Path(dest).write_bytes(r.body())
                        return
                    try:
                        cont.first.click(timeout=5000); clicked = True
                    except Exception:
                        pass
                page.wait_for_timeout(1000)
            if downloads:
                downloads[0].save_as(dest)
            elif "/files/" in page.url:
                r = page.request.get(page.url)
                if not r.ok: raise RuntimeError(f"HTTP {r.status} for {page.url}")
                Path(dest).write_bytes(r.body())
            else:
                raise RuntimeError(f"no file after 90 s (page: {page.url})")
        finally:
            browser.close()


def fetch_direct(e, tid, src, rec, today, dry_run):
    """A non-Mutopia source from extra_sources.json: download the MIDI, re-check the licence class."""
    lic = classify_licence(src.get("licence"), src.get("licence_url"))
    if lic not in ACCEPTED_LICENCES or lic != src.get("license_status"):
        raise RuntimeError(f"licence '{src.get('licence')}' -> {lic} (declared {src.get('license_status')})")
    folder = SOURCES.parent / slug(src.get("source_name") or "Other") / composer_key(e["composer"]) / slug(f"{e['catalog']} {e['title']}")[:60]
    url = src["midi_url"]
    name = src.get("file_name") or url.rsplit("/", 1)[-1].split("?")[0] or f"{tid}.mid"
    dest = folder / name
    if dry_run:
        print(f"[{tid}] would download {url}"); return
    folder.mkdir(parents=True, exist_ok=True)
    if src.get("local_file"):                    # a file the user downloaded by hand (musescore.com needs a login)
        folder.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src["local_file"], dest)
    if not dest.exists():
        try:
            data = get(url)
            if data[:2] == b"\x1f\x8b" or data[:15].lower().startswith((b"<!doctype html", b"<html")):
                raise RuntimeError("the site answered with a web page instead of the file")
            dest.write_bytes(data)
        except Exception as ex:   # sites that only serve browsers (IMSLP disclaimer page): real Chrome via Playwright
            try:
                browser_download(url, dest)
            except Exception as ex2:
                raise RuntimeError(f"{ex}; browser: {ex2} -> download it in a browser from {src.get('page_url') or url} "
                                   f"and save it as {dest}") from None
    downloads = [dict(url=url, file=name, sha256=sha256(dest), role="midi")]
    source = dict(
        targetId=tid, composer=e["composer"], canonicalTitle=ascii_fold(e["title"]), catalog=e["catalog"],
        targetMovement=e["target_movement"], targetInstrumentation=e["target_instrumentation"],
        source=src.get("source_name"), mutopiaPageUrl=src.get("page_url"), mutopiaId=src.get("source_id") or src.get("page_url"),
        pageTitle=src.get("page_title"), pageComposer=e["composer"], opus=e["catalog"],
        sourceInstrumentation=src.get("listed_instrumentation"), edition=src.get("edition"),
        licence=src.get("licence"), licenceStatus=lic, licenceUrl=src.get("licence_url"),
        attribution=src.get("attribution"), lastUpdated=None, downloads=downloads, downloadDate=today,
        evidence=src.get("note"))
    (folder / "source.json").write_text(json.dumps(source, indent=2, ensure_ascii=False), encoding="utf-8")
    rec.update(status="SOURCE_OK", folder=str(folder.relative_to(ROOT)), source=source, downloads=downloads)
    print(f"[{tid}] SOURCE_OK  {src.get('source_name')} | {src.get('licence')} | {name}")


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
        mu = effective_source(e)
        if mu is None:
            state.setdefault(tid, {})["status"] = "PENDING_NO_APPROVED_SOURCE"
            summary["pending"] += 1
            continue
        if only and tid not in only: continue

        rec = state.setdefault(tid, {})
        same_source = mu.get("provider") != "direct" or (rec.get("source") or {}).get("mutopiaPageUrl") == mu.get("page_url")
        if rec.get("status") not in (None, "PENDING_NO_APPROVED_SOURCE", "SOURCE_FAILED") and rec.get("source") and same_source:
            summary["reused"] += 1          # already fetched (selected / rendered / done): nothing to download
            continue
        if not same_source:                      # a new source replaces the old one: start the target again
            for k in ("selection", "render", "reason", "source", "downloads"): rec.pop(k, None)
        if mu.get("provider") == "direct":
            try:
                fetch_direct(e, tid, mu, rec, today, args.dry_run)
                summary["downloaded"] += 1
            except Exception as ex:
                rec.update(status="SOURCE_FAILED", reason=f"{type(ex).__name__}: {ex}")
                summary["failed"] += 1
                print(f"[{tid}] SOURCE_FAILED: {rec['reason']}")
            continue
        folder = SOURCES / composer_key(e["composer"]) / slug(f"{e['catalog']} {e['title']}")[:60]
        try:
            page = parse_page(get(mu["source_page"]))
            problems, lic = verify(e, page, mu)
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
