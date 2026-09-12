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

> 提示词与全部生成参数记在同目录的 `ref_enemy_ashigaru_01.prompt.txt`——
> **可复现**（固定 seed ＋ 清单文件），换台机器跑同样参数能得到同样的图。

## 字体 / 音频库

暂无。UI 使用 Godot 默认字体；BGM 尚未引入。

---

## 待办（引入任何外部资产时逐条补齐）

- [ ] Mixamo 动画（需记录具体动作名与下载日期，Adobe 免费可商用）
- [ ] Quaternius / Kenney / Poly Haven 模型与音效（CC0）
- [ ] Freesound 音效（**逐个确认授权**，部分为 CC-BY-NC）
- [ ] Sonniss GDC Game Audio Bundle（免版税，但要记录具体包名）
- [ ] 字体（若使用中文字体，注意 SIL OFL 与商用条款）
