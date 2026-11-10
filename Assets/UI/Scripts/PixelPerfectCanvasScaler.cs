using UnityEngine;
using UnityEngine.UI;

namespace UI
{
	// Целочисленный масштаб интерфейса для пиксельной графики. UI верстается в маленьком "виртуальном" разрешении
	// (Reference Resolution, например 960×540), а на экран выводится с масштабом ×1, ×2, ×3... — наибольшим целым,
	// при котором виртуальный экран целиком помещается в реальный. 1920×1080 → ×2, 2560×1440 → ×2 (с полями),
	// 3840×2160 → ×4, 1280×720 → ×1. Каждый пиксель спрайта и шрифта — ровно квадрат N×N, без мыла.
	//
	// Вешается на Canvas рядом с Canvas Scaler (тот переключается в Constant Pixel Size, а его Scale Factor
	// выставляет этот скрипт — руками его не менять). Работает и в редакторе, чтобы Game view показывал то же,
	// что будет в игре. Спрайты UI — с Pixels Per Unit, равным Reference Pixels Per Unit канваса (обычно 100),
	// Filter Mode = Point, без сжатия.
	[ExecuteAlways]
	[RequireComponent(typeof(Canvas), typeof(CanvasScaler))]
	public class PixelPerfectCanvasScaler : MonoBehaviour
	{
		[Tooltip("Виртуальное разрешение, в котором верстается этот UI (размеры в инспекторе — в этих пикселях)")]
		[SerializeField] private Vector2Int referenceResolution = new Vector2Int(960, 540);
		[Tooltip("Меньше этого масштаба не уменьшать, даже если экран меньше виртуального (UI тогда обрежется по краям)")]
		[Min(1)]
		[SerializeField] private int minScale = 1;

		private CanvasScaler _scaler;
		private Vector2Int _lastScreenSize;

		public int Scale { get; private set; } = 1;

		private void OnEnable()
		{
			_scaler = GetComponent<CanvasScaler>();
			_lastScreenSize = default;
			Apply();
		}

		private void OnValidate()
		{
			referenceResolution = Vector2Int.Max(referenceResolution, Vector2Int.one);
			_lastScreenSize = default;
		}

		// размер окна меняется редко, но может поменяться в любой момент (окно, смена разрешения в настройках)
		private void Update()
		{
			if (Screen.width != _lastScreenSize.x || Screen.height != _lastScreenSize.y) Apply();
		}

		private void Apply()
		{
			if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
			_lastScreenSize = new Vector2Int(Screen.width, Screen.height);

			int fitX = Screen.width / referenceResolution.x;
			int fitY = Screen.height / referenceResolution.y;
			Scale = Mathf.Max(minScale, Mathf.Min(fitX, fitY));

			_scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			_scaler.scaleFactor = Scale;
		}
	}
}
