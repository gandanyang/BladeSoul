# 资产来源与授权记录

> 07 文档 §4 的纪律：**每引入一个资产就当场记一行**，不要"以后再补"。
> 发布前必须过一遍——尤其是"免费但不可商用"的资产，漏记就是法律风险。

## 音频

| 资产 | 来源 | 授权 | 用途 | 状态 |
|---|---|---|---|---|
| `assets/audio/sfx/sfx_*.wav`（14 个） | **本项目程序化生成**，脚本 `tools/gen_placeholder_sfx.ps1` | 自制，等同 CC0，可自由使用与替换 | 战斗音效占位（命中/格挡/弹开/拼刀/破防/一闪/忍杀/挥空/闪避/危攻击预警） | **占位，待替换** |

> 这 14 个音效是**用代码合成的占位品**（正弦 + 噪声 + 指数包络），
> 不是最终资产。它们的作用是让 M0.5/M1 阶段的手感能被听见——
> 07 文档说"音效是战斗手感的 50%"，而这件事不能等到美术阶段才做。
>
> 重新生成：`powershell -NoProfile -File tools\gen_placeholder_sfx.ps1`
> （固定随机种子，同样的脚本永远生成同样的字节）

## 模型 / 动画 / 贴图 / 环境

| 资产 | 来源 | 授权 | 用途 | 状态 |
|---|---|---|---|---|
| `assets/models/model_player_congyun_01.glb` | **AI 生成**：TapTap Maker `create_3d_asset`（Tripo 后端）文本→四视图审核→模型→自动绑骨；asset `e933654bd1064c068d7ce28709d9586b`，2026-09-12 | AI 生成内容，本项目自有，可自由使用 | 主角「丛云」静态模型（几何 + 法线 + UV0 + 内嵌贴图），依赖 03 §2.5 造型设定 | **静态版，无骨架**；绑骨/重定向待 Blender → Mixamo |
| `assets/models/model_player_congyun_02.glb` | **本地 AI 生成**：ComfyUI ＋ Hunyuan3D 2.1（`hunyuan_3d_v2.1.safetensors`），2026-09-13，输入图 `ref_protagonist_congyun_v2_a_front.png` | AI 生成内容，本项目自有 | 主角「丛云」**按新方向（中国剑客×西幻剑客）重做的几何基线**：415,894 面 → 去渣 83,180 块 → 降面 **11,500 面 / 287 KB**，包围盒 0.95×1.95×0.46（人形），法线已由 `cleanup_mesh.py` 补上 | ⚠️ **只有几何**：`POSITION+NORMAL`，**无骨架 / 无 UV / 无贴图**（旧 01 是带 `JOINTS_0+WEIGHTS_0+TEXCOORD_0` 的绑骨模型）。**尚未接入游戏**——接线要等 Blender 重拓扑＋绑定 ＋ Mixamo 骨架 ＋ T27 贴图 |


> 该 GLB 由 Maker 产出的 UrhoX `.mdl`（UMD2）经自写导出器转换而来
> （临时脚本 `%TEMP%\opencode\mdl2glb.py`，复用了 TapMaker `mdl-voxelize` 技能的只读解析器）。
> 官方 `convert` 不支持 MDL 源、本机无 Blender，故只能导出**静态几何**——骨架数据不在此通道内。
>
> 两个已知导出细节，接入时注意：
> 1. **比例**：模型被归一化到高度 ≈ 1.0 单位，需缩放约 **1.75×** 才是设定身高（≈175cm）。
> 2. **手系**：UrhoX 为左手系，导出时按惯例做了 `Z 取反 + 翻转绕序`；若在 Godot 中看起来左右镜像，
>    用 `--no-flip` 重导一次即可。

其余可见内容仍是**程序化灰盒**（`src/Dev/BlockoutRig.cs`），
或 Godot 内置的 `BoxMesh` / `CapsuleShape3D` / `PlaneMesh`。

### 参考图（非运行时资产）

| 资产 | 来源 | 授权 | 用途 |
|---|---|---|---|
| `assets/references/ref_enemy_ashigaru_01.png` / `_02.png` | **本地 AI 生成**：ComfyUI ＋ Z-Image Turbo，2026-09-13 | AI 生成内容，本项目自有 | **魔骸足兵**的 3D 生成输入图（T35 本地 / T36 在线，两路共用同一张） |

| `assets/references/ref_enemy_ashigaru_v2_a…f.png`（6 张） | **本地 AI 生成**：ComfyUI ＋ Z-Image Turbo，2026-09-13 | AI 生成内容，本项目自有 | **v2 版输入图**。v1 两次生成 3D 都不满意，按 [12 §6](12-资产来源与后备方案.md) 的六条规格重做；**主用 `_v2_d`（纯白背景，对比度最高）** |

| `assets/references/ref_heroine_aya_view_a…f.png`（6 张） | **本地 AI 生成**：ComfyUI ＋ Z-Image Turbo，2026-09-13 | AI 生成内容，本项目自有 | **女主角/同伴「绫」的各方位参考图**：正面 A 字姿 / 右侧 / 背面 / 左侧 / 左前 3/4 / 面部特写。供 T29 建模（本地 Hunyuan3D 或 Tripo）使用 |

| `assets/references/ref_heroine_aya_concept_a…f.png`（6 张） | **本地 AI 生成**：ComfyUI ＋ Z-Image Turbo，2026-09-12（原 jobs 清单：`_aya_jobs2.txt`） | AI 生成内容，本项目自有 | 「绫」的**人设图**：半身 / 全身 / 背面 / 诊伤 / 雨巷 / 面部特写。气质与配色的锚点 |

| `assets/references/ref_heroine_aya_v2_a…f.png`（6 张） | **本地 AI 生成**：ComfyUI ＋ Z-Image Turbo，2026-09-13 | AI 生成内容，本项目自有 | 「绫」的**各方位参考图 v2（3D 管线用）**：深灰背景 ＋ 无行囊。正面与背面已实测能生成正常模型 |

| `assets/references/ref_protagonist_congyun_v2_a_front.png` / `_b_back.png` / `_c_face.png`（3 张） | **AI 生成**：GPT-image（制作人操作），2026-09-13 | AI 生成内容，本项目自有 | 主角「丛云」的**外观定案参考图**（正面全身 / 背面全身 / 面部特写）。对应 **2026-09-13 的方向修正**：主角从"日本浪人剑客"改为**中国剑客 × 西幻剑客**（见 [09](09-主角外观设定.md) 抬头）。**这 3 张是当前唯一有效的主角外观参考**；`model_player_congyun_01.glb` 是方向修正前的旧模型 |

> 主角 v2 的提示词记在 `ref_protagonist_congyun_v2.prompt.txt`。
> **⚠️ 这 3 张与规格之间还有 5 条未消除的偏差**（薄片结构 / 笼手材质 / 袍长 / 肤色 / 剑出画），
> 逐条记在该文件的"实测偏差"里——**送给 3D 管线前先看那一节**。

> 提示词与全部生成参数记在同目录的 `ref_enemy_ashigaru_01.prompt.txt`——
> **可复现**（固定 seed ＋ 清单文件），换台机器跑同样参数能得到同样的图。
> v2 的参数记在 `ref_enemy_ashigaru_v2.prompt.txt`（含六张各自的变量与规格对照表）。
>
> 「绫」的各方位图参数记在 `ref_heroine_aya_v1.prompt.txt`（含六个 seed 与**四条已知偏差**：
> 脚底接触阴影未完全去除 / 风格偏手绘感 3D / 年龄感偏小 / 行囊偏大）。
> v2 的参数与**对照实验记录**记在 `ref_heroine_aya_v2.jobs.txt`（v1 为什么废、两次对照组的数据）。

| `assets/references/uv_congyun_regionmap.png` / `uv_congyun_layout.png`（2 张，2048²） | **本地工具生成**：`tools/uv_protagonist.py`（headless Blender 4.5.13），2026-09-13 | 本项目自有 | 丛云模型的 **UV 分区配色图**：9 个部位各一块纯色（配色直接取 [09](09-主角外观设定.md) §3 配色板）。**用途：作为 GPT-image 生成「无缝材质表」时的参考图**，告诉模型哪块面积对应哪个部位。`_layout` 是叠了岛边界的版本 |

| `assets/models/model_player_congyun_03_uv.glb` | 本地工具生成：`tools/uv_protagonist.py`，2026-09-13 | 本项目自有 | `_03_rigged.glb` 的 **UV 副本**：按部位分组 `unwrap` ＋ 整体 `pack_islands`，覆盖率 84.7%，9 块连通岛。**不覆盖 `_rigged`**（T48 的在制品不受影响）。⚠️ 该 UV **带自相交**（分组展开未切缝），当参考图够用、当最终贴图布局不够用 |

| `assets/references/tex_prompt_congyun.md` | 本项目自有（制作人操作 GPT-image） | — | 主角「无缝材质表」的提示词，六格锁定 [09](09-主角外观设定.md) §3 配色板。**贴在 17 §2.5 的 UV 流程之后**：UV 分区图当参考图 → 出 6 张无缝材质 → 按 `Body/Cloth/Leather/Metal/Gauntlet/Eye` 分槽回填 |

| `assets/references/tex_sheet_congyun_v1.png` | **AI 生成**：GPT-image（制作人操作，用上面那条提示词），2026-09-14，1254² | AI 生成内容，本项目自有 | 主角「无缝材质表」原图：3 列 × 2 行（411×621 竖长条格 ＋ 白分隔缝）。六格顺序 cloth_in / cloth_out / leather / metal / gauntlet / belt。**一次通过**——无红色、无金色、笼手正是 09 §5 要的「湿的会生长的甲壳 ＋ 沟槽紫光」 |

| `assets/textures/congyun/tex_{cloth_in,cloth_out,leather,metal,gauntlet,belt}.png`（6 张，512²） | 本地工具生成：`tools/slice_material_sheet.py`，2026-09-14，源图 = 上面那张 | 本项目自有 | 从上表**按检测到的分隔缝精确切**（不是按百分比裁）＋ **真无缝化**后的成品贴图。对照组：接缝比 皮革横向 **48.4→1.20**、金属 **26.8→0.94**；边缘亮度 0.21~0.54（阈值 0.85，防白缝残留） |

| `assets/models/model_player_congyun_03_textured.glb` | 本地工具生成：`tools/apply_materials.py`，2026-09-14，3.4 MB | 本项目自有 | **当前主角外观的最终形态，`Player.tscn` 用的就是它**：输入是 T48 切完剑的 `_03_rigged_weapon.glb`（含 `Weapon_R` / `Scabbard` 两根附加骨）＋ **9 个材质槽**（`Body`/`Hair`/`Scabbard` 纯色，`ClothInner`/`ClothOuter`/`Leather`/`Metal`/`Gauntlet`/`Belt` 贴图）。盒子投影现做 UV（不用那张带自相交的展开图）。**导出后自检通过**：`skins=1`、带 skin 节点=1、9 个材质全部带贴图或颜色、图片全部内嵌（`bufferView`，不是外部 URI） |

## 字体 / 音频库

暂无。UI 使用 Godot 默认字体；BGM 尚未引入。

---

## 待办（引入任何外部资产时逐条补齐）

- [ ] Mixamo 动画（需记录具体动作名与下载日期，Adobe 免费可商用）
- [ ] Quaternius / Kenney / Poly Haven 模型与音效（CC0）
- [ ] Freesound 音效（**逐个确认授权**，部分为 CC-BY-NC）
- [ ] Sonniss GDC Game Audio Bundle（免版税，但要记录具体包名）
- [ ] 字体（若使用中文字体，注意 SIL OFL 与商用条款）
