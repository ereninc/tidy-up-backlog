"""Build an editable cozy game shop from the approved shelf library.

blender -b ../GamingShelf_v01_2026-10-09/CozyGamingShelf_v01.blend -P build_cozy_store.py
Flags: -- --skip-renders, or -- --render=01_entrance_eye,05_layout_top
Only this generator's owned objects refresh. No Unity files are touched.
"""
import bpy
import bmesh
import hashlib
import json
import math
import os
import sys
import tempfile
from collections import deque
from pathlib import Path
from mathutils import Matrix, Vector

# ------------------------ Artist parameters ------------------------
OUT = Path(__file__).resolve().parent
SHOP_WIDTH, SHOP_LENGTH, SHOP_HEIGHT = 12.0,18.0,4.55
STOCK_WIDTH, STOCK_LENGTH, STOCK_HEIGHT = 4.0,5.0,3.40
STOCK_CENTER_X = 3.80
WALL = 0.22
TILE = 0.75
MIN_MAIN_AISLE = 2.0
EYE_HEIGHT = 1.68
HUMAN_HEIGHT = 1.80
LOW_SHELF_HEIGHT = 1.50
LOW_DECK_TOPS = [0.13,0.94]
ISLAND_X = [-2.10,2.10]
ISLAND_Y = 0.25
BEVEL, BEVEL_SEGMENTS = 0.018,2
SAMPLES = 64
RENDER_WIDTH, RENDER_HEIGHT = 1600,1000
DO_RENDER = '--skip-renders' not in sys.argv
RENDER_NAMES = next((arg.split('=',1)[1].split(',') for arg in sys.argv if arg.startswith('--render=')),None)
# -------------------------------------------------------------------
OWNER, KEY, P = 'codex.cozy_gaming_store.v01','cozy_store_owner','CST01_'
FRONT, BACK = -SHOP_LENGTH/2,SHOP_LENGTH/2
LEFT, RIGHT = -SHOP_WIDTH/2,SHOP_WIDTH/2
SX0, SX1 = STOCK_CENTER_X-STOCK_WIDTH/2,STOCK_CENTER_X+STOCK_WIDTH/2
END = BACK+STOCK_LENGTH
scene_name = 'CozyGamingStore_Review_v01'
SOURCE_FILE = bpy.data.filepath
OUT.mkdir(parents=True,exist_ok=True)
optix_cache = Path(tempfile.gettempdir())/'codex_cozy_store_optix'
optix_cache.mkdir(exist_ok=True)
os.environ.setdefault('OPTIX_CACHE_PATH',str(optix_cache))
assert bpy.data.objects.get('CGS01_Module_Pivot_Ground_Center'), 'Load the approved three-shelf .blend first.'


def library_fingerprint():
    previous = bpy.context.window.scene
    for name in ['Scene','CozyGamingShelf_Review_v01','CozyGamingShelf_DoubleSided_v01','CozyGamingShelf_Featured_v01']:
        bpy.context.window.scene = bpy.data.scenes[name]
        bpy.context.view_layer.update()
    states = []
    for ob in sorted(bpy.data.objects,key=lambda ob:ob.name):
        if not (ob.name.startswith(('CGS01_','CGS02_','CGS03_')) or ob.name in {'GameBox','Placeholder_Shelf_Type1'}):
            continue
        state = [ob.name,ob.type,[list(row) for row in ob.matrix_world],
                 ob.parent.name if ob.parent else None,sorted(c.name for c in ob.users_collection),
                 [(slot.material.name if slot.material else None,slot.link) for slot in ob.material_slots]]
        if ob.type == 'MESH':
            state += [[list(v.co) for v in ob.data.vertices],[(list(p.vertices),p.material_index) for p in ob.data.polygons]]
        elif ob.type == 'FONT':
            state += [ob.data.body,ob.data.size,ob.data.extrude]
        state += [[(m.type,getattr(m,'width',None),getattr(m,'segments',None)) for m in ob.modifiers]]
        states.append(state)
    for mat in sorted(bpy.data.materials,key=lambda mat:mat.name):
        if mat.name.startswith('CGS') or mat.name in {'m_CaseShell','m_CoverArt'}:
            shader = mat.node_tree.nodes.get('Principled BSDF') if mat.node_tree else None
            states.append([mat.name,list(mat.diffuse_color),
                           [list(shader.inputs['Base Color'].default_value),shader.inputs['Roughness'].default_value,
                            shader.inputs['Metallic'].default_value] if shader else None])
    bpy.context.window.scene = previous
    return hashlib.sha256(json.dumps(states,sort_keys=True).encode()).hexdigest()


original_signature = library_fingerprint()
scene = bpy.data.scenes.get(scene_name) or bpy.data.scenes.new(scene_name)
scene[KEY] = OWNER
bpy.context.window.scene = scene
scene.unit_settings.system,scene.unit_settings.scale_length = 'METRIC',1
for ob in list(bpy.data.objects):
    if ob.get(KEY) == OWNER:
        bpy.data.objects.remove(ob,do_unlink=True)
for group in [bpy.data.meshes,bpy.data.curves,bpy.data.cameras,bpy.data.lights]:
    for data in list(group):
        if data.get(KEY) == OWNER and data.users == 0:
            group.remove(data)


def collection(name,parent=None):
    name = P+name
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        coll[KEY] = OWNER
    elif coll.get(KEY) != OWNER:
        raise RuntimeError('User-owned collection: '+name)
    holder = parent or scene.collection
    if name not in holder.children:
        holder.children.link(coll)
    coll.hide_render = coll.hide_viewport = False
    return coll


architecture = collection('Architecture')
walls = collection('Walls_and_Trim',architecture)
floor = collection('Floor',architecture)
front = collection('FrontWall_and_Entrance',architecture)
ceiling = collection('Ceiling_Hide_For_Editing',architecture)
stock = collection('Stockroom_Architecture',architecture)
shelves = collection('ShelfPlaceholders')
props = collection('Props')
fixtures = collection('CeilingFixtures',props)
lighting = collection('PreviewLighting')
refs = collection('ScaleReferences_NoExport')
exports = collection('Export_Hidden')
root = bpy.data.objects.new(P+'Environment_Root',None)
root[KEY] = OWNER
architecture.objects.link(root)
root['units'] = 'Metres; identity transform; Blender front -Y, up +Z'
shelf_root = bpy.data.objects.new(P+'ShelfPlaceholders_Root',None)
shelf_root[KEY] = OWNER
shelves.objects.link(shelf_root)


def rgba(hex_value):
    rgb = [int(hex_value.lstrip('#')[i:i+2],16)/255 for i in (0,2,4)]
    return tuple(v/12.92 if v <= 0.04045 else ((v+0.055)/1.055)**2.4 for v in rgb)+(1,)


palette = {}


def material(name,color,roughness=.75,emission=0):
    name = P+name
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat[KEY] = OWNER
    mat.use_nodes = True
    mat.diffuse_color = rgba(color)
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = rgba(color)
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Metallic'].default_value = 0
    if emission:
        shader.inputs['Emission Color'].default_value = rgba(color)
        shader.inputs['Emission Strength'].default_value = emission
    palette[name] = {'srgb_hex':color,'roughness':roughness,'metallic':0,'emission':emission}
    return mat


oak,cream,blue,mustard = [bpy.data.materials['CGS01_'+name] for name in ['Warm_Oak','Soft_Cream','Dusty_Blue','Mustard']]
ink = bpy.data.materials['CGS01_Preview_Ink']
wall_mat = material('Warm_Plaster','#F1E8D7',.85)
light_wood = material('Light_Wood','#C5A17C',.74)
tile_cream = material('Floor_Cream','#DDD5C5',.82)
tile_blue = material('Floor_BlueGrey','#CBD2CD',.82)
teal = material('Soft_Teal','#67958B',.76)
clay = material('Muted_Terracotta','#B77D68',.82)
cardboard = material('Delivery_Cardboard','#B69C78',.88)
leaf_mat = material('Sage_Leaves','#829B70',.83)
bulb_mat = material('Lamp_Warm_Diffuser','#FFF1D2',.55,2)
glass = material('Window_Glass','#D6E5DF',.14)
glass.node_tree.nodes['Principled BSDF'].inputs['Transmission Weight'].default_value = .96
glass.node_tree.nodes['Principled BSDF'].inputs['IOR'].default_value = 1.45
palette[glass.name].update({'transmission':.96,'ior':1.45})
mesh_cache = {}
label_font = bpy.data.fonts.get(P+'Friendly_Label_Font')
if label_font is None:
    label_font = bpy.data.fonts.load(r'C:\Windows\Fonts\trebucbd.ttf')
    label_font.name = P+'Friendly_Label_Font'
    label_font[KEY] = OWNER
    label_font.pack()


def object_mesh(name,verts,faces,mat,coll=props,parent=root,materials=None,indices=None):
    data = bpy.data.meshes.new(P+name+'_Mesh')
    data[KEY] = OWNER
    data.from_pydata(verts,[],faces)
    data.update()
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    if max(v.co.y for v in bm.verts)-min(v.co.y for v in bm.verts) < 1e-8:
        for face in bm.faces:
            if face.normal.y > 0:
                face.normal_flip()
    bm.to_mesh(data)
    bm.free()
    for m in materials or ([mat] if mat else []):
        data.materials.append(m)
    if indices:
        for face,index in zip(data.polygons,indices):
            face.material_index = index
    ob = bpy.data.objects.new(P+name,data)
    ob[KEY] = OWNER
    coll.objects.link(ob)
    ob.parent = parent
    return ob


def box(name,center,size,mat,coll=props,bevel=BEVEL,parent=root):
    assert min(size) > 0, name
    cache_key = tuple(size)+(mat.name,)
    if cache_key in mesh_cache:
        ob = bpy.data.objects.new(P+name,mesh_cache[cache_key])
        ob[KEY] = OWNER
        coll.objects.link(ob)
        ob.parent = parent
    else:
        signs = [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]
        verts = [tuple(sign[i]*size[i]/2 for i in range(3)) for sign in signs]
        faces = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        ob = object_mesh(name,verts,faces,mat,coll,parent)
        mesh_cache[cache_key] = ob.data
    ob.location = center
    if bevel:
        mod = ob.modifiers.new('Soft_Edge','BEVEL')
        mod.width,mod.segments = min(bevel,min(size)*.28),BEVEL_SEGMENTS
    return ob


def empty(name,coll=props,parent=root,location=(0,0,0)):
    ob = bpy.data.objects.new(P+name,None)
    ob[KEY] = OWNER
    coll.objects.link(ob)
    ob.parent,ob.location = parent,location
    return ob


def text(name,body,center,size,mat=ink,coll=props,parent=root,rear=False):
    data = bpy.data.curves.new(P+name,'FONT')
    data[KEY] = OWNER
    data.body,data.size,data.align_x,data.align_y = body,size,'CENTER','CENTER'
    data.font = label_font
    data.resolution_u,data.extrude = 2,.001
    data.materials.append(mat)
    ob = bpy.data.objects.new(P+name,data)
    ob[KEY] = OWNER
    coll.objects.link(ob)
    ob.parent,ob.location = parent,center
    ob.rotation_euler = (math.pi/2,0,math.pi if rear else 0)
    return ob


def lathe(name,profile,center,mat,coll=props,parent=root,n=16):
    verts = [(radius*math.cos(i*2*math.pi/n),radius*math.sin(i*2*math.pi/n),z)
             for radius,z in profile for i in range(n)]
    count = len(profile)
    faces = []
    for ring in range(count-1):
        faces += [(ring*n+i,ring*n+(i+1)%n,(ring+1)*n+(i+1)%n,(ring+1)*n+i) for i in range(n)]
    faces += [tuple(range(n-1,-1,-1)),tuple(range((count-1)*n,count*n))]
    ob = object_mesh(name,verts,faces,mat,coll,parent)
    ob.location = center
    return ob


def front_disc(name,center,radius,mat,coll=props,parent=root,n=16):
    ob = lathe(name,[(radius,-.002),(radius,.002)],center,mat,coll,parent,n)
    ob.rotation_euler.x = math.pi/2
    return ob


def aim(ob,target):
    ob.rotation_euler = (Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()


# Solid floor and a single mesh containing all modest, metre-scale tiles.
box('Main_Floor_Slab',(0,0,-.12),(SHOP_WIDTH+2*WALL,SHOP_LENGTH+2*WALL,.22),light_wood,floor,.025)
verts,faces,indices = [],[],[]
nx,ny = round(SHOP_WIDTH/TILE),round(SHOP_LENGTH/TILE)
for iy in range(ny):
    for ix in range(nx):
        cx,cy = LEFT+(ix+.5)*TILE,FRONT+(iy+.5)*TILE
        h = (TILE-.006)/2
        offset = len(verts)
        verts += [(cx+sx*h,cy+sy*h,z) for z in [-.025,0] for sx,sy in [(-1,-1),(1,-1),(1,1),(-1,1)]]
        fs = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        faces += [tuple(offset+i for i in f) for f in fs]
        indices += [((ix//2+iy//2)%2)]*6
object_mesh('Floor_Tiles_Combined_075m',verts,faces,None,floor,root,[tile_cream,tile_blue],indices)
box('Stockroom_Floor',(STOCK_CENTER_X,BACK+STOCK_LENGTH/2,-.08),(STOCK_WIDTH+2*WALL,STOCK_LENGTH+WALL,.16),tile_cream,stock,.015)
box('Front_Stoop',(0,FRONT-.85,-.09),(SHOP_WIDTH+1.0,1.70,.18),light_wood,front,.025)
for xx in [LEFT,RIGHT]:
    box('SideWall_'+str(xx),(xx+math.copysign(WALL/2,xx),0,SHOP_HEIGHT/2),(WALL,SHOP_LENGTH+2*WALL,SHOP_HEIGHT),wall_mat,walls,.012)
    box('Side_Skirting_'+str(xx),(xx-math.copysign(.025,xx),0,.12),(.05,SHOP_LENGTH,.24),blue,walls,.009)
    box('Side_ChairRail_'+str(xx),(xx-math.copysign(.018,xx),0,3.34),(.036,SHOP_LENGTH,.065),light_wood,walls,.008)
    box('Side_Crown_'+str(xx),(xx-math.copysign(.045,xx),0,SHOP_HEIGHT-.12),(.10,SHOP_LENGTH,.24),oak,walls,.012)


def span_wall(name,a,b,y,height,coll=walls,bottom=0):
    return box(name,((a+b)/2,y,bottom+height/2),(b-a,WALL,height),wall_mat,coll,.01)


# Back wall is genuinely cut around the 2.2 m stockroom opening.
door_x,door_w,door_h = STOCK_CENTER_X,2.20,2.80
door_left,door_right = door_x-door_w/2,door_x+door_w/2
span_wall('BackWall_Left',LEFT,door_left,BACK+WALL/2,SHOP_HEIGHT)
span_wall('BackWall_Right',door_right,RIGHT,BACK+WALL/2,SHOP_HEIGHT)
span_wall('BackWall_Over_Door',door_left,door_right,BACK+WALL/2,SHOP_HEIGHT-door_h,bottom=door_h)
for a,b in [(LEFT,door_left),(door_right,RIGHT)]:
    box('Back_Skirting_'+str(a),((a+b)/2,BACK-.025,.12),(b-a,.05,.24),blue,walls,.008)
box('Back_Crown',(0,BACK-.045,SHOP_HEIGHT-.12),(SHOP_WIDTH,.1,.24),oak,walls,.01)
for xx in [SX0,SX1]:
    box('Stock_SideWall_'+str(xx),(xx+(-1 if xx==SX0 else 1)*WALL/2,BACK+STOCK_LENGTH/2,STOCK_HEIGHT/2),
        (WALL,STOCK_LENGTH,STOCK_HEIGHT),wall_mat,stock,.01)
    box('Stock_Skirting_'+str(xx),(xx+(.025 if xx==SX0 else -.025),BACK+STOCK_LENGTH/2,.10),(.05,STOCK_LENGTH,.20),blue,stock,.006)
box('Stock_BackWall',(STOCK_CENTER_X,END+WALL/2,STOCK_HEIGHT/2),(STOCK_WIDTH+2*WALL,WALL,STOCK_HEIGHT),wall_mat,stock,.01)

# Front facade: two window bays and a real open, framed double entrance.
entrance_x,entrance_w,entrance_h = -.50,2.20,2.80
entry_left,entry_right = entrance_x-entrance_w/2,entrance_x+entrance_w/2
window_specs = [('Left',LEFT+.35,entry_left-.50),('Right',entry_right+.50,RIGHT-.35)]
cuts = [(LEFT,window_specs[0][1]),(window_specs[0][2],entry_left),
        (entry_right,window_specs[1][1]),(window_specs[1][2],RIGHT)]
for index,(a,b) in enumerate(cuts):
    span_wall('Front_Pier_'+str(index),a,b,FRONT-WALL/2,SHOP_HEIGHT,front)
window_bottom,window_top = .68,3.25
for name,a,b in window_specs:
    span_wall('Window_'+name+'_SillWall',a,b,FRONT-WALL/2,window_bottom,front)
    span_wall('Window_'+name+'_HeaderWall',a,b,FRONT-WALL/2,SHOP_HEIGHT-window_top,front,window_top)
    mid,z = (a+b)/2,(window_bottom+window_top)/2
    box('Window_'+name+'_Glass',(mid,FRONT, z),(b-a-.12,.018,window_top-window_bottom-.12),glass,front,0)
    for xx in [a+.045,b-.045]:
        box('Window_'+name+'_VerticalFrame_'+str(xx),(xx,FRONT+.015,z),(.09,.14,window_top-window_bottom),oak,front,.012)
    for zz in [window_bottom+.045,window_top-.045]:
        box('Window_'+name+'_HorizontalFrame_'+str(zz),(mid,FRONT+.015,zz),(b-a,.14,.09),oak,front,.012)
    for xx in [a+(b-a)/3,a+2*(b-a)/3]:
        box('Window_'+name+'_Mullion_'+str(xx),(xx,FRONT+.005,z),(.055,.10,window_top-window_bottom-.1),blue,front,.008)
    box('Window_'+name+'_Sill',(mid,FRONT+.10,window_bottom),(b-a+.08,.32,.11),light_wood,front,.016)
span_wall('Entrance_HeaderWall',entry_left,entry_right,FRONT-WALL/2,SHOP_HEIGHT-entrance_h,front,entrance_h)
for xx in [entry_left-.055,entry_right+.055]:
    box('Entrance_Frame_'+str(xx),(xx,FRONT+.035,entrance_h/2),(.11,.22,entrance_h),oak,front,.015)
box('Entrance_Frame_Header',(entrance_x,FRONT+.035,entrance_h+.055),(entrance_w+.22,.22,.11),oak,front,.015)
for side,hinge_x,sign in [('L',entry_left,1),('R',entry_right,-1)]:
    hinge = empty('Entrance_'+side+'_Hinge',front,root,(hinge_x,FRONT,0))
    hinge.rotation_euler.z = -sign*math.pi/2
    hinge['pivot'] = 'Hinge edge at floor; leaf opened outward 90 degrees'
    cx = sign*.525
    box('Entrance_'+side+'_Glass',(cx,0,1.40),(1.00,.018,2.62),glass,front,0,hinge)
    for local_x in [0,sign*1.05]:
        box('Entrance_'+side+'_Stile_'+str(local_x),(local_x,0,1.40),(.075,.07,2.72),blue,front,.01,hinge)
    for zz in [.08,2.72]:
        box('Entrance_'+side+'_Rail_'+str(zz),(cx,0,zz),(1.05,.07,.08),blue,front,.01,hinge)
    box('Entrance_'+side+'_Handle',(sign*.89,.060,1.15),(.035,.035,.36),mustard,front,.009,hinge)
box('Front_Interior_Fascia',(0,FRONT+.10,3.75),(SHOP_WIDTH-.45,.16,.55),blue,front,.02)
text('Front_Interior_Greeting','GOOD GAMES.  GOOD COMPANY.',(0,FRONT+.19,3.75),.215,cream,front,rear=True)
box('Exterior_Store_Sign',(0,FRONT-.25,3.83),(5.3,.12,.80),oak,front,.025)
box('Exterior_Sign_Inset',(0,FRONT-.318,3.83),(5.06,.025,.58),cream,front,.012)
text('Exterior_Store_Name','S I D E   Q U E S T',(0,FRONT-.335,3.86),.33,blue,front)
box('Front_Upper_Trim',(0,FRONT+.045,SHOP_HEIGHT-.12),(SHOP_WIDTH,.10,.24),oak,front,.012)

# Stockroom door with an independent hinge and a true open passage.
for xx in [door_left-.065,door_right+.065]:
    box('Stock_Door_Jamb_'+str(xx),(xx,BACK,door_h/2),(.13,.24,door_h),oak,walls,.018)
box('Stock_Door_Lintel',(door_x,BACK,door_h+.065),(door_w+.26,.24,.13),oak,walls,.018)
stock_hinge = empty('Stockroom_Door_Hinge',stock,root,(door_left,BACK-.035,0))
stock_hinge.rotation_euler.z = math.pi
stock_hinge['pivot'] = 'Left hinge edge at floor; leaf parked 180 degrees against sales-floor wall'
box('Stockroom_Open_Door_Leaf',(1.05,0,1.37),(2.10,.065,2.70),blue,stock,.018,stock_hinge)
box('Stockroom_Door_Cream_Inset',(1.05,-.04,1.55),(1.80,.02,1.48),cream,stock,.012,stock_hinge)
box('Stockroom_Door_Handle',(1.88,-.10,1.10),(.17,.035,.045),mustard,stock,.01,stock_hinge)
box('Stockroom_Header_Plate',(door_x,BACK-.14,3.18),(2.60,.055,.36),blue,walls,.018)
text('Stockroom_Header_Letters','DELIVERIES  /  STOCK',(door_x,BACK-.173,3.18),.17,cream)

# Separately hideable roof pieces and simple rhythm of structural ceiling beams.
for label,a,b in [('Front',FRONT,0),('Rear',0,BACK)]:
    box('Ceiling_Main_'+label,(0,(a+b)/2,SHOP_HEIGHT+.07),(SHOP_WIDTH+2*WALL,b-a,.14),wall_mat,ceiling,.008)
for yy in [FRONT+.2,-4.4,.2,4.7,BACK-.2]:
    box('Ceiling_Beam_'+str(yy),(0,yy,SHOP_HEIGHT-.075),(SHOP_WIDTH,.14,.15),light_wood,ceiling,.01)
box('Ceiling_Stockroom',(STOCK_CENTER_X,BACK+STOCK_LENGTH/2,STOCK_HEIGHT+.06),
    (STOCK_WIDTH+2*WALL,STOCK_LENGTH,.12),wall_mat,ceiling,.008)

# Low shelf master is a separate editable source; original shelf data is reused.
low_scene = bpy.data.scenes.get('CozyStore_LowShelf_Source') or bpy.data.scenes.new('CozyStore_LowShelf_Source')
low_scene[KEY] = OWNER
low_coll = bpy.data.collections.get(P+'LowShelf_Editable_Source')
if not low_coll:
    low_coll = bpy.data.collections.new(P+'LowShelf_Editable_Source')
    low_coll[KEY] = OWNER
if low_coll.name not in low_scene.collection.children:
    low_scene.collection.children.link(low_coll)
low_pivot = empty('LowShelf_Source_Pivot',low_coll,None)
box('LowShelf_Oak_Base',(0,0,.05),(5.528,.72,.10),oak,low_coll,.022,low_pivot)
for end in [-1,1]:
    xx = end*(5.448/2-.055)
    box('LowShelf_Cream_End_'+str(end),(xx,.025,(LOW_SHELF_HEIGHT+.10)/2),
        (.11,.63,LOW_SHELF_HEIGHT-.10),cream,low_coll,.018,low_pivot)
    box('LowShelf_Oak_Post_'+str(end),(xx,-.322,(LOW_SHELF_HEIGHT+.10)/2),
        (.128,.10,LOW_SHELF_HEIGHT-.10),oak,low_coll,.018,low_pivot)
box('LowShelf_Cream_Back',(0,.285,(LOW_SHELF_HEIGHT-.05+.10)/2),(5.228,.11,LOW_SHELF_HEIGHT-.15),cream,low_coll,.008,low_pivot)
box('LowShelf_Back_Cap',(0,.285,LOW_SHELF_HEIGHT-.025),(5.228,.11,.05),oak,low_coll,.01,low_pivot)
for row,top in enumerate(LOW_DECK_TOPS,1):
    old_top = .32+(row-1)*.86
    for suffix in ['Flat_Wood_Deck','Blue_Retaining_Lip','Blank_Name_Plate','Plate_Pin_Left','Plate_Pin_Right']:
        src = bpy.data.objects[f'CGS01_Row_{row:02}_'+suffix]
        ob = src.copy()
        ob.name = P+f'LowShelf_Row{row}_'+suffix
        if 'cozy_shelf_owner' in ob:
            del ob['cozy_shelf_owner']
        ob[KEY] = OWNER
        ob.parent = low_pivot
        low_coll.objects.link(ob)
        ob.location = src.location+Vector((0,0,top-old_top))
bpy.context.window.scene = low_scene
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()


def evaluated_copy(ob,coll,name,parent=None):
    data = bpy.data.meshes.new_from_object(ob.evaluated_get(dg),preserve_all_data_layers=True,depsgraph=dg)
    data[KEY] = OWNER
    if ob.type == 'FONT':
        bm = bmesh.new()
        bm.from_mesh(data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(data)
        bm.free()
    copy = bpy.data.objects.new(name,data)
    copy[KEY] = OWNER
    coll.objects.link(copy)
    copy.matrix_world = ob.matrix_world.copy()
    if parent:
        matrix = copy.matrix_world.copy()
        copy.parent = parent
        copy.matrix_world = matrix
    return copy


low_copies = []
for ob in list(low_coll.objects):
    if ob.type == 'MESH':
        low_copies.append(evaluated_copy(ob,low_coll,P+'Low_Ready_'+ob.name))
bpy.ops.object.select_all(action='DESELECT')
for ob in low_copies:
    ob.select_set(True)
bpy.context.view_layer.objects.active = low_copies[0]
bpy.ops.object.join()
low_ready = bpy.context.object
low_ready.name = P+'LowShelf_Linked_Master'
low_ready.hide_render = True
low_ready.hide_set(True)
low_ready.data.transform(low_ready.matrix_world)
low_ready.matrix_world = Matrix.Identity(4)
bpy.context.window.scene = scene

placements = []


def shelf_instance(name,data,position,angle,kind):
    holder = empty(name,shelves,shelf_root,position)
    holder.rotation_euler.z = angle
    holder['replacement_hint'] = 'Replace this root with your Unity gameplay prefab; keep metre scale.'
    holder['model_type'] = kind
    ob = bpy.data.objects.new(P+name+'_Visual',data)
    ob[KEY] = OWNER
    shelves.objects.link(ob)
    ob.parent = holder
    placements.append({'root':holder.name,'visual':ob.name,'type':kind,'position':list(position),'rotation_z_degrees':math.degrees(angle)})
    return holder


single_data = bpy.data.objects['CGS01_GamingShelf_Module_ExportReady'].data
double_data = bpy.data.objects['CGS02_Module_ExportReady'].data
display_data = bpy.data.objects['CGS03_Module_ExportReady'].data
for wall_side,xx,angle in [('West',-5.59,math.pi/2),('East',5.59,-math.pi/2)]:
    for row,yy in enumerate([-2.036,3.864],1):
        shelf_instance('Shelf_Wall_'+wall_side+'_'+str(row),single_data,(xx,yy,0),angle,'Approved_Single_3Rows')
for island,xx in enumerate(ISLAND_X,1):
    shelf_instance('Shelf_Island_'+str(island)+'_EastFace',low_ready.data,(xx+.36,ISLAND_Y,0),math.pi/2,'Low_Derivative_2Rows')
    shelf_instance('Shelf_Island_'+str(island)+'_WestFace',low_ready.data,(xx-.36,ISLAND_Y,0),-math.pi/2,'Low_Derivative_2Rows')
shelf_instance('Shelf_Rear_Archive_Double',double_data,(0,5.90,0),0,'Approved_Double_4Rows')
display_root = shelf_instance('Shelf_Entry_Spotlight',display_data,(3.45,-6.25,0),0,'Approved_Featured_Display')

# A small original poster, associated with the replacement placeholder.
front_disc('Spotlight_Poster_Moon',(.52,.245,2.20),.15,cream,shelves,display_root)
object_mesh('Spotlight_Poster_Mountains',[(-1.02,.241,1.68),(-1.02,.241,1.82),(-.48,.241,2.20),
             (.08,.241,1.82),(.43,.241,2.03),(1.02,.241,1.76),(1.02,.241,1.68)],
             [tuple(range(7))],cream,shelves,display_root)
object_mesh('Spotlight_Poster_Trail',[(-.30,.238,1.68),(.30,.238,1.68),(-.35,.238,2.04),(-.40,.238,2.04)],[(0,1,2,3)],mustard,shelves,display_root)
text('Spotlight_Poster_Title','FIND YOUR NEXT WORLD',(0,.240,2.47),.108,cream,shelves,display_root)
text('Spotlight_Poster_Subtitle','A   N E W   A D V E N T U R E   A W A I T S',(0,.240,1.49),.050,cream,shelves,display_root)

# Counter and restrained point-of-sale decor; the sales floor stays empty.
counter = empty('Checkout_Counter',props,root,(-3.10,-6.45,0))
box('Counter_Oak_Toe',(0,0,.07),(2.76,1.04,.14),oak,props,.023,counter)
box('Counter_Cream_Body',(0,0,.54),(2.60,.92,.92),cream,props,.025,counter)
box('Counter_Teal_Front',(0,.472,.55),(2.48,.035,.70),teal,props,.018,counter)
box('Counter_LightWood_Top',(0,0,1.05),(2.82,1.10,.12),light_wood,props,.028,counter)
box('Counter_Cream_Brand',(0,.497,.60),(1.58,.018,.23),cream,props,.015,counter)
text('Counter_Brand','S I D E  Q U E S T',(0,.510,.60),.105,blue,props,counter,True)
box('Till_Base',(.55,-.10,1.14),(.52,.38,.07),blue,props,.015,counter)
box('Till_Stand',(.55,-.10,1.28),(.07,.08,.24),oak,props,.014,counter)
box('Till_Monitor',(.55,-.075,1.48),(.57,.075,.38),blue,props,.021,counter)
box('Till_Screen',(.55,-.029,1.48),(.49,.012,.30),ink,props,.008,counter)
text('Till_Screen_Text','READY TO PLAY',(.55,-.019,1.49),.043,cream,props,counter,True)
box('Till_Small_CardPad',(-.20,.15,1.145),(.22,.26,.07),blue,props,.014,counter)
box('Till_Pad_Screen',(-.20,.15,1.184),(.14,.16,.012),cream,props,.004,counter)


def hanging_sign(name,body,x,y,z,width):
    sign = empty('Sign_'+name,props,root,(x,y,z))
    box(name+'_Oak_Frame',(0,0,0),(width,.075,.44),oak,props,.019,sign)
    for back in [False,True]:
        sy = .045 if back else -.045
        box(name+'_Cream_Face_'+str(back),(0,sy,0),(width-.09,.015,.34),cream,props,.010,sign)
        text(name+'_Letters_'+str(back),body,(0,sy+(.010 if back else -.010),0),.14,ink,props,sign,back)
    for xx in [-width*.36,width*.36]:
        box(name+'_Suspension_'+str(xx),(xx,0,(SHOP_HEIGHT-z)/2),(.013,.013,SHOP_HEIGHT-z),ink,props,.002,sign)


hanging_sign('Adventure','01  /  ADVENTURE',-2.10,.25,3.10,2.55)
hanging_sign('Cozy','02  /  COZY + CO-OP',2.10,.25,3.10,2.70)
hanging_sign('Vault','THE VAULT  /  CLASSICS',0,5.90,4.03,3.2)


def framed_poster(name,center,title,accent,width=.95,height=1.30,rotation=0):
    holder = empty(name,props,root,center)
    holder.rotation_euler.z = rotation
    box(name+'_Wood_Frame',(0,0,0),(width+.10,.06,height+.10),oak,props,.014,holder)
    box(name+'_Cream_Card',(0,-.037,0),(width,.018,height),cream,props,.009,holder)
    front_disc(name+'_Moon',(width*.20,-.049,height*.17),width*.145,accent,props,holder)
    object_mesh(name+'_Mountains',[(-width*.43,-.054,-height*.23),(-width*.43,-.054,-height*.03),
                (-width*.15,-.054,height*.20),(width*.11,-.054,-height*.08),
                (width*.29,-.054,height*.08),(width*.43,-.054,-height*.13),(width*.43,-.054,-height*.23)],
                [tuple(range(7))],blue,props,holder)
    text(name+'_Title',title,(0,-.057,height*.36),.09,ink,props,holder)
    text(name+'_Caption','PRESS PLAY.  STAY AWHILE.',(0,-.057,-height*.38),.037,ink,props,holder)


framed_poster('Poster_Back_Orbit',(-4.35,BACK-.10,2.45),'SMALL WORLDS',mustard)
framed_poster('Poster_Back_Paths',(-2.75,BACK-.10,2.45),'BIG ADVENTURES',clay)
framed_poster('Poster_Front_Welcome',(-4.25,FRONT+.20,2.0),'PLAY SOMETHING NEW',teal,1.10,1.45,math.pi)


def plant(name,x,y,height=1.15):
    holder = empty(name,props,root,(x,y,0))
    lathe(name+'_Clay_Pot',[(.22,0),(.27,.34),(.28,.36),(.24,.38),(.23,.33)],(0,0,0),clay,props,holder)
    lathe(name+'_Soil',[(.228,.335),(.228,.345)],(0,0,0),oak,props,holder)
    for i in range(7):
        angle = i*2*math.pi/7
        top = height-(i%3)*.10
        radius = .30+(i%2)*.05
        axis = Vector((math.cos(angle),math.sin(angle),0))
        cross = Vector((-math.sin(angle),math.cos(angle),0))*.12
        base = Vector((0,0,.36))
        tip = axis*radius+Vector((0,0,top))
        mid = axis*(radius*.6)+Vector((0,0,(top+.36)/2))
        verts = [tuple(base),tuple(mid+cross),tuple(tip),tuple(mid-cross),tuple(mid+Vector((0,0,.04)))]
        object_mesh(name+'_Leaf_'+str(i),verts,[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(0,3,2,1)],leaf_mat,props,holder)


plant('Plant_Checkout',-5.30,-8.10,1.12)
plant('Plant_Spotlight',5.25,-8.10,1.22)
plant('Plant_Rear_Nook',-5.42,8.18,1.20)

# Stockroom props occupy side/back bays, leaving a straight working route.
bench = empty('Stock_Workbench',props,root,(2.15,12.00,0))
box('Stock_Bench_Top',(0,0,.94),(.68,2.45,.10),light_wood,props,.02,bench)
for yy in [-1.04,1.04]:
    box('Stock_Bench_Leg_'+str(yy),(0,yy,.44),(.10,.10,.88),blue,props,.012,bench)
box('Stock_Bench_Lower_Shelf',(0,0,.23),(.60,2.34,.07),oak,props,.012,bench)
box('Stock_Packing_Pad',(0,-.22,1.004),(.43,.70,.025),teal,props,.006,bench)
cartons = []
for index,(center,size) in enumerate([
        ((5.17,13.30,.38),(.90,.90,.76)),((5.17,13.30,1.035),(.74,.78,.54)),
        ((5.20,12.20,.32),(.84,.88,.64)),((2.35,13.62,.28),(.90,.70,.56))]):
    name = 'Delivery_Carton_'+str(index+1)
    holder = empty(name,props,root,center)
    box(name+'_Box',(0,0,0),size,cardboard,props,.016,holder)
    box(name+'_Tape',(0,0,size[2]/2+.002),(.085,size[1]-.02,.005),cream,props,.001,holder)
    box(name+'_Label',(0,-size[1]/2-.003,.02),(.30,.007,.17),cream,props,.002,holder)
    text(name+'_Label_Text','SIDE QUEST',(0,-size[1]/2-.008,.02),.041,ink,props,holder)
    cartons.append(holder.name)

# Six simple pendants, with shared silhouettes and separate preview lights.
for index,(xx,yy) in enumerate([(-3.65,-5.15),(3.65,-5.15),(-3.65,.60),(3.65,.60),(-3.65,6.45),(3.65,6.45)],1):
    lamp = empty('Pendant_'+str(index),fixtures,root,(xx,yy,0))
    lathe('Pendant_Shade_'+str(index),[(.13,4.15),(.36,3.88),(.335,3.88),(.105,4.13)],(0,0,0),blue,fixtures,lamp)
    lathe('Pendant_Diffuser_'+str(index),[(.30,3.888),(.30,3.90)],(0,0,0),bulb_mat,fixtures,lamp)
    lathe('Pendant_Cable_'+str(index),[(.009,4.15),(.009,SHOP_HEIGHT)],(0,0,0),ink,fixtures,lamp,8)
    lathe('Pendant_Canopy_'+str(index),[(.08,SHOP_HEIGHT-.035),(.08,SHOP_HEIGHT)],(0,0,0),oak,fixtures,lamp)
box('Stock_Ceiling_Fixture',(STOCK_CENTER_X,11.50,STOCK_HEIGHT-.05),(1.10,.48,.10),blue,fixtures,.015)
box('Stock_Ceiling_Diffuser',(STOCK_CENTER_X,11.50,STOCK_HEIGHT-.108),(.98,.36,.014),bulb_mat,fixtures,.004)

# Preview-only measurement references, hidden until explicitly enabled.
human = empty('Reference_Human_180cm',refs,None,(.4,-4.8,0))
box('Reference_Human_Torso',(0,0,1.21),(.43,.24,.58),teal,refs,.04,human)
for xx in [-.12,.12]:
    box('Reference_Human_Leg_'+str(xx),(xx,0,.46),(.17,.20,.92),ink,refs,.03,human)
lathe('Reference_Human_Head',[(.12,1.53),(.14,1.58),(.14,1.75),(.10,1.80)],(0,0,0),cream,refs,human,12)
for xx in [-.28,.28]:
    box('Reference_Human_Arm_'+str(xx),(xx,0,1.20),(.12,.18,.58),teal,refs,.025,human)
raw = bpy.data.objects['GameBox']
ref_case = bpy.data.objects.new(P+'Reference_GameBox_048x0048x064',raw.data)
ref_case[KEY] = OWNER
refs.objects.link(ref_case)
ref_case.location,ref_case.scale = (1.3,-4.8,.32),(.4,.24,.4)
for slot,mat in zip(ref_case.material_slots,[ink,blue]):
    slot.link,slot.material = 'OBJECT',mat
refs.hide_render = refs.hide_viewport = True


def area_light(name,position,target,power,size,color=(1,.96,.89),shape='DISK',size_y=None):
    data = bpy.data.lights.new(P+name,'AREA')
    data[KEY] = OWNER
    ob = bpy.data.objects.new(P+name,data)
    ob[KEY] = OWNER
    lighting.objects.link(ob)
    ob.location = position
    data.energy,data.shape,data.size,data.color = power,shape,size,color
    if size_y:
        data.size_y = size_y
    aim(ob,target)
    return ob


world = bpy.data.worlds.get(P+'Daylight_World') or bpy.data.worlds.new(P+'Daylight_World')
world[KEY] = OWNER
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.65,.73,.78,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .35
scene.world = world
for xx in [-3.7,3.65]:
    area_light('Window_Daylight_'+str(xx),(xx,FRONT-1.0,2.5),(xx,-2,1.3),1800,4.0,(.94,.97,1),'RECTANGLE',2.8)
for index,(xx,yy) in enumerate([(-3.65,-5.15),(3.65,-5.15),(-3.65,.60),(3.65,.60),(-3.65,6.45),(3.65,6.45)],1):
    area_light('Pendant_Light_'+str(index),(xx,yy,3.85),(xx,yy,0),430,1.55,(1,.91,.77))
area_light('Ceiling_Soft_Bounce',(0,0,4.43),(0,1,0),1800,9,(1,.97,.91),'RECTANGLE',13)
area_light('Stock_Worklight',(STOCK_CENTER_X,11.50,STOCK_HEIGHT-.13),(STOCK_CENTER_X,11.5,0),350,1.5,(1,.97,.91))

scene.render.engine = 'CYCLES'
scene.cycles.device,scene.cycles.samples,scene.cycles.use_denoising = 'CPU',SAMPLES,True
cycle_prefs = bpy.context.preferences.addons['cycles'].preferences
try:
    cycle_prefs.compute_device_type = 'OPTIX'
    cycle_prefs.get_devices()
    gpu_devices = [device for device in cycle_prefs.devices if device.type == 'OPTIX']
    for device in cycle_prefs.devices:
        device.use = device in gpu_devices
    if gpu_devices:
        scene.cycles.device = 'GPU'
except (TypeError,RuntimeError):
    pass
scene.cycles.max_bounces = 8
scene.cycles.transparent_max_bounces = 8
scene.render.resolution_x,scene.render.resolution_y,scene.render.resolution_percentage = RENDER_WIDTH,RENDER_HEIGHT,100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.view_settings.view_transform,scene.view_settings.look = 'AgX','AgX - Medium High Contrast'
scene.view_settings.exposure,scene.view_settings.gamma = -2.0,1
cameras = {}
for name,location,target,lens in [
        ('01_entrance_eye',(-.5,-8.0,EYE_HEIGHT),(0,2.5,1.9),21),
        ('02_main_aisle_eye',(0,-1.8,EYE_HEIGHT),(0,6.8,1.9),23),
        ('03_checkout_eye',(-3.10,-7.90,EYE_HEIGHT),(1.3,1.2,1.80),21),
        ('04_stock_connection_eye',(3.80,7.65,EYE_HEIGHT),(3.80,11.7,1.58),20),
        ('05_layout_top',(0,1.75,30),(0,1.75,0),45),
        ('06_front_storefront',(8,-18,6.5),(0,-5.3,2.15),34),
        ('07_stock_workroom',(4.85,10.0,EYE_HEIGHT),(3.40,12.8,1.35),22),
        ('08_scale_reference',(0,-7.80,EYE_HEIGHT),(.8,-4.80,.95),25),
        ('09_front_display_eye',(-1.2,-8.30,EYE_HEIGHT),(3.45,-5.65,1.65),24)]:
    data = bpy.data.cameras.new(P+name)
    data[KEY] = OWNER
    ob = bpy.data.objects.new(P+name,data)
    ob[KEY] = OWNER
    lighting.objects.link(ob)
    ob.location = location
    data.lens,data.clip_end = lens,150
    if name == '05_layout_top':
        data.type,data.ortho_scale = 'ORTHO',27.4
    aim(ob,target)
    cameras[name] = ob


def bounds(ob):
    points = [ob.matrix_world @ Vector(v) for v in ob.bound_box]
    return ([min(p[i] for p in points) for i in range(3)],
            [max(p[i] for p in points) for i in range(3)])


bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
for entry in placements:
    ob = bpy.data.objects[entry['visual']]
    entry['bounds_min'],entry['bounds_max'] = bounds(ob)
    assert list(ob.scale) == [1,1,1]
    assert abs(entry['bounds_min'][2]) < 1e-5
    assert entry['bounds_min'][0] >= LEFT and entry['bounds_max'][0] <= RIGHT
    assert entry['bounds_min'][1] >= FRONT and entry['bounds_max'][1] <= BACK

# Measured passages use the actual mesh bounds, not nominal pivot distances.
west_front = max(p['bounds_max'][0] for p in placements if 'Wall_West' in p['root'])
east_front = min(p['bounds_min'][0] for p in placements if 'Wall_East' in p['root'])
island_west = [p for p in placements if 'Island_1' in p['root']]
island_east = [p for p in placements if 'Island_2' in p['root']]
west_min,west_max = min(p['bounds_min'][0] for p in island_west),max(p['bounds_max'][0] for p in island_west)
east_min,east_max = min(p['bounds_min'][0] for p in island_east),max(p['bounds_max'][0] for p in island_east)
island_end = max(p['bounds_max'][1] for p in island_west+island_east)
rear = next(p for p in placements if 'Rear_Archive' in p['root'])
counter_front = -6.45+.55
counter_front_clear = min(p['bounds_min'][1]-counter_front for p in placements
    if p['bounds_min'][1] >= counter_front and p['bounds_min'][0] < -3.10+1.41 and p['bounds_max'][0] > -3.10-1.41)
entry_parts_l = [ob for ob in front.objects if ob.parent and ob.parent.name == P+'Entrance_L_Hinge']
entry_parts_r = [ob for ob in front.objects if ob.parent and ob.parent.name == P+'Entrance_R_Hinge']
entry_clear = min(bounds(ob)[0][0] for ob in entry_parts_r)-max(bounds(ob)[1][0] for ob in entry_parts_l)
door_parts = [ob for ob in stock.objects if ob.parent == stock_hinge]
stock_door_clear = door_right-max(door_left,max(bounds(ob)[1][0] for ob in door_parts))
clearances = {'west_main_aisle':west_min-west_front,'centre_main_aisle':east_min-west_max,
              'east_main_aisle':east_front-east_max,'island_to_rear_rack':rear['bounds_min'][1]-island_end,
              'behind_rear_rack':BACK-rear['bounds_max'][1],
              'rear_rack_west_end':rear['bounds_min'][0]-west_front,
              'rear_rack_east_end':east_front-rear['bounds_max'][0],
              'entrance_structural_opening':entrance_w,'entrance_open_leaves_clear':entry_clear,
              'stock_door_structural_opening':door_w,'stock_door_open_leaf_clear':stock_door_clear,
              'counter_front_approach':counter_front_clear,'counter_staff_to_front_wall':(-6.45-.55)-FRONT,
              'display_front_approach':next(p for p in placements if 'Entry_Spotlight' in p['root'])['bounds_min'][1]-FRONT}
assert min(clearances.values()) >= MIN_MAIN_AISLE-1e-5, clearances
low_upper_case_top = LOW_DECK_TOPS[-1]+.64
assert LOW_DECK_TOPS[1]-.085-(LOW_DECK_TOPS[0]+.64) > .05
assert low_upper_case_top < EYE_HEIGHT

# Clearance and connectivity audit with a 0.6 m diameter person on a 0.2 m grid.
obstacles = []
for entry in placements:
    obstacles.append((entry['root'],entry['bounds_min'][:2],entry['bounds_max'][:2]))
obstacles.append(('Counter',[-3.10-1.41,-6.45-.55],[-3.10+1.41,-6.45+.55]))
for name,x,y,radius in [('Plant_Checkout',-5.30,-8.10,.36),('Plant_Spotlight',5.25,-8.10,.36),('Plant_Rear_Nook',-5.42,8.18,.36)]:
    obstacles.append((name,[x-radius,y-radius],[x+radius,y+radius]))
obstacles += [('Workbench',[1.81,10.77],[2.49,13.23]),('Carton_Back',[4.72,12.85],[5.62,13.75]),
              ('Carton_Side',[4.78,11.76],[5.62,12.64]),('Carton_Left',[1.90,13.27],[2.80,13.97]),
              ('Open_Stock_Door',[door_left-2.10,BACK-.08],[door_left+.04,BACK])]
radius,step = .30,.20


def walkable(x,y):
    main = LEFT+radius <= x <= RIGHT-radius and FRONT+radius <= y <= BACK-radius
    depot = SX0+radius <= x <= SX1-radius and BACK+radius <= y <= END-radius
    doorway = door_left+radius <= x <= door_right-radius and BACK-radius-.02 <= y <= BACK+radius+.02
    if not (main or depot or doorway):
        return False
    return not any(lo[0]-radius < x < hi[0]+radius and lo[1]-radius < y < hi[1]+radius for _,lo,hi in obstacles)


grid = {}
for iy in range(round((END-FRONT)/step)+1):
    for ix in range(round(SHOP_WIDTH/step)+1):
        x,y = LEFT+ix*step,FRONT+iy*step
        if walkable(x,y):
            grid[ix,iy] = (x,y)


def nearest(point):
    return min(grid,key=lambda key:(grid[key][0]-point[0])**2+(grid[key][1]-point[1])**2)


start = nearest((-.5,-8.0))
visited,queue = {start},deque([start])
while queue:
    x,y = queue.popleft()
    for neighbor in [(x+1,y),(x-1,y),(x,y+1),(x,y-1)]:
        if neighbor in grid and neighbor not in visited:
            visited.add(neighbor)
            queue.append(neighbor)
waypoints = {'entrance':(-.5,-8.0),'checkout':(-3.1,-4.9),'west_aisle':(-4.0,.25),
             'centre_aisle':(0,.25),'east_aisle':(4.0,.25),'rear_cross_aisle':(0,4.1),
             'rear_rack_back':(0,7.8),'depot':(3.8,12.0),'spotlight':(3.45,-8.0)}
assert all(nearest(point) in visited for point in waypoints.values())
assert len(visited) == len(grid), 'Disconnected floor cells: '+str([grid[key] for key in grid if key not in visited][:25])
for name,camera in cameras.items():
    if name not in {'05_layout_top','06_front_storefront'}:
        assert walkable(camera.location.x,camera.location.y), 'Player camera inside obstacle: '+name

def penetrates(a,b):
    return all(min(a[1][i],b[1][i])-max(a[0][i],b[0][i]) > 1e-5 for i in range(3))

prop_parts = [(ob.name,bounds(ob.evaluated_get(dg))) for ob in props.all_objects if ob.type in {'MESH','FONT'}]
for entry in placements:
    shelf_bounds = (entry['bounds_min'],entry['bounds_max'])
    for name,part_bounds in prop_parts:
        assert not penetrates(shelf_bounds,part_bounds), 'Prop blocks shelf: '+name+' / '+entry['root']
bench_parts = [bounds(ob.evaluated_get(dg)) for ob in props.objects if ob.parent == bench and ob.type == 'MESH']
for carton in cartons:
    for ob in props.objects:
        if ob.parent and ob.parent.name == carton and ob.type in {'MESH','FONT'}:
            assert not any(penetrates(bounds(ob.evaluated_get(dg)),bb) for bb in bench_parts), 'Carton intersects workbench: '+carton
free_zone = {'name':'Front organizing space','min':[-1.60,-6.5],'max':[1.60,-3.3],'area_m2':10.24}
for name,lo,hi in obstacles:
    assert not (min(hi[0],free_zone['max'][0])-max(lo[0],free_zone['min'][0])>0 and
                min(hi[1],free_zone['max'][1])-max(lo[1],free_zone['min'][1])>0), name

# Independent export copies, preserving object/door/shelf roots and hierarchy.
env_export = collection('Environment_FBX',exports)
shelf_export = collection('ShelfPlaceholders_FBX',exports)
env_copy_root = empty('Export_Environment_Root',env_export,None)
placeholder_copy_root = empty('Export_Placeholder_Root',shelf_export,None)
scene_objects = list(scene.objects)


def belongs(ob,coll):
    return any(c == coll or c in coll.children_recursive for c in ob.users_collection)


def make_export_group(objects,coll,export_root):
    mapping = {}
    shared_data = {}
    for ob in objects:
        if ob.type in {'MESH','FONT'}:
            data_key = (ob.data.name,tuple((m.type,getattr(m,'width',None),getattr(m,'segments',None)) for m in ob.modifiers))
            if data_key in shared_data:
                copy = bpy.data.objects.new(P+'FBX_'+ob.name,shared_data[data_key])
                copy[KEY] = OWNER
                coll.objects.link(copy)
                copy.matrix_world = ob.matrix_world.copy()
            else:
                copy = evaluated_copy(ob,coll,P+'FBX_'+ob.name)
                shared_data[data_key] = copy.data
        elif ob.type == 'EMPTY':
            copy = empty('FBX_'+ob.name,coll,None)
            copy.matrix_world = ob.matrix_world.copy()
        else:
            continue
        mapping[ob] = copy
    for original,copy in mapping.items():
        matrix = copy.matrix_world.copy()
        copy.parent = mapping.get(original.parent,export_root)
        copy.matrix_world = matrix
    return list(mapping.values())+[export_root]


env_source = [ob for ob in scene_objects if belongs(ob,architecture) or belongs(ob,props)]
placeholder_source = [ob for ob in scene_objects if belongs(ob,shelves)]
env_export_objects = make_export_group(env_source,env_export,env_copy_root)
shelf_export_objects = make_export_group(placeholder_source,shelf_export,placeholder_copy_root)
geometry = {}
for label,objects in [('environment',env_export_objects),('shelf_placeholders',shelf_export_objects)]:
    triangles,mats,nonmanifold,degenerate,negative,open_parts = 0,set(),0,0,[],[]
    for ob in objects:
        if min(ob.scale) <= 0:
            negative.append(ob.name)
        if ob.type != 'MESH':
            continue
        ob.data.calc_loop_triangles()
        triangles += len(ob.data.loop_triangles)
        mats.update(m.name for m in ob.data.materials if m)
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        open_edges = sum(not e.is_manifold for e in bm.edges)
        nonmanifold += open_edges
        if open_edges:
            open_parts.append({'name':ob.name,'edges':open_edges})
        degenerate += sum(f.calc_area()<1e-12 for f in bm.faces)
        bm.free()
    assert not negative
    # Flat poster illustrations intentionally have open boundaries.
    geometry[label] = {'triangles':triangles,'material_count':len(mats),'materials':sorted(mats),
                       'mesh_objects':sum(ob.type=='MESH' for ob in objects),'negative_scales':negative,
                       'nonmanifold_edges_including_intentional_flat_art':nonmanifold,'open_parts':open_parts,'degenerate_faces':degenerate}

manifest = {}
for label,objects in [('environment',env_export_objects),('shelf_placeholders',shelf_export_objects)]:
    manifest[label] = []
    for ob in objects:
        item = {'name':ob.name,'type':ob.type,'parent':ob.parent.name if ob.parent else None,
                'matrix_world':[list(row) for row in ob.matrix_world]}
        if ob.type == 'MESH':
            item['bounds_min'],item['bounds_max'] = bounds(ob)
            ob.data.calc_loop_triangles()
            item['triangles'] = len(ob.data.loop_triangles)
            item['materials'] = [m.name if m else None for m in ob.data.materials]
            mat_counts = {}
            for triangle in ob.data.loop_triangles:
                mat_name = item['materials'][triangle.material_index] if item['materials'] else None
                mat_counts[mat_name] = mat_counts.get(mat_name,0)+1
            item['triangles_per_material'] = mat_counts
            if 'Mountains' in ob.name or 'Poster_Trail' in ob.name:
                item['poster_front_world_normal'] = list((ob.matrix_world.to_3x3() @ Vector((0,-1,0))).normalized())
        manifest[label].append(item)
(OUT/'fbx_export_manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')


def export_fbx(objects,path):
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'EMPTY','MESH'},
        use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
        global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        use_space_transform=True,bake_space_transform=False,path_mode='AUTO')


export_fbx(env_export_objects,OUT/'CozyGamingStore_Environment.fbx')
export_fbx(shelf_export_objects,OUT/'CozyGamingStore_ShelfPlaceholders.fbx')
exports.hide_render = exports.hide_viewport = True
assert library_fingerprint() == original_signature, 'Approved shelf library/reference changed.'
report = {'blender_version':bpy.app.version_string,'source_library_file':SOURCE_FILE,
          'original_three_shelves_and_gamebox_preserved':True,'original_library_signature':original_signature,
          'main_interior_metres':[SHOP_WIDTH,SHOP_LENGTH,SHOP_HEIGHT],
          'stockroom_interior_metres':[STOCK_WIDTH,STOCK_LENGTH,STOCK_HEIGHT],
          'shelf_placements':placements,'measured_clearances_metres':clearances,
          'low_shelf_height':LOW_SHELF_HEIGHT,'low_shelf_deck_tops':LOW_DECK_TOPS,
          'low_shelf_upper_case_top':low_upper_case_top,'camera_eye_height':EYE_HEIGHT,
          'human_reference_height':HUMAN_HEIGHT,'gamebox_dimensions_width_depth_height':[.48,.048,.64],
          'low_shelf_derivative_authorized_by_user':True,'walkability_grid_step':step,
          'walkability_person_diameter':radius*2,'connected_walkable_cells':len(visited),
          'all_walkable_cells_connected':len(visited)==len(grid),'connected_waypoints':waypoints,
          'front_clear_organizing_zone':free_zone,'geometry':geometry,'palette':palette,
          'no_game_cases_or_piles_spawned_in_store':True,'fbx_axis_forward':'-Z','fbx_axis_up':'Y',
          'export_root_scale':[1,1,1],'unity_validation_performed':False}
report['render_settings'] = {'engine':'Cycles','device':scene.cycles.device,'samples':SAMPLES,
                            'view_transform':'AgX','look':scene.view_settings.look,'exposure':scene.view_settings.exposure}
report['approved_shelf_library'] = str(OUT.parent/'GamingShelf_v01_2026-10-09'/'CozyGamingShelf_v01.blend')
report['all_player_eye_cameras_in_walkable_space'] = True
report['props_to_shelves_bounding_box_penetrations'] = 0
report['cartons_to_workbench_bounding_box_penetrations'] = 0
(OUT/'store_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
scene.camera = cameras['01_entrance_eye']
scene.render.filepath = str(OUT/'01_entrance_eye.png')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.shading.type = 'MATERIAL'
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CozyGamingStore_v01.blend'))
print('STORE_READY '+json.dumps({'geometry':geometry,'clearances':clearances,'placements':len(placements)}),flush=True)
if DO_RENDER:
    for name,camera in cameras.items():
        if RENDER_NAMES is not None and name not in RENDER_NAMES:
            continue
        scene.camera = camera
        ceiling.hide_render = name == '05_layout_top'
        fixtures.hide_render = name == '05_layout_top'
        refs.hide_render = name != '08_scale_reference'
        plan_headers = [bpy.data.objects[P+suffix] for suffix in ['BackWall_Over_Door','Stock_Door_Lintel',
            'Back_Crown','Entrance_HeaderWall','Entrance_Frame_Header','Front_Interior_Fascia',
            'Front_Interior_Greeting','Front_Upper_Trim','Exterior_Store_Sign','Exterior_Sign_Inset','Exterior_Store_Name']]
        for ob in plan_headers:
            ob.hide_render = name == '05_layout_top'
        if name == '05_layout_top':
            scene.render.resolution_x,scene.render.resolution_y = 1280,2048
        else:
            scene.render.resolution_x,scene.render.resolution_y = RENDER_WIDTH,RENDER_HEIGHT
        scene.render.filepath = str(OUT/(name+'.png'))
        bpy.ops.render.render(write_still=True,scene=scene.name)
        ceiling.hide_render = fixtures.hide_render = False
        refs.hide_render = True
        for ob in plan_headers:
            ob.hide_render = False
        print('PREVIEW_SAVED '+name,flush=True)
