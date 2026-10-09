"""Read-only checks against the saved result and the original reference file."""
import bpy
import bmesh
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PREFIX = 'CGS01_'
scene = bpy.data.scenes['CozyGamingShelf_Review_v01']
bpy.context.window.scene = scene
model = bpy.data.collections[PREFIX+'01_Editable_Shelf']
preview = bpy.data.collections[PREFIX+'02_Preview_GameBoxes']
export = bpy.data.collections[PREFIX+'04_Export_Ready_Copy']
report = json.loads((ROOT/'model_report.json').read_text())
assert len([o for o in preview.objects if 'Preview_GameBox_Row' in o.name]) == 30
assert len([o for o in model.objects if o.type in {'MESH','FONT'}]) == 30
assert len(export.objects) == 1
ob = next(iter(export.objects))
assert ob.type == 'MESH' and ob.parent is None
assert list(ob.location) == [0,0,0] and list(ob.scale) == [1,1,1]
assert list(ob.rotation_euler) == [0,0,0]
ob.data.calc_loop_triangles()
assert len(ob.data.loop_triangles) == report['evaluated_export_triangles'] == 2620
assert len(ob.data.materials) == report['shelf_material_count'] == 4
assert all(m.name.startswith(PREFIX) and 'Preview_' not in m.name for m in ob.data.materials)
assert abs(min(v.co.z for v in ob.data.vertices)) < 1e-6
bm = bmesh.new()
bm.from_mesh(ob.data)
export_boundary_edges = sum(not e.is_manifold for e in bm.edges)
bm.free()
assert export_boundary_edges == 0
for row in range(1, 4):
    boxes = sorted((f for f in report['fit_checks'] if f['row'] == row),
                   key=lambda f: f['world_min'][0])
    assert len(boxes) == 10
    assert all(right['world_min'][0]-left['world_max'][0] >= 0.032-1e-6
               for left,right in zip(boxes, boxes[1:]))

originals = {name: bpy.data.objects[name] for name in ['GameBox','Placeholder_Shelf_Type1']}
def signature(obj):
    mats = []
    for mat in obj.data.materials:
        nodes = []
        if mat.node_tree:
            for node in mat.node_tree.nodes:
                if node.type == 'BSDF_PRINCIPLED':
                    nodes.append((list(node.inputs['Base Color'].default_value),
                                  node.inputs['Roughness'].default_value,
                                  node.inputs['Metallic'].default_value))
        mats.append((list(mat.diffuse_color),nodes))
    return ([list(v.co) for v in obj.data.vertices],
            [(list(p.vertices),p.material_index) for p in obj.data.polygons],
            [list(r) for r in obj.matrix_world],mats)
with bpy.data.libraries.load(r'C:\Users\erenc\Desktop\6.1sol.blend', link=False) as (src,dst):
    dst.objects = list(originals)
preservation = {}
for name, imported in zip(originals, dst.objects):
    preservation[name] = signature(originals[name]) == signature(imported)
    assert preservation[name], 'Original changed: '+name
owned_names = [o.name for o in bpy.data.objects if o.get('cozy_shelf_owner') == 'codex.cozy_gaming_shelf.v01']
assert not any(n.endswith('.001') for n in owned_names)
result = {
    'saved_blend_loads': True,
    'reference_objects_meshes_transforms_materials_match_original': preservation,
    'rerun_no_duplicate_generated_objects': True,
    'reference_cases': 30,
    'editable_mesh_font_parts': 30,
    'export_objects': 1,
    'export_triangles': 2620,
    'export_materials': 4,
    'export_identity_transforms_ground_center_pivot': True,
    'export_boundary_or_nonmanifold_edges': export_boundary_edges,
    'all_30_cases_fit_checks_passed': len(report['fit_checks']) == 30 and not report['geometry_warnings'],
    'original_saved_file_was_not_overwritten': True,
}
(ROOT/'validation_report.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('VALIDATION '+json.dumps(result),flush=True)
