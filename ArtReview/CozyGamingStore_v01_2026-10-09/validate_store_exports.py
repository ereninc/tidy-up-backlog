"""Import each exported FBX into a new empty scene; compare to saved manifest.

Run in a separate background Blender: blender -b --factory-startup -P this.py
Does not delete existing objects or overwrite the editable source .blend.
"""
import bpy
import bmesh
import json
from pathlib import Path
from mathutils import Vector,Matrix

ROOT = Path(__file__).resolve().parent
manifest = json.loads((ROOT/'fbx_export_manifest.json').read_text(encoding='utf-8'))
report = json.loads((ROOT/'store_report.json').read_text(encoding='utf-8'))
result = {'imported_into_new_empty_scenes':True,'models':{},'unity_validation_performed':False}
for key,filename in [('environment','CozyGamingStore_Environment.fbx'),
                     ('shelf_placeholders','CozyGamingStore_ShelfPlaceholders.fbx')]:
    scene = bpy.data.scenes.new('RoundTrip_'+key)
    bpy.context.window.scene = scene
    scene.unit_settings.system,scene.unit_settings.scale_length = 'METRIC',1
    bpy.ops.import_scene.fbx(filepath=str(ROOT/filename),global_scale=1,use_custom_normals=True)
    bpy.context.view_layer.update()
    actual = {ob.name:ob for ob in scene.objects}
    expected = {item['name']:item for item in manifest[key]}
    assert set(actual) == set(expected), (key,set(actual)-set(expected),set(expected)-set(actual))
    max_matrix_error,max_bounds_error = 0,0
    total_triangles,open_parts = 0,[]
    closed_count,negative_count = 0,0
    for name,ob in actual.items():
        item = expected[name]
        assert ob.type == item['type'] and ob.type in {'MESH','EMPTY'},name
        assert (ob.parent.name if ob.parent else None) == item['parent'],name
        matrix_error = max(abs(ob.matrix_world[i][j]-item['matrix_world'][i][j]) for i in range(4) for j in range(4))
        max_matrix_error = max(max_matrix_error,matrix_error)
        assert matrix_error < 1e-4,(name,matrix_error)
        assert min(ob.scale)>0,name
        if ob.parent is None:
            assert max(abs(v-1) for v in ob.scale) < 1e-6,name
        if ob.type != 'MESH':
            continue
        points = [ob.matrix_world @ Vector(v) for v in ob.bound_box]
        lo = [min(p[i] for p in points) for i in range(3)]
        hi = [max(p[i] for p in points) for i in range(3)]
        error = max(abs(a-b) for a,b in zip(lo+hi,item['bounds_min']+item['bounds_max']))
        max_bounds_error = max(max_bounds_error,error)
        assert error < 1e-4,(name,error)
        ob.data.calc_loop_triangles()
        count = len(ob.data.loop_triangles)
        assert count == item['triangles'],(name,count,item['triangles'])
        total_triangles += count
        materials = [m.name if m else None for m in ob.data.materials]
        # Reused material names acquire .001 suffixes when importing the second
        # independent FBX into this validation process; strip only that suffix.
        canonical = lambda n: n[:-4] if n and n[-4:].startswith('.') and n[-3:].isdigit() else n
        materials = [canonical(n) for n in materials]
        assert materials == item['materials'],(name,materials,item['materials'])
        counts = {}
        for tri in ob.data.loop_triangles:
            mat = materials[tri.material_index] if materials else None
            counts[mat] = counts.get(mat,0)+1
        assert counts == item['triangles_per_material'],(name,counts,item['triangles_per_material'])
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        edges = sum(not e.is_manifold for e in bm.edges)
        degenerates = sum(f.calc_area()<1e-12 for f in bm.faces)
        assert degenerates == 0,name
        if edges:
            open_parts.append({'name':name,'edges':edges})
            assert 'Mountains' in name or 'Poster_Trail' in name, ('Unexpected open geometry',name,edges)
            front = Vector(item['poster_front_world_normal'])
            for face in bm.faces:
                normal = (ob.matrix_world.to_3x3() @ face.normal).normalized()
                assert normal.dot(front) > .999, ('Poster would be backface-culled',name)
        else:
            closed_count += 1
            if bm.calc_volume(signed=True) <= 0:
                negative_count += 1
        bm.free()
    assert total_triangles == report['geometry'][key]['triangles']
    assert negative_count == 0
    result['models'][key] = {'file':filename,'objects':len(actual),
        'mesh_objects':sum(ob.type=='MESH' for ob in actual.values()),
        'distinct_mesh_datablocks':len({ob.data for ob in actual.values() if ob.type=='MESH'}),
        'triangles':total_triangles,'root_scale_one':True,'hierarchy_and_door_pivots_preserved':True,
        'forward_up_and_metre_geometry_match':True,'max_world_matrix_error':max_matrix_error,
        'max_world_bounds_error_metres':max_bounds_error,'per_object_material_slots_match':True,
        'triangle_material_assignments_match':True,'closed_meshes_with_outward_normals':closed_count,
        'negative_volume_meshes':negative_count,'intentional_flat_poster_art':open_parts,
        'preview_lights_cameras_scale_references_absent':True}

# Keep an inspectable round-trip result separately from editable source.
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'FBX_RoundTrip_Check.blend'))
(ROOT/'fbx_roundtrip_report.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('FBX_ROUNDTRIP '+json.dumps(result),flush=True)
