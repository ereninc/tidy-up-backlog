"""Verify saved store, metre references and unchanged approved shelf library.

First run on original library with -- --baseline, then on the delivered store.
No source .blend is saved or changed by this check.
"""
import ast
import bpy
import hashlib
import json
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
# Reuse the exact read-only fingerprint function without executing generation.
tree = ast.parse((ROOT/'build_cozy_store.py').read_text(encoding='utf-8'))
function = next(node for node in tree.body if isinstance(node,ast.FunctionDef) and node.name=='library_fingerprint')
namespace = {'bpy':bpy,'hashlib':hashlib,'json':json}
exec(compile(ast.Module(body=[function],type_ignores=[]),'library_fingerprint','exec'),namespace)
signature = namespace['library_fingerprint']()
baseline = ROOT/'approved_library_baseline.json'
if '--baseline' in sys.argv:
    baseline.write_text(json.dumps({'signature':signature},indent=2),encoding='utf-8')
    print('APPROVED_LIBRARY_BASELINE_RECORDED',flush=True)
else:
    assert signature == json.loads(baseline.read_text())['signature'],'Approved models/reference differ from library file.'
    scene = bpy.data.scenes['CozyGamingStore_Review_v01']
    bpy.context.window.scene = scene
    assert scene.unit_settings.scale_length == 1
    for name in ['CST01_Environment_Root','CST01_ShelfPlaceholders_Root']:
        ob = bpy.data.objects[name]
        assert list(ob.location)==[0,0,0] and list(ob.rotation_euler)==[0,0,0] and list(ob.scale)==[1,1,1]
    refs = bpy.data.collections['CST01_ScaleReferences_NoExport']
    assert refs.hide_render and refs.hide_viewport
    refs.hide_viewport = False
    bpy.context.view_layer.update()

    def bounds(ob):
        vertices = [ob.matrix_world @ Vector(v) for v in ob.bound_box]
        return ([min(p[i] for p in vertices) for i in range(3)],
                [max(p[i] for p in vertices) for i in range(3)])

    case = bpy.data.objects['CST01_Reference_GameBox_048x0048x064']
    lo,hi = bounds(case)
    dimensions = [hi[i]-lo[i] for i in range(3)]
    assert max(abs(a-b) for a,b in zip(dimensions,[.48,.048,.64])) < 1e-6
    human_parts = [ob for ob in refs.objects if ob.type=='MESH' and ob.parent and ob.parent.name=='CST01_Reference_Human_180cm']
    human_lo = min(bounds(ob)[0][2] for ob in human_parts)
    human_hi = max(bounds(ob)[1][2] for ob in human_parts)
    assert abs(human_hi-human_lo-1.8) < 1e-6
    shelves = bpy.data.collections['CST01_ShelfPlaceholders']
    roots = [ob for ob in shelves.objects if ob.type=='EMPTY' and ob.name!='CST01_ShelfPlaceholders_Root']
    assert len(roots)==10
    for ob in roots:
        assert list(ob.scale)==[1,1,1]
        visual = next(child for child in ob.children if child.name.endswith('_Visual'))
        kind = ob['model_type']
        if kind=='Approved_Single_3Rows':
            assert visual.data == bpy.data.objects['CGS01_GamingShelf_Module_ExportReady'].data
        elif kind=='Approved_Double_4Rows':
            assert visual.data == bpy.data.objects['CGS02_Module_ExportReady'].data
        elif kind=='Approved_Featured_Display':
            assert visual.data == bpy.data.objects['CGS03_Module_ExportReady'].data
        else:
            assert kind=='Low_Derivative_2Rows'
            assert visual.data == bpy.data.objects['CST01_LowShelf_Linked_Master'].data
            a,b = bounds(visual)
            assert abs(b[2]-a[2]-1.50)<1e-6
    owned = [ob for ob in bpy.data.objects if ob.get('cozy_store_owner')=='codex.cozy_gaming_store.v01']
    assert not any(ob.name.endswith('.001') for ob in owned)
    assert all(min(ob.scale)>0 for ob in owned)
    assert all(not ob.name.startswith('CGS') for ob in scene.objects)
    result = {'saved_blend_loads':True,'approved_three_models_and_gamebox_match_original_library':True,
        'original_model_data_linked_by_placeholders':True,'shelf_roots_at_identity_scale':len(roots),
        'low_shelf_height_metres':1.50,'human_measured_height_metres':human_hi-human_lo,
        'gamebox_measured_width_depth_height':dimensions,'scene_units_metres_scale_one':True,
        'store_contains_no_spawned_gameboxes_or_piles':True,'scale_references_hidden_and_not_exported':True,
        'duplicate_owned_object_names':0,'negative_scales':0,'unity_playmode_validation_performed':False}
    (ROOT/'source_validation_report.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('STORE_SOURCE_VALIDATION '+json.dumps(result),flush=True)
