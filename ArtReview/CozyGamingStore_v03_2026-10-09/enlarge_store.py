"""Load the v01 store and save a larger, export-ready v03. No renders."""
import bpy
import math
from pathlib import Path
from mathutils import Matrix, Vector

FACTOR = 2.25
OUT = Path(__file__).resolve().parent
OUT.mkdir(parents=True, exist_ok=True)
source = OUT.parent/'CozyGamingStore_v01_2026-10-09'/'CozyGamingStore_v01.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
scene = bpy.data.scenes['CozyGamingStore_Review_v01']
bpy.context.window.scene = scene
scene.name = 'CozyGamingStore_Review_v03'
bpy.context.view_layer.update()
objects = list(scene.objects)
architecture = set(bpy.data.collections['CST01_Architecture'].all_objects)
export_objects = set(bpy.data.collections['CST01_Export_Hidden'].all_objects)
refs = set(bpy.data.collections['CST01_ScaleReferences_NoExport'].all_objects)
lighting = set(bpy.data.collections['CST01_PreviewLighting'].all_objects)
env_root = bpy.data.objects['CST01_Environment_Root']
shelf_root = bpy.data.objects['CST01_ShelfPlaceholders_Root']
old_matrices = {ob: ob.matrix_world.copy() for ob in objects}
old_shelf_dims = {ob.name: tuple(ob.dimensions) for ob in objects if ob.name.endswith('_Visual') and ob.parent and ob.parent.parent == shelf_root}
original_meshes = {ob: ob.data for ob in bpy.data.objects if ob.name.startswith(('CGS01_', 'CGS02_', 'CGS03_')) or ob.name == 'GameBox'}
horizontal = Matrix.Diagonal((FACTOR, FACTOR, 1, 1))

# Stretch the building footprint, while retaining metre-scale transforms.
# Make architecture mesh copies before editing shared geometry.
for ob in architecture:
    if ob.type == 'MESH':
        old_world = old_matrices[ob]
        target = old_world.copy()
        target.translation.x *= FACTOR
        target.translation.y *= FACTOR
        ob.data = ob.data.copy()
        ob.data.transform(target.inverted() @ horizontal @ old_world)
        ob.data.update()

# Move complete furniture clusters rather than resizing their contents.
for ob in objects:
    if ob in export_objects or ob in refs or ob in lighting:
        continue
    if ob.parent in {env_root, shelf_root}:
        old = old_matrices[ob].translation
        target = Vector((old.x*FACTOR, old.y*FACTOR, old.z))
        if ob.parent == shelf_root and 'Shelf_Wall_' in ob.name:
            target.x = math.copysign(6.0*FACTOR-.41, old.x)
        elif ob.parent == shelf_root and 'Shelf_Island_' in ob.name:
            centre = -2.10 if 'Island_1_' in ob.name else 2.10
            target.x = centre*FACTOR + (old.x-centre)
        ob.location = target

bpy.context.view_layer.update()
# Door children were stretched locally as well: their local translations
# must follow the enlarged hinge geometry. They retain their original rotation.
for ob in architecture:
    if ob.parent in architecture:
        target = old_matrices[ob].copy()
        target.translation.x *= FACTOR
        target.translation.y *= FACTOR
        ob.matrix_world = target

# Preview lighting is retained without rendering; move it with the footprint.
for ob in lighting:
    if ob.parent is None:
        ob.location.x *= FACTOR
        ob.location.y *= FACTOR
        if ob.type == 'CAMERA' and ob.data.type == 'ORTHO':
            ob.data.ortho_scale *= FACTOR

bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()

# Refresh the existing export copies from the edited source; the original
# mesh/font objects remain editable in the review scene.
copied_meshes = {}
def parent_depth(ob):
    depth = 0
    while ob.parent:
        depth += 1
        ob = ob.parent
    return depth

for ob in sorted(export_objects, key=parent_depth):
    if not ob.name.startswith('CST01_FBX_'):
        continue
    src = bpy.data.objects[ob.name[len('CST01_FBX_'):]]
    ob.matrix_world = src.matrix_world.copy()
    if ob.type == 'MESH':
        key = (src.data.as_pointer(), tuple((mod.type, mod.name) for mod in src.modifiers))
        if key not in copied_meshes:
            copied_meshes[key] = bpy.data.meshes.new_from_object(src.evaluated_get(dg), depsgraph=dg)
        ob.data = copied_meshes[key]
    bpy.context.view_layer.update()

bpy.context.view_layer.update()
for ob in export_objects:
    if ob.name.startswith('CST01_FBX_'):
        src = bpy.data.objects[ob.name[len('CST01_FBX_'):]]
        assert max(abs(ob.matrix_world[i][j]-src.matrix_world[i][j]) for i in range(4) for j in range(4)) < 1e-5, ob.name

def bounds(ob):
    points = [ob.matrix_world @ Vector(corner) for corner in ob.bound_box]
    return tuple(min(p[i] for p in points) for i in range(3)), tuple(max(p[i] for p in points) for i in range(3))

shelves = [ob for ob in objects if ob.name in old_shelf_dims]
for ob in shelves:
    assert all(abs(a-b) < 1e-5 for a, b in zip(ob.dimensions, old_shelf_dims[ob.name])), ob.name
    assert all(abs(value-1) < 1e-5 for value in ob.scale), ob.name
    lo, hi = bounds(ob)
    assert -6*FACTOR <= lo[0] < hi[0] <= 6*FACTOR and -9*FACTOR <= lo[1] < hi[1] <= 9*FACTOR, ob.name
for ob, data in original_meshes.items():
    assert ob.data == data, 'Original library mesh changed: '+ob.name

def find_bounds(fragment):
    entries = [bounds(ob) for ob in shelves if fragment in ob.name]
    return ([min(lo[i] for lo, hi in entries) for i in range(3)],
            [max(hi[i] for lo, hi in entries) for i in range(3)])

west, island1, island2, east = [find_bounds(name) for name in ['Wall_West', 'Island_1', 'Island_2', 'Wall_East']]
aisles = [island1[0][0]-west[1][0], island2[0][0]-island1[1][0], east[0][0]-island2[1][0]]
assert min(aisles) > 3.0, aisles

env_collection = bpy.data.collections['CST01_Environment_FBX']
shelf_collection = bpy.data.collections['CST01_ShelfPlaceholders_FBX']
export_scene = bpy.data.scenes.new('CozyGamingStore_Export_v03')
export_scene.unit_settings.system = 'METRIC'
export_scene.unit_settings.scale_length = 1
export_scene.collection.children.link(env_collection)
export_scene.collection.children.link(shelf_collection)
export_scene.world = scene.world
bpy.context.window.scene = export_scene
bpy.context.view_layer.update()
for ob in export_scene.objects:
    ob.hide_set(False)
    ob.hide_viewport = False
    ob.hide_render = False
    ob.select_set(True)
assert all(ob.type in {'MESH', 'EMPTY'} for ob in export_scene.objects)
bpy.context.view_layer.objects.active = bpy.data.objects['CST01_Export_Environment_Root']
export_scene['main_interior_metres'] = [12*FACTOR, 18*FACTOR, 4.55]
export_scene['stockroom_interior_metres'] = [4*FACTOR, 5*FACTOR, 3.4]
export_scene['description'] = 'Export this active scene as FBX. Geometry only; shelves at original size. Review/source scenes are preserved separately.'
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            region = area.spaces.active.region_3d
            region.view_perspective = 'PERSP'
            region.view_location = (0, 1, 1.8)
            region.view_distance = 46.5
            region.view_rotation = Vector((.9, -1.3, 1.4)).to_track_quat('Z', 'Y')
            area.spaces.active.shading.type = 'SOLID'
            area.spaces.active.shading.color_type = 'MATERIAL'
bpy.context.preferences.filepaths.save_version = 0
path = OUT/'CozyGamingStore_v03.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(path))
print('ENLARGED_STORE_READY', str(path), 'main=27x40.5', 'stock=9x11.25', 'aisles=', aisles, 'export_objects=', len(export_scene.objects), flush=True)

