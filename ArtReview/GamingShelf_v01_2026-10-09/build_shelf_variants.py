"""Append two editable furniture variants to CozyGamingShelf_v01.blend.

Run: blender -b CozyGamingShelf_v01.blend -P build_shelf_variants.py
Use -- --skip-renders for geometry-only regeneration. Existing first-shelf
objects and materials are preserved; only this script's tagged objects refresh.
"""
import bpy
import bmesh
import hashlib
import json
import math
import shutil
import sys
from pathlib import Path
from mathutils import Vector, Matrix

# -------------------------- Artist parameters --------------------------
OUTPUT_DIR = Path(__file__).resolve().parent
BLEND_PATH = OUTPUT_DIR / 'CozyGamingShelf_v01.blend'
CASE_WIDTH, CASE_HEIGHT, CASE_THICKNESS = 0.48, 0.64, 0.048
CASE_GAP = 0.032
DOUBLE_COLUMNS = 10
DOUBLE_ROWS = 4
ROW_HEIGHT = 0.86
PANEL_THICKNESS = 0.11
DECK_THICKNESS = 0.085
SIDE_CLEARANCE = 0.07
DOUBLE_WIDTH = DOUBLE_COLUMNS * CASE_WIDTH + (DOUBLE_COLUMNS-1)*CASE_GAP + 2*(SIDE_CLEARANCE+PANEL_THICKNESS)
DOUBLE_DEPTH = 1.38
DOUBLE_FIRST_DECK = 0.32
DOUBLE_HEIGHT = DOUBLE_FIRST_DECK + (DOUBLE_ROWS-1)*ROW_HEIGHT + CASE_HEIGHT + 0.17
FEATURE_COLUMNS = 5
FEATURE_CASES_DEEP = 3
FEATURE_WIDTH = FEATURE_COLUMNS*CASE_WIDTH + (FEATURE_COLUMNS-1)*CASE_GAP + 2*(SIDE_CLEARANCE+PANEL_THICKNESS)
FEATURE_DEPTH = 1.04
FEATURE_HEIGHT = 3.25
FEATURE_DECK_TOP = 0.56
BEVEL = 0.018
BEVEL_SEGMENTS = 2
RENDER_SAMPLES = 48
RENDER_WIDTH, RENDER_HEIGHT = 1440, 1080
RENDER_PREVIEWS = '--skip-renders' not in sys.argv
RENDER_NAMES = next((arg.split('=',1)[1].split(',') for arg in sys.argv if arg.startswith('--render=')), None)
# -----------------------------------------------------------------------
OWNER = 'codex.cozy_gaming_shelf.variants.v01'
KEY = 'cozy_shelf_owner'
if not bpy.data.objects.get('CGS01_Module_Pivot_Ground_Center'):
    raise RuntimeError('Open the delivered CozyGamingShelf_v01.blend first.')
reference = bpy.data.objects['GameBox']
oak, cream, blue, mustard = [bpy.data.materials['CGS01_'+name] for name in
                            ['Warm_Oak', 'Soft_Cream', 'Dusty_Blue', 'Mustard']]
ink = bpy.data.materials['CGS01_Preview_Ink']
rose = bpy.data.materials['CGS01_Preview_Dusty_Rose']
ground = bpy.data.materials['CGS01_Preview_Ground']
local_size = Vector([max(v.co[i] for v in reference.data.vertices)-min(v.co[i] for v in reference.data.vertices)
                     for i in range(3)])
case_scale = Vector((CASE_WIDTH/local_size.x, CASE_THICKNESS/local_size.y, CASE_HEIGHT/local_size.z))
if ROW_HEIGHT - DECK_THICKNESS < CASE_HEIGHT + 0.07:
    raise ValueError('Row pitch leaves insufficient case headroom.')
if DOUBLE_DEPTH/2 - PANEL_THICKNESS/2 < CASE_THICKNESS + 0.17:
    raise ValueError('Double shelf face depth is too small.')


def preserved_fingerprint():
    payload = []
    for ob in sorted(bpy.data.objects, key=lambda item: item.name):
        if not (ob.name.startswith('CGS01_') or ob.name in {'GameBox', 'Placeholder_Shelf_Type1'}):
            continue
        item = [ob.name, ob.type, [list(row) for row in ob.matrix_world],
                ob.parent.name if ob.parent else None,
                sorted(c.name for c in ob.users_collection),
                [(m.material.name if m.material else None, m.link) for m in ob.material_slots]]
        if ob.type == 'MESH':
            item += [[list(v.co) for v in ob.data.vertices],
                     [(list(p.vertices), p.material_index, p.use_smooth) for p in ob.data.polygons]]
        if ob.type == 'FONT':
            item += [ob.data.body, ob.data.size, ob.data.extrude]
        item += [[(m.name, m.type, getattr(m, 'width', None), getattr(m, 'segments', None)) for m in ob.modifiers]]
        payload.append(item)
    for mat in sorted(bpy.data.materials, key=lambda item: item.name):
        if not (mat.name.startswith('CGS01_') or mat.name in {'m_CaseShell', 'm_CoverArt'}):
            continue
        shader = mat.node_tree.nodes.get('Principled BSDF') if mat.node_tree else None
        payload.append([mat.name, list(mat.diffuse_color),
                        [list(shader.inputs['Base Color'].default_value), shader.inputs['Roughness'].default_value,
                         shader.inputs['Metallic'].default_value] if shader else None])
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()


before = preserved_fingerprint()
backup = OUTPUT_DIR / 'CozyGamingShelf_v01_before_variants.blend'
if not backup.exists():
    shutil.copy2(BLEND_PATH, backup)
for ob in list(bpy.data.objects):
    if ob.get(KEY) == OWNER:
        bpy.data.objects.remove(ob, do_unlink=True)
for group in [bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights]:
    for data in list(group):
        if data.get(KEY) == OWNER and not data.users:
            group.remove(data)


def mesh_object(name, verts, faces, mat, coll):
    mesh = bpy.data.meshes.new(name+'_Mesh')
    mesh[KEY] = OWNER
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    ob = bpy.data.objects.new(name, mesh)
    ob[KEY] = OWNER
    coll.objects.link(ob)
    if mat:
        mesh.materials.append(mat)
    return ob


class Module:
    def __init__(self, prefix, name):
        self.prefix = prefix
        self.scene = bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
        self.scene[KEY] = OWNER
        bpy.context.window.scene = self.scene
        self.scene.unit_settings.system = 'METRIC'
        self.scene.unit_settings.scale_length = 1
        self.model = self.collection('01_Editable_Model')
        self.preview = self.collection('02_Preview_Only')
        self.studio = self.collection('03_Studio_Only')
        self.export = self.collection('04_ExportReady_Hidden')
        self.export.hide_viewport = False
        self.export.hide_render = False
        self.root = bpy.data.objects.new(prefix+'Pivot_Ground_Center', None)
        self.root[KEY] = OWNER
        self.root['pivot'] = 'Ground center; front -Y, up +Z'
        self.model.objects.link(self.root)
        self.parts, self.fit_checks, self.cameras = [], [], {}

    def collection(self, suffix):
        name = self.prefix+suffix
        coll = bpy.data.collections.get(name)
        if coll is None:
            coll = bpy.data.collections.new(name)
            coll[KEY] = OWNER
        elif coll.get(KEY) != OWNER:
            raise RuntimeError('User-owned collection: '+name)
        if name not in self.scene.collection.children:
            self.scene.collection.children.link(coll)
        return coll

    def own(self, ob, preview=False):
        if not preview:
            ob.parent = self.root
            self.parts.append(ob)
        return ob

    def mesh(self, name, verts, faces, mat, preview=False):
        return self.own(mesh_object(self.prefix+name, verts, faces, mat,
                                   self.preview if preview else self.model), preview)

    def box(self, name, center, size, mat, bevel=BEVEL, preview=False):
        signs = [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                 (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]
        verts = [tuple(sign[i]*size[i]/2 for i in range(3)) for sign in signs]
        faces = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        ob = self.mesh(name, verts, faces, mat, preview)
        ob.location = center
        if bevel:
            mod = ob.modifiers.new('Small_Silhouette_Bevel', 'BEVEL')
            mod.width, mod.segments, mod.limit_method = min(bevel, min(size)*0.28), BEVEL_SEGMENTS, 'ANGLE'
        return ob

    def profile(self, name, points_yz, xcenter, thickness, mat):
        n = len(points_yz)
        verts = [(xcenter+side*thickness/2, y, z) for side in [-1,1] for y,z in points_yz]
        faces = [tuple(range(n-1,-1,-1)), tuple(range(n,2*n))]
        faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        ob = self.mesh(name, verts, faces, mat)
        bevel = ob.modifiers.new('Profile_Edge_Bevel', 'BEVEL')
        bevel.width, bevel.segments = BEVEL, BEVEL_SEGMENTS
        return ob

    def text(self, name, body, center, size, mat, preview=False, rear=False):
        data = bpy.data.curves.new(self.prefix+name, 'FONT')
        data[KEY] = OWNER
        data.body, data.size = body, size
        data.align_x = data.align_y = 'CENTER'
        data.resolution_u, data.extrude = 2, 0 if preview else 0.0008
        ob = bpy.data.objects.new(self.prefix+name, data)
        ob[KEY] = OWNER
        (self.preview if preview else self.model).objects.link(ob)
        ob.location = center
        ob.rotation_euler = (math.pi/2, 0, math.pi if rear else 0)
        data.materials.append(mat)
        return self.own(ob, preview)

    def circle(self, name, center, radius, depth, mat, preview=False):
        n = 12
        verts = [(radius*math.cos(i*2*math.pi/n), y, radius*math.sin(i*2*math.pi/n))
                 for y in [-depth/2,depth/2] for i in range(n)]
        faces = [tuple(range(n-1,-1,-1)), tuple(range(n,2*n))]
        faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        ob = self.mesh(name, verts, faces, mat, preview)
        ob.location = center
        return ob

    def case(self, name, x, y, bottom, row, column, rear=False):
        ob = bpy.data.objects.new(self.prefix+name, reference.data)
        ob[KEY] = OWNER
        self.preview.objects.link(ob)
        ob.location = (x,y,bottom+CASE_HEIGHT/2)
        ob.scale = case_scale
        ob.rotation_euler.z = math.pi if rear else 0
        colors = [blue,cream,rose,blue,mustard]
        for i, slot in enumerate(ob.material_slots):
            slot.link = 'OBJECT'
            slot.material = ink if i == 0 else colors[column%len(colors)]
        ob['case_preview'] = True
        ob['row'], ob['column'], ob['rear'] = row, column, rear
        ob['deck_top'] = bottom
        # Cover graphics are separate preview assets, never part of the model.
        side = -1 if rear else 1
        face_y = y-side*(CASE_THICKNESS/2+0.0015)
        self.circle(name+'_Moon', (x+side*0.10, face_y, bottom+0.42), 0.073, 0.001, cream, True)
        points = [(-0.205,0.20),(-0.07,0.36),(0.02,0.24),(0.11,0.31),(0.205,0.18),(0.205,0.10),(-0.205,0.10)]
        self.mesh(name+'_Mountains', [(x+side*a,face_y-side*0.002,bottom+z) for a,z in points],
                  [tuple(range(len(points)))], blue if column%5 == 1 else mustard, True)
        titles = ['ORBIT','GROVE','DRIFT','NOVA','EMBER']
        self.text(name+'_Title', titles[column%5], (x,face_y-side*0.004,bottom+0.075),
                  0.048, ink if column%5 in [1,4] else cream, True, rear)
        self.fit_checks.append({'object': ob.name, 'row': row, 'column': column,
                                'rear': rear, 'deck_top': bottom})
        return ob

    def label(self, name, x, y, z, width, text=None, rear=False):
        self.box(name+'_Cream_Plate', (x,y,z), (width,0.025,0.105), cream, 0.014)
        side = 1 if rear else -1
        for n, xx in enumerate([x-width/2+0.045, x+width/2-0.045]):
            self.circle(name+'_Mustard_Pin_'+str(n), (xx,y+side*0.015,z), 0.011, 0.007, mustard)
        if text:
            self.text(name+'_Preview_Label', text, (x,y+side*0.018,z), 0.047, ink, True, rear)

    def setup_studio(self, specs, height):
        mesh_object(self.prefix+'Ground', [(-100,-100,-0.006),(100,-100,-0.006),
                                         (100,100,-0.006),(-100,100,-0.006)],
                    [(0,1,2,3)], ground, self.studio)
        self.scene.world = bpy.data.worlds['CGS01_Studio_World']
        for name, pos, energy, size, color in [
                ('Key',(-3.8,-4.2,7.4),1050,4.8,(1,.95,.88)),
                ('Fill',(4,-2.6,4.4),700,4,(.89,.94,1)),
                ('Rim',(1,3.3,5.3),850,3,(1,.97,.91))]:
            data = bpy.data.lights.new(self.prefix+name, 'AREA')
            data[KEY] = OWNER
            ob = bpy.data.objects.new(self.prefix+name, data)
            ob[KEY] = OWNER
            self.studio.objects.link(ob)
            ob.location = pos
            data.energy, data.shape, data.size, data.color = energy,'DISK',size,color
            aim(ob, (0,0,height/2))
        for name, pos, target, scale in specs:
            data = bpy.data.cameras.new(self.prefix+name)
            data[KEY] = OWNER
            ob = bpy.data.objects.new(self.prefix+name, data)
            ob[KEY] = OWNER
            self.studio.objects.link(ob)
            ob.location = pos
            data.type, data.ortho_scale, data.clip_end = 'ORTHO',scale,1000
            aim(ob, target)
            self.cameras[name] = ob
        scene = self.scene
        scene.render.engine = 'CYCLES'
        scene.cycles.device, scene.cycles.samples, scene.cycles.use_denoising = 'CPU',RENDER_SAMPLES,True
        scene.cycles.max_bounces = 6
        scene.render.resolution_x, scene.render.resolution_y = RENDER_WIDTH,RENDER_HEIGHT
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        scene.view_settings.view_transform, scene.view_settings.look = 'Standard','None'
        scene.view_settings.exposure, scene.view_settings.gamma = -1.5,1
        scene.camera = next(iter(self.cameras.values()))

    def validate_and_export_copy(self):
        bpy.context.window.scene = self.scene
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        copies, points = [], []
        for ob in self.parts:
            mesh = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            mesh[KEY] = OWNER
            bm = bmesh.new()
            bm.from_mesh(mesh)
            if ob.type == 'FONT':
                bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-7)
                bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
            if any(not e.is_manifold for e in bm.edges):
                raise RuntimeError('Nonmanifold model part: '+ob.name)
            if any(f.calc_area() < 1e-12 for f in bm.faces):
                raise RuntimeError('Degenerate model face: '+ob.name)
            if bm.calc_volume(signed=True) <= 0:
                raise RuntimeError('Incorrect outward normals: '+ob.name)
            bm.to_mesh(mesh)
            bm.free()
            if min(ob.scale) <= 0:
                raise RuntimeError('Nonpositive scale: '+ob.name)
            mesh.transform(ob.matrix_world)
            points.extend(v.co.copy() for v in mesh.vertices)
            copied = bpy.data.objects.new(self.prefix+'ExportPart_'+ob.name, mesh)
            copied[KEY] = OWNER
            self.export.objects.link(copied)
            copies.append(copied)
        bpy.ops.object.select_all(action='DESELECT')
        for ob in copies:
            ob.select_set(True)
        bpy.context.view_layer.objects.active = copies[0]
        bpy.ops.object.join()
        merged = bpy.context.object
        merged.name = self.prefix+'Module_ExportReady'
        merged.matrix_world = Matrix.Identity(4)
        merged['pivot'] = 'Ground center; front -Y, up +Z; identity transforms'
        merged.data.calc_loop_triangles()
        self.export.hide_render = self.export.hide_viewport = True
        lo = [min(p[i] for p in points) for i in range(3)]
        hi = [max(p[i] for p in points) for i in range(3)]
        # Fit complete-module views from projected bounds; closeups retain framing.
        corners = [Vector((x,y,z)) for x in [lo[0],hi[0]] for y in [lo[1],hi[1]] for z in [lo[2],hi[2]]]
        for name,camera in self.cameras.items():
            if 'box_fit' in name:
                continue
            rotation = camera.rotation_euler.to_matrix()
            local = [rotation.transposed() @ (p-camera.location) for p in corners]
            left,right = min(p.x for p in local),max(p.x for p in local)
            bottom,top = min(p.y for p in local),max(p.y for p in local)
            camera.location += rotation @ Vector(((left+right)/2,(bottom+top)/2,0))
            camera.data.ortho_scale = max(right-left,(top-bottom)*RENDER_WIDTH/RENDER_HEIGHT)*1.09
        assert abs(lo[2]) < 1e-6
        assert len(merged.data.materials) == 4
        for fit in self.fit_checks:
            ob = bpy.data.objects[fit['object']]
            bounds = [ob.matrix_world @ Vector(v) for v in ob.bound_box]
            minimum = Vector([min(p[i] for p in bounds) for i in range(3)])
            maximum = Vector([max(p[i] for p in bounds) for i in range(3)])
            assert abs(minimum.z-fit['deck_top']) < 1e-5, ob.name
            half = (DOUBLE_WIDTH if self.prefix == 'CGS02_' else FEATURE_WIDTH)/2-PANEL_THICKNESS
            assert minimum.x >= -half+SIDE_CLEARANCE-1e-5 and maximum.x <= half-SIDE_CLEARANCE+1e-5
            if self.prefix == 'CGS02_':
                front_clear = DOUBLE_DEPTH/2 - 0.055 - max(abs(minimum.y),abs(maximum.y))
                back_clear = min(abs(minimum.y),abs(maximum.y))-PANEL_THICKNESS/2
                headroom = ROW_HEIGHT-DECK_THICKNESS-CASE_HEIGHT if fit['row'] < DOUBLE_ROWS else DOUBLE_HEIGHT-maximum.z
            else:
                front_clear = minimum.y-(-FEATURE_DEPTH/2+0.055)
                back_clear = (FEATURE_DEPTH/2-0.15)-maximum.y
                headroom = 1.40-maximum.z
                lane_half = (CASE_WIDTH+CASE_GAP)/2-0.007
                center = ob.location.x
                assert minimum.x >= center-lane_half-1e-5 and maximum.x <= center+lane_half+1e-5
            assert min(front_clear,back_clear,headroom) > 0
            fit.update({'base_error':abs(minimum.z-fit['deck_top']), 'front_clearance':front_clear,
                        'back_clearance':back_clear, 'headroom':headroom})
        return {'scene':self.scene.name, 'source_collection':self.model.name,
                'export_copy':merged.name, 'evaluated_triangles':len(merged.data.loop_triangles),
                'material_count':len(merged.data.materials), 'editable_parts':len(self.parts),
                'dimensions_width_depth_height':[hi[i]-lo[i] for i in range(3)],
                'bounds_min':lo, 'bounds_max':hi, 'preview_cases':len(self.fit_checks),
                'fit_checks':self.fit_checks, 'nonmanifold_edges':0, 'nonpositive_scales':0,
                'degenerate_faces':0, 'outward_normals_checked':True}


def aim(ob, target):
    ob.rotation_euler = (Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()


# Double sided: open ends, shared cream spine, four cantilever decks per face.
double = Module('CGS02_', 'CozyGamingShelf_DoubleSided_v01')
double.box('Oak_Stable_Platform', (0,0,0.115), (DOUBLE_WIDTH+0.08,DOUBLE_DEPTH+0.07,0.23), oak,0.028)
spine_top = DOUBLE_HEIGHT-0.11
double.box('Cream_Central_Spine', (0,0,(spine_top+0.23)/2),
           (DOUBLE_WIDTH-0.08,PANEL_THICKNESS,spine_top-0.23), cream,0.03)
for end in [-1,1]:
    xx = end*(DOUBLE_WIDTH/2-PANEL_THICKNESS/2)
    double.box('Oak_Spine_End_'+str(end), (xx,0,(spine_top+0.23)/2),
               (PANEL_THICKNESS,0.155,spine_top-0.23),oak,0.022)
double.box('Oak_Rounded_Top', (0,0,DOUBLE_HEIGHT-0.055), (DOUBLE_WIDTH+0.02,0.19,0.11),oak,0.028)
for rear in [False,True]:
    side = 1 if rear else -1
    face = 'Back' if rear else 'Front'
    lip_y = side*(DOUBLE_DEPTH/2-0.025)
    deck_near, deck_far = PANEL_THICKNESS/2, DOUBLE_DEPTH/2-0.055
    double.box(face+'_Blue_Kickstrip', (0,side*(DOUBLE_DEPTH/2+0.038),0.115),
               (DOUBLE_WIDTH-0.16,0.018,0.08),blue,0.008)
    for row in range(DOUBLE_ROWS):
        z = DOUBLE_FIRST_DECK+row*ROW_HEIGHT
        name = face+'_Row_'+str(row+1)
        double.box(name+'_Oak_Deck', (0,side*(deck_near+deck_far)/2,z-DECK_THICKNESS/2),
                   (DOUBLE_WIDTH,deck_far-deck_near,DECK_THICKNESS),oak)
        double.box(name+'_Blue_Retaining_Lip', (0,lip_y,z-0.006),
                   (DOUBLE_WIDTH,0.06,0.095),blue,0.014)
        double.label(name,0,side*(DOUBLE_DEPTH/2+0.019),z-0.012,1.55,
                     ['COZY PICKS','ARCADE','ADVENTURE','DISCOVER'][row%4],rear)
        for col in range(DOUBLE_COLUMNS):
            xx = (col-(DOUBLE_COLUMNS-1)/2)*(CASE_WIDTH+CASE_GAP)
            double.case(name+'_Case_'+str(col+1),xx,side*(DOUBLE_DEPTH/2-0.165),z,row+1,col+1,rear)
    double.label(face+'_Top_Sign',0,side*0.102,DOUBLE_HEIGHT-0.085,1.40)
    double.text(face+'_PLAY', 'P L A Y', (0,side*0.121,DOUBLE_HEIGHT-0.085),0.073,blue,rear=rear)
double.setup_studio([
    ('05_double_front',(7.5,-10,5.6),(0,0,DOUBLE_HEIGHT/2),DOUBLE_WIDTH*1.18),
    ('06_double_back',(-7.5,10,5.6),(0,0,DOUBLE_HEIGHT/2),DOUBLE_WIDTH*1.18),
    ('07_double_empty_end',(9,-4.8,5.3),(0,0,DOUBLE_HEIGHT/2),DOUBLE_WIDTH*1.08),
],DOUBLE_HEIGHT)
double_report = double.validate_and_export_copy()

# Featured display: five flat box lanes below a replaceable upright poster.
featured = Module('CGS03_', 'CozyGamingShelf_Featured_v01')
featured.box('Oak_Plinth', (0,0,0.075), (FEATURE_WIDTH+0.07,FEATURE_DEPTH+0.04,0.15),oak,0.024)
featured.box('Cream_Tray_Cabinet', (0,0,0.33), (FEATURE_WIDTH-0.15,FEATURE_DEPTH-0.06,0.36),cream,0.022)
featured.box('Oak_Tray_Deck', (0,-0.01,FEATURE_DECK_TOP-DECK_THICKNESS/2),
             (FEATURE_WIDTH-2*PANEL_THICKNESS,FEATURE_DEPTH-0.02,DECK_THICKNESS),oak)
side_points = [(-FEATURE_DEPTH/2,0.15),(FEATURE_DEPTH/2,0.15),
               (FEATURE_DEPTH/2,FEATURE_HEIGHT-0.17),(FEATURE_DEPTH/2-0.08,FEATURE_HEIGHT-0.09),
               (0.12,FEATURE_HEIGHT-0.09),(0.34,FEATURE_HEIGHT-0.52),
               (0.34,0.64),(-FEATURE_DEPTH/2,0.64)]
for end in [-1,1]:
    xx = end*(FEATURE_WIDTH/2-PANEL_THICKNESS/2)
    featured.profile('Cream_Shaped_Side_'+str(end),side_points,xx,PANEL_THICKNESS,cream)
    featured.box('Oak_Rear_Post_'+str(end),(xx,FEATURE_DEPTH/2-0.035,1.71),
                 (PANEL_THICKNESS+0.02,0.08,2.90),oak)
featured.box('Cream_Upright_Back', (0,0.40,1.64), (FEATURE_WIDTH-2*PANEL_THICKNESS,0.12,2.18),cream)
header_profile = [(0.34,2.73),(0.46,2.73),(0.52,3.08),(0.44,3.16),(0.12,3.16)]
featured.profile('Cream_Angled_Header',header_profile,0,FEATURE_WIDTH-2*PANEL_THICKNESS,cream)
featured.box('Oak_Header_Crown', (0,0.30,3.205), (FEATURE_WIDTH+0.02,0.44,0.09),oak,0.021)
featured.box('Cream_Header_Plaque', (0,0.076,3.195), (1.58,0.022,0.16),cream,0.019)
featured.text('SPOTLIGHT_Letters', 'S P O T L I G H T', (0,0.060,3.195),0.085,blue)
for xx in [-0.94,0.94]:
    featured.circle('Header_Mustard_Badge_'+str(xx),(xx,0.074,3.195),0.036,0.015,mustard)
featured.box('Blue_Tray_Front', (0,-FEATURE_DEPTH/2+0.015,0.48),
             (FEATURE_WIDTH-2*PANEL_THICKNESS,0.06,0.30),blue,0.021)
featured.box('Oak_Tray_Front_Lip', (0,-FEATURE_DEPTH/2+0.025,0.59),
             (FEATURE_WIDTH-2*PANEL_THICKNESS,0.06,0.08),oak,0.012)
lane_pitch = CASE_WIDTH+CASE_GAP
for divider in range(FEATURE_COLUMNS-1):
    xx = (divider-(FEATURE_COLUMNS-2)/2)*lane_pitch
    featured.box('Blue_Lane_Divider_'+str(divider+1), (xx,-0.09,FEATURE_DECK_TOP+0.021),
                 (0.014,0.75,0.042),blue,0.004)
for col in range(FEATURE_COLUMNS):
    xx = (col-(FEATURE_COLUMNS-1)/2)*lane_pitch
    featured.label('Lane_'+str(col+1),xx,-FEATURE_DEPTH/2-0.020,0.46,0.39,
                   ['NEW','COZY','PICKS','PLAY','GEMS'][col%5])
    for depth in range(FEATURE_CASES_DEEP):
        featured.case('Lane_'+str(col+1)+'_Case_'+str(depth+1),xx,-0.34+depth*0.09,
                      FEATURE_DECK_TOP,1,col+1)

poster_z, poster_w, poster_h = 2.005,2.10,1.25
featured.box('Oak_Poster_Frame_Back', (0,0.309,poster_z), (poster_w+0.15,0.055,poster_h+0.15),oak,0.019)
featured.box('Cream_Poster_Mat', (0,0.274,poster_z), (poster_w+0.075,0.022,poster_h+0.075),cream,0.012)
poster_surface = featured.box('Poster_Replaceable_Surface', (0,0.257,poster_z),
                             (poster_w,0.012,poster_h),blue,0.005)
poster_surface['purpose'] = 'Replace with your poster material; art in Preview_Only is removable.'
# Explicit front-facing planar UVs for a future Unity poster material.
uv = poster_surface.data.uv_layers.new(name='PosterUV')
for poly in poster_surface.data.polygons:
    for li in poly.loop_indices:
        co = poster_surface.data.vertices[poster_surface.data.loops[li].vertex_index].co
        uv.data[li].uv = (co.x/poster_w+0.5,co.z/poster_h+0.5)

# Native mesh poster illustration: cream moon, two quiet hills and a path.
featured.circle('Poster_Art_Moon', (0.51,0.246,2.24),0.16,0.002,cream,True)
featured.mesh('Poster_Art_Distant_Hills',
              [(-1.02,0.243,1.65),(-1.02,0.243,1.92),(-0.63,0.243,2.22),
               (-0.08,0.243,1.85),(0.34,0.243,2.05),(1.02,0.243,1.76),(1.02,0.243,1.65)],
              [tuple(range(7))],cream,True)
featured.mesh('Poster_Art_Near_Hills',
              [(-1.02,0.240,1.65),(-1.02,0.240,1.79),(-0.48,0.240,1.98),
               (0.18,0.240,1.74),(0.61,0.240,1.95),(1.02,0.240,1.75),(1.02,0.240,1.65)],
              [tuple(range(7))],oak,True)
featured.mesh('Poster_Art_Path', [(-0.15,0.237,1.65),(0.36,0.237,1.65),
                                (-0.19,0.237,1.92),(-0.25,0.237,1.92)],[(0,1,2,3)],mustard,True)
featured.text('Poster_Art_Title','YOUR NEXT ADVENTURE',(0,0.242,2.49),0.104,cream,True)
featured.text('Poster_Art_Subtitle','F I N D   Y O U R   N E X T   W O R L D',
              (0,0.242,1.49),0.053,cream,True)
featured.setup_studio([
    ('08_featured_three_quarter',(4.9,-7.2,4.4),(0,0,1.62),4.20),
    ('09_featured_front',(0,-8,3.15),(0,0,1.62),4.15),
    ('10_featured_box_fit',(-2.9,-4.2,2.35),(0,-0.09,0.86),3.45),
],FEATURE_HEIGHT)
featured_report = featured.validate_and_export_copy()

assert preserved_fingerprint() == before, 'Existing shelf/reference/material changed!'
report = {'blender_version':bpy.app.version_string, 'first_model_and_reference_preserved':True,
          'preserved_fingerprint':before, 'backup':backup.name,
          'preview_case_scale_from_raw_mesh':list(case_scale),
          'double_sided':double_report, 'featured_display':featured_report,
          'shared_model_materials':[oak.name,cream.name,blue.name,mustard.name],
          'note':'Preview cases, poster art, labels, cameras and lights excluded from export copies. No FBX export or Unity changes.'}
(OUTPUT_DIR/'variants_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

# Save with the double-sided review scene active. Existing first scene retained.
bpy.context.window.scene = double.scene
double.scene.camera = double.cameras['05_double_front']
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.shading.type = 'SOLID'
            area.spaces.active.shading.color_type = 'MATERIAL'
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
print('VARIANTS_READY '+json.dumps({'double_triangles':double_report['evaluated_triangles'],
                                  'featured_triangles':featured_report['evaluated_triangles'],
                                  'existing_preserved':True}),flush=True)
if RENDER_PREVIEWS:
    for module in [double,featured]:
        bpy.context.window.scene = module.scene
        for name,camera in module.cameras.items():
            if RENDER_NAMES is not None and name not in RENDER_NAMES:
                continue
            module.scene.camera = camera
            module.preview.hide_render = 'empty' in name
            module.scene.render.filepath = str(OUTPUT_DIR/(name+'.png'))
            bpy.ops.render.render(write_still=True,scene=module.scene.name)
            module.preview.hide_render = False
            print('PREVIEW_SAVED '+name,flush=True)
