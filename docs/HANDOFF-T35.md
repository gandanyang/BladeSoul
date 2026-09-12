# T35 交接：本地 3D 生成通道（现场快照）

> 用途：上一个对话（thread `01a094ca-46d9-7270-a825-6571557d8d3f`）在**结果回传那一步**
> 被 provider 拒绝而中断。这份文档把磁盘上的真实状态固化下来，
> **新会话读这一个文件即可接上**，不需要重放那 30MB 的 rollout。
>
> 快照时间：2026-09-13 03:0x ｜ 仓库 HEAD：`41e6707`（+ 若干未提交）

---

## 一、一句话状态

**通道跑通了，模型也清理干净并进了仓库、过了 Godot 导入。**
剩下的只有"需要人眼/需要 GPU 计时"的三项：同屏辨识截图、显存峰值与单次耗时、流程文档。

## 二、已完成（都有实测数字）

| 步 | 状态 | 证据 |
|---|---|---|
| 下载权重 | ✅ | `models/checkpoints/hunyuan_3d_v2.1.safetensors` **7.37 GB** |
| 跑通生成 | ✅ | `F:\ComfyUI-aki-v3\ComfyUI\output\3d\ashigaru_v2d_00001_.glb`（660,136 面） |
| **去碎渣** | ✅ | `tools/cleanup_mesh.py` → 175,278 面 |
| **降面** | ✅ | → **11,500 面**，包围盒 1.355 × 1.700 × 0.457 |
| **进仓库 + Godot 导入** | ✅ | `assets/models/ashigaru_v2d_clean.glb`（**209 KB**），`.import` 正常，`check.ps1` 全绿（验收 1） |

### ★ 关键发现：第一次生成的模型有 73.5% 是碎渣

用 `trimesh` 体检第一次的产物：

```
面数 660,136   顶点 213,504   包围盒 1.962 × 1.963 × 0.464
连通块 214,826 个！最大的一块只占 175,278 面（26.5%）
欧拉数 57,373（大量孔洞/非流形）  水密 False  无 UV、无贴图，只有顶点色
```

也就是说 **21 万多块浮空碎屑**撑着包围盒。三视图里能看到本体其实是好的
（兜、肩甲、胴、腰带、草摺、腿俱全），但旁边挂着一大片"碎渣幕布"。

**原因**：蓝图的 `VoxelToMesh ['surface net', 0.6]` 在这种参数下会切出大量碎块；
`VAEDecodeHunyuan3D [8000, 256]` 的 `256` 是 **octree 分辨率**，660k 面就是它给的
（官方做法是高分辨率生成 + FastSimplification 降到目标面数）。

**解法**：`tools/cleanup_mesh.py` —— **取最大连通块 → 再降面**。
只留最大块之后包围盒立刻收敛成人形（1.353 × 1.701 × 0.463），碎渣和薄板一起消失，
再降到 11,500 面时轮廓完整（三视图已确认）。

> ⚠️ 注意**必须先清理再降面**：直接对 660k 降面会在 81,016 面就卡住降不下去——
> 21 万个碎块每块都要保住最少面数。这个坑我踩过了。

## 三、附带发现的两个问题（要处理）

1. **`models/vae/hunyuan3d_v2_vae.safetensors` 是个坏文件** —— 内容就是 15 字节的
   `Entry not found`（下载失败把响应正文写进去了）。
   本次生成照样成功，说明 **7.37 GB 的 checkpoint 自带了 VAE**，
   卡里"三个权重"那份清单与实际不符（`clip_vision/` 目录也是空的，只有占位文件）。
   → 要么把坏文件删掉以免误用，要么按正确 URL 重下。
2. **产物只有 `POSITION`** —— 没有 UV、没有贴图、**也没有顶点色**（早先"有顶点色"是
   trimesh 默认占位色 `[102,102,102]` 被误读，glb 的 JSON chunk 里并没有 `COLOR_0`）。
   而且**没有法线**，Godot 里缺 `FormatNormal` → 渲染成没有明暗的纯白块（验收 2 第一版翻车原因）。
   → 法线已由 `cleanup_mesh.py` 强制重算写入；**颜色仍须等 T27 补 UV + 贴图**。
   在那之前，"一眼分得清谁是人"只能靠**剪影**成立（10 §1：魔骸佝偻不对称 vs 主角干净直立）。

## 四、还没做的（剩余验收）

- [ ] **验收 2**：同屏摆"本地生成的魔骸 + 主角"，截图看出**一眼分得清谁是人**（10 §1）。
      场景还没搭——搭好之后在编辑器里 F6 截图即可。
- [ ] **验收 3**：跑 Hunyuan3D 的**显存峰值**与**单次生成耗时**。本机是 RTX 4060 Ti / 16 GB。
      ComfyUI 正在跑（`127.0.0.1:8188`），可以直接跑一次流程采样 `system_stats` 取数。
- [ ] **交付物 4**：把 `Z-Image 出四视图 → Hunyuan3D 出模型 → 去碎渣 → 降面 → 导入 Godot`
      整条流程写进 `docs/`。**"去碎渣 → 降面"这一步是卡里没有的，务必补进去。**
- [ ] 未提交文件是否提交：`tools/fetch_hf_file.py`、`tools/preview_glb.py`、
      `tools/cleanup_mesh.py`、`assets/models/ashigaru_v2d_clean.glb`、
      `assets/references/_t35_preview_*.png`

---

## 五、坑在哪：为什么"看完预览图"这一步会把会话打死（已修）

不是 Hunyuan3D 的问题，是 **Codex 与 provider 之间的请求组装 bug**。触发需要三个条件同时成立：

1. 这一轮**有并行工具调用**；
2. 其中之一返回**图片**，且该图**长边超过 2048px**（于是 Codex 插入缩放提示）；
3. 那条 `<image_resize_notice>` 被插在图片输出**紧后面**——当图片不是本轮最后一条输出时，
   它就把 `call → output` 的配对劈开了。provider 走 `wire_api = "responses"`，
   这个 API 强校验配对 → 整轮请求被拒。

两次会话各崩一次，序列一字不差（旧会话行 4443、新会话行 105）。
**两个 rollout 文件都已经修好**（删掉那条错位提示，备份在同目录 `.bak-20260913-*`），
复验：旧会话 594 个调用、新会话 15 个调用，**悬空 0 个**。

### 防复发（两条硬规则）

1. **`view_image` 绝不与其他工具调用放在同一轮。** 看图就单独一轮。
2. **预览图长边 ≤2048px。** `tools/preview_glb.py` 的默认值已改：`--size 660`
   （三视图合计 1980px），并在文件头写清了原因。

## 六、T35 卡上的硬约束（别忘了）

- **纯本地、离线**，不许调用需要账号/积分的服务 —— 所以**不能用**画廊里那几个
  `api_hunyuan3d_smart_topology` / `api_hunyuan3d_retopo_uv`（它们是 API 节点，走腾讯云）。
- 进入游戏前必须过 `tools\check.ps1`。
- 模型面数按 10 §2.1（MVP ≤ 12k）—— 现为 11,500 ✓
- 不要执行 `git commit`（由制作人统一提交）。

---

## 七、上色（2026-09-13 补）

### 为什么生成的模型没有颜色

**因为工作流里根本没有纹理那一段。** 从 ComfyUI 历史取出的实际执行图只有 10 个节点，
最后一个是 `SaveGLB` —— 从头到尾没有 UV / 贴图 / paint 节点。Hunyuan3D 2.1 的
"形状"和"贴图"是两段管线，我们只跑了前一段。

### 本机**没有**本地贴图的路

| 检查 | 结果 |
|---|---|
| 本地 Hunyuan3D 节点 | 只有几何那 4 个（`Hunyuan3Dv2Conditioning` / `EmptyLatentHunyuan3Dv2` / `VAEDecodeHunyuan3D` / `...MultiView`） |
| `Hunyuan3D: 3D Texture Edit` / `Model to UV` / `Smart Topology` | 全在 `partner/3d/Tencent` = **API 节点，走腾讯云**（要账号/积分）→ **违反 T35「纯本地、离线」** |
| paint / texture 权重 | 盘上没有 |
| `xatlas`（trimesh 的 UV 展开后端） | **没装** |
| Blender | **没装** |

### MVP 方案（已实施）：顶点色，不走贴图

依据：10 §1 已经把魔骸的样子写死了（**黑 + 血红**主色，眼窝/伤口透红光），
而 T35 的验收是「**一眼分得清谁是人**」——那靠**剪影 + 色块**，不靠烘焙贴图。

`tools/paint_mesh.py` 按 10 §1 的配色**程序化写顶点色**（顶点色不需要 UV）：

```
主体 黑 #141014 / 甲片冷灰 #3A3E44 / 皮肤 #4A4850 / 血红 #7A1418 / 发光红 #C8323A
产出 assets/models/ashigaru_v2d_colored.glb（239 KB，12 个顶点色层）
彩色转台：assets/references/_t35_turntable_colored.gif
```

### ★ 必须注意的坑：Godot 不会自动打开顶点色

实测（同一个 glb 导入后读材质）：

```
colored.glb | COLOR_0=true | 材质=StandardMaterial3D | vertex_color_use_as_albedo=false
```

**顶点色导进去了，但引擎默认不显示它** —— 模型在 Godot 里还是一片灰。
所以用本地生成的资产时**必须显式套材质**：`data/materials/makugai_body.tres`
（已建好，`vertex_color_use_as_albedo = true`，粗糙度按 10 §1 的"湿"给到 0.62）。

### 想要真贴图的话（未做，属新卡）

两条路：
1. **本地**：装 `xatlas`（pip，小）或 Blender → UV 展开 → 把 6 张 Z-Image 视图投影烘焙成贴图。
   这是正确但完整的一步工作。
2. **云端**：用 `TencentModelTo3DUVNode` / `Tencent3DTextureEditNode`。
   **它违反 T35 的硬约束**，而且本质上是 T36（Tripo 在线实测）那条线的范围，由项目主人决定。

> 另外 `tools/turntable_glb.py` 是本轮新增的：绕一圈的 GIF 动图，
> 用来"3D 浏览"模型而不用装任何查看器（`--frames` / `--size` 可调）。
