import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
report = {
    "blender_version": bpy.app.version_string,
    "loaded_saved_file": bpy.data.filepath,
    "scene_units": {"system": bpy.context.scene.unit_settings.system,
                    "scale_length": bpy.context.scene.unit_settings.scale_length},
    "objects": [],
}
for ob in bpy.context.scene.objects:
    entry = {"name": ob.name, "type": ob.type,
             "location": list(ob.location), "rotation_euler": list(ob.rotation_euler),
             "scale": list(ob.scale), "world_matrix": [list(r) for r in ob.matrix_world],
             "dimensions": list(ob.dimensions), "parent": ob.parent.name if ob.parent else None}
    if ob.type == 'MESH':
        coords = [v.co for v in ob.data.vertices]
        entry.update(mesh=ob.data.name, vertices=len(coords), polygons=len(ob.data.polygons),
                     local_min=[min(v[i] for v in coords) for i in range(3)],
                     local_max=[max(v[i] for v in coords) for i in range(3)],
                     materials=[m.name if m else None for m in ob.data.materials],
                     modifiers=[(m.name, m.type) for m in ob.modifiers])
    report['objects'].append(entry)
report['images'] = [{"name": im.name, "filepath": im.filepath,
                     "packed": bool(im.packed_file)} for im in bpy.data.images]
ROOT.joinpath('reference_inspection.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('REFERENCE_INSPECTION ' + json.dumps(report, separators=(',', ':')))
