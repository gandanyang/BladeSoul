using System.Collections.Generic;
using Godot;
using Oniblade.Audio;
using Oniblade.Combat.Data;

namespace Oniblade.Dev;

/// <summary>
/// 无头自检（04 文档 §14 第 2 层）：
///     godot --headless --path . res://scenes/tests/SelfTest.tscn
///
/// 它检查的是"测试工程管不到的那一半"——那些只有引擎才认得的东西：
/// .tres 能不能加载、类型对不对、帧数据是否自洽。
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class SelfTest : Node
{
    private readonly List<string> _errors = new();
    private int _resources;
    private int _attacks;
    private int _difficulties;
    private int _actorData;
    private int _audio;
    private int _world;

    private int _materials;

    public override void _Ready()
    {
        Scan("res://data");
        CheckAudio();

        foreach (string error in _errors)
            GD.PrintErr($"[自检] ✗ {error}");

        GD.Print($"[自检] 资源 {_resources} 个（招式 {_attacks} / 难度 {_difficulties} / 角色数据 {_actorData} / 氛围 {_world} / 材质 {_materials}），音效 {_audio} 个，错误 {_errors.Count} 项");

        if (_errors.Count == 0)
            GD.Print("[自检] ✓ 通过");

        GetTree().Quit(_errors.Count == 0 ? 0 : 1);
    }

    /// <summary>战斗音效必须齐全：缺了不会崩，但会让"手感"在无声中退化。</summary>
    private void CheckAudio()
    {
        foreach (CombatSfx sfx in System.Enum.GetValues<CombatSfx>())
        {
            string path = AudioDirector.PathFor(sfx);

            if (!ResourceLoader.Exists(path))
            {
                _errors.Add($"缺少音效 {path}（跑 tools\\gen_placeholder_sfx.ps1 生成）");
                continue;
            }

            if (ResourceLoader.Load(path) is AudioStream)
                _audio++;
            else
                _errors.Add($"{path} 不是 AudioStream");
        }
    }

    private void Scan(string path)
    {
        using DirAccess? dir = DirAccess.Open(path);
        if (dir is null)
        {
            _errors.Add($"打不开目录：{path}");
            return;
        }

        dir.ListDirBegin();
        while (true)
        {
            string name = dir.GetNext();
            if (string.IsNullOrEmpty(name))
                break;
            if (name is "." or "..")
                continue;

            string full = path.PathJoin(name);
            if (dir.CurrentIsDir())
                Scan(full);
            else if (name.EndsWith(".tres"))
                Inspect(full);
        }

        dir.ListDirEnd();
    }

    private void Inspect(string path)
    {
        Resource? resource = ResourceLoader.Load(path);
        if (resource is null)
        {
            _errors.Add($"加载失败：{path}");
            return;
        }

        _resources++;

        if (path.Contains("/difficulty/"))
        {
            _difficulties++;
            if (resource is not DifficultyProfile profile)
            {
                _errors.Add($"{path} 不是 DifficultyProfile（实际 {resource.GetType().Name}）");
                return;
            }

            if (profile.DeflectWindowFrames <= 0)
                _errors.Add($"{path} 的 DeflectWindowFrames 必须为正数");
            if (profile.IssenWindowFrames <= 0)
                _errors.Add($"{path} 的 IssenWindowFrames 必须为正数");
            return;
        }

        if (path.Contains("/attacks/"))
        {
            _attacks++;
            if (resource is not AttackData attack)
            {
                _errors.Add($"{path} 不是 AttackData（实际 {resource.GetType().Name}）——data/attacks/ 下只许放招式");
                return;
            }

            InspectAttack(path, attack);
            return;
        }

        // data/actors/ 下是角色数据包：属性表与招式表。
        if (path.Contains("/actors/"))
        {
            _actorData++;
            if (resource is ActorStats or PlayerAttackSet)
                return;

            _errors.Add($"{path} 不是 ActorStats 也不是 PlayerAttackSet（实际 {resource.GetType().Name}）");
            return;
        }

        // data/world/ 下是氛围档（T34）。
        if (path.Contains("/world/"))
        {
            _world++;
            if (resource is Oniblade.World.AtmosphereProfile)
                return;

            _errors.Add($"{path} 不是 AtmosphereProfile（实际 {resource.GetType().Name}）");
            return;
        }

        // data/materials/ 下是共享材质（T35）。
        // 存在的理由：本地生成的资产用**顶点色**上色（没有 UV/贴图），
        // 而 glTF 导入**不会**自动打开 vertex_color_use_as_albedo——
        // 实测 COLOR_0 导进去了但材质里那个开关是 false，模型在引擎里仍是一片灰。
        // 所以这类资产必须显式套一个开了该开关的材质。
        if (path.Contains("/materials/"))
        {
            _materials++;
            if (resource is Material)
                return;

            _errors.Add($"{path} 不是 Material（实际 {resource.GetType().Name}）");
            return;
        }

        _errors.Add($"{path} 放在了一个 SelfTest 不认识的目录下，请更新自检规则");
    }

    private void InspectAttack(string path, AttackData attack)
    {
        if (string.IsNullOrEmpty(attack.Id))
            _errors.Add($"{path} 缺 Id");
        if (attack.StartupFrames <= 0)
            _errors.Add($"{path} 前摇必须为正数");
        if (attack.ActiveFrames <= 0)
            _errors.Add($"{path} 判定帧必须为正数");
        if (attack.RecoveryFrames < 0)
            _errors.Add($"{path} 后摇不能为负");
        if (attack.Cancelable && attack.CancelOpenFrame >= attack.TotalFrames)
            _errors.Add($"{path} 取消窗({attack.CancelOpenFrame}) 不早于整招结束({attack.TotalFrames})，形同虚设；不可取消请用 -1");

        // 可读性铁律（02 文档 §3）：杂兵前摇 ≥24 帧，精英 ≥16 帧。
        // 这里只兜底"人类来得及反应"的绝对下限。
        if (attack.Perilous && attack.StartupFrames < 20)
            _errors.Add($"{path} 危攻击前摇 {attack.StartupFrames} 帧太短，无法反应");

        // 02 §3 裁定：**弹开只对一般攻击有效，「危」一律不可弹开**。
        if (attack.Perilous && attack.Parryable)
            _errors.Add($"{path} 是「危」攻击却标记为可弹开（02 §3：弹开只对一般攻击有效）");

        // 02 §3 裁定：**一闪可以应对一切攻击**，所以敌方招式都该可被一闪。
        if (path.Contains("/enemies/") && !attack.IssenVulnerable)
            _errors.Add($"{path} 敌方招式标记为不可一闪（02 §3：一闪可以应对一切攻击）");

        if (attack.Unblockable && attack.Parryable)
            _errors.Add($"{path} Unblockable 与 Parryable 同时为真，规则会自相矛盾");
    }
}
