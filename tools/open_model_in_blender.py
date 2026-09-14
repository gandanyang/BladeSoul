"""打开 Blender 就把主角模型连动画一起load好（给不熟悉 Blender 的人用）。

配合 tools/open_model_in_blender.bat 使用；也可以手动：
    blender.exe --factory-startup --python tools/open_model_in_blender.py
"""
import bpy

GLB = r'G:\Game\assets\models\model_player_congyun_03_textured.glb'

# 清掉默认场景（那个方块、灯、相机），只留我们导入的
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

bpy.ops.import_scene.gltf(filepath=GLB)
print('IMPORTED', GLB)

# 注意：_03 只有几何（骨架那版还没导出成功），所以这里的动作挂载会跳过，按空格不会动
# 把第一个动作挂上，这样按空格就能看到东西在动
rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
if rigs and len(bpy.data.actions) > 0:
    rig = rigs[0]
    rig.animation_data_create()
    rig.animation_data.action = bpy.data.actions[0]
    print('ACTION', bpy.data.actions[0].name,
          '| 可用动作:', [a.name for a in bpy.data.actions])

sc = bpy.context.scene
sc.frame_start, sc.frame_end = 1, 30
sc.frame_set(1)

# 把视野框到模型上（失败也不影响导入）
try:
    for area in bpy.context.screen.areas:
        if area.type == 'VIEW_3D':
            for region in area.regions:
                if region.type == 'WINDOW':
                    with bpy.context.temp_override(area=area, region=region):
                        bpy.ops.view3d.view_all()
except Exception as e:
    print('VIEW_ALL_SKIP', e)
