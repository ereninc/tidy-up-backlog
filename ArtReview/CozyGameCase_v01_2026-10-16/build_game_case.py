"""One closed, low-poly case; Shell=0 / Cover=1; legacy GameBox dimensions."""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Matrix, Vector

OUT = Path(__file__).resolve().parent
ASSET = OUT.parents[1]/'Assets/GameAssets/Model/CozyGameCase_v01'
WIDTH, THICKNESS, HEIGHT = 1.20, .20, 1.60
LEGACY_MESH_SCALE = .01
OWNER = 'codex.cozy_game_case.v01'
OUT.mkdir(parents=True, exist_ok=True)
ASSET.mkdir(parents=True, exist_ok=True)
for ob in list(bpy.data.objects):
    if ob.get('generator_owner') == OWNER:
        bpy.data.objects.remove(ob, do_unlink=True)
scene = bpy.data.scenes.get('CozyGameCase_Source') or bpy.data.scenes.new('CozyGameCase_Source')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
coll = bpy.data.collections.get('CozyGameCase_Model') or bpy.data.collections.new('CozyGameCase_Model')
if coll.name not in scene.collection.children:
    scene.collection.children.link(coll)


def canvas(name, width, height, background):
    pixels = list(background)*(width*height)
    def pixel(x, y, color):
        if 0 <= x < width and 0 <= y < height:
            index = (y*width+x)*4
            pixels[index:index+4] = color
    def rect(x0, y0, x1, y1, color):
        for y in range(max(0, int(y0)), min(height, int(y1))):
            for x in range(max(0, int(x0)), min(width, int(x1))):
                pixel(x, y, color)
    def circle(cx, cy, radius, color):
        for y in range(int(cy-radius), int(cy+radius)+1):
            for x in range(int(cx-radius), int(cx+radius)+1):
                if (x-cx)**2+(y-cy)**2 <= radius**2:
                    pixel(x, y, color)
    def triangle(a, b, c, color):
        def cross(p, q, r):
            return (q[0]-p[0])*(r[1]-p[1])-(q[1]-p[1])*(r[0]-p[0])
        for y in range(max(0, int(min(a[1], b[1], c[1]))), min(height, int(max(a[1], b[1], c[1]))+1)):
            for x in range(max(0, int(min(a[0], b[0], c[0]))), min(width, int(max(a[0], b[0], c[0]))+1)):
                signs = [cross(a,b,(x,y)), cross(b,c,(x,y)), cross(c,a,(x,y))]
                if all(v >= 0 for v in signs) or all(v <= 0 for v in signs):
                    pixel(x, y, color)
    glyphs = {
        'S':['01111','10000','10000','01110','00001','00001','11110'],
        'I':['11111','00100','00100','00100','00100','00100','11111'],
        'D':['11110','10001','10001','10001','10001','10001','11110'],
        'E':['11111','10000','10000','11110','10000','10000','11111'],
        'Q':['01110','10001','10001','10001','10101','10010','01101'],
        'U':['10001','10001','10001','10001','10001','10001','01110'],
        'T':['11111','00100','00100','00100','00100','00100','00100'],
        ' ':['00000']*7,
    }
    def label(text, cx, y, size, color):
        x0 = int(cx-(len(text)*6-1)*size/2)
        for index, character in enumerate(text):
            for row, line in enumerate(glyphs[character]):
                for column, value in enumerate(line):
                    if value == '1':
                        x = x0+(index*6+column)*size
                        yy = y+(6-row)*size
                        rect(x, yy, x+size, yy+size, color)
    def save(path):
        im = bpy.data.images.new(name, width=width, height=height, alpha=True)
        im.pixels.foreach_set(pixels)
        im.filepath_raw, im.file_format = str(path), 'PNG'
        im.save()
        im.pack()
        return im
    return rect, circle, triangle, label, save


grey = lambda value: (value, value, value, 1)
rect, circle, triangle, label, save = canvas('CGC01_Shell_Tones', 256, 256, grey(.88))
rect(133, 0, 158, 256, grey(.42))
rect(166, 0, 194, 256, grey(1))
rect(201, 0, 230, 256, grey(.94))
rect(237, 0, 256, 256, grey(.80))
# Subtle molded back badge, entirely in the shared shell atlas.
rect(25, 90, 104, 164, grey(.82))
rect(28, 93, 101, 161, grey(.89))
triangle((54, 108), (54, 145), (80, 126), grey(.78))
label('SIDE', 65, 72, 2, grey(.79))
label('QUEST', 65, 52, 2, grey(.79))
shell_image = save(ASSET/'CozyGameCase_ShellTones.png')

cream = (.914, .875, .800, 1)
blue = (.471, .612, .659, 1)
ink = (.216, .275, .314, 1)
mustard = (.835, .682, .329, 1)
rect, circle, triangle, label, save = canvas('CGC01_Cover_Preview', 384, 512, cream)
rect(18, 20, 366, 492, blue)
rect(18, 388, 366, 492, ink)
label('SIDE QUEST', 192, 420, 5, cream)
circle(280, 314, 37, mustard)
triangle((34, 113), (139, 324), (254, 113), cream)
triangle((139, 113), (257, 271), (350, 113), (.76, .81, .76, 1))
triangle((121, 286), (139, 324), (161, 282), (.98, .96, .90, 1))
rect(18, 20, 366, 86, ink)
label('SIDE QUEST', 192, 41, 3, cream)
cover_image = save(ASSET/'CozyGameCase_CoverPreview.png')


def material(name, color, image):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = color
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = color
    bsdf.inputs['Roughness'].default_value = .48
    texture = mat.node_tree.nodes.new('ShaderNodeTexImage')
    texture.image = image
    if name.endswith('CaseShell'):
        multiply = mat.node_tree.nodes.new('ShaderNodeMixRGB')
        multiply.blend_type = 'MULTIPLY'
        multiply.inputs[0].default_value = 1
        multiply.inputs[2].default_value = color
        mat.node_tree.links.new(texture.outputs['Color'], multiply.inputs[1])
        mat.node_tree.links.new(multiply.outputs[0], bsdf.inputs['Base Color'])
    else:
        mat.node_tree.links.new(texture.outputs['Color'], bsdf.inputs['Base Color'])
    return mat


shell = material('CGC01_0_CaseShell', blue, shell_image)
cover = material('CGC01_1_CoverArt', cream, cover_image)


def outline(half_width, half_height, corner):
    return [(-half_width+corner, -half_height), (half_width-corner, -half_height),
            (half_width, -half_height+corner), (half_width, half_height-corner),
            (half_width-corner, half_height), (-half_width+corner, half_height),
            (-half_width, half_height-corner), (-half_width, -half_height+corner)]


half_w, half_h, half_t = WIDTH/2, HEIGHT/2, THICKNESS/2
# Recessed cover, flat rim, edge bevel, subtle V-groove seam, closed rear.
profile = [
    (half_w-.050, half_h-.050, .025, -half_t+.004),
    (half_w-.041, half_h-.041, .030, -half_t),
    (half_w-.015, half_h-.015, .040, -half_t),
    (half_w, half_h, .055, -half_t+.025),
    (half_w, half_h, .055, -.006),
    (half_w-.006, half_h-.006, .052, 0),
    (half_w, half_h, .055, .006),
    (half_w, half_h, .055, half_t-.025),
    (half_w-.015, half_h-.015, .040, half_t),
]
vertices, faces, material_indices, categories = [], [], [], []
for hw, hh, corner, y in profile:
    vertices.extend((x, y, z) for x, z in outline(hw, hh, corner))
faces.append(tuple(range(8)))
material_indices.append(1)
categories.append('cover')
for ring in range(len(profile)-1):
    for edge in range(8):
        nxt = (edge+1)%8
        faces.append((ring*8+edge, (ring+1)*8+edge, (ring+1)*8+nxt, ring*8+nxt))
        material_indices.append(0)
        categories.append('seam' if ring in {4,5} else 'edge' if ring in {2,7} else 'frame' if ring in {0,1} else 'side')
faces.append(tuple(reversed(range((len(profile)-1)*8, len(profile)*8))))
material_indices.append(0)
categories.append('back')
# Unity assigns imported submeshes in first-use order, so shell faces come first.
faces.append(faces.pop(0))
material_indices.append(material_indices.pop(0))
categories.append(categories.pop(0))

mesh = bpy.data.meshes.new('CozyGameCase_SourceMesh')
mesh.from_pydata(vertices, [], faces)
mesh.materials.clear()
mesh.materials.append(shell)
mesh.materials.append(cover)
uv = mesh.uv_layers.new(name='UVMap')
atlas_u = {'seam': .565, 'edge': .705, 'frame': .845, 'side': .963}
for poly, mat_index, category in zip(mesh.polygons, material_indices, categories):
    poly.material_index = mat_index
    poly.use_smooth = False
    for offset, loop_index in enumerate(poly.loop_indices):
        x, y, z = vertices[mesh.loops[loop_index].vertex_index]
        if category == 'cover':
            uv.data[loop_index].uv = (.5+x/(2*profile[0][0]), .5+z/(2*profile[0][1]))
        elif category == 'back':
            uv.data[loop_index].uv = (.02+.46*(.5-x/(2*profile[-1][0])), .02+.96*(.5+z/(2*profile[-1][1])))
        else:
            uv.data[loop_index].uv = (atlas_u[category]+(-.015 if offset in {0,3} else .015),
                                      .05 if offset in {0,1} else .95)
mesh.update()
mesh.transform(Matrix.Scale(LEGACY_MESH_SCALE, 4))
mesh.update()
ob = bpy.data.objects.new('CozyGameCase', mesh)
ob['generator_owner'] = OWNER
ob['Unity_visual_localScale'] = [40,24,40]
ob['Unity_visual_rotationZ'] = 180
ob['material_contract'] = 'Shell=0, Cover=1; UV1 surface mask is generated by existing CaseMeshCombiner.'
coll.objects.link(ob)

bm = bmesh.new()
bm.from_mesh(mesh)
assert all(edge.is_manifold for edge in bm.edges), 'Case must be one closed shell.'
assert bm.calc_volume() > 0, 'Normals point inward.'
bm.free()
mesh.calc_loop_triangles()
assert len(mesh.loop_triangles) == 140
assert all(abs(a-b)<1e-6 for a,b in zip(ob.dimensions,
    (WIDTH*LEGACY_MESH_SCALE,THICKNESS*LEGACY_MESH_SCALE,HEIGHT*LEGACY_MESH_SCALE)))
assert len(mesh.materials) == 2
bpy.ops.object.select_all(action='DESELECT')
ob.select_set(True)
bpy.context.view_layer.objects.active = ob
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            space = area.spaces.active
            space.shading.type = 'MATERIAL'
            space.region_3d.view_location = (0,0,0)
            space.clip_start = .0001
            space.region_3d.view_distance = 2.8*LEGACY_MESH_SCALE
            space.region_3d.view_rotation = Vector((1,-2,.6)).to_track_quat('Z','Y')
            space.region_3d.view_perspective = 'PERSP'
bpy.ops.export_scene.fbx(filepath=str(ASSET/'CozyGameCase_v01.fbx'), use_selection=True,
    object_types={'MESH'}, use_mesh_modifiers=True, axis_forward='-Z', axis_up='Y',
    global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=False, add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CozyGameCase_v01.blend'))
report = {'blender_version': bpy.app.version_string, 'vertices_before_normal_splits': len(mesh.vertices),
    'triangles': len(mesh.loop_triangles), 'material_slots': 2, 'closed_manifold': True,
    'source_dimensions': [WIDTH*LEGACY_MESH_SCALE,THICKNESS*LEGACY_MESH_SCALE,HEIGHT*LEGACY_MESH_SCALE],
    'unity_mesh_dimensions_expected': [.012,.002,.016],
    'game_visible_dimensions': [.48,.048,.64], 'pivot': 'centre', 'cover_normal': '-Y',
    'combiner_contract': 'single mesh / Shell=0 / Cover=1', 'renders_created': False}
(OUT/'model_report.json').write_text(json.dumps(report,indent=2), encoding='utf-8')
print('CASE_READY '+json.dumps(report), flush=True)
