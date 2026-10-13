"""Publish additions only. Existing deliverables are never replaced or removed."""
from __future__ import annotations

import hashlib
import os
import re
from pathlib import Path

LOD_TARGETS = [{'label': label, 'quads': quads} for label, quads in
               [('ULTRA', 50000), ('HIGH', 30000), ('MIDHIGH', 20000),
                ('MEDIUM', 10000), ('LOW', 5000)]]


def _normal_name(name):
    return re.sub(r'[^a-z0-9]', '', name.casefold())


def model_lod_rank(path: Path):
    stem = _normal_name(path.stem)
    if stem.endswith('rigged'):
        stem = stem[:-6]
    for rank, target in enumerate(LOD_TARGETS):
        labels = [target['label']]
        if rank == 2:
            labels += ['HIGHMID', 'MEDIUM']
        for label in labels:
            if any(stem.endswith(_normal_name(label + token)) for token in
                   (str(target['quads']), f"{target['quads']//1000}k")):
                return rank
    folders = {'ultra': 0, 'high': 1, 'midhigh': 2, 'highmid': 2,
               'medium': 3, 'low': 4}
    return folders.get(_normal_name(path.parent.name), 999)


def lod_files(directory: Path, target, extension='.glb'):
    """Recognise quality by suffix or LOD folder, not by the asset's prefix.

    Archived RAWs and rigged deliverables never count as retopo models.
    """
    label, quads = target['label'], target['quads']
    labels = [label]
    if label == 'MIDHIGH':
        labels += ['MID-HIGH', 'HIGH-MID', 'HIGHMID']
    tokens = [str(quads), f'{quads // 1000}k'] if quads % 1000 == 0 else [str(quads)]
    suffixes = [_normal_name(l + t) for l in labels for t in tokens]
    if label == 'MIDHIGH' and quads == 20000:
        suffixes += [_normal_name('MEDIUM' + t) for t in tokens]
    folders = {_normal_name(l) for l in labels} | set(suffixes)
    found = []
    if not directory.is_dir():
        return found
    for path in directory.rglob('*'):
        if not path.is_file() or path.suffix.casefold() != extension.casefold():
            continue
        relative = path.relative_to(directory)
        parents = relative.parts[:-1]
        blocked = {'preprocess', '_work'}
        if extension.casefold() == '.glb':
            blocked.add('rigged')
        if any(part.casefold() in blocked or part.startswith('.') for part in parents):
            continue
        stem = _normal_name(path.stem)
        if stem.endswith('rigged'):
            stem = stem[:-6]
        named_quality = re.search(r'(ultra|high|midhigh|highmid|medium|low)(50k|30k|20k|10k|5k|50000|30000|20000|10000|5000)$', stem)
        if any(stem.endswith(suffix) for suffix in suffixes) or (not named_quality and any(_normal_name(part) in folders for part in parents)):
            found.append(path)
    return sorted(found)


def file_hash(path: Path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def missing_lods(asset: str, directory: Path, targets):
    missing = []
    for target in targets:
        if not lod_files(directory, target):
            missing.append(target)
    return missing


def publish_additions(source: Path, destination: Path):
    """Preflight every collision, then atomically link new files on the same disk.

    FBX is published last, so its presence means its accompanying files were
    published too. Hard links provide exclusive creation without an overwrite
    race; removing staging afterwards does not remove the published files.
    """
    files = sorted((p for p in source.rglob("*") if p.is_file()),
                   key=lambda p: (p.suffix.lower() == ".fbx", str(p)))
    additions = []
    for path in files:
        target = destination / path.relative_to(source)
        if target.exists():
            if not target.is_file() or file_hash(path) != file_hash(target):
                raise RuntimeError(f"Resultat existent protegit; no el sobreescric: {target}")
        else:
            additions.append((path, target))
    created = []
    try:
        for path, target in additions:
            target.parent.mkdir(parents=True, exist_ok=True)
            os.link(path, target)
            created.append((path, target))
    except Exception:
        # Roll back only our own newly linked files, never pre-existing data.
        for path, target in reversed(created):
            if target.is_file() and os.path.samefile(path, target):
                target.unlink()
        raise
    return [target for _, target in additions]
