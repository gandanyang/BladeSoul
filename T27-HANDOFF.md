# T27 交接 · 魔骸枪兵：建模全流程 + 一处影响全场的配色缺陷修复

## 一句话

**枪兵从头走完了本项目的"人物建模流程"（参考图 → 3D 生成 → 去碎渣降面补法线 → 上色 → 入库 → 同屏验收），
并在过程中挖出并修掉了一个已经上线的缺陷：魔骸的顶点色按 sRGB 写入、被引擎当线性读，
于是"黑 + 血红"在游戏里一直显示成"灰 + 粉红"。**

| 交付 | 文件 | 数字 |
|---|---|---|
| 参考图 6 张 | `assets/references/ref_enemy_yari_v1_{a..f}.png` | 1024×1536，种子与全部参数见 `.jobs.txt` |
| 枪兵本体 | `assets/models/yari_v1_clean.glb` / `yari_v1_colored.glb` | **11,500 面**（预算 ≤12k）；包围盒 0.847×1.251×0.308（瘦长 ✓） |
| 完整长枪 | `assets/models/yari_spear_v1_clean.glb` / `_colored.glb` | **1,499 面**；1.904m 长 |
| 同屏验收图 | `assets/references/t27_yari_showcase.png` | 主角 + 足兵 + 枪兵 |
| 道具图 | `assets/references/t27_yari_spear.png` | 枪兵 + 完整长枪 |

---

## 一、先说影响面：我改了已经上线的东西（**请制作人过目**）

### 1. `tools/paint_mesh.py`：COLOR_0 是**线性**通道，脚本一直按 sRGB 字节写

`assets/models/ashigaru_v2d_colored.glb` 就是道场里那尊魔骸（`Dojo.tscn` → `Makugai`，
材质 `data/materials/makugai_body.tres` 开了 `vertex_color_use_as_albedo`）。
所以这不是"某个测试场景的问题"，是**游戏里一直在发生**的：

| 调色板（10 §1 规定） | 修前，引擎里显示成 | 修后 |
|---|---|---|
| 血红 `#7A1418` | **`#B84D54` 粉红** | `#7A1616`（8bit 量化极限，差 2 级） |
| 主色黑 `#141014` | `#3D373D` 灰 | `#141014` |

**两条独立证据**：

1. **数值**：`t27_yari_showcase.png` 修前采样 = **(185,92,100)**；
   "sRGB 字节按线性读"的理论值 = (184,77,84)——吻合。
   修后 = **(130,46,47)**。道场现有截图 `t40_after.png` 里魔骸腰带 = (163,85,90)，同样偏粉。
2. **自检**：脚本现在自己把写入值按引擎的方式回读一遍：
   `自检: 血红 写入线性 #320202 → 引擎回读 #7A1616（与 #7A1418 最大差 2 级） ✅`；
   用 `--color-space raw` 复现旧行为时，同一行会报 **差 62 级**。

**我动了什么**：`paint_mesh.py` 加了 `--color-space`（默认 `srgb-to-linear`，`raw` 保留旧行为），
并**重生成**了 `ashigaru_v2d_colored.glb` 与 `yari_v1_colored.glb`。
**道场那尊魔骸的外观会一起变（灰粉 → 黑红），这是预期的。**
改动前的两份 glb 备份在 `.workbuddy/tmp/*.before-srgb-fix.glb`。

### 2. 顺带修掉两处"实现与自己注释不符"的分层缺陷

`paint_mesh.py` 里 `血红` 那行的注释写的是"**腰带一带**"，实现却是**纯高度切带**：

| 缺陷 | 现象 | 现在 |
|---|---|---|
| 手臂落进腰带的高度区间 | **红手套**（前臂与手全红） | 加**横向闸门**（相对半宽 0.55，两个模型共用）：足兵排除 380 个、枪兵排除 458 个手臂/手顶点 |
| 眼窝环带 4.5% 身高 ≈ 7.6cm | 像一条**红色眼罩** | 收窄到 2%（≈3.4cm）；血红顶点占比 足兵 33.1%→27.0%、枪兵 36.8%→29.5% |

> **仍然是 MVP 近似**：它按高度切，不知道"哪块是甲、哪块是手"。要真正准确得先有 UV 或骨骼权重——
> 那是另一张卡。若制作人觉得红腰带画在草摺上偏大，改 `tools/paint_mesh.py` 顶部两个常量即可。

---

## 二、枪兵是怎么做出来的（可复现）

```
① 出图   python zimage_run.py --jobs-file assets/references/ref_enemy_yari_v1.jobs.txt \
                              --num 1 --style none --tag yari1
② 生成   comfy-local run_workflow  .workbuddy/tmp/yari_v1_b_api.json
         （= tools/comfy_hunyuan3d_2.1_api.json 换 LoadImage/seed/prefix；crop 必须 none）
③ 体检   python tools/preview_glb.py <原始.glb> --out x.png --min-faces 500
④ 清理   python tools/cleanup_mesh.py <原始.glb> --out assets/models/yari_v1_clean.glb --faces 11500
⑤ 上色   python tools/paint_mesh.py assets/models/yari_v1_clean.glb \
                --out assets/models/yari_v1_colored.glb --glow 1.0
⑥ 入库   godot --headless --path . --import
⑦ 验收   godot --path . res://scenes/tests/ModelShowcaseYari.tscn   （带窗口）
```

**用哪个 python**：`tools/*.py` 需要 `trimesh`，本机只有 **ComfyUI 自带的那个**有
（`F:\ComfyUI-aki-v3\python\python.exe`）。managed python 与系统 Python310 都没有 trimesh。

**实测数字**（与足兵同条件，用于判断"枪兵是不是特别差"）：

| | 足兵 v2d | 枪兵 v1 |
|---|---|---|
| 原始面数 / 连通块 | 660,972 / 214,826 | 512,890 / 198,363 |
| 最大连通块 | 175,278 | 79,228 |
| 清理后 | 11,500 面，1.355×1.700×0.457（略宽） | **11,500 面，0.847×1.251×0.308（瘦长 ✓）** |
| 表面破面 | **大面积破洞** | 同级别（三视图对照过） |

→ 破面是**本管线固有的质量水位**（surface net 产物 + 去碎渣留下的孔），
枪兵不低于已入库的足兵。**这是全项目的问题，不是这一张卡的。**

### 两个坑这次没有再踩（都用判据核过）

1. **`crop=none`**：工作流里已固定；本次头**没有被切掉**（docs/13 §3 那条坑）。
2. **只喂正面 → 背面是编的**：本工作流是**单图**结构（一个 `LoadImage` + 一个 `CLIPVisionEncode`），
   **吃不了多视图**。这次靠"提示词里让背面没有可编的东西"降低风险（枪兵背甲无挂件）——
   但这是**降级手段，不是解决**。女主「绫」的腰后挂件靠这招就救不回来。

---

## 三、没做的（别以为完成了）

| 项 | 状态 |
|---|---|
| **足兵的打刀与阵笠** | ❌ **从来没做过**（T39 采纳了"先砍掉阵笠和刀"的建议，之后没补）。T27 的交付物清单里足兵那一栏并未真正闭环 |
| **长枪装到枪兵身上** | 长枪已建模（1,499 面），但**没有挂点**：敌人现在没有武器骨（主角的 `Weapon_R` 是 T48 单独加的）。谁装配敌人 actor，谁决定挂法 |
| **材质槽 / UV / 贴图** | 未做。现在是顶点色 MVP，10 §2.2 的"枪尖红光拖尾"也还没做（那是 VFX，属 T28 的地盘） |
| **枪兵进关卡** | 只在展示场景里。道场（`Dojo.tscn`）与 L01 都没摆——**那张卡/那个人来摆** |
| **本次出图的"地面阴影"** | 六张都有。12 §6 第 2 条**靠提示词保证不了**（见 `.jobs.txt` 里的记录）。本次按足兵先例继续走，结果没生成底盘 |

## 四、没有改动

- 未执行任何 `git commit`。
- 未改战斗逻辑、`data/**`、`CombatResolver`、任何判定窗。
- 未改 `Dojo.tscn`（只在 `scenes/tests/` 下加了两个展示场景）。
- 未改 `ModelShowcase.cs`（它本来就支持第三个模型位）。

---

# 第二轮（同日）：补齐足兵的**阵笠**与**打刀**

上面第三节里"足兵的打刀与阵笠 ❌ 从来没做过"这一条，本轮补上了。

| 交付 | 文件 | 数字 |
|---|---|---|
| 阵笠 | `assets/models/jingasa_v1_clean.glb` / `_colored.glb` | **616 面**；直径 0.800m、高 0.185m、缘厚 0.020m、内腔深 0.072m；**水密 True** |
| 打刀 | `assets/models/uchigatana_v1_colored.glb` / `_clean.glb` | **864 面**；全长 1.018m（柄 0.255 + 鍔 0.007 + 刀身 0.760）、反り 22mm、元幅 33→先幅 21mm；**水密 True** |
| 生成器 | `tools/gen_props.py`（新） | 参数在文件头；能求解阵笠落点 |
| 装配验收 | `scenes/tests/ModelShowcaseAshigaruDressed.tscn` + `src/Dev/PropFitShowcase.cs`（新） | 足兵 ＋ 笠 ＋ 刀 同框，**必须带窗口跑** |
| 体检三视图 | `assets/references/t27_jingasa_views.png` / `t27_uchigatana_views.png` | `tools/preview_glb.py` |
| 装配图 | `assets/references/t27_ashigaru_dressed.png` | 1100×1100 |
| 头部特写 | `assets/references/t27_ashigaru_head.png` | 用来判断"头在哪"（见第五节） |

## 〇、★★ 又修掉一个**已上线**的缺陷：`paint_mesh.py` 把 `NORMAL` 丢了

**症状**：魔骸与道具在引擎里**没有形体明暗**——胴甲、肩甲、草摺、佩楯全糊成一块平色。
不是"不好看"，是**看不出身上有任何甲片**，与本管线 §2.4 记的"没有法线 → 没有明暗"完全一致。

**定位**：`cleanup_mesh.py` 第 3 步专门补过法线（Hunyuan3D 的 `SaveGLB` 只写 `POSITION`），
但 **trimesh 的 glb 导出是 `include_normals=None`——只在 `vertex_normals` 缓存里已有法线时才写**，
而 `load()` 进来的法线**不进缓存** → `paint_mesh.py` 一导出就**又丢掉了**。

**证据**（读 glb 的 JSON chunk，不看观感）：

| 文件 | `primitive.attributes` |
|---|---|
| `jingasa_v1_clean.glb`（上色前） | `['NORMAL', 'POSITION']` ✅ |
| `jingasa_v1_colored.glb`（**修前**） | `['COLOR_0', 'POSITION']` ← **NORMAL 没了** |
| `jingasa_v1_colored.glb`（**修后**） | `['COLOR_0', 'NORMAL', 'POSITION']` ✅ |

**影响面**：`paint_mesh.py` 是**所有** `*_colored.glb` 的出口——包括**道场场上那尊魔骸**
（`ashigaru_v2d_colored.glb`）与枪兵、长枪。所以这不是某一件资产的问题，是**全项目魔骸都在发生**。

**修法**：导出前 `mesh = mesh.copy(); _ = mesh.vertex_normals`，并 `include_normals=True`；
再加一条**读回自检**（把写出的 glb 读回来，断言 `COLOR_0` 与 `NORMAL` 都在）。
**故意不调 `fix_normals()`**：生成式产物**不水密**，`fix_normals` 依赖闭合体积判内外，
在不水密网格上可能翻掉一整片的朝向。

**实测视觉差异**（同一场景、同一光照，只差 `NORMAL`）：
修前 `t27_ashigaru_dressed.png` 的躯干是一片平灰、**看不出甲片**；
修后同一角度能读出胴、肩甲、草摺、佩楯的分块与转折。**这是本轮最大的画质提升。**

> ⚠️ **它藏了很久，因为渲染"看起来正常"**——缺法线时 Godot 会兜底生成，
> 只是生成的东西没有形体。**判据必须落在文件上（读 glb chunk），不能落在观感上。**
> 备份：修前的五份 `*_colored.glb` 在 `.workbuddy/tmp/colored-before-normal-fix/`。

## 一、这两件**故意没走** Hunyuan3D（理由都在 docs 里记着）

| 已记录的失败模式 | 记在哪 | 程序化为什么能绕过 |
|---|---|---|
| **薄片/悬空结构会被做坏** | TASKS.md T39：「阵笠是最容易被 clean topology 做坏的（retopo 常把薄片糊成一块或穿洞）」 | 车削一个**闭合的、有厚度的壳**——"薄片"在构造上不存在 |
| **细长结构伸出画面 → 深度估计失准、形状散掉** | 12 §6 第 4 条（打刀正是细长物） | 长宽厚按参数给定，不经过深度估计 |
| **产物没有 NORMAL → 引擎渲染成纯白块** | docs/13 §3（T35 验收第一版翻车） | 导出前显式 `fix_normals()` ＋ 触发 `vertex_normals` 缓存（沿用 `cleanup_mesh.py` 的做法） |
| **必须先清碎渣再降面，否则降不动** | docs/13 §3（660k 面/21 万块，降到 81,016 面卡住） | 面数按参数生成，**没有碎渣可去** |

> **边界（别把这条通道用错地方）**：程序化只适合**人造的、几何规则**的东西——
> 笠、刀、枪、甲片、建筑构件。**有机形体（人、魔骸的躯干、布料）仍然必须走生成式通道。**

## 二、"笠该戴在哪"是**算出来的**，不是手填的

`python tools/gen_props.py --what jingasa --fit assets/models/ashigaru_v2d_clean.glb`

从 y=0.40 往上每 0.5mm 扫一次，找**第一个不与本体穿模**的笠缘平面高度。
判据是 `contains`（点在实体内），所以要求外壳水密——不水密直接报错，不给假数字。

```
落点  : 笠缘平面 y = 0.7660   （判据：1,407 个上半身顶点全部落在笠外壳之外）
        接触距离 0.24mm（≈0 才是「搭住」）；压低 2mm 立刻穿模 4 处
```

**两组对照**（AGENTS.md §7：有对照实验必须给对照组数据）：

| 项 | 值 | 说明 |
|---|---|---|
| 接触距离 | **0.24mm** | ≈0 → 这个落点是真的"搭住"，不是浮空 |
| 再压低 2mm | **4 处穿模** | 边界是真的；这一项若为 0 就说明判据在扫空气，落点不算数 |

**装配高度取 0.800**（略高于求解出的 0.766）：求解值给出的是**物理下限**，
再低一定穿模；0.800 是美术取值——让笠缘离肩甲留出约 6cm，**脖子读得出来**。
`PropFitShowcase` 会把这一层关系打印出来。

## 三、★ 两轮返工，都是**看图**才发现的（数值断言全绿）

| 返工 | 现象 | 根因 |
|---|---|---|
| 笠像**斗篷**，一路滑到肩膀上 | 罩住整颗头、与肩甲连成一片 | 常量名写成 `JINGASA_CAVITY`，语义是"顶点比顶面**低**多少"，代码里 `H - 0.030` 得到的是**内腔深 20.5cm**——而足兵的"头"只有 0.21m 高。**名字起错会一直误导自己**，故改名为 `JINGASA_CAVITY_DEPTH`（= 内腔深，单位就是深度） |
| 笠像**平碟子**扣在头上 | 圆顶感、不是锥 | 外轮廓控制点太凸（中间高于直线锥）。阵笠的笠缘本来是**微微外张下压**的，改成"直线锥 + 靠近笠缘处略下垂" |

> 这两条都不是"数值错了"，是"看着不对"。**3D 资产必须同屏看图验收**——
> 与上一轮那个配色缺陷同一类，断言/包围盒/面数全绿也证明不了"看起来对不对"。

## 四、道具的局部原点约定（装配参考点，不是世界原点）

| 部件 | 原点 | 挂法 |
|---|---|---|
| 阵笠 | **笠缘平面的圆心**。倾斜已经**烘进网格**（10 §1 的"歪掉的阵笠"是这只笠的设定），所以场景端不用再给角度 | 位置 = `HatOffset`，旋转 = 单位 |
| 打刀 | **鍔**（握把与刀身的分界），+y 指向切先 | 位置 + 旋转 |

## 五、要制作人拍板的（本轮**没有**自行改）

量出来的**足兵本体解剖事实**（`ashigaru_v2d_clean.glb`，总高 1.6998）：

| 部位 | 高度区间 | 横向宽度 |
|---|---|---|
| 肩（最宽） | t 0.80~0.88（y 0.49~0.61） | **0.80~0.85m** |
| 颈/肩甲过渡 | t 0.88~0.97（y 0.63~0.78） | 0.76 → 0.47m |
| **真正的头** | **t 0.97~1.00（y 0.78~0.834）** | **只有 0.11m 宽、5cm 高** |
| 血红腰带 | t 0.42~0.62（y −0.15~0.19） | 占满全宽 → 看起来像**红短裤/红围裙** |

**两处待拍板**（我只报数，不动手）：

1. **"眼窝红光"实际涂在颈/肩上**：`paint_mesh.py` 的 `EYE_BAND = (0.925, 0.945)`
   落在 y 0.706~0.740，而那一层宽 **0.6212m**（= 胸/肩宽度）。
   但这只模型的真头只在 t≥0.97，整个头才 5cm 高——**"眼窝"在这个模型上没有落点**。
   现在的效果读起来像"颈部一道发光的裂口"，仍在 10 §1「伤口与眼窝透红光」的语汇内，
   所以**我没有改**。要改的话，正确做法不是挪一个百分比，而是**先找脖子**
   （上段宽度下降最陡的那一层）再按头的比例定位——那是 `paint_mesh.py` 的一次重构。
2. **血红腰带占身高 20% 且涂满全宽**，看起来像红短裤，与"低饱和魔骸"的印象相冲。
   10 §1 写的是"腰带一带"。收窄 / 加横向门都是改 `paint_mesh.py` 顶部常量。

> 两条都会**同时改变道场里那尊魔骸的外观**（它与展示场景共用 `ashigaru_v2d_colored.glb`）。
> 上一轮的配色修正已经改过一次它的外观，所以这次不再叠一次未经拍板的变化。

## 六、没做的（别以为完成了）

| 项 | 状态 |
|---|---|
| **长枪 / 打刀 / 阵笠 的挂点** | ❌ 敌人现在**没有武器骨也没有挂点**（主角的 `Weapon_R` 是 T48 单独加的）。本轮的挂法是 `PropFitShowcase` 里的场景数值，**不是** actor 装配逻辑 |
| **打刀握持姿势** | 提案：右手 (0.680, 0.037, −0.057)、切先朝下的"拖刀"。足兵是 A 字姿、手臂张得很开，这个读法本身也需要制作人认可 |
| **材质槽 / UV / 贴图** | 未做。仍是顶点色 MVP；10 §2.2 的"枪尖红光拖尾"也没做（VFX，属 T28） |
| **进关卡** | 三件道具都只在展示场景里。道场与 L01 都没摆 |
| **足兵本体是否需要重出** | 见第五节：头只有 5cm、肩宽 0.85m、红腰带占 20% 身高。这是"要不要重出模型"的决策，不是本轮的范围 |

## 七、复现（照抄即可）

```bash
PY=F:/ComfyUI-aki-v3/python/python.exe      # tools/*.py 依赖 trimesh，本机只有这个 python 有

$PY tools/gen_props.py --what both --out-dir assets/models \
      --fit assets/models/ashigaru_v2d_clean.glb --report .workbuddy/tmp/props_report.json
$PY tools/paint_mesh.py assets/models/jingasa_v1_clean.glb \
      --out assets/models/jingasa_v1_colored.glb --style jingasa
$PY tools/paint_mesh.py assets/models/uchigatana_v1_clean.glb \
      --out assets/models/uchigatana_v1_colored.glb --style katana --glow 1.4
$PY tools/preview_glb.py assets/models/jingasa_v1_colored.glb \
      --out assets/references/t27_jingasa_views.png --size 640

# 入库 + 装配验收（**带窗口**，--headless 是 dummy 渲染器会抓空帧）
godot --headless --path . --import
godot --path . res://scenes/tests/ModelShowcaseAshigaruDressed.tscn
```

想改笠的形状/大小/歪的程度，改 `tools/gen_props.py` 顶部的 `JINGASA_*` 常量重生成；
想改配色，改 `tools/paint_mesh.py` 顶部——**调色板与色彩空间只有那一个地方定义**。

## 八、本轮验证的真实输出

```
--- 0/34 import new assets ---          ← 自动发现两件新道具并导入
--- 1/34 build ---
--- 2/34 unit tests (pure logic) ---
...（35 个步骤标记，全过）
ALL CHECKS PASSED
CHECK_EXIT=0
```

（`powershell -NoProfile -File tools\check.ps1`，跑完把日志转 UTF-8 后逐条核对；
按 `FAIL|错误|Exception|✗` 过滤 **0 行命中**。）

手工验收：

```bash
godot --path . res://scenes/tests/ModelShowcaseAshigaruDressed.tscn   # 足兵+阵笠+打刀
godot --path . res://scenes/tests/ModelShowcaseAshigaruHead.tscn      # 头部特写（判断"头在哪"）
godot --path . res://scenes/tests/ModelShowcaseYari.tscn              # 主角 vs 足兵 vs 枪兵
```

`PropFitShowcase` 会把挂载关系直接打印出来（屏幕上该看到什么 = 日志里的这几行）：

```
[道具] 本体：原始高 1.700m → 缩放 ×1.0001（目标 1.70m）
[道具] 阵笠：局部偏移 (0.0009, 0.8, -0.0639)  朝向 (0, 0, 0)  自身包围盒 0.792×0.247×0.792m
[道具] 打刀：局部偏移 (0.68, 0.037, -0.057)  朝向 (180, 0, 0)  自身包围盒 0.084×1.018×0.070m
[道具] 装好后整体：宽 1.397 × 高 1.853 × 深 0.793 m
[道具] 阵笠把总高从 1.700m 抬到 1.853m
[道具] 判断「盖住头」：笠缘平面 1.666m，本体头顶 1.700m → 笠缘在头顶**下方**（罩住整颗头，阵笠的正常戴法）
[道具] 已出图：res://assets/references/t27_ashigaru_dressed.png（1100×1100）
```

> 注意 `阵笠：局部偏移 y = 0.8` 是**美术取值**（见第二节）；求解出的物理下限是 **0.766**。
> 两个数分工不同，别把它们当成同一个。

## 九、本轮沉淀的可复用教训（已写进 `docs/13` §7 与用户级技能 `procedural-3d-props`）

1. **分两支选通道**：人造规则物 → 程序化；有机形体 → 生成式。混用会两头都亏。
2. **"贴到已有资产上"的位置要解出来 + 带对照组**，求解值（下限）与美术取值（最终）是两个数。
3. **判据落在文件上，不落在观感上**：`NORMAL` 丢了渲染照样"正常"，
   只能靠读 glb 的 `primitive.attributes` 发现。
4. **常量名与语义反着来会一直误导自己**（`JINGASA_CAVITY` 那一次）。
5. **3D 资产必须同屏看图验收**，且**必须带窗口**。
