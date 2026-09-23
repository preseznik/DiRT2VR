"""Blender background authoring. All coordinates in mesh.json are EGO metres, Y up.
Run: blender --background --python author.py -- <new output directory>
"""
import bpy
import json
import math
import pathlib
import sys

out = pathlib.Path(sys.argv[sys.argv.index('--') + 1]).resolve()
out.mkdir(parents=True, exist_ok=False)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

# A stadium loop: two 90 m straights and two radius-28 m turns.
radius, straight, cx, cz, base_y = 28.0, 90.0, 80.0, 40.0, 8.0
length = 2 * straight + 2 * math.pi * radius
count = 180

def point(distance):
    s = distance % length
    if s < straight:
        x, z, tx, tz = radius, -straight/2+s, 0, 1
        height = 1.5 * math.sin(math.pi * (s-30)/30)**2 if 30 < s < 60 else 0
    elif s < straight + math.pi*radius:
        a = (s-straight)/radius
        x, z, tx, tz = radius*math.cos(a), straight/2+radius*math.sin(a), -math.sin(a), math.cos(a)
        height = 0
    elif s < 2*straight + math.pi*radius:
        x, z, tx, tz = -radius, straight/2-(s-straight-math.pi*radius), 0, -1
        height = 0
    else:
        a = math.pi+(s-2*straight-math.pi*radius)/radius
        x, z, tx, tz = radius*math.cos(a), -straight/2+radius*math.sin(a), -math.sin(a), math.cos(a)
        height = 0
    return [cx+x, base_y+height, cz+z], [tx, 0, tz]

points = []
for i in range(count):
    p, t = point(length*i/count)
    points.append({'position':p, 'tangent':t, 'distance':length*i/count})

meshes = []
def strip(name, inner, outer, surface):
    verts, faces = [], []
    for gate in points:
        p, t = gate['position'], gate['tangent']
        # Left is (-tz, 0, tx). Positive offsets run to the left.
        for offset in (inner, outer):
            verts.append([p[0]-t[2]*offset, p[1], p[2]+t[0]*offset])
    for i in range(count):
        j = (i+1) % count
        faces.extend([[2*i,2*j,2*j+1],[2*i,2*j+1,2*i+1]])
    # Ensure upward-facing triangles in the EGO coordinate basis.
    for tri in faces:
        a,b,c = [verts[k] for k in tri]
        ny = (b[2]-a[2])*(c[0]-a[0])-(b[0]-a[0])*(c[2]-a[2])
        if ny < 0: tri[1],tri[2] = tri[2],tri[1]
    meshes.append({'name':name, 'surface':surface, 'vertices':verts, 'triangles':faces})
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([(v[0],-v[2],v[1]) for v in verts],[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    mat=bpy.data.materials.get(surface) or bpy.data.materials.new(surface)
    mat.diffuse_color = (0.18,0.17,0.16,1) if name=='road' else (0.48,0.34,0.17,1)
    mesh.materials.append(mat)

strip('road',-5,5,'RDT+')
strip('runoff_right',-9,-5,'DRT+')
strip('runoff_left',5,9,'DRT+')

# Low, visibly modelled barriers use the same triangles for rendering and physics.
for side in (-1,1):
    verts, faces = [], []
    for gate in points:
        p,t=gate['position'],gate['tangent']
        for offset,height in ((9*side-0.15,0),(9*side+0.15,0),(9*side+0.15,1.2),(9*side-0.15,1.2)):
            verts.append([p[0]-t[2]*offset,p[1]+height,p[2]+t[0]*offset])
    for i in range(count):
        j=(i+1)%count
        for k in range(4):
            a,b,c,d=4*i+k,4*j+k,4*j+(k+1)%4,4*i+(k+1)%4
            faces.extend([[a,b,c],[a,c,d]])
    name=f'barrier_{side}'
    meshes.append({'name':name,'surface':'CON+','vertices':verts,'triangles':faces})
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([(v[0],-v[2],v[1]) for v in verts],[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    mat=bpy.data.materials.get('CON+') or bpy.data.materials.new('CON+')
    mat.diffuse_color=(0.8,0.65,0.1,1)
    mesh.materials.append(mat)
spec={'schema':1,'length':length,'width':10,'baseHeight':base_y,'points':points,'meshes':meshes,
      'coordinateSystem':'right-handed Y-up metres','runtimeValidated':False}
(out/'mesh.json').write_text(json.dumps(spec,indent=2)+'\n',encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'prototype.blend'))
bpy.ops.export_scene.gltf(filepath=str(out/'collision.glb'),export_format='GLB',export_yup=True)
print(f'Authored {length:.2f} m prototype: {out}')
