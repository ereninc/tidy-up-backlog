"""Read-only geometry/preservation checks. -- --baseline records the backup."""
import bpy
import bmesh
import hashlib
import json
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent


def original_signature():
    # Blender has not evaluated transforms of inactive scenes after file load.
    for name in ['Scene','CozyGamingShelf_Review_v01']:
        bpy.context.window.scene = bpy.data.scenes[name]
        bpy.context.view_layer.update()
    objects = []
    for ob in sorted(bpy.data.objects, key=lambda item:item.name):
        if not (ob.name.startswith('CGS01_') or ob.name in {'GameBox','Placeholder_Shelf_Type1'}):
            continue
        state = [ob.name,ob.type,[list(row) for row in ob.matrix_world],
                 ob.parent.name if ob.parent else None,sorted(c.name for c in ob.users_collection),
                 [(s.material.name if s.material else None,s.link) for s in ob.material_slots]]
        if ob.type == 'MESH':
            state.extend([[list(v.co) for v in ob.data.vertices],
                          [(list(p.vertices),p.material_index,p.use_smooth) for p in ob.data.polygons]])
        elif ob.type == 'FONT':
            state.extend([ob.data.body,ob.data.size,ob.data.extrude])
        state.append([(m.name,m.type,getattr(m,'width',None),getattr(m,'segments',None)) for m in ob.modifiers])
        objects.append(state)
    mats = []
    for mat in sorted(bpy.data.materials,key=lambda item:item.name):
        if mat.name.startswith('CGS01_') or mat.name in {'m_CaseShell','m_CoverArt'}:
            shader = mat.node_tree.nodes.get('Principled BSDF') if mat.node_tree else None
            mats.append([mat.name,list(mat.diffuse_color),
                         [list(shader.inputs['Base Color'].default_value),shader.inputs['Roughness'].default_value,
                          shader.inputs['Metallic'].default_value] if shader else None])
    scene = bpy.data.scenes['CozyGamingShelf_Review_v01']
    state = [objects,mats,sorted(c.name for c in scene.collection.children),scene.camera.name,
             scene.render.engine,scene.render.resolution_x,scene.render.resolution_y,
             scene.view_settings.view_transform,scene.view_settings.exposure,
             list(scene.world.node_tree.nodes['Background'].inputs['Color'].default_value),
             scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value]
    return hashlib.sha256(json.dumps(state,sort_keys=True).encode()).hexdigest()


baseline_file = ROOT/'variants_preservation_baseline.json'
if '--baseline' in sys.argv:
    baseline_file.write_text(json.dumps({'signature':original_signature()},indent=2))
    print('BASELINE_RECORDED',flush=True)
else:
    baseline = json.loads(baseline_file.read_text())
    assert baseline['signature'] == original_signature(), 'First shelf or reference changed versus backup.'
    report = json.loads((ROOT/'variants_report.json').read_text())
    result = {'saved_blend_loads':True, 'first_shelf_and_original_references_match_backup':True,
              'first_shelf_scene_and_palette_unchanged':True, 'models':{}}
    for prefix,key,expected_cases in [('CGS02_','double_sided',80),('CGS03_','featured_display',15)]:
        entry = report[key]
        scene = bpy.data.scenes[entry['scene']]
        bpy.context.window.scene = scene
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        coll = bpy.data.collections[entry['source_collection']]
        preview = bpy.data.collections[prefix+'02_Preview_Only']
        export = bpy.data.collections[prefix+'04_ExportReady_Hidden']
        cases = [o for o in preview.objects if o.get('case_preview')]
        assert len(cases) == expected_cases
        assert len(export.objects) == 1 and export.hide_render and export.hide_viewport
        merged = next(iter(export.objects))
        assert merged.parent is None
        assert list(merged.location) == [0,0,0] and list(merged.scale) == [1,1,1]
        assert list(merged.rotation_euler) == [0,0,0]
        assert abs(min(v.co.z for v in merged.data.vertices)) < 1e-6
        merged.data.calc_loop_triangles()
        assert len(merged.data.loop_triangles) == entry['evaluated_triangles']
        assert set(m.name for m in merged.data.materials) == set(report['shared_model_materials'])
        bm = bmesh.new()
        bm.from_mesh(merged.data)
        nonmanifold = sum(not e.is_manifold for e in bm.edges)
        degenerate = sum(f.calc_area() < 1e-12 for f in bm.faces)
        bm.free()
        assert nonmanifold == degenerate == 0
        assert all(min(ob.scale) > 0 for ob in coll.objects)

        def bounds(ob):
            points = [ob.matrix_world @ Vector(p) for p in ob.bound_box]
            return ([min(p[i] for p in points) for i in range(3)],
                    [max(p[i] for p in points) for i in range(3)])

        def penetrates(a,b):
            return all(min(a[1][i],b[1][i])-max(a[0][i],b[0][i]) > 1e-5 for i in range(3))

        parts = [o for o in coll.objects if o.type in {'MESH','FONT'}]
        part_bounds = [(o.name,bounds(o.evaluated_get(dg))) for o in parts]
        case_bounds = [(o.name,bounds(o)) for o in cases]
        for case,cb in case_bounds:
            assert abs(cb[0][2]-bpy.data.objects[case]['deck_top']) < 1e-5
            for part,pb in part_bounds:
                assert not penetrates(cb,pb), 'Case penetrates model: '+case+' / '+part
        for index,(a,ab) in enumerate(case_bounds):
            for b,bb in case_bounds[index+1:]:
                assert not penetrates(ab,bb), 'Case overlap: '+a+' / '+b
        owned = [o.name for o in bpy.data.objects if o.get('cozy_shelf_owner') == 'codex.cozy_gaming_shelf.variants.v01']
        assert not any(n.endswith('.001') for n in owned)
        result['models'][key] = {'triangles':len(merged.data.loop_triangles), 'shared_materials':4,
            'cases_verified':len(cases), 'cases_rest_on_decks':True,
            'case_to_case_and_case_to_model_bounding_box_penetrations':0,
            'nonmanifold_edges':nonmanifold, 'degenerate_faces':degenerate,
            'positive_scales_identity_export_ground_pivot':True, 'rerun_duplicate_objects':0}
    poster = bpy.data.objects['CGS03_Poster_Replaceable_Surface']
    assert poster.data.uv_layers.get('PosterUV') is not None
    result['poster_front_uv_available'] = True
    result['unity_editor_or_export_validation_performed'] = False
    (ROOT/'variants_validation_report.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('VARIANT_VALIDATION '+json.dumps(result),flush=True)
