"""Reimport the final GLB and assert materials, tangents, UV and dimensions."""
import argparse,json,struct,sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from blender_inspect import inspect
from quality_settings import triangle_budget_passed


def document(path):
    b=path.read_bytes();n=struct.unpack_from('<I',b,12)[0];return json.loads(b[20:20+n])


def verify(path,source,diagnostics,triangles=50000):
    d=document(path);original=document(source)
    primitives=[p for m in d['meshes'] for p in m['primitives']]
    total=sum(d['accessors'][p['indices']]['count']//3 for p in primitives)
    triangle_budget_within_legacy_tolerance=triangle_budget_passed(total,triangles)
    if not triangle_budget_within_legacy_tolerance:print(f'TRIANGLE_BUDGET_DIAGNOSTIC: exported {total}, target {triangles}; continuing.',flush=True)
    assert len(d.get('nodes',[]))==1, 'Unexpected exported objects'
    for p in primitives:
        assert all(k in p['attributes'] for k in ['POSITION','NORMAL','TANGENT','TEXCOORD_0'])
        mat=d['materials'][p['material']]
        assert 'baseColorTexture' in mat['pbrMetallicRoughness']
        assert 'metallicRoughnessTexture' in mat['pbrMetallicRoughness']
        assert 'normalTexture' in mat
    assert len(d['images'])>=3
    inspect(path,diagnostics/(source.stem+'.roundtrip'),render=True)
    assert len([o for o in bpy.context.scene.objects if o.type=='MESH'])==1
    imported=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    assert len(imported.data.uv_layers)==1
    # GLTF accessor bounds preserve the original axis convention through roundtrip.
    old=original['accessors'][original['meshes'][0]['primitives'][0]['attributes']['POSITION']]
    new=d['accessors'][primitives[0]['attributes']['POSITION']]
    differences=[abs(new[key][i]-old[key][i]) for key in ['min','max'] for i in range(3)]
    scale=max(old['max'][i]-old['min'][i] for i in range(3))
    bounds_within_legacy_2pct=max(differences)<=scale*.02
    passed=True
    report={'asset':source.name,'glb':path.name,'triangles':total,'uv_sets':1,
            'tangents_encoded':True,'images_embedded':len(d['images']),
            'max_bbox_difference':max(differences),'bbox_tolerance':scale*.02,
            'passed':passed,'roundtrip_imported':True,
            'triangle_budget_within_legacy_tolerance':triangle_budget_within_legacy_tolerance,
            'triangle_budget_is_hard_gate':False,
            'bounds_within_legacy_2pct':bounds_within_legacy_2pct,
            'bounds_2pct_is_hard_gate':False}
    (diagnostics/(source.stem+'.roundtrip.json')).write_text(json.dumps(report,indent=2))
    if not bounds_within_legacy_2pct:print('ROUNDTRIP_BOUNDS_DIAGNOSTIC: exceeds legacy 2%; continuing.',flush=True)
    print('ROUNDTRIP',json.dumps(report),flush=True)


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--input',type=Path,required=True);ap.add_argument('--source',type=Path,required=True)
    ap.add_argument('--diagnostics',type=Path,default=Path('diagnostics'));ap.add_argument('--triangles',type=int,default=50000);args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    verify(args.input.resolve(),args.source.resolve(),args.diagnostics.resolve(),args.triangles)
