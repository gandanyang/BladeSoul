using System.Collections.Generic;
using Godot;

namespace Oniblade.Dev;

/// <summary>走位自检用的胶囊。尺寸与 <c>Player.tscn</c> 的碰撞体一致（半径 0.4 / 高 1.8）——
/// 判定"能不能走"的是碰撞体，不是整个 PlayerActor，所以这里不需要模拟输入。</summary>
public partial class WhiteboxWalker : CharacterBody3D
{
    private float _gravity;

    public override void _Ready()
    {
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        });
    }

    /// <summary>朝目标水平走。脚下一米有落差就落下去——不跳跃，因为本作没有跳跃。</summary>
    public void StepToward(Vector3 target, float speed, float delta)
    {
        Vector3 direction = target - GlobalPosition;
        direction.Y = 0f;

        Vector3 velocity = Velocity;

        if (IsOnFloor())
        {
            if (velocity.Y < 0f)
                velocity.Y = 0f;
        }
        else
        {
            velocity.Y -= _gravity * (float)delta;
        }

        velocity.X = direction.LengthSquared() > 0.01f ? direction.Normalized().X * speed : 0f;
        velocity.Z = direction.LengthSquared() > 0.01f ? direction.Normalized().Z * speed : 0f;

        Velocity = velocity;
        MoveAndSlide();
    }
}
