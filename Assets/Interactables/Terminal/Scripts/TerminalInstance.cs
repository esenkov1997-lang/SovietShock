using UnityEngine;

namespace Interactables
{
	// Вешается на КОРЕНЬ префаба терминала (модель + экран + UITerminal + UICamera). Делает так, чтобы
	// один и тот же префаб можно было ставить в уровень сколько угодно раз:
	//
	//  1. Свой экран у каждого терминала. На старте создаётся собственная RenderTexture, UICamera рисует
	//     в неё, а материал экрана показывает её через MaterialPropertyBlock (сам материал-ассет не
	//     меняется и остаётся общим). Без этого все копии рисовали бы в один ассет RT_TerminalScreen —
	//     и на всех экранах была бы одна и та же картинка.
	//  2. UICamera сама выравнивается по TerminalCanvas (позиция, размер, клиппинг) — руками настраивать
	//     камеру под canvas не нужно, и она не "видит" canvas соседних терминалов.
	//  3. Всё, что отличается между терминалами (письма, пароль), задаётся здесь, на корне, а не
	//     во вложенных объектах. TerminalMainController сам забирает эти настройки в своём Awake.
	//  4. Камера экрана рисует только пока экран кто-то видит — десяток терминалов в уровне не стоит
	//     десяти лишних рендеров за кадр.
	[DisallowMultipleComponent]
	public class TerminalInstance : MonoBehaviour
	{
		[Header("Content (у каждого терминала своё)")]
		[Tooltip("Письма этого терминала. Пусто — остаётся то, что назначено в MailAppController внутри префаба")]
		[SerializeField] private TerminalDataSO data;
		[SerializeField] private bool requirePassword = true;
		[SerializeField] private string password = "1234";

		[Header("Screen")]
		[Tooltip("Renderer экрана на модели терминала — на нём показывается картинка UI")]
		[SerializeField] private Renderer screenRenderer;
		[Tooltip("Номер материала экрана в Renderer (если у модели несколько материалов)")]
		[SerializeField] private int screenMaterialIndex;
		[Tooltip("Свойства текстуры в шейдере экрана, в которые подставляется картинка. URP Lit/Unlit — _BaseMap")]
		[SerializeField] private string[] textureProperties = { "_BaseMap", "_MainTex" };
		[Tooltip("Множитель разрешения относительно размера TerminalCanvas (1024×768 → 1 = 1024×768)")]
		[Range(0.25f, 2f)]
		[SerializeField] private float resolutionScale = 1f;
		[Tooltip("Рисовать UI только пока экран виден какой-либо камере (экономия на терминалах вне поля зрения)")]
		[SerializeField] private bool renderOnlyWhenVisible = true;

		[Header("References (необязательно — ищутся среди дочерних объектов)")]
		[SerializeField] private Canvas terminalCanvas;
		[SerializeField] private Camera uiCamera;

		// расстояние от UICamera до canvas в долях высоты canvas — клиппинг берётся узким слоем вокруг
		// этой плоскости, поэтому чужие canvas не попадут в кадр, даже если окажутся рядом
		private const float CameraDistanceFactor = 0.5f;

		private RenderTexture _renderTexture;
		private MaterialPropertyBlock _propertyBlock;
		private bool _settingsApplied;

		private void Awake()
		{
			ApplySettings();
			SetupScreen();
		}

		private void LateUpdate()
		{
			if (!renderOnlyWhenVisible || uiCamera == null || screenRenderer == null) return;

			// isVisible учитывает все камеры (в редакторе — и окно Scene). Тени тоже считаются, поэтому
			// у экрана лучше выключить Cast Shadows, иначе он "виден" по тени
			uiCamera.enabled = screenRenderer.isVisible;
		}

		private void OnDestroy()
		{
			if (uiCamera != null && uiCamera.targetTexture == _renderTexture) uiCamera.targetTexture = null;
			if (_renderTexture != null)
			{
				_renderTexture.Release();
				Destroy(_renderTexture);
			}
		}

		// ---------- Настройки контента ----------

		// Вызывается и отсюда, и из TerminalMainController.Awake — кто раньше, тот и применит (повторный
		// вызов ничего не делает). Так порядок Awake разных объектов не важен
		public void ApplySettings()
		{
			if (_settingsApplied) return;
			_settingsApplied = true;

			TerminalMainController controller = GetComponentInChildren<TerminalMainController>(true);
			if (controller != null) controller.ConfigurePassword(requirePassword, password);

			if (data != null)
			{
				foreach (MailAppController mail in GetComponentsInChildren<MailAppController>(true))
				{
					mail.Data = data;
				}
			}
		}

		// ---------- Экран ----------

		private void SetupScreen()
		{
			if (terminalCanvas == null) terminalCanvas = GetComponentInChildren<Canvas>(true);
			if (uiCamera == null) uiCamera = GetComponentInChildren<Camera>(true);

			if (terminalCanvas == null || uiCamera == null || screenRenderer == null)
			{
				Debug.LogError($"{nameof(TerminalInstance)} на {name}: не найден TerminalCanvas, UICamera " +
					"или не назначен Screen Renderer — экран терминала не будет работать", this);
				return;
			}

			// AudioListener на UICamera лишний: в сцене должен быть ровно один (у камеры игрока)
			if (uiCamera.TryGetComponent(out AudioListener listener)) listener.enabled = false;

			FitCameraToCanvas();
			CreateRenderTexture();
		}

		// Ставит ортографическую UICamera ровно напротив canvas: кадр совпадает с canvas один в один,
		// а клиппинг — тонкий слой вокруг его плоскости. В игре вызывается автоматически в Awake;
		// в редакторе — кнопкой в инспекторе (см. Editor/TerminalInstanceEditor.cs), чтобы видеть результат
		public Camera UICamera => uiCamera != null ? uiCamera : GetComponentInChildren<Camera>(true);
		public Canvas TerminalCanvas => terminalCanvas != null ? terminalCanvas : GetComponentInChildren<Canvas>(true);

		public void FitCameraToCanvas()
		{
			if (terminalCanvas == null) terminalCanvas = GetComponentInChildren<Canvas>(true);
			if (uiCamera == null) uiCamera = GetComponentInChildren<Camera>(true);
			if (terminalCanvas == null || uiCamera == null) return;

			RectTransform canvasRect = (RectTransform)terminalCanvas.transform;
			Vector3 center = canvasRect.TransformPoint(canvasRect.rect.center);
			float worldHeight = canvasRect.rect.height * canvasRect.lossyScale.y;
			float distance = worldHeight * CameraDistanceFactor;

			// world-space canvas читается "спереди", если смотреть вдоль его +Z
			uiCamera.transform.SetPositionAndRotation(center - canvasRect.forward * distance, canvasRect.rotation);
			uiCamera.orthographic = true;
			uiCamera.orthographicSize = worldHeight * 0.5f;
			uiCamera.nearClipPlane = distance * 0.5f;
			uiCamera.farClipPlane = distance * 1.5f;
			uiCamera.cullingMask = 1 << terminalCanvas.gameObject.layer;

			// canvas должен рендериться именно этой камерой, а не только "висеть" в мире
			terminalCanvas.worldCamera = uiCamera;
		}

		private void CreateRenderTexture()
		{
			RectTransform canvasRect = (RectTransform)terminalCanvas.transform;
			int width = Mathf.Max(16, Mathf.RoundToInt(canvasRect.rect.width * resolutionScale));
			int height = Mathf.Max(16, Mathf.RoundToInt(canvasRect.rect.height * resolutionScale));

			_renderTexture = new RenderTexture(width, height, 24)
			{
				name = $"RT_{name}",
				antiAliasing = 1,
			};
			_renderTexture.Create();

			// камера сама возьмёт соотношение сторон из текстуры — оно совпадает с canvas
			uiCamera.targetTexture = _renderTexture;

			// MaterialPropertyBlock, а не renderer.material: не создаём копию материала на каждый терминал
			// и не трогаем общий ассет Mat_TerminalScreen
			_propertyBlock ??= new MaterialPropertyBlock();
			screenRenderer.GetPropertyBlock(_propertyBlock, screenMaterialIndex);

			Material material = screenMaterialIndex < screenRenderer.sharedMaterials.Length
				? screenRenderer.sharedMaterials[screenMaterialIndex]
				: null;
			foreach (string property in textureProperties)
			{
				if (material == null || material.HasProperty(property)) _propertyBlock.SetTexture(property, _renderTexture);
			}

			screenRenderer.SetPropertyBlock(_propertyBlock, screenMaterialIndex);
		}
	}
}
