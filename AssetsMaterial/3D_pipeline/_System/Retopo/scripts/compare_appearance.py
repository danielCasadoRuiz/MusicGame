"""Render matching source/HIGH close views with PBR and unlit BaseColor."""
import argparse,sys
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from bake_export import switch_emission,restore
ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True);ap.add_argument('--output',type=Path,required=True);ap.add_argument('--color-only',action='store_true');ap.add_argument('--front-only',action='store_true');a=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint));source=bpy.data.objects['SOURCE'];target=bpy.data.objects['HIGH'];scene=bpy.context.scene
for o in scene.objects:
 if o.type=='MESH':o.hide_render=True
source.hide_set(False);target.hide_set(False)
bounds=[source.matrix_world@Vector(v) for v in source.bound_box];lo=Vector(tuple(min(v[i] for v in bounds) for i in range(3)));hi=Vector(tuple(max(v[i] for v in bounds) for i in range(3)));center=(lo+hi)/2;size=max(hi-lo)
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=8;scene.render.resolution_x=1536;scene.render.resolution_y=1536;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
scene.world=bpy.data.worlds.new('CompareWorld');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.13,.13,.13,1);scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.7
for o in list(scene.objects):
 if o.type=='LIGHT':bpy.data.objects.remove(o,do_unlink=True)
l=bpy.data.lights.new('CompareLight','AREA');l.energy=450;l.size=size*2;lamp=bpy.data.objects.new(l.name,l);scene.collection.objects.link(lamp);lamp.location=center+Vector((size,-size,size*1.5));lamp.rotation_euler=(center-lamp.location).to_track_quat('-Z','Y').to_euler()
c=bpy.data.cameras.new('CompareCamera');cam=bpy.data.objects.new(c.name,c);scene.collection.objects.link(cam);scene.camera=cam;c.type='ORTHO';c.ortho_scale=size*1.17
for mode in (['color'] if a.color_only else ['color','pbr']):
 for name,obj in [('source',source),('high',target)]:
  source.hide_render=True;target.hide_render=True;obj.hide_render=False
  rec=switch_emission(obj.active_material,'Base Color') if mode=='color' else None
  for view,axis in ([('front',(0,-1,0))] if a.front_only else [('front',(0,-1,0)),('back',(0,1,0))]):
   cam.location=center+Vector(axis)*size*3;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(a.output/f'{name}.{mode}.{view}.png');bpy.ops.render.render(write_still=True)
  if rec:restore(obj.active_material,rec)
print('APPEARANCE_RENDERED',flush=True)
