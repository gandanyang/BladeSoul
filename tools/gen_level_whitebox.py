#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
关卡白盒生成器（T32 的一次性引导工具）。

为什么用生成器而不是手写 .tscn：白盒有 40 多个盒子，手算 transform 极易出错，
而且**尺寸合规（10 §3.4）必须按构造成立**，不能靠"我看着摆对了"。
这里把 10 §3.4 冻结的五个数字写成常量，所有几何都由它们算出来。

生成之后 **.tscn 就是源文件**——在编辑器里挪墙、加房间都以它为准；
这个脚本只在"想重新铺一遍"时用，不再往回同步。

    python tools/gen_level_whitebox.py
"""

import io
import os

# ── 10 §3.4 冻结的规格（改这里就是改规格，改别处一律无效）────────────
GRID = 4.0                # 主格距
LAYER_HEIGHT = 3.2        # 一层净高（含梁 3.6）
DOOR_SINGLE = 1.2         # 单人门洞
DOOR_PASSAGE = 2.4        # 通路门洞
MAX_STEP = 0.2            # 可走落差上限
COMBAT_CLEAR = 8.0        # 战斗区净空边长下限
INDOOR_MIN_HEIGHT = 5.0   # 室内战斗区净高下限（摄像机 4.2m 倒推）

WALL = 0.4                # 墙厚
DOJO_HEIGHT = 5.6         # 道场净高（≥5 ✓）

# 颜色纪律（10 §1）：低饱和青灰。**这里一个暖色都没有**——暖色留给灯笼。
SHELL_COLOR = (0.42, 0.45, 0.47, 1.0)
GROUND_COLOR = (0.30, 0.32, 0.33, 1.0)
STONE_COLOR = (0.36, 0.39, 0.42, 1.0)


def _num(value):
    text = f"{float(value):.4f}".rstrip("0").rstrip(".")
    return text if text not in ("-0", "") else "0"


def _vec3(v):
    return f"Vector3({_num(v[0])}, {_num(v[1])}, {_num(v[2])})"


def _transform(center):
    return (f"Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, "
            f"{_num(center[0])}, {_num(center[1])}, {_num(center[2])})")


class Scene:
    """极简 .tscn 写入器（只支持本工具需要的东西）。"""

    def __init__(self, root_name):
        self.root_name = root_name
        self.ext = []
        self.sub = []
        self.nodes = []   # (name, type_or_empty, parent, props, header_extra)

    def add_ext(self, rtype, path, rid):
        self.ext.append((rtype, path, rid))
        return rid

    def add_sub(self, rtype, sid, lines):
        self.sub.append((rtype, sid, lines))
        return sid

    def node(self, name, ntype, parent=".", groups=None, instance=None, **props):
        """type 传空字符串 = 实例化节点（头部不写 type=，写 instance=）。

        parent 传 None = 根节点（根节点**不能**有 parent 属性）。
        """
        lines = [f"{k} = {v}" for k, v in props.items()]

        attrs = f' type="{ntype}"' if ntype else ""
        if parent:
            attrs += f' parent="{parent}"'
        if instance:
            attrs += f' instance=ExtResource("{instance}")'
        if groups:
            quoted = ", ".join(f'"{g}"' for g in groups)
            attrs += f" groups=[{quoted}]"

        self.nodes.append((name, attrs, lines))

    def box(self, name, parent, center, size, material):
        """一个带碰撞的灰盒体。CSGBox3D：一个节点同时是网格与碰撞，白盒够用。"""
        self.node(name, "CSGBox3D", parent,
                  transform=_transform(center),
                  size=_vec3(size),
                  use_collision="true",
                  material=f'SubResource("{material}")')

    def render(self):
        out = [f"[gd_scene load_steps={len(self.ext) + len(self.sub) + 1} format=3]", ""]

        for rtype, path, rid in self.ext:
            out.append(f'[ext_resource type="{rtype}" path="{path}" id="{rid}"]')
        if self.ext:
            out.append("")

        for rtype, sid, lines in self.sub:
            out.append(f'[sub_resource type="{rtype}" id="{sid}"]')
            out.extend(lines)
            out.append("")

        for name, attrs, lines in self.nodes:
            out.append(f'[node name="{name}"{attrs}]')
            out.extend(lines)
            out.append("")

        return "\n".join(out).rstrip() + "\n"


def _material(scene, sid, color, roughness=0.9):
    scene.add_sub("StandardMaterial3D", sid, [
        f"albedo_color = Color({color[0]}, {color[1]}, {color[2]}, {color[3]})",
        f"roughness = {roughness}",
    ])
    return sid


def _environment(scene, sid, background, ambient, energy):
    return scene.add_sub("Environment", sid, [
        "background_mode = 1",
        f"background_color = Color({background[0]}, {background[1]}, {background[2]}, 1)",
        "ambient_light_source = 2",
        f"ambient_light_color = Color({ambient[0]}, {ambient[1]}, {ambient[2]}, 1)",
        f"ambient_light_energy = {energy}",
    ])


def _atmosphere(scene, lanterns):
    """氛围三件套（T34）：降级总闸 + 氛围控制器 + 灯笼标记。

    灯笼只是**标记**（Marker3D + 加入 lantern 组）——灯与自发光由
    AtmosphereController 按 .tres 里的档位生成，场景里不写任何氛围数值。
    """
    quality = scene.add_ext("Script", "res://src/World/QualityDirector.cs", "8_quality")
    atmo = scene.add_ext("Script", "res://src/World/AtmosphereController.cs", "9_atmo")
    profile = scene.add_ext("Resource", "res://data/world/atmosphere_rainy_night.tres", "10_profile")

    scene.node("QualityDirector", "Node", ".", script=f'ExtResource("{quality}")')
    scene.node("Atmosphere", "Node3D", ".",
               script=f'ExtResource("{atmo}")',
               Profile=f'ExtResource("{profile}")')

    scene.node("Lanterns", "Node3D", ".")
    for index, (lx, ly, lz) in enumerate(lanterns, 1):
        scene.node(f"Lantern{index}", "Marker3D", "Lanterns",
                   groups=["lantern"], transform=_transform((lx, ly, lz)))


def _combat_area(scene, name, parent, center, size):
    """战斗区标记：Area3D + BoxShape3D，加入 combat_area 组，供自检读净空。

    layer/mask 全 0、monitoring 关掉——它是**纯标记**，不参与任何物理交互。
    """
    shape = scene.add_sub("BoxShape3D", f"BoxShape3D_{name}", [f"size = {_vec3(size)}"])
    scene.node(name, "Area3D", parent, groups=["combat_area"],
               transform=_transform(center),
               collision_layer="0",
               collision_mask="0",
               monitoring="false",
               monitorable="false")
    child_parent = name if parent == "." else f"{parent}/{name}"
    scene.node("Shape", "CollisionShape3D", child_parent,
               shape=f'SubResource("{shape}")')


def _path(scene, waypoints, parent="."):
    scene.node("Path", "Node3D", parent)
    child_parent = "Path" if parent == "." else f"{parent}/Path"

    for index, point in enumerate(waypoints, start=1):
        scene.node(f"P{index}", "Marker3D", child_parent,
                   transform=_transform((point[0], 0.0, point[1])))


# ── 道场（重做主场景，要保留旧的运行节点名）────────────────────────
# 室内 24×16m、净高 5.6m。战斗区 8×8 居中且**完全净空**：
# 四根柱子全部落在战斗区之外；门在 +Z 侧、宽 2.4m（通路门）。
def build_dojo():
    scene = Scene("Dojo")
    shell = _material(scene, "StandardMaterial3D_shell", SHELL_COLOR)
    ground = _material(scene, "StandardMaterial3D_ground", GROUND_COLOR, 0.95)
    stone = _material(scene, "StandardMaterial3D_stone", STONE_COLOR)

    half_x, half_z = 12.0, 8.0

    scene.node("Dojo", "Node3D", parent=None)

    env = _environment(scene, "Environment_dojo", (0.12, 0.14, 0.17), (0.42, 0.46, 0.55), 0.5)
    scene.node("WorldEnvironment", "WorldEnvironment", environment=f'SubResource("{env}")')
    scene.node("Sun", "DirectionalLight3D",
               rotation="Vector3(-1, -0.6, 0)", shadow_enabled="true")

    scene.node("Shell", "Node3D")

    scene.box("Floor", "Shell", (0.0, -WALL / 2, 0.0),
              (half_x * 2, WALL, half_z * 2), ground)
    scene.box("Ceiling", "Shell", (0.0, DOJO_HEIGHT + WALL / 2, 0.0),
              (half_x * 2 + WALL, WALL, half_z * 2 + WALL), shell)

    scene.box("WallNorth", "Shell", (0.0, DOJO_HEIGHT / 2, -half_z - WALL / 2),
              (half_x * 2 + WALL, DOJO_HEIGHT, WALL), shell)
    scene.box("WallWest", "Shell", (-half_x - WALL / 2, DOJO_HEIGHT / 2, 0.0),
              (WALL, DOJO_HEIGHT, half_z * 2), shell)
    scene.box("WallEast", "Shell", (half_x + WALL / 2, DOJO_HEIGHT / 2, 0.0),
              (WALL, DOJO_HEIGHT, half_z * 2), shell)

    # 南墙留 2.4m 通路门：两段 + 门楣
    door_half = DOOR_PASSAGE / 2
    segment = half_x - door_half + WALL / 2
    scene.box("WallSouthLeft", "Shell",
              (-(door_half + segment / 2), DOJO_HEIGHT / 2, half_z + WALL / 2),
              (segment, DOJO_HEIGHT, WALL), shell)
    scene.box("WallSouthRight", "Shell",
              (door_half + segment / 2, DOJO_HEIGHT / 2, half_z + WALL / 2),
              (segment, DOJO_HEIGHT, WALL), shell)
    scene.box("DoorLintel", "Shell",
              (0.0, (DOOR_PASSAGE + DOJO_HEIGHT) / 2, half_z + WALL / 2),
              (DOOR_PASSAGE, DOJO_HEIGHT - DOOR_PASSAGE, WALL), shell)

    # 门外门廊：**门必须通向某个地方**。
    # 第一版门洞外面什么都没有，走出去就一路下坠——
    # 那条路径自检没有覆盖门口，所以没抓到（由 FallReset 兜底时才发现）。
    scene.box("Porch", "Shell", (0.0, -WALL / 2, half_z + WALL + 2.0),
              (8.0, WALL, 4.0), ground)

    # 四根柱子：全部在战斗区（±4m）之外
    for index, (px, pz) in enumerate([(-6.0, -5.0), (6.0, -5.0), (-6.0, 5.0), (6.0, 5.0)], 1):
        scene.box(f"Pillar{index}", "Shell", (px, DOJO_HEIGHT / 2, pz),
                  (0.6, DOJO_HEIGHT, 0.6), stone)

    _combat_area(scene, "CombatArea_Dojo", ".", (0.0, DOJO_HEIGHT / 2, 0.0),
                 (COMBAT_CLEAR, DOJO_HEIGHT, COMBAT_CLEAR))

    # 路径刻意避开四根柱子（柱心在 ±6, ±5，最外沿 ±6.3）：
    # 长边走 x=±8.5、横边走 z=-6.5，离柱子至少 1.2m。
    _atmosphere(scene, [(-3.0, 4.2, 6.0), (3.0, 4.2, 6.0)])

    _path(scene, [
        (0.0, 10.5), (0.0, 7.0), (0.0, 3.0), (0.0, 0.0), (0.0, -6.5),
        (8.5, -6.5), (8.5, 0.0), (-8.5, 0.0), (-8.5, -6.5),
    ])

    arbiter = scene.add_ext("Script", "res://src/Core/CombatArbiter.cs", "1_arbiter")
    reset = scene.add_ext("Script", "res://src/Core/BattleReset.cs", "2_reset")
    player = scene.add_ext("PackedScene", "res://scenes/actors/Player.tscn", "3_player")
    dummy = scene.add_ext("PackedScene", "res://scenes/actors/TrainingDummy.tscn", "4_dummy")
    attacker = scene.add_ext("PackedScene", "res://scenes/actors/AttackingDummy.tscn", "5_attacker")
    respawn = scene.add_ext("PackedScene", "res://scenes/actors/RespawnDummy.tscn", "6_respawn")
    spear = scene.add_ext("PackedScene", "res://scenes/actors/SpearDummy.tscn", "7_spear")

    scene.node("Actors", "Node3D")
    scene.node("CombatArbiter", "Node", "Actors", script=f'ExtResource("{arbiter}")')
    scene.node("BattleReset", "Node", "Actors", script=f'ExtResource("{reset}")')
    scene.node("Player", "", "Actors", instance=player,
               transform=_transform((0.0, 0.1, 6.0)))

    # 四个靶子全部挪到战斗区之外、靠近两侧墙——这样 8×8 战斗区是干净的
    for name, res, pos in [
        ("Dummy1", dummy, (-8.0, 0.1, 0.0)),
        ("Attacker", attacker, (-8.0, 0.1, 4.0)),
        ("RespawnDummy", respawn, (8.0, 0.1, 0.0)),
        ("Spear", spear, (8.0, 0.1, 4.0)),
    ]:
        scene.node(name, "", "Actors", instance=res, transform=_transform(pos))

    return scene


# ── 岐阜城下 第一章（03 §7 的模板）────────────────────────────────
# 入口 → 探索 → 遭遇战A → 探索 → 遭遇战B → 鬼火 → 短探索 → BOSS房
# 战斗区全部**露天**（城下町的街巷与广场），垂直净空天然不成问题。
# 另加一条支巷做"回头路"（03 §7：方便刷魂与取回掉落）。
def build_gifu():
    scene = Scene("L01Gifu")
    ground = _material(scene, "StandardMaterial3D_ground", GROUND_COLOR, 0.95)
    shell = _material(scene, "StandardMaterial3D_shell", SHELL_COLOR)
    stone = _material(scene, "StandardMaterial3D_stone", STONE_COLOR)

    street = GRID          # 街巷 4m 宽
    wall_h = LAYER_HEIGHT  # 町屋层高：只做"通过"，不做打架（10 §3.4）

    scene.node("L01Gifu", "Node3D", parent=None)

    env = _environment(scene, "Environment_gifu", (0.13, 0.15, 0.19), (0.40, 0.44, 0.53), 0.45)
    scene.node("WorldEnvironment", "WorldEnvironment", environment=f'SubResource("{env}")')
    scene.node("Sun", "DirectionalLight3D",
               rotation="Vector3(-0.95, -0.75, 0)", shadow_enabled="true")

    scene.node("Shell", "Node3D")

    # 地面：整条街一条长板（省件数，07 §7 的 Draw Call 纪律）
    scene.box("Ground", "Shell", (36.0, -WALL / 2, 0.0), (88.0, WALL, 40.0), ground)

    # 入口（三面墙 + 东侧敞开接街巷）
    scene.node("Entrance", "Node3D")
    scene.box("BackWall", "Entrance", (-WALL / 2, wall_h / 2, 0.0), (WALL, wall_h, 9.0), shell)
    scene.box("SideWallLeft", "Entrance", (0.0, wall_h / 2, -4.0 - WALL / 2),
              (9.0 + WALL, wall_h, WALL), shell)
    scene.box("SideWallRight", "Entrance", (0.0, wall_h / 2, 4.0 + WALL / 2),
              (9.0 + WALL, wall_h, WALL), shell)

    # 探索段 A：街巷
    scene.node("StreetA", "Node3D")
    for index, x in enumerate([6.0, 10.0], 1):
        scene.box(f"WallLeft{index}", "StreetA", (x, wall_h / 2, -street / 2 - WALL / 2),
                  (4.0, wall_h, WALL), shell)
        scene.box(f"WallRight{index}", "StreetA", (x, wall_h / 2, street / 2 + WALL / 2),
                  (4.0, wall_h, WALL), shell)

    # 遭遇战 A：8×8 露天（2 杂兵）
    scene.node("CombatA", "Node3D")
    for index, z in enumerate([4.0 + WALL / 2, -4.0 - WALL / 2], 1):
        scene.box(f"Wall{index}", "CombatA", (17.0, wall_h / 2, z), (9.0, wall_h, WALL), shell)
    _combat_area(scene, "CombatArea_A", "CombatA", (17.0, 1.0, 0.0),
                 (COMBAT_CLEAR, 4.0, COMBAT_CLEAR))

    # 探索段 B：街巷
    scene.node("StreetB", "Node3D")
    for index, x in enumerate([24.0, 28.0, 32.0], 1):
        scene.box(f"WallLeft{index}", "StreetB", (x, wall_h / 2, -street / 2 - WALL / 2),
                  (4.0, wall_h, WALL), shell)
        scene.box(f"WallRight{index}", "StreetB", (x, wall_h / 2, street / 2 + WALL / 2),
                  (4.0, wall_h, WALL), shell)

    # 遭遇战 B：12×12 露天（3 杂兵 + 1 精英）
    scene.node("CombatB", "Node3D")
    scene.box("WallLeft", "CombatB", (36.0, wall_h / 2, -6.0 - WALL / 2), (4.0, wall_h, WALL), shell)
    scene.box("WallRight", "CombatB", (36.0, wall_h / 2, 6.0 + WALL / 2), (4.0, wall_h, WALL), shell)
    # 远端墙留 2.4m 通路门——**战斗区必须有出口**。
    # 第一版这里是整面墙，无头自检当场把胶囊卡在 x=39.4 报了出来。
    far_half = DOOR_PASSAGE / 2
    far_segment = (13.0 - DOOR_PASSAGE) / 2
    scene.box("WallFarNorth", "CombatB",
              (40.0, wall_h / 2, -(far_half + far_segment / 2)), (WALL, wall_h, far_segment), shell)
    scene.box("WallFarSouth", "CombatB",
              (40.0, wall_h / 2, far_half + far_segment / 2), (WALL, wall_h, far_segment), shell)
    _combat_area(scene, "CombatArea_B", "CombatB", (36.0, 1.0, 0.0), (12.0, 4.0, 12.0))

    # 支巷（回头路）：把遭遇战 B 接回探索段 B 的北侧
    scene.node("Alley", "Node3D")
    scene.box("AlleyWallNorth", "Alley", (32.0, wall_h / 2, -10.0 - WALL / 2),
              (16.0, wall_h, WALL), shell)
    scene.box("AlleyWallSouth", "Alley", (32.0, wall_h / 2, -6.0 + WALL / 2),
              (16.0, wall_h, WALL), shell)

    # 鬼火存档（4×4 凹间）
    scene.node("SavePoint", "Node3D")
    scene.box("NookWallLeft", "SavePoint", (44.0, wall_h / 2, -2.0 - WALL / 2),
              (4.0, wall_h, WALL), shell)
    scene.box("NookWallRight", "SavePoint", (44.0, wall_h / 2, 2.0 + WALL / 2),
              (4.0, wall_h, WALL), shell)
    scene.node("Bonfire", "Marker3D", "SavePoint", transform=_transform((44.0, 0.0, 0.0)))

    # 短探索
    scene.node("StreetC", "Node3D")
    for index, x in enumerate([50.0, 54.0], 1):
        scene.box(f"WallLeft{index}", "StreetC", (x, wall_h / 2, -street / 2 - WALL / 2),
                  (4.0, wall_h, WALL), shell)
        scene.box(f"WallRight{index}", "StreetC", (x, wall_h / 2, street / 2 + WALL / 2),
                  (4.0, wall_h, WALL), shell)

    # BOSS 房：16×16 露天广场，半高石垣
    scene.node("BossRoom", "Node3D")
    for index, (x, z, sx, sz) in enumerate([
        (68.0, -8.0 - WALL / 2, 17.0, WALL),
        (68.0, 8.0 + WALL / 2, 17.0, WALL),
        (76.0 + WALL / 2, 0.0, WALL, 17.0),
    ], 1):
        scene.box(f"Wall{index}", "BossRoom", (x, (wall_h + 0.8) / 2, z),
                  (sx, wall_h + 0.8, sz), stone)

    # 西墙留 2.4m 入口——**第一版四面全封，自检把胶囊卡在 x=59.2 报了出来**。
    boss_half = DOOR_PASSAGE / 2
    boss_segment = (17.0 - DOOR_PASSAGE) / 2
    scene.box("WallWestNorth", "BossRoom",
              (60.0 - WALL / 2, (wall_h + 0.8) / 2, -(boss_half + boss_segment / 2)),
              (WALL, wall_h + 0.8, boss_segment), stone)
    scene.box("WallWestSouth", "BossRoom",
              (60.0 - WALL / 2, (wall_h + 0.8) / 2, boss_half + boss_segment / 2),
              (WALL, wall_h + 0.8, boss_segment), stone)

    _combat_area(scene, "CombatArea_Boss", "BossRoom", (68.0, 1.0, 0.0), (16.0, 4.0, 16.0))

    scene.node("BossSpawn", "Marker3D", "BossRoom", transform=_transform((72.0, 0.0, 0.0)))
    scene.node("PlayerSpawn", "Marker3D", transform=_transform((1.5, 0.1, 0.0)))

    # 灯笼沿街四盏（≤ MaxLanterns=6）。暖色＝注意力，密度刻意稀疏。
    _atmosphere(scene, [
        (13.0, 3.0, 1.8), (26.0, 3.0, -1.8), (44.0, 3.0, 1.8), (52.0, 3.0, -1.8),
    ])

    _path(scene, [
        (3.0, 0.0), (8.0, 0.0), (13.0, 0.0), (17.0, 0.0),
        (22.0, 0.0), (30.0, 0.0), (36.0, 0.0), (40.0, 0.0),
        (44.0, 0.0), (50.0, 0.0), (58.0, 0.0), (62.0, 0.0), (68.0, 0.0),
    ])

    arbiter = scene.add_ext("Script", "res://src/Core/CombatArbiter.cs", "1_arbiter")
    reset = scene.add_ext("Script", "res://src/Core/BattleReset.cs", "2_reset")
    player = scene.add_ext("PackedScene", "res://scenes/actors/Player.tscn", "3_player")

    scene.node("Systems", "Node3D")
    scene.node("CombatArbiter", "Node", "Systems", script=f'ExtResource("{arbiter}")')
    scene.node("BattleReset", "Node", "Systems", script=f'ExtResource("{reset}")')
    scene.node("Player", "", ".", instance=player, transform=_transform((1.5, 0.1, 0.0)))

    return scene


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    targets = [
        (build_dojo(), os.path.join(root, "scenes", "levels", "Dojo.tscn")),
        (build_gifu(), os.path.join(root, "scenes", "levels", "L01_Gifu.tscn")),
    ]

    for scene, path in targets:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with io.open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(scene.render())
        print(f"wrote {os.path.relpath(path, root)}")


if __name__ == "__main__":
    main()
