"""Compare source/HIGH occupancy with fixed cameras and unlit white material."""
import argparse,json,sys
from pathlib import Path
import bpy,numpy as np
from mathutils import Vector
ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True);ap.add_argument('--diagnostics',type=Path,required=True);ap.add_argument('--label',required=True)
a=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint));source=bpy.data.objects['SOURCE'];target=bpy.data.objects['HIGH'];scene=bpy.context.scene
for obj in scene.objects:
    if obj.type=='MESH':obj.hide_render=True
for obj in [source,target]:
    mat=bpy.data.materials.new('SilhouetteWhite');mat.use_nodes=True;nt=mat.node_tree;nt.nodes.clear();out=nt.nodes.new('ShaderNodeOutputMaterial');em=nt.nodes.new('ShaderNodeEmission');em.inputs['Color'].default_value=(1,1,1,1);nt.links.new(em.outputs[0],out.inputs['Surface']);obj.data.materials.clear();obj.data.materials.append(mat);obj.hide_set(False)
scene.world=bpy.data.worlds.new('SilhouetteBlack');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(0,0,0,1)
scene.render.engine='CYCLES';scene.cycles.samples=1;scene.cycles.use_denoising=False;scene.cycles.max_bounces=0;scene.render.threads_mode='FIXED';scene.render.threads=8
scene.view_settings.view_transform='Standard';scene.render.resolution_x=384;scene.render.resolution_y=384;scene.render.resolution_percentage=100
bounds=[source.matrix_world@Vector(v) for v in source.bound_box];lo=Vector(tuple(min(v[i] for v in bounds) for i in range(3)));hi=Vector(tuple(max(v[i] for v in bounds) for i in range(3)));center=(lo+hi)/2;size=max(hi-lo)
camera=bpy.data.cameras.new('FixedSilhouetteCamera');cam=bpy.data.objects.new(camera.name,camera);scene.collection.objects.link(cam);scene.camera=cam;camera.type='ORTHO';camera.ortho_scale=size*1.17
result={};a.diagnostics.mkdir(parents=True,exist_ok=True)
for direction,axis in [('front',(0,-1,0)),('back',(0,1,0)),('side',(1,0,0))]:
    cam.location=center+Vector(axis)*size*3;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();masks=[]
    for name,obj in [('source',source),('high',target)]:
        source.hide_render=True;target.hide_render=True;obj.hide_render=False
        scene.render.filepath=str(a.diagnostics/(a.label+'.'+direction+'.'+name+'.png'));bpy.ops.render.render(write_still=True)
        rendered=bpy.data.images.load(scene.render.filepath,check_existing=False);data=np.empty(384*384*4,dtype=np.float32);rendered.pixels.foreach_get(data);masks.append(data.reshape(384,384,4)[:,:,0]>.5);bpy.data.images.remove(rendered)
    gt,pred=masks;union=np.count_nonzero(gt|pred);missing=np.count_nonzero(gt & ~pred)
    result[direction]={'iou':float(np.count_nonzero(gt&pred)/max(1,union)),'source_missing_fraction':float(missing/max(1,gt.sum())),'extra_fraction':float(np.count_nonzero(pred & ~gt)/max(1,gt.sum()))}
(a.diagnostics/(a.label+'.silhouette.json')).write_text(json.dumps(result,indent=2));print('SILHOUETTE_RESULT',a.label,result,flush=True)
