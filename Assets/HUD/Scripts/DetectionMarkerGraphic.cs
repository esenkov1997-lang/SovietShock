using UnityEngine;
using UnityEngine.UI;

namespace HUD
{
	// Квадрат-индикатор, который заполняется снизу вверх (Fill 0..1): подложка, заливка и рамка. Рисуется сам,
	// без спрайтов — размер берётся из RectTransform. Цвет заливки — обычное поле Color у Graphic.
	// Только отрисовка — что показывать, задаёт DetectionIndicatorUI.
	[RequireComponent(typeof(CanvasRenderer))]
	public class DetectionMarkerGraphic : MaskableGraphic
	{
		[Tooltip("Цвет пустой части квадрата. Прозрачный — подложки нет")]
		[SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.4f);
		[Tooltip("Цвет рамки")]
		[SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.8f);
		[Tooltip("Толщина рамки, px. 0 — без рамки")]
		[SerializeField] private float borderThickness = 2f;

		private float _fill;

		public float Fill
		{
			get => _fill;
			set
			{
				value = Mathf.Clamp01(value);
				if (Mathf.Approximately(_fill, value)) return;
				_fill = value;
				SetVerticesDirty();
			}
		}

		public void Configure(Color background, Color border, float thickness)
		{
			backgroundColor = background;
			borderColor = border;
			borderThickness = thickness;
			SetVerticesDirty();
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();
			Rect rect = rectTransform.rect;

			float b = Mathf.Clamp(borderThickness, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
			Rect inner = new Rect(rect.xMin + b, rect.yMin + b, rect.width - 2f * b, rect.height - 2f * b);
			float fillTop = Mathf.Lerp(inner.yMin, inner.yMax, _fill);

			if (backgroundColor.a > 0f && fillTop < inner.yMax)
				AddQuad(vh, inner.xMin, inner.xMax, fillTop, inner.yMax, backgroundColor);
			if (_fill > 0f)
				AddQuad(vh, inner.xMin, inner.xMax, inner.yMin, fillTop, color);

			if (b > 0f && borderColor.a > 0f)
			{
				AddQuad(vh, rect.xMin, rect.xMax, rect.yMin, rect.yMin + b, borderColor); // низ
				AddQuad(vh, rect.xMin, rect.xMax, rect.yMax - b, rect.yMax, borderColor); // верх
				AddQuad(vh, rect.xMin, rect.xMin + b, rect.yMin + b, rect.yMax - b, borderColor); // лево
				AddQuad(vh, rect.xMax - b, rect.xMax, rect.yMin + b, rect.yMax - b, borderColor); // право
			}
		}

		private static void AddQuad(VertexHelper vh, float xMin, float xMax, float yMin, float yMax, Color32 c)
		{
			int start = vh.currentVertCount;
			vh.AddVert(new Vector3(xMin, yMin), c, Vector2.zero);
			vh.AddVert(new Vector3(xMin, yMax), c, Vector2.zero);
			vh.AddVert(new Vector3(xMax, yMax), c, Vector2.zero);
			vh.AddVert(new Vector3(xMax, yMin), c, Vector2.zero);
			vh.AddTriangle(start, start + 1, start + 2);
			vh.AddTriangle(start + 2, start + 3, start);
		}
	}
}
