using Godot;

namespace Oniblade.Vfx;

/// <summary>
/// 特效用的共享网格与材质。**全部程序化生成，不依赖任何外部资源**——
/// 与 T3 的程序化占位音效同一条纪律：占位阶段不许手工塞一次性二进制，
/// 否则以后没人能重建它。
/// </summary>
internal static class VfxMesh
{
	/// <summary>
	/// 一颗自发光小球。
	/// <c>VertexColorUseAsAlbedo</c> 是必须的：粒子系统会把每颗粒子的 COLOR
	/// 通过顶点色传给 draw pass，不开这个开关的话 <c>ParticleProcessMaterial.Color</c>
	/// 就完全不起作用（粒子会全白）。
	/// </summary>
	public static Mesh Glow(float radius, Color color)
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = color,
			EmissionEnergyMultiplier = 2.2f,
			VertexColorUseAsAlbedo = true,
		};

		return new SphereMesh
		{
			Radius = radius,
			Height = radius * 2f,
			RadialSegments = 6,
			Rings = 3,
			Material = material,
		};
	}
}
