#!/usr/bin/env python3
"""Local Meshy GLB -> ~50k triangles and baked 4K PBR. Python stdlib launcher."""
import argparse
import hashlib
import json
import math
import os
import shutil
import struct
import subprocess
import sys
from pathlib import Path

PACKAGE = Path(__file__).resolve().parent
VERSION = '1.5.0-quad-remesher-v101-per-model-config'
BASE = dict(method='ANGLE_BASED', seed=42, sharp=False, smooth=True, torso=False,
            attempts=8, normalize_density=False, scaled_bake=False, multi_cage=True,
            correct_channels=False)
PROFILES = {
    'standard': dict(BASE, uv='upper'),
    'wig': dict(BASE, uv='hood'),
    'trousers': dict(BASE, uv='trousers'),
    'shoe': dict(BASE, uv='shoe'),
    # Advanced recipes are opt-in; batch defaults use the shared base recipe.
    'vest': dict(BASE, uv='upper', method='MINIMUM_STRETCH', attempts=1,
                 normalize_density=True, scaled_bake=True, multi_cage=True,
                 correct_channels=True, torso=True),
    'coat': dict(BASE, uv='upper', method='MINIMUM_STRETCH', seed=43, sharp=True,
                 smooth=False, attempts=1, normalize_density=True, scaled_bake=True,
                 multi_cage=True, correct_channels=True),
}


def auto_profile(path):
    """Filename seam hints only: all automatic profiles share the base process."""
    name = path.stem.casefold()
    if any(word in name for word in ['trouse', 'pants', 'pantal']): return 'trousers'
    if any(word in name for word in ['shoe', 'boot', 'sabata', 'zapato', 'bota']): return 'shoe'
    if any(word in name for word in ['wig', 'hood', 'perruca', 'peluca']): return 'wig'
    return 'standard'

STAGES = ['inspect', 'retopo', 'uv', 'bake', 'normals', 'torso_color', 'torso_normal', 'verify', 'preview']


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''): h.update(chunk)
    return h.hexdigest()


def write_json(path, value):
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')
    temp.replace(path)


def read_json(path): return json.loads(path.read_text(encoding='utf-8'))


QR_DEFAULTS = {
    'adaptive_size': 0,
    'adapt_quad_count': False,
    'use_vertex_color': False,
    'quad_density': 1.0,
    'use_materials': False,
    'use_normals_splitting': False,
    'detect_hard_edges': True,
    'symmetry_x': False,
    'symmetry_y': False,
    'symmetry_z': False,
}
QR_ALLOWED_KEYS = set(QR_DEFAULTS)


def load_quad_remesher_config(src):
    """Per-model optional config. Missing file = QR_DEFAULTS."""
    candidates = [
        src.with_suffix('.retopo.json'),
        src.parent / 'retopo_config.json',
    ]
    config_path = next((p for p in candidates if p.is_file()), None)
    values = dict(QR_DEFAULTS)

    if config_path is not None:
        doc = read_json(config_path)
        if not isinstance(doc, dict):
            raise ValueError(f'{config_path}: el JSON ha de ser un objecte.')
        section = doc.get('quad_remesher', doc)
        if not isinstance(section, dict):
            raise ValueError(f'{config_path}: quad_remesher ha de ser un objecte.')
        unknown = sorted(set(section) - QR_ALLOWED_KEYS)
        if unknown:
            raise ValueError(f'{config_path}: camps Quad Remesher desconeguts: {unknown}')
        values.update(section)

    if isinstance(values['adaptive_size'], bool) or not isinstance(values['adaptive_size'], (int, float)):
        raise ValueError('adaptive_size ha de ser numèric.')
    values['adaptive_size'] = float(values['adaptive_size'])
    if not 0 <= values['adaptive_size'] <= 100:
        raise ValueError('adaptive_size ha d\'estar entre 0 i 100.')

    if isinstance(values['quad_density'], bool) or not isinstance(values['quad_density'], (int, float)):
        raise ValueError('quad_density ha de ser numèric.')
    values['quad_density'] = float(values['quad_density'])
    if not 0.25 <= values['quad_density'] <= 4.0:
        raise ValueError('quad_density ha d\'estar entre 0.25 i 4.0.')

    for key in (
        'adapt_quad_count', 'use_vertex_color', 'use_materials',
        'use_normals_splitting', 'detect_hard_edges',
        'symmetry_x', 'symmetry_y', 'symmetry_z'
    ):
        if type(values[key]) is not bool:
            raise ValueError(f'{key} ha de ser true o false.')

    return values, config_path


def validate_input(path):
    with path.open('rb') as f:
        header = f.read(20)
        if len(header) != 20: raise ValueError(f'GLB incomplet: {path}')
        magic, version, length, size, kind = struct.unpack('<5I', header)
        if magic != 0x46546C67 or version != 2 or kind != 0x4E4F534A or length != path.stat().st_size:
            raise ValueError(f'Cal un GLB 2 vàlid: {path}')
        doc = json.loads(f.read(size))
    meshes = doc.get('meshes', [])
    if len(meshes) != 1 or len(meshes[0].get('primitives', [])) != 1:
        raise ValueError('Perfil admès: una malla i una primitiva/material per fitxer.')
    if sum('mesh' in n for n in doc.get('nodes', [])) != 1:
        raise ValueError('Cal una sola instància de la malla.')
    prim = meshes[0]['primitives'][0]
    if doc.get('skins') or doc.get('animations') or prim.get('targets'):
        raise ValueError('Aquest procés és per a peces estàtiques sense rig, animació ni morphs.')
    if prim.get('mode', 4) != 4: raise ValueError('La malla d’entrada ha de tenir triangles.')
    if not {'POSITION', 'NORMAL', 'TEXCOORD_0'}.issubset(prim.get('attributes', {})):
        raise ValueError('Falten posicions, normals o UV de la textura original.')
    mats = doc.get('materials', [])
    if len(mats) != 1: raise ValueError('Cal un sol material PBR Meshy.')
    mat = mats[0]; pbr = mat.get('pbrMetallicRoughness', {})
    if pbr.get('baseColorFactor', [1,1,1,1]) != [1,1,1,1] or pbr.get('metallicFactor',1) != 1 or pbr.get('roughnessFactor',1) != 1:
        raise ValueError('La correcció PBR requereix factors BaseColor/Metallic/Roughness iguals a 1.')
    if not pbr.get('baseColorTexture') or not pbr.get('metallicRoughnessTexture') or not mat.get('normalTexture'):
        raise ValueError('Calen BaseColor, Metallic/Roughness i Normal d’entrada.')
    if mat.get('alphaMode','OPAQUE') != 'OPAQUE' or mat.get('extensions'):
        raise ValueError('Aquest perfil no inclou transparència ni extensions de material.')
    if doc.get('extensionsRequired'): raise ValueError('Aquest perfil no inclou extensions GLB obligatòries.')
    if any('uri' in v for v in doc.get('images', []) + doc.get('buffers', [])):
        raise ValueError('Cal un GLB amb textures i geometria incrustades, sense fitxers externs.')
    for texture in [pbr['baseColorTexture'], pbr['metallicRoughnessTexture'], mat['normalTexture']]:
        if texture.get('texCoord', 0) != 0: raise ValueError('Aquest perfil utilitza TEXCOORD_0.')
    return doc


def find_blender(explicit):
    if explicit:
        p = Path(explicit).expanduser()
        if not p.is_file(): raise ValueError(f'No trobo Blender: {p}')
        return str(p.resolve())
    candidates = []
    if os.environ.get('BLENDER_EXE'): candidates.append(Path(os.environ['BLENDER_EXE']))
    found = shutil.which('blender')
    if found: candidates.append(Path(found))
    if os.name == 'nt':
        for root in [Path(os.environ.get('ProgramFiles', 'C:/Program Files'))/'Blender Foundation',
                     Path(os.environ.get('LOCALAPPDATA', '.'))/'Programs'/'Blender Foundation']:
            candidates.extend(sorted(root.glob('Blender*/blender.exe')))
    if sys.platform == 'darwin': candidates.append(Path('/Applications/Blender.app/Contents/MacOS/Blender'))
    for candidate in candidates:
        if candidate.is_file():
            try:
                version = subprocess.check_output([str(candidate),'--version'],text=True,stderr=subprocess.DEVNULL).splitlines()[0]
                if version.split()[:2] == ['Blender','4.5.3']: return str(candidate.resolve())
            except (OSError,subprocess.SubprocessError,IndexError): pass
    raise ValueError('No trobo Blender 4.5.3. Indica --blender amb el camí de blender.exe.')


def run_asset(src, profile, args, binary, version, helpers_hash):
    settings = PROFILES[profile].copy()
    if args.local_projection: settings['torso'] = False
    qr_settings, qr_config_path = load_quad_remesher_config(src)
    fingerprint = dict(package=VERSION, source_sha256=sha(src), profile=profile, settings=settings,
                       quad_remesher=qr_settings,
                       quad_remesher_config=(str(qr_config_path) if qr_config_path else None),
                       blender=version, scripts_sha256=helpers_hash, resolution=4096, quads=args.quads,
                       threads=8, preview=not args.no_preview)
    job = args.output / '_w'
    published = args.output / (src.stem + '_HIGH.glb')
    manifest = job / 'job.json'
    if manifest.exists():
        state = read_json(manifest)
        if not args.resume: raise ValueError(f'Ja existeix {job}; utilitza --resume o una altra carpeta de sortida.')
        if state['fingerprint'] != fingerprint: raise ValueError(f'Input, perfil, versió o scripts diferents: crea una carpeta de sortida nova ({job}).')
    else:
        if job.exists() and any(job.iterdir()): raise ValueError(f'Carpeta de sortida ocupada: {job}')
        state = dict(fingerprint=fingerprint, completed=[], status='pending')
    if args.resume and manifest.exists():
        for name, expected in state.get('artifacts', {}).items():
            artifact = job/name
            if not artifact.is_file() or sha(artifact) != expected:
                raise ValueError(f'Checkpoint modificat o absent: {artifact}; crea una sortida nova.')
    if published.exists():
        if state.get('published_sha256') != sha(published):
            raise ValueError(f'Sortida final existent o modificada: {published}; utilitza una carpeta nova.')
    if args.dry_run:
        print(json.dumps(dict(input=str(src), job=str(job), final_glb=str(published), config=fingerprint,
                              completed=state['completed'], stages=STAGES), indent=2, ensure_ascii=False))
        return
    job.mkdir(parents=True, exist_ok=True)
    diag = job/'diagnostics'; diag.mkdir(exist_ok=True)
    (job/'input').mkdir(exist_ok=True)
    copy = job/'input'/(src.stem+'.glb')
    if copy.exists():
        if sha(copy) != fingerprint['source_sha256']: raise ValueError('La còpia de l’input ha canviat; fes servir una carpeta nova.')
    else: shutil.copy2(src, copy)
    shutil.copytree(PACKAGE/'scripts', job/'scripts', dirs_exist_ok=True)
    if (PACKAGE/'vendor').is_dir():
        shutil.copytree(PACKAGE/'vendor', job/'vendor', dirs_exist_ok=True)
    out = job/'output'/src.stem
    checkpoint = out/'03_uv.blend'; baked = out/'04_baked.blend'
    state['status'] = 'running'; write_json(manifest, state)
    prefix_factory = [binary, '-b', '--factory-startup', '--threads', '8', '--python-exit-code', '1', '--python']
    prefix_user = [binary, '-b', '--threads', '8', '--python-exit-code', '1', '--python']
    # Quad Remesher Engine 1.4 needs a real Blender GUI host (xremesh returns HostApp com failed under -b).
    # Retopo therefore uses a temporary automated GUI Blender process; it minimizes and closes itself.
    # Other stages stay background/headless.
    prefix_user_gui = [binary, '--threads', '8', '--python-exit-code', '1', '--python']

    def run(script, params, label, use_factory=True, gui=False):
        log = diag/(label+'.log')
        print(f'[{src.name}] {label} — registre: {log}', flush=True)
        with log.open('w', encoding='utf-8') as f:
            prefix = prefix_factory if use_factory else (prefix_user_gui if gui else prefix_user)
            result = subprocess.run(prefix+[str(job/'scripts'/script), '--']+list(map(str,params)), stdout=f, stderr=subprocess.STDOUT, cwd=job)
        if result.returncode: raise RuntimeError(f'{label} ha fallat ({result.returncode}). Consulta {log}')

    def gate(suffix, field):
        if not read_json(diag/(src.stem+suffix))[field]: raise RuntimeError(f'Control de qualitat fallit: {suffix}')

    def repair_uv():
        try: run('repair_uv.py', ['--checkpoint',checkpoint,'--diagnostics',diag,'--smart'], 'uv_repair_smart')
        except RuntimeError:
            # Only retry a recorded UV failure, never a crash/OOM.
            report = diag/(src.stem+'.uv.json')
            if not report.exists() or read_json(report).get('passed'): raise
            run('repair_uv.py', ['--checkpoint',checkpoint,'--diagnostics',diag], 'uv_repair_local')
        gate('.uv.json','passed')

    try:
        for stage in STAGES:
            if STAGES.index(stage) > STAGES.index(args.until): break
            if stage in state['completed']: continue
            if stage == 'inspect':
                run('blender_inspect.py', ['--input',copy,'--diagnostics',diag], stage)
                report = read_json(diag/(src.stem+'.blender.json'))
                mesh = [o for o in report['objects'] if o['type']=='MESH'][0]
                dim = max(mesh['dimensions'])
                if not math.isfinite(dim) or dim <= 0: raise ValueError('Dimensions invàlides.')
                state['working_scale'] = 190.0/dim if settings['scaled_bake'] else 1.0
            elif stage == 'retopo':
                print('QUAD_REMESHER_MODEL_CONFIG', json.dumps({
                    'source': str(src),
                    'config_file': str(qr_config_path) if qr_config_path else None,
                    'settings': qr_settings
                }, ensure_ascii=False), flush=True)
                params = [
                    '--input',copy,'--output',job/'output','--diagnostics',diag,
                    '--quads',str(args.quads),
                    '--qr-config-json',json.dumps(qr_settings,separators=(',',':'))
                ]
                run('retopology.py',params,stage,use_factory=False,gui=True)
                if not (out/'02_retopology.blend').is_file():
                    raise RuntimeError('retopo ha acabat sense generar 02_retopology.blend; consulta retopo.log')
            elif stage == 'uv':
                shutil.copy2(out/'02_retopology.blend',checkpoint)
                run(
                    'smart_uv_simple.py',
                    ['--checkpoint',checkpoint,'--diagnostics',diag,'--resolution','4096'],
                    'uv_smart'
                )
            elif stage == 'bake':
                params=['--checkpoint',checkpoint,'--diagnostics',diag,'--resolution','4096','--working-scale',state['working_scale']]
                if settings['multi_cage']: params += ['--multi-cage']
                run('bake_export.py',params,stage)
            elif stage == 'normals' and settings['correct_channels']:
                run('reproject_normals.py',['--checkpoint',baked,'--diagnostics',diag,'--correct-channels'],stage)
            elif stage in ['torso_color','torso_normal'] and settings['torso']:
                params=['--checkpoint',baked,'--diagnostics',diag]
                if stage=='torso_normal': params += ['--normal-only']
                run('project_garment_torso.py',params,stage)
            elif stage == 'verify':
                run('verify_export.py',['--input',out/'HIGH.glb','--source',copy,'--diagnostics',diag,'--triangles',str(args.quads*2)],stage)
                run('validate_final_geometry.py',['--checkpoints',baked,'--diagnostics',diag],'final_geometry')
            elif stage == 'preview' and not args.no_preview:
                run('compare_appearance.py',['--checkpoint',baked,'--output',job/'review'],stage)
            if stage == 'verify':
                temp = published.with_suffix('.tmp')
                shutil.copy2(out/'HIGH.glb',temp);temp.replace(published)
                state['published_sha256'] = sha(published)
                print(f'GLB verificat: {published}',flush=True)
            state['completed'].append(stage)
            critical = [out/'02_retopology.blend', checkpoint, baked, out/'HIGH.glb'] + sorted(diag.glob('*.json')) + sorted((out/'textures').glob('*.png'))
            state['artifacts'] = {str(p.relative_to(job)):sha(p) for p in critical if p.exists()}
            write_json(manifest,state)
            if args.until == stage: break
        if sha(src) != fingerprint['source_sha256'] or sha(copy) != fingerprint['source_sha256']: raise RuntimeError('El checksum de l’input ha canviat.')
        if 'verify' in state['completed'] and not published.exists():
            temp = published.with_suffix('.tmp')
            shutil.copy2(out/'HIGH.glb',temp);temp.replace(published)
            state['published_sha256'] = sha(published)
        state['status'] = 'complete' if len(state['completed']) == len(STAGES) else 'paused'
        state.pop('error',None);write_json(manifest,state)
        print(f'{state["status"]}: {published if state["status"]=="complete" else job}',flush=True)
    except BaseException as e:
        state['status']='interrupted' if isinstance(e,KeyboardInterrupt) else 'failed'
        state['error']=str(e);write_json(manifest,state);raise


def main(argv=None):
    p=argparse.ArgumentParser(description='GLB Meshy → target configurable amb la recepta ORIGINAL WINDOWS v1.1 + textures 4K.')
    p.add_argument('input',type=Path,nargs='?',default=PACKAGE/'models',help='Fitxer GLB o carpeta (sense recórrer subcarpetes).')
    p.add_argument('--quads',type=int,default=50000,help='Target aproximat de quads de Quad Remesher.')
    p.add_argument('--output',type=Path,help='Per defecte: carpeta resultats dins la carpeta d’entrada.')
    group=p.add_mutually_exclusive_group()
    group.add_argument('--profile',choices=['auto']+list(PROFILES),default='auto',help='Perfil per al fitxer o per a tota la carpeta.')
    group.add_argument('--profiles',type=Path,help='JSON {"nom.glb": "vest", ...} per a una carpeta mixta.')
    p.add_argument('--blender',help='Camí de blender.exe / executable Blender 4.5.3.')
    p.add_argument('--resume',action='store_true',default=True,help='Reprèn només amb input, perfil i scripts idèntics.')
    p.add_argument('--no-resume',dest='resume',action='store_false',help='Rebutja qualsevol treball existent.')
    p.add_argument('--dry-run',action='store_true',help='Valida i mostra el pla sense generar fitxers.')
    p.add_argument('--until',choices=STAGES,default='preview',help='Atura després d’aquest pas; reprèn amb --resume.')
    p.add_argument('--no-preview',action='store_true',help='Omet la comparació visual addicional.')
    p.add_argument('--local-projection',action='store_true',help='Desactiva la projecció exterior del tors del perfil vest.')
    a=p.parse_args(argv)
    if a.quads < 1000: p.error('--quads ha de ser >= 1000.')
    a.input=a.input.expanduser().resolve()
    source_dir = a.input if a.input.is_dir() else a.input.parent
    a.output = a.output.expanduser().resolve() if a.output else source_dir/'resultats'
    if a.output == source_dir: p.error('La sortida ha de ser una subcarpeta o una carpeta diferent, per no reprocessar els resultats.')
    files=sorted((f for f in a.input.iterdir() if f.suffix.lower()=='.glb'),key=lambda f:f.name.casefold()) if a.input.is_dir() else [a.input]
    if not files or any(not f.is_file() or f.suffix.lower()!='.glb' for f in files): p.error('No hi ha fitxers GLB vàlids.')
    if len({f.stem.casefold() for f in files})!=len(files): p.error('Noms de peces duplicats; separa les sortides.')
    mapping=read_json(a.profiles) if a.profiles else {f.name:(auto_profile(f) if a.profile=='auto' else a.profile) for f in files}
    binary=find_blender(a.blender)
    version=subprocess.check_output([binary,'--version'],text=True).splitlines()[0].strip()
    if version.split()[:2]!=['Blender','4.5.3']: p.error(f'Versió validada: Blender 4.5.3. Has seleccionat {version}.')
    helpers=hashlib.sha256()
    for script in sorted((PACKAGE/'scripts').glob('*.py')):
        helpers.update(script.name.encode());helpers.update(script.read_bytes())
    helpers.update(Path(__file__).read_bytes())
    failures=[]
    for f in files:
        try:
            validate_input(f)
            if mapping.get(f.name) not in PROFILES: raise ValueError(f'Falta un perfil vàlid per a {f.name}.')
            run_asset(f,mapping[f.name],a,binary,version,helpers.hexdigest())
        except KeyboardInterrupt: print('Interromput. Pots reprendre amb --resume.',file=sys.stderr);return 130
        except Exception as e: print(f'ERROR {f.name}: {e}',file=sys.stderr);failures.append(f.name)
    if failures: print('Han fallat: '+', '.join(failures),file=sys.stderr);return 1
    return 0


if __name__=='__main__':
    try: sys.exit(main())
    except Exception as error: print(f'ERROR: {error}',file=sys.stderr);sys.exit(1)
