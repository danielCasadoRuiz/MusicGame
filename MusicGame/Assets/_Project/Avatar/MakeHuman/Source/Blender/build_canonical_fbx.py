"""
Builds MakeHuman_Canonical.fbx from the data MakeHumanBodyBuilder exports (headless Blender 4.2):

  blender -b --factory-startup --python build_canonical_fbx.py -- <canonical.json> <out.fbx>

The JSON is the SOURCE OF TRUTH (our bake: canonical 1.75 m bodies re-posed into the T-pose rest,
the canonical skeleton, top-4 skin weights, UVs, the Gender + six body morphs). This script only
translates it into Blender objects and exports one clean FBX:

  Armature  (MakeHuman game_engine bones: head/tail/roll from the rig definition, oriented by the
             T-pose rotation of each bone, heads at the T-pose joints)
  Body      (skinned mesh, shape keys: Gender, MaleSlim, MaleHeavy, MaleMuscle, FemaleSlim, FemaleHeavy, FemaleMuscle)
  Eyes      (skinned mesh, same shape keys)

Nothing else: no cameras, lights, animation or helper objects.

Coordinates: the JSON is in Unity space (left-handed, Y up, character facing +Z, its left at -X).
Blender (right-handed, Z up, character facing -Y) = (-x, -z, y). That is a reflection, so triangle
winding is reversed here. The FBX is exported with Unity's usual axis settings (-Z forward, Y up,
apply transform), so Unity imports it back into exactly the original coordinates.
"""
import json
import sys
import bpy
from mathutils import Matrix, Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
json_path, fbx_path = argv[0], argv[1]
data = json.load(open(json_path, encoding="utf-8"))

C = Matrix(((-1.0, 0.0, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0)))  # Unity -> Blender (reflection)

def v(u):
    return Vector((-u[0], -u[2], u[1]))

def unity_rotation_to_blender(q):
    # Unity quaternion (x, y, z, w) -> 3x3, conjugated by the reflection C.
    m = Quaternion((q[3], q[0], q[1], q[2])).to_matrix()
    return C @ m @ C.transposed()

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

# ---- Armature
arm_data = bpy.data.armatures.new("Armature")
arm_obj = bpy.data.objects.new("Armature", arm_data)
scene.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='EDIT')

edit = {}
for b in data["bones"]:
    eb = arm_data.edit_bones.new(b["name"])
    head, tail = v(b["headA"]), v(b["tailA"])
    if (tail - head).length < 1e-4:
        tail = head + Vector((0.0, 0.0, 0.05))
    eb.head, eb.tail, eb.roll = head, tail, b["roll"]
    eb.use_connect = False
    edit[b["name"]] = eb
for b in data["bones"]:
    if b["parent"]:
        edit[b["name"]].parent = edit[b["parent"]]

# Re-orient every bone into the T-pose: A-pose orientation (rig's own head/tail/roll) rotated by the
# bone's T-pose world rotation, head moved to its T-pose joint; the length follows the canonical scale.
scale = data["tposeScale"]
for b in data["bones"]:
    eb = edit[b["name"]]
    length = eb.length * scale
    rotation = unity_rotation_to_blender(b["rotation"]) @ eb.matrix.to_3x3()
    eb.matrix = Matrix.Translation(v(b["headT"])) @ rotation.to_4x4()
    eb.length = length
bpy.ops.object.mode_set(mode='OBJECT')

# ---- Meshes
for part in data["parts"]:
    positions = part["positions"]
    verts = [v(positions[i:i + 3]) for i in range(0, len(positions), 3)]
    tris = part["triangles"]
    faces = [(tris[i], tris[i + 2], tris[i + 1]) for i in range(0, len(tris), 3)]  # reflection -> reverse winding
    uvs = part["uvs"]  # per triangle corner, Unity corner order (a, b, c)

    mesh = bpy.data.meshes.new(part["name"])
    mesh.from_pydata(verts, [], faces)
    mesh.validate(verbose=False)
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for poly_index, poly in enumerate(mesh.polygons):
        corners = (0, 2, 1)  # matches the reversed winding above
        for k, loop_index in enumerate(range(poly.loop_start, poly.loop_start + poly.loop_total)):
            c = poly_index * 3 + corners[k]
            uv_layer.data[loop_index].uv = (uvs[c * 2], uvs[c * 2 + 1])
    mesh.shade_smooth()

    obj = bpy.data.objects.new(part["name"], mesh)
    scene.collection.objects.link(obj)
    obj.parent = arm_obj
    modifier = obj.modifiers.new("Armature", 'ARMATURE')
    modifier.object = arm_obj

    groups = {}
    for b in data["bones"]:
        groups[b["name"]] = obj.vertex_groups.new(name=b["name"])
    weights = part["weights"]  # per vertex: [bone index, weight, bone index, weight, ...]
    for vi, entry in enumerate(weights):
        for j in range(0, len(entry), 2):
            groups[data["bones"][entry[j]]["name"]].add([vi], entry[j + 1], 'REPLACE')

    obj.shape_key_add(name="Basis", from_mix=False)
    for name, flat in part["shapes"].items():
        key = obj.shape_key_add(name=name, from_mix=False)
        coords = []
        for i in range(0, len(flat), 3):
            p = v(flat[i:i + 3])
            coords.extend((p.x, p.y, p.z))
        key.data.foreach_set("co", coords)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=fbx_path,
    use_selection=False,
    object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=False,          # keep shape keys
    mesh_smooth_type='OFF',            # Unity recalculates normals (ModelImporter: Calculate)
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    armature_nodetype='NULL',
    use_armature_deform_only=False,
    bake_anim=False,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',
    axis_up='Y',
    bake_space_transform=True,
    path_mode='AUTO',
    embed_textures=False,
)
print(f"[build_canonical_fbx] wrote {fbx_path}: {len(data['bones'])} bones, {len(data['parts'])} meshes")
