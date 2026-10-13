using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HUD
{
	// HUD как часть "видеосигнала" шлема: вместо того чтобы рисоваться поверх готового кадра (Screen Space - Overlay),
	// HUD рисуется отдельной камерой в текстуру размером с экран, а шейдер помех SignalGlitch накладывает её на кадр
	// и искажает вместе с картинкой — при сбое интерфейс рвётся теми же полосами.
	//
	// Вешается на Canvas HUD. Камеру и текстуру создаёт сам, Canvas сам переводит в Screen Space - Camera.
	// Нужно в Unity (один раз):
	//   - отдельный слой для HUD (Canvas и все его дети на нём), и этот слой убран из Culling Mask основной камеры
	//     и камеры оружия — иначе они нарисуют HUD ещё раз прямо в мире;
	//   - отдельный рендерер БЕЗ эффекта помех (Renderer Index) — иначе помехи применились бы и к самой отрисовке HUD.
	// Если компонент выключить, HUD возвращается в обычный режим Overlay и просто рисуется поверх, без помех.
	[RequireComponent(typeof(Canvas))]
	public class HUDSignalRenderer : MonoBehaviour
	{
		[Tooltip("Индекс рендерера для камеры HUD в списке Renderer List пайплайна (Ultra_PipelineAsset). " +
			"Это должен быть рендерер БЕЗ Full Screen Pass с помехами")]
		[SerializeField] private int rendererIndex = 1;
		[Tooltip("Расстояние от камеры HUD до плоскости Canvas — на вид не влияет")]
		[SerializeField] private float planeDistance = 1f;

		private static readonly int TextureId = Shader.PropertyToID("_SignalHUDTexture");
		private static readonly int EnabledId = Shader.PropertyToID("_SignalHUDEnabled");

		private Canvas _canvas;
		private Camera _camera;
		private RenderTexture _texture;

		private void Awake()
		{
			_canvas = GetComponent<Canvas>();

			// камера не дочерняя к Canvas: Canvas в режиме Screen Space - Camera сам встаёт перед своей камерой,
			// и если бы камера была внутри него, они двигали бы друг друга
			GameObject cameraObject = new GameObject($"{name}_SignalCamera");
			_camera = cameraObject.AddComponent<Camera>();
			_camera.clearFlags = CameraClearFlags.SolidColor;
			_camera.backgroundColor = Color.clear; // прозрачный фон — шейдер наложит только сам HUD
			_camera.cullingMask = 1 << gameObject.layer;
			_camera.orthographic = true;
			_camera.nearClipPlane = 0.01f;
			_camera.farClipPlane = planeDistance + 1f;
			_camera.allowHDR = false;
			_camera.allowMSAA = false;
			_camera.useOcclusionCulling = false;
			// рисуем раньше основной камеры, чтобы её шейдер помех в этом же кадре взял свежий HUD
			Camera main = Camera.main;
			_camera.depth = main != null ? main.depth - 1f : -100f;

			UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
			data.renderType = CameraRenderType.Base;
			data.renderPostProcessing = false; // bloom и цветокоррекция не должны менять цвета интерфейса
			data.renderShadows = false;
			data.antialiasing = AntialiasingMode.None;
			data.SetRenderer(rendererIndex);

			if (main != null && (main.cullingMask & _camera.cullingMask) != 0)
				Debug.LogWarning($"{name}: основная камера рисует слой HUD ({LayerMask.LayerToName(gameObject.layer)}) — " +
					"убери его из её Culling Mask (и у камеры оружия), иначе HUD будет нарисован дважды", this);
		}

		private void OnEnable()
		{
			EnsureTexture();
			_camera.enabled = true;
			_canvas.renderMode = RenderMode.ScreenSpaceCamera;
			_canvas.worldCamera = _camera;
			_canvas.planeDistance = planeDistance;
			Shader.SetGlobalFloat(EnabledId, 1f);
		}

		private void OnDisable()
		{
			Shader.SetGlobalFloat(EnabledId, 0f);
			if (_canvas != null) _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			if (_camera != null) _camera.enabled = false;
		}

		private void OnDestroy()
		{
			if (_camera != null) Destroy(_camera.gameObject);
			ReleaseTexture();
		}

		private void LateUpdate()
		{
			// окно могли растянуть / сменить разрешение — текстура должна совпадать с экраном пиксель в пиксель
			EnsureTexture();
		}

		private void EnsureTexture()
		{
			int width = Mathf.Max(1, Screen.width);
			int height = Mathf.Max(1, Screen.height);
			if (_texture != null && _texture.width == width && _texture.height == height) return;

			ReleaseTexture();
			// sRGB: UI рисуется в гамма-пространстве, а шейдер читает уже линейные цвета — как у основной картинки
			_texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
			{
				name = "SignalHUD"
			};
			_texture.Create();
			_camera.targetTexture = _texture;
			Shader.SetGlobalTexture(TextureId, _texture);
		}

		private void ReleaseTexture()
		{
			if (_texture == null) return;
			if (_camera != null && _camera.targetTexture == _texture) _camera.targetTexture = null;
			_texture.Release();
			Destroy(_texture);
			_texture = null;
		}

		// при каждом запуске Play Mode HUD-текстуры ещё нет — шейдер не должен её накладывать
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Shader.SetGlobalFloat(EnabledId, 0f);
		}
	}
}
