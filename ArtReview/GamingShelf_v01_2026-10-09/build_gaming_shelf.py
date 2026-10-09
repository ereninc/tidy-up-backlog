"""Run in Blender's Text Editor, or: blender -b reference.blend -P this_file.py.

Creates a separate review scene; never clears an existing scene. Rerunning
replaces only objects tagged by this generator. No FBX/Unity export is performed.
"""
import bpy
import bmesh
import hashlib
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Matrix

# ------------------------- Artist parameters -------------------------
OUTPUT_DIR = Path(__file__).resolve().parent
REFERENCE_BLEND = Path(r"C:\Users\erenc\Desktop\6.1sol.blend")
REFERENCE_OBJECT = "GameBox"
CASE_WIDTH = 0.48
CASE_HEIGHT = 0.64
CASE_THICKNESS = 0.048
BOXES_PER_ROW = 10
CASE_GAP = 0.032
SIDE_CLEARANCE = 0.07
ROW_COUNT = 3
ROW_HEIGHT = 0.86                    # deck top to next deck top
PANEL_THICKNESS = 0.11
SHELF_THICKNESS = 0.085
SHELF_WIDTH = (BOXES_PER_ROW * CASE_WIDTH +
               (BOXES_PER_ROW - 1) * CASE_GAP + 2 * SIDE_CLEARANCE +
               2 * PANEL_THICKNESS)
SHELF_DEPTH = 0.68
FIRST_DECK_TOP = 0.32
BEVEL_AMOUNT = 0.018
BEVEL_SEGMENTS = 2
RENDER_SAMPLES = 48
RENDER_WIDTH = 1440
RENDER_HEIGHT = 1080
RENDER_PREVIEWS = True
MAKE_EXPORT_READY_COPY = True        # hidden collection, NOT a file export
PALETTE = {
    "Warm_Oak": ("#9B6849", 0.72),
    "Soft_Cream": ("#E9DFCC", 0.78),
    "Dusty_Blue": ("#789CA8", 0.65),
    "Mustard": ("#D5AE54", 0.67),
}
# ---------------------------------------------------------------------
if '--skip-renders' in sys.argv:
    RENDER_PREVIEWS = False
OWNER = "codex.cozy_gaming_shelf.v01"
SCENE_NAME = "CozyGamingShelf_Review_v01"
PREFIX = "CGS01_"
INNER_WIDTH = SHELF_WIDTH - 2 * PANEL_THICKNESS
TOP_UNDERSIDE = FIRST_DECK_TOP + ROW_COUNT * ROW_HEIGHT - SHELF_THICKNESS
HEIGHT = TOP_UNDERSIDE + max(PANEL_THICKNESS, 0.225)
SOURCE_PATH = bpy.data.filepath


def fingerprint_reference(ob):
    payload = {
        "vertices": [list(v.co) for v in ob.data.vertices],
        "faces": [(list(p.vertices), p.material_index) for p in ob.data.polygons],
        "matrix": [list(r) for r in ob.matrix_world],
        "materials": [],
    }
    for mat in ob.data.materials:
        nodes = []
        if mat and mat.node_tree:
            for node in mat.node_tree.nodes:
                if node.type == 'BSDF_PRINCIPLED':
                    nodes.append({k: list(node.inputs[k].default_value)
                                  if k == 'Base Color' else node.inputs[k].default_value
                                  for k in ['Base Color', 'Roughness', 'Metallic']})
        payload['materials'].append((mat.name if mat else None,
                                     list(mat.diffuse_color) if mat else None, nodes))
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()


reference = bpy.data.objects.get(REFERENCE_OBJECT)
if reference is None:
    if not REFERENCE_BLEND.exists():
        raise RuntimeError("Open the supplied review blend or set REFERENCE_BLEND.")
    with bpy.data.libraries.load(str(REFERENCE_BLEND), link=False) as (src, dst):
        if REFERENCE_OBJECT not in src.objects:
            raise RuntimeError("GameBox mesh is missing from the reference blend.")
        dst.objects = [REFERENCE_OBJECT]
    reference = dst.objects[0]
    source_collection = bpy.data.collections.new("GameBox_Original_Reference")
    bpy.context.scene.collection.children.link(source_collection)
    source_collection.objects.link(reference)
if reference.type != 'MESH':
    raise RuntimeError("REFERENCE_OBJECT must be the original GameBox mesh.")
before_reference = fingerprint_reference(reference)
local_min = Vector([min(v.co[i] for v in reference.data.vertices) for i in range(3)])
local_max = Vector([max(v.co[i] for v in reference.data.vertices) for i in range(3)])
local_size = local_max - local_min
if min(local_size) <= 0:
    raise RuntimeError("Reference mesh has invalid dimensions.")
# Source GameBox is Z-up and its cover normal is -Y. Explicit measured Unity
# world dimensions prevent applying the Unity visual's (40,24,40) twice.
case_scale = Vector((CASE_WIDTH / local_size.x,
                     CASE_THICKNESS / local_size.y,
                     CASE_HEIGHT / local_size.z))
if ROW_HEIGHT - SHELF_THICKNESS <= CASE_HEIGHT + 0.04:
    raise ValueError("Not enough vertical clearance above the cases.")
required_width = BOXES_PER_ROW * CASE_WIDTH + (BOXES_PER_ROW - 1) * CASE_GAP
if INNER_WIDTH < required_width + 2 * SIDE_CLEARANCE - 1e-6:
    raise ValueError("Shelf width cannot fit the requested case count and margins.")
if SHELF_DEPTH <= CASE_THICKNESS + 0.15:
    raise ValueError("Shelf depth leaves insufficient case support/ledge clearance.")

scene = bpy.data.scenes.get(SCENE_NAME)
if scene is None:
    scene = bpy.data.scenes.new(SCENE_NAME)
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
for ob in list(bpy.data.objects):
    if ob.get('cozy_shelf_owner') == OWNER:
        bpy.data.objects.remove(ob, do_unlink=True)
for datablocks in [bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights]:
    for data in list(datablocks):
        if data.get('cozy_shelf_owner') == OWNER and data.users == 0:
            datablocks.remove(data)


def collection(name):
    coll = bpy.data.collections.get(PREFIX + name)
    if coll is None:
        coll = bpy.data.collections.new(PREFIX + name)
        coll['cozy_shelf_owner'] = OWNER
    elif coll.get('cozy_shelf_owner') != OWNER:
        raise RuntimeError("Collection name belongs to the user: " + coll.name)
    if coll.name not in scene.collection.children:
        scene.collection.children.link(coll)
    return coll


model = collection('01_Editable_Shelf')
cases = collection('02_Preview_GameBoxes')
studio = collection('03_Preview_Studio')
export = collection('04_Export_Ready_Copy')
export.hide_render = False
export.hide_viewport = False


def linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def rgba(hex_color):
    color = hex_color.lstrip('#')
    return tuple(linear(int(color[i:i+2], 16) / 255) for i in (0, 2, 4)) + (1.0,)


def material(name, hex_color, roughness=0.7):
    mat = bpy.data.materials.get(PREFIX + name)
    if mat is None:
        mat = bpy.data.materials.new(PREFIX + name)
        mat['cozy_shelf_owner'] = OWNER
    elif mat.get('cozy_shelf_owner') != OWNER:
        raise RuntimeError("Material name belongs to the user: " + mat.name)
    mat.use_nodes = True
    color = rgba(hex_color)
    mat.diffuse_color = color
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = color
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Metallic'].default_value = 0.0
    return mat


materials = {name: material(name, *values) for name, values in PALETTE.items()}
oak, cream, blue, mustard = [materials[k] for k in PALETTE]
ink = material('Preview_Ink', '#374650', 0.82)
rose = material('Preview_Dusty_Rose', '#B77C70', 0.8)
ground_mat = material('Preview_Ground', '#DAD6CA', 0.85)


def tag(ob, coll):
    ob['cozy_shelf_owner'] = OWNER
    coll.objects.link(ob)
    if ob.data and ob.data != reference.data:
        ob.data['cozy_shelf_owner'] = OWNER
    return ob


root = tag(bpy.data.objects.new(PREFIX + 'Module_Pivot_Ground_Center', None), model)
root.empty_display_type = 'PLAIN_AXES'
root.empty_display_size = 0.3


def mesh_object(name, verts, faces, mat, coll=model, bevel=0.0):
    mesh = bpy.data.meshes.new(PREFIX + name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    ob = tag(bpy.data.objects.new(PREFIX + name, mesh), coll)
    ob.data.materials.append(mat)
    if coll == model:
        ob.parent = root
    if bevel > 0:
        mod = ob.modifiers.new('Soft edge / editable', 'BEVEL')
        mod.width = bevel
        mod.segments = BEVEL_SEGMENTS
        mod.limit_method = 'ANGLE'
    return ob


def box(name, center, size, mat, coll=model, bevel=BEVEL_AMOUNT):
    x, y, z = [a / 2 for a in size]
    verts = [(a*x, b*y, c*z) for a, b, c in
             [(-1,-1,-1), (1,-1,-1), (1,1,-1), (-1,1,-1),
              (-1,-1,1), (1,-1,1), (1,1,1), (-1,1,1)]]
    faces = [(0,3,2,1), (4,5,6,7), (0,1,5,4),
             (1,2,6,5), (2,3,7,6), (3,0,4,7)]
    ob = mesh_object(name, verts, faces, mat, coll,
                     min(bevel, min(size) * 0.28))
    ob.location = center
    return ob


def cylinder_front(name, center, radius, depth, mat, coll=model, vertices=12):
    verts = [(math.cos(i*math.tau/vertices)*radius, y,
              math.sin(i*math.tau/vertices)*radius)
             for y in (-depth/2, depth/2) for i in range(vertices)]
    faces = [tuple(range(vertices-1, -1, -1)), tuple(range(vertices, vertices*2))]
    faces += [(i, (i+1)%vertices, (i+1)%vertices+vertices, i+vertices)
              for i in range(vertices)]
    ob = mesh_object(name, verts, faces, mat, coll)
    ob.location = center
    return ob


def text(name, value, center, size, mat, coll=cases, align='CENTER'):
    curve = bpy.data.curves.new(PREFIX + name, 'FONT')
    curve.body = value
    curve.align_x = align
    curve.align_y = 'CENTER'
    curve.size = size
    curve.resolution_u = 2
    curve.extrude = 0.0008 if coll == model else 0
    ob = tag(bpy.data.objects.new(PREFIX + name, curve), coll)
    ob.data.materials.append(mat)
    ob.location = center
    ob.rotation_euler.x = math.pi / 2
    if coll == model:
        ob.parent = root
    return ob


# The supporting planes stay planar and level; bevel affects only the edges.
box('Plinth_Wood', (0, 0, 0.11), (SHELF_WIDTH+0.08, SHELF_DEPTH+0.04, 0.22), oak)
box('Plinth_Blue_Inset', (0, -SHELF_DEPTH/2-0.014, 0.13),
    (SHELF_WIDTH-0.13, 0.022, 0.085), blue, bevel=0.008)
panel_bottom = 0.22
panel_height = TOP_UNDERSIDE - panel_bottom
for side, sign in [('Left', -1), ('Right', 1)]:
    x = sign * (SHELF_WIDTH/2 - PANEL_THICKNESS/2)
    box(side+'_Cream_Side_Panel', (x, 0.025, panel_bottom+panel_height/2),
        (PANEL_THICKNESS, SHELF_DEPTH-0.05, panel_height), cream)
    box(side+'_Wood_Front_Post', (x, -SHELF_DEPTH/2+0.018,
                                  panel_bottom+panel_height/2),
        (PANEL_THICKNESS+0.018, 0.10, panel_height+0.025), oak)
back_y = SHELF_DEPTH/2 - PANEL_THICKNESS/2
box('Cream_Back_Panel', (0, back_y, panel_bottom+panel_height/2),
    (INNER_WIDTH, PANEL_THICKNESS, panel_height), cream, bevel=0.008)
deck_tops = []
case_objects = []
for row in range(ROW_COUNT):
    deck_top = FIRST_DECK_TOP + row * ROW_HEIGHT
    deck_tops.append(deck_top)
    label_z = deck_top - 0.018
    front_y = -SHELF_DEPTH/2 - 0.018
    deck_front = front_y + 0.03
    deck_back = back_y - PANEL_THICKNESS/2
    box(f'Row_{row+1:02}_Flat_Wood_Deck',
        (0, (deck_front+deck_back)/2, deck_top-SHELF_THICKNESS/2),
        (INNER_WIDTH, deck_back-deck_front, SHELF_THICKNESS), oak, bevel=0.01)
    box(f'Row_{row+1:02}_Blue_Retaining_Lip', (0, front_y, label_z),
        (INNER_WIDTH+0.012, 0.06, 0.095), blue, bevel=0.012)
    box(f'Row_{row+1:02}_Blank_Name_Plate', (0, front_y-0.038, label_z),
        (0.90, 0.022, 0.112), cream, bevel=0.01)
    cylinder_front(f'Row_{row+1:02}_Plate_Pin_Left', (-0.40, front_y-0.051, label_z),
                   0.010, 0.003, mustard, vertices=8)
    cylinder_front(f'Row_{row+1:02}_Plate_Pin_Right', (0.40, front_y-0.051, label_z),
                   0.010, 0.003, mustard, vertices=8)
    captions = ['COZY PICKS', 'ARCADE', 'ADVENTURE']
    text(f'Preview_Row_{row+1:02}_Label', captions[row % len(captions)],
         (0, front_y-0.0528, label_z), 0.062, ink)
    left_x = -required_width/2 + CASE_WIDTH/2
    case_y = -SHELF_DEPTH/2 + 0.14
    for i in range(BOXES_PER_ROW):
        ob = tag(reference.copy(), cases)
        ob.name = PREFIX + f'Preview_GameBox_Row{row+1:02}_{i+1:02}'
        ob.parent = None
        ob.rotation_euler = (0, 0, 0)
        ob.scale = case_scale
        center_x = left_x + i * (CASE_WIDTH+CASE_GAP)
        ob.location = (center_x - (local_min.x+local_max.x)/2*case_scale.x,
                       case_y - (local_min.y+local_max.y)/2*case_scale.y,
                       deck_top - local_min.z*case_scale.z)
        # OBJECT-linked overrides affect only these preview instances. The
        # original object's mesh, material datablocks and slot links stay intact.
        for slot in ob.material_slots:
            slot.link = 'OBJECT'
        ob.material_slots[0].material = ink
        cover = [blue, cream, rose, blue, mustard][i % 5]
        ob.material_slots[1].material = cover
        case_objects.append(ob)
        cover_y = case_y-CASE_THICKNESS/2-0.0008
        accent = cream if cover != cream else blue
        cylinder_front(f'Preview_Cover_Moon_{row}_{i}',
                       (center_x+0.085, cover_y-0.0006, deck_top+0.42),
                       0.047, 0.001, accent, cases, vertices=16)
        verts = [(center_x-0.19, cover_y-0.0015, deck_top+0.17),
                 (center_x-0.055, cover_y-0.0015, deck_top+0.37),
                 (center_x+0.08, cover_y-0.0015, deck_top+0.17),
                 (center_x+0.025, cover_y-0.0016, deck_top+0.17),
                 (center_x+0.12, cover_y-0.0016, deck_top+0.30),
                 (center_x+0.20, cover_y-0.0016, deck_top+0.17)]
        mesh_object(f'Preview_Cover_Landscape_{row}_{i}', verts,
                    [(0,1,2), (3,4,5)], accent, cases)
        text(f'Preview_Game_Title_{row}_{i}',
             ['ORBIT', 'GROVE', 'DRIFT', 'NOVA', 'EMBER'][i % 5],
             (center_x, cover_y-0.002, deck_top+0.095), 0.047, accent)

box('Wood_Top_Cap', (0, 0, TOP_UNDERSIDE+PANEL_THICKNESS/2),
    (SHELF_WIDTH+0.08, SHELF_DEPTH+0.04, PANEL_THICKNESS), oak, bevel=0.024)
header_z = TOP_UNDERSIDE + 0.095
box('Wood_Header_Fascia', (0, -SHELF_DEPTH/2+0.015, header_z),
    (SHELF_WIDTH-0.04, 0.11, 0.26), oak, bevel=0.023)
box('Cream_Header_Plaque', (0, -SHELF_DEPTH/2-0.048, header_z),
    (1.55, 0.032, 0.19), cream, bevel=0.024)
text('Header_PLAY_Letters', 'P L A Y', (0.14, -SHELF_DEPTH/2-0.068, header_z),
     0.125, blue, model)
box('Controller_Simple_Body', (-0.49, -SHELF_DEPTH/2-0.070, header_z),
    (0.235, 0.018, 0.10), blue, bevel=0.025)
cross = [(-0.027,-0.009),(-0.009,-0.009),(-0.009,-0.027),
         (0.009,-0.027),(0.009,-0.009),(0.027,-0.009),
         (0.027,0.009),(0.009,0.009),(0.009,0.027),
         (-0.009,0.027),(-0.009,0.009),(-0.027,0.009)]
verts = [(x-0.55, y-SHELF_DEPTH/2-0.081, z+header_z)
         for y in (-0.004,0.004) for x,z in cross]
faces = [tuple(range(11,-1,-1)), tuple(range(12,24))]
faces += [(i,(i+1)%12,(i+1)%12+12,i+12) for i in range(12)]
mesh_object('Controller_OnePiece_Dpad', verts, faces, cream)
cylinder_front('Controller_Button_A', (-0.43, -SHELF_DEPTH/2-0.084, header_z-0.012),
               0.012, 0.007, mustard)
cylinder_front('Controller_Button_B', (-0.405, -SHELF_DEPTH/2-0.084, header_z+0.012),
               0.012, 0.007, mustard)

# Studio assets are deliberately separate from the source and export geometry.
mesh_object('Studio_Ground', [(-100,-100,-0.006),(100,-100,-0.006),
                             (100,100,-0.006),(-100,100,-0.006)],
            [(0,1,2,3)], ground_mat, studio)
world = bpy.data.worlds.get(PREFIX+'Studio_World') or bpy.data.worlds.new(PREFIX+'Studio_World')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.4, 0.4, 0.4, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.65
scene.world = world


def aim(ob, target):
    ob.rotation_euler = (Vector(target)-ob.location).to_track_quat('-Z', 'Y').to_euler()


for name, location, energy, size, color in [
        ('Key', (-3.8,-4.2,6.8), 1050, 4.8, (1.0,0.95,0.88)),
        ('Fill', (4.0,-2.6,4.4), 700, 4.0, (0.89,0.94,1.0)),
        ('Rim', (1.0,3.3,5.3), 850, 3.0, (1.0,0.97,0.91))]:
    data = bpy.data.lights.new(PREFIX+name, 'AREA')
    ob = tag(bpy.data.objects.new(PREFIX+name, data), studio)
    ob.location = location
    data.energy, data.shape, data.size, data.color = energy, 'DISK', size, color
    aim(ob, (0,0,HEIGHT/2))

camera_specs = [
    ('01_front', (0,-10,HEIGHT/2+0.8), (0,0,HEIGHT/2), SHELF_WIDTH*1.16),
    ('02_three_quarter', (7.4,-10.5,5.8), (0,0,HEIGHT*0.49), SHELF_WIDTH*1.15),
    ('03_fit_closeup', (-3.1,-4.8,2.30), (-1.35,-0.12,1.38), 2.45),
]
cameras = {}
for name, location, target, scale in camera_specs:
    data = bpy.data.cameras.new(PREFIX+name)
    ob = tag(bpy.data.objects.new(PREFIX+name, data), studio)
    ob.location = location
    data.type = 'ORTHO'
    data.ortho_scale = scale
    data.lens = 50
    data.clip_end = 1000
    aim(ob, target)
    cameras[name] = ob

scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = RENDER_SAMPLES
scene.cycles.use_denoising = True
scene.cycles.max_bounces = 6
scene.render.resolution_x = RENDER_WIDTH
scene.render.resolution_y = RENDER_HEIGHT
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.view_settings.exposure = -1.5
scene.view_settings.gamma = 1

# Validate actual evaluated geometry, not an estimated polygon budget.
bpy.context.view_layer.update()
depsgraph = bpy.context.evaluated_depsgraph_get()
source_meshes = [ob for ob in model.objects if ob.type in {'MESH','FONT'}]
triangle_count = 0
geometry_warnings = []
model_world_vertices = []
for ob in source_meshes:
    evaluated = ob.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    mesh.calc_loop_triangles()
    triangle_count += len(mesh.loop_triangles)
    model_world_vertices.extend(ob.matrix_world @ v.co for v in mesh.vertices)
    if any(v <= 0 for v in ob.scale):
        geometry_warnings.append(ob.name + ': nonpositive scale')
    if any(p.area < 1e-12 for p in mesh.polygons):
        geometry_warnings.append(ob.name + ': degenerate polygon')
    if ob.type == 'MESH':
        bm = bmesh.new()
        bm.from_mesh(mesh)
        if any(not e.is_manifold for e in bm.edges):
            geometry_warnings.append(ob.name + ': nonmanifold edge')
        bm.free()
    evaluated.to_mesh_clear()
model_min = Vector([min(v[i] for v in model_world_vertices) for i in range(3)])
model_max = Vector([max(v[i] for v in model_world_vertices) for i in range(3)])
actual_dimensions = model_max - model_min

fit_checks = []
for ob in case_objects:
    bounds = [ob.matrix_world @ Vector(v) for v in ob.bound_box]
    lo = Vector([min(v[i] for v in bounds) for i in range(3)])
    hi = Vector([max(v[i] for v in bounds) for i in range(3)])
    row = min(range(ROW_COUNT), key=lambda r: abs(lo.z-deck_tops[r]))
    checks = {
        'name': ob.name, 'row': row+1, 'world_min': list(lo), 'world_max': list(hi),
        'left_clearance': lo.x+INNER_WIDTH/2,
        'right_clearance': INNER_WIDTH/2-hi.x,
        'base_error': abs(lo.z-deck_tops[row]),
        'headroom': deck_tops[row]+ROW_HEIGHT-SHELF_THICKNESS-hi.z,
        'back_clearance': back_y-PANEL_THICKNESS/2-hi.y,
        'front_lip_clearance': lo.y-(-SHELF_DEPTH/2-0.018+0.03),
        'front_loading_headroom': (
            header_z-0.13 if row == ROW_COUNT-1
            else deck_tops[row]+ROW_HEIGHT-SHELF_THICKNESS
        ) - (hi.z+max(0.095,0.112)/2-0.018),
    }
    if min(checks[k] for k in ['left_clearance','right_clearance','headroom',
                               'back_clearance','front_lip_clearance',
                               'front_loading_headroom']) < -1e-6:
        raise RuntimeError('Case intersects the shelf: '+ob.name)
    if checks['base_error'] > 1e-5:
        raise RuntimeError('Case does not rest on the deck: '+ob.name)
    fit_checks.append(checks)

if MAKE_EXPORT_READY_COPY:
    export_objects = []
    for ob in source_meshes:
        mesh = bpy.data.meshes.new_from_object(ob.evaluated_get(depsgraph),
                                              preserve_all_data_layers=True,
                                              depsgraph=depsgraph)
        if ob.type == 'FONT':
            # Converted font caps have coincident, disconnected seam vertices.
            # Weld only this part; leave structural panel joints independent.
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-7)
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
            bm.to_mesh(mesh)
            bm.free()
        mesh.transform(ob.matrix_world)
        mesh.update()
        copied = tag(bpy.data.objects.new(PREFIX+'Export_'+ob.name, mesh), export)
        export_objects.append(copied)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in export_objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = export_objects[0]
    bpy.ops.object.join()
    export_object = bpy.context.object
    export_object.name = PREFIX+'GamingShelf_Module_ExportReady'
    export_object.matrix_world = Matrix.Identity(4)
    export_object['pivot'] = 'Ground center (0,0,0); Blender front -Y, up +Z'
    export_object.data.calc_loop_triangles()
    triangle_count = len(export_object.data.loop_triangles)
    assert len(export_object.data.materials) == len(materials)
    export.hide_render = True
    export.hide_viewport = True

assert fingerprint_reference(reference) == before_reference, 'Original reference was modified!'
report = {
    'blender_version': bpy.app.version_string,
    'source_saved_blend': SOURCE_PATH,
    'source_reference_object': reference.name,
    'source_reference_fingerprint_unchanged': True,
    'reference_mesh_local_dimensions': list(local_size),
    'reference_original_object_scale': list(reference.scale),
    'preview_case_scale_from_raw_mesh': list(case_scale),
    'unity_case_dimensions_width_height_thickness': [CASE_WIDTH,CASE_HEIGHT,CASE_THICKNESS],
    'module_dimensions_width_depth_height': list(actual_dimensions),
    'module_world_bounds_min': list(model_min),
    'module_world_bounds_max': list(model_max),
    'row_count': ROW_COUNT, 'boxes_per_row': BOXES_PER_ROW,
    'source_mesh_or_font_parts': len(source_meshes),
    'evaluated_export_triangles': triangle_count,
    'shelf_material_count': len(materials),
    'palette_srgb_hex_roughness_metallic': {k: [v[0],v[1],0.0] for k,v in PALETTE.items()},
    'geometry_warnings': geometry_warnings,
    'fit_checks': fit_checks,
    'preview_material_note': 'Original mesh/materials unchanged. Object-linked material overrides only on preview copies; cover graphics/cameras/lights are not exported.',
}
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
OUTPUT_DIR.joinpath('model_report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
scene.camera = cameras['02_three_quarter']
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.shading.type = 'SOLID'
            area.spaces.active.shading.color_type = 'MATERIAL'
scene.render.filepath = str(OUTPUT_DIR/'02_three_quarter.png')
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_DIR/'CozyGamingShelf_v01.blend'))
print('MODEL_READY '+json.dumps({k:report[k] for k in [
    'module_dimensions_width_depth_height', 'evaluated_export_triangles',
    'shelf_material_count', 'geometry_warnings', 'source_reference_fingerprint_unchanged']}), flush=True)
if RENDER_PREVIEWS:
    for name, camera in cameras.items():
        scene.camera = camera
        scene.render.filepath = str(OUTPUT_DIR/(name+'.png'))
        bpy.ops.render.render(write_still=True, scene=scene.name)
        print('PREVIEW_SAVED '+name, flush=True)
    cases.hide_render = True
    scene.camera = cameras['02_three_quarter']
    scene.render.filepath = str(OUTPUT_DIR/'04_empty_structure.png')
    bpy.ops.render.render(write_still=True, scene=scene.name)
    cases.hide_render = False
    print('PREVIEW_SAVED 04_empty_structure', flush=True)
