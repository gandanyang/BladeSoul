# data/levels/

关卡级的参数（T42 起）。目前只有一样东西：

**鬼火存档点**（`SavePoint.cs`）的参数**跟着场景走**——半径、灯色、亮度、范围都是
`[Export]`，直接摆在 `.tscn` 的节点上即可，所以这里暂时没有 `.tres`。

> 为什么不放 `data/`：铁律 1 管的是"**数值不许硬编码进 C#**"，
> 而 `[Export]` 的值存在场景文件里，改它同样零编译——
> 这与 `PlayerActor.MouseSensitivity`、`EnemyController.AttackCooldown` 是同一套做法。

敌人配置在 `data/encounters/`，招式在 `data/attacks/`，难度在 `data/difficulty/`。
