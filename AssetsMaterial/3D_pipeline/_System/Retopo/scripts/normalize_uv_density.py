"""Normalize chart density after local UV repairs and audit the repacked atlas."""
import argparse,sys,json
from pathlib import Path
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from blender_inspect import activate
from uv_seams import uv_audit
ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True);ap.add_argument('--diagnostics',type=Path,required=True);ap.add_argument('--margin',type=int,default=12);a=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint));obj=bpy.data.objects['HIGH'];activate(obj)
p=a.diagnostics/(a.checkpoint.parent.name+'.uv.json');report=json.loads(p.read_text());bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.select_all(action='SELECT');bpy.ops.uv.average_islands_scale();bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM',rotate=True,margin_method='FRACTION',margin=a.margin/report['resolution'],shape_method='CONCAVE',scale=True);bpy.ops.object.mode_set(mode='OBJECT');audit=uv_audit(obj);report['uv_audits'].append(audit);report['density_normalized_after_repairs']=True;report['packing_margin_pixels']=a.margin;report['passed']=audit['finite'] and min(audit['min'])>=0 and max(audit['max'])<=1 and not audit['positive_area_overlap_pairs'] and not audit['degenerate_uv_triangles'];p.write_text(json.dumps(report,indent=2));bpy.ops.wm.save_as_mainfile(filepath=str(a.checkpoint));print('NORMALIZED_UV',report['passed'],{k:v for k,v in audit.items() if k!='overlap_face_ids'},flush=True)
m=obj.data;m.calc_loop_triangles();layer=m.uv_layers.active;np.savez_compressed(str(a.diagnostics/(a.checkpoint.parent.name+'.uv_geometry.npz')),triangles=np.array([[list(layer.data[i].uv) for i in t.loops] for t in m.loop_triangles]))
if not report['passed']:raise RuntimeError('Density normalization needs UV repairs')
