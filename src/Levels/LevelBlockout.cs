using Godot;

namespace Oniblade.Levels;

public partial class LevelBlockout : Node3D
{
	[Export] public int EnemyCount { get; set; } = 3;
	[Export] public float SpawnRadius { get; set; } = 9f;

	public override void _Ready()
	{
		var sun = GetNode<DirectionalLight3D>("Sun");
		sun.RotationDegrees = new Vector3(-52f, -35f, 0f);

		var enemyScene = GD.Load<PackedScene>("res://scenes/actors/enemies/Enemy.tscn");
		for (int i = 0; i < EnemyCount; i++)
		{
			float angle = Mathf.Tau * i / Mathf.Max(1, EnemyCount) + Mathf.Pi;
			var enemy = enemyScene.Instantiate<Node3D>();
			enemy.Position = new Vector3(Mathf.Cos(angle) * SpawnRadius, 0.05f, Mathf.Sin(angle) * SpawnRadius - 3f);
			AddChild(enemy);
		}
	}
}
