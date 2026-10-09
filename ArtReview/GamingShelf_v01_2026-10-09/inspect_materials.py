import bpy, json
ob=bpy.data.objects['GameBox']
print('CASE_MATERIALS', [(m.name, list(m.diffuse_color), m.use_nodes) for m in ob.data.materials])
print('CASE_FACES', [(p.index,p.material_index,list(p.normal)) for p in ob.data.polygons])
for m in ob.data.materials:
 if m.use_nodes:
  for n in m.node_tree.nodes:
   if n.type=='BSDF_PRINCIPLED': print('PRINCIPLED',m.name,[(k,n.inputs[k].default_value[:]) if k=='Base Color' else (k,n.inputs[k].default_value) for k in ['Base Color','Metallic','Roughness']])
