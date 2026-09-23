"""Render an authoring preview of an already opened .blend, without game/UI automation."""
import bpy
import pathlib
import sys
out=pathlib.Path(sys.argv[sys.argv.index('--')+1]).resolve()
if out.exists():
    raise RuntimeError('Preview output must be new')
scene=bpy.context.scene
camera=bpy.data.cameras.new('Preview camera')
camera.type='ORTHO'
camera.ortho_scale=190
obj=bpy.data.objects.new('Preview camera',camera)
scene.collection.objects.link(obj)
obj.location=(80,-40,200)
obj.rotation_euler=(0,0,0)
scene.camera=obj
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'
scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD'
scene.world.color=(0.08,0.1,0.12)
scene.render.resolution_x=900
scene.render.resolution_y=900
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(out)
bpy.ops.render.render(write_still=True)
