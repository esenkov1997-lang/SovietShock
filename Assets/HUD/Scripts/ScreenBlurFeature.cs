using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HUD
{
	// Renderer Feature размытия экрана (Volume-компонент ScreenBlur). Добавляется в каждый Forward Renderer
	// (по одному на уровень качества). Сама ничего не делает, пока в Volume нет ScreenBlur с Intensity > 0.
	//
	// Почему не стандартный Depth of Field: оружие рисует отдельная overlay-камера (WeaponCamera) со своей
	// глубиной, и DoF размыл бы его вместе с фоном. Здесь размытие встаёт в нужную точку стека камер:
	//   - Include Weapon выкл — на основной камере, после мира, но ДО того, как WeaponCamera дорисует оружие;
	//   - Include Weapon вкл — на последней камере стека, когда оружие уже в кадре.
	// Пост-процесс и полноэкранные проходы после него (SignalGlitch с HUD) идут уже поверх — их размытие не трогает.
	public class ScreenBlurFeature : ScriptableRendererFeature
	{
		[Tooltip("Hidden/SovietShock/ScreenBlur — подставляется сам при добавлении фичи")]
		[SerializeField] private Shader shader;
		[SerializeField] private RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

		private Material _material;
		private ScreenBlurPass _pass;

		public override void Create()
		{
#if UNITY_EDITOR
			// шейдер должен быть сохранён в ассете рендерера — иначе в билд он не попадёт
			if (shader == null)
			{
				shader = Shader.Find("Hidden/SovietShock/ScreenBlur");
				if (shader != null) UnityEditor.EditorUtility.SetDirty(this);
			}
#endif
			CoreUtils.Destroy(_material);
			_material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
			_pass = _material != null ? new ScreenBlurPass(_material) { renderPassEvent = renderPassEvent } : null;
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			// нужна ли этой камере — решает сам проход (ему виден Volume-стек именно этой камеры)
			if (_pass != null) renderer.EnqueuePass(_pass);
		}

		protected override void Dispose(bool disposing)
		{
			CoreUtils.Destroy(_material);
			_material = null;
		}

		private class ScreenBlurPass : ScriptableRenderPass
		{
			private const int MaxIterations = 6;
			private const int DownPass = 0;
			private const int UpPass = 1;
			private const int CompositePass = 2;

			private static readonly int OffsetId = Shader.PropertyToID("_ScreenBlurOffset");
			private static readonly int IntensityId = Shader.PropertyToID("_ScreenBlurIntensity");
			private static readonly string[] LevelNames =
				{ "_ScreenBlur0", "_ScreenBlur1", "_ScreenBlur2", "_ScreenBlur3", "_ScreenBlur4", "_ScreenBlur5" };

			private readonly Material _material;
			private readonly TextureHandle[] _levels = new TextureHandle[MaxIterations];
			private readonly Vector2Int[] _sizes = new Vector2Int[MaxIterations];

			private class PassData
			{
				public TextureHandle source;
				public Material material;
				public int pass;
				public Vector4 offset;
				public float intensity;
			}

			public ScreenBlurPass(Material material)
			{
				_material = material;
				profilingSampler = new ProfilingSampler("Screen Blur");
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				if (cameraData.cameraType != CameraType.Game) return;

				ScreenBlur blur = VolumeManager.instance.stack.GetComponent<ScreenBlur>();
				if (blur == null || !blur.IsActive()) return;

				bool run = blur.includeWeapon.value
					? cameraData.resolveFinalTarget                      // последняя камера стека — оружие уже нарисовано
					: cameraData.renderType == CameraRenderType.Base;    // основная — оружие ещё не нарисовано
				if (!run) return;

				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				// камера пишет прямо в экран (без промежуточной текстуры) — читать кадр не из чего
				if (resourceData.isActiveTargetBackBuffer) return;

				TextureHandle color = resourceData.activeColorTexture;
				RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
				desc.depthBufferBits = 0;
				desc.msaaSamples = 1;

				float spread = blur.spread.value;
				int iterations = Mathf.Clamp(blur.iterations.value, 1, MaxIterations);

				// вниз: каждая ступень вдвое меньше предыдущей
				TextureHandle source = color;
				Vector2Int sourceSize = new Vector2Int(desc.width, desc.height);
				int count = 0;
				for (int i = 0; i < iterations; i++)
				{
					Vector2Int size = new Vector2Int(Mathf.Max(1, sourceSize.x / 2), Mathf.Max(1, sourceSize.y / 2));
					if (size.x < 2 || size.y < 2) break;

					desc.width = size.x;
					desc.height = size.y;
					TextureHandle level = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, LevelNames[i], false, FilterMode.Bilinear);
					AddBlitPass(renderGraph, "Screen Blur Down", source, level, AccessFlags.WriteAll, DownPass, Offset(sourceSize, spread), 0f);

					_levels[i] = level;
					_sizes[i] = size;
					source = level;
					sourceSize = size;
					count++;
				}
				if (count == 0) return;

				// вверх: обратно по ступеням, последняя ложится прямо на кадр камеры
				for (int i = count - 1; i > 0; i--)
					AddBlitPass(renderGraph, "Screen Blur Up", _levels[i], _levels[i - 1], AccessFlags.WriteAll, UpPass, Offset(_sizes[i], spread), 0f);

				AddBlitPass(renderGraph, "Screen Blur Composite", _levels[0], color, AccessFlags.ReadWrite, CompositePass,
					Offset(_sizes[0], spread), blur.intensity.value);
			}

			private static Vector4 Offset(Vector2Int sourceSize, float spread)
			{
				return new Vector4(0.5f * spread / sourceSize.x, 0.5f * spread / sourceSize.y, 0f, 0f);
			}

			private void AddBlitPass(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle destination,
				AccessFlags destinationAccess, int pass, Vector4 offset, float intensity)
			{
				using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out PassData data, profilingSampler))
				{
					data.source = source;
					data.material = _material;
					data.pass = pass;
					data.offset = offset;
					data.intensity = intensity;

					builder.UseTexture(source, AccessFlags.Read);
					builder.SetRenderAttachment(destination, 0, destinationAccess);
					builder.AllowGlobalStateModification(true);

					builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
					{
						context.cmd.SetGlobalVector(OffsetId, d.offset);
						context.cmd.SetGlobalFloat(IntensityId, d.intensity);
						Blitter.BlitTexture(context.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.pass);
					});
				}
			}
		}
	}
}
