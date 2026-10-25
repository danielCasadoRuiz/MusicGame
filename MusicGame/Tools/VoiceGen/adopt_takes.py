"""
Adopts APPROVED takes as the result of a production job, without generating again (GPU generation is not always
bit-exact, so an approved take could otherwise come out different). For each job item, a mapped approved file (already
processed exactly as production would: post-process + announcer FX) is copied byte for byte to the game path, written
to the master path, and recorded in the results file; then Unity's `-ingest JOB` imports it as usual.

    .venv/Scripts/python adopt_takes.py --job <job.json> --take "<item id>=<approved.wav>" [--take ...]
"""
import argparse, json, os, shutil, sys
import soundfile as sf


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--job", required=True)
    ap.add_argument("--take", action="append", required=True)
    a = ap.parse_args()
    job = json.load(open(a.job, encoding="utf-8-sig"))
    takes = dict(t.split("=", 1) for t in a.take)
    items = {it["id"]: it for it in job["items"]}
    missing = [i for i in items if i not in takes] + [t for t in takes if t not in items]
    if missing:
        sys.exit(f"job items and takes differ: {missing}")
    results = os.path.splitext(a.job)[0] + ".results.jsonl"
    with open(results, "w", encoding="utf-8") as out:
        for n, (iid, it) in enumerate(items.items()):
            src = takes[iid]
            os.makedirs(os.path.dirname(it["game"]), exist_ok=True)
            shutil.copyfile(src, it["game"])                                    # the exact approved file
            y, sr = sf.read(src)
            os.makedirs(os.path.dirname(it["master"]), exist_ok=True)
            sf.write(it["master"], y, sr, subtype="PCM_24")
            out.write(json.dumps({"event": "progress", "done": n, "total": len(items), "id": iid}) + "\n")
            out.write(json.dumps({"event": "result", "id": iid, "ok": True, "seconds": 0, "sample_rate": sr,
                                  "duration": round(len(y) / sr, 3), "lead_silence": 0, "lufs_in": None, "peak": round(float(abs(y).max()), 3),
                                  "warnings": [f"adopted approved take {os.path.basename(src)}"]}) + "\n")
            print(json.dumps({"adopted": iid, "from": src}))
        out.write(json.dumps({"event": "progress", "done": len(items), "total": len(items), "id": ""}) + "\n")
        out.write(json.dumps({"event": "finished"}) + "\n")


if __name__ == "__main__":
    main()
