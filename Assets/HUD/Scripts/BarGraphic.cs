using UnityEngine;
using UnityEngine.UI;

namespace HUD
{
	// Рисует горизонтальную полосу сама, без спрайтов: подложка, "след" и заполнение поверх.
	// Длина полосы — ширина RectTransform, толщина — поле Thickness (полоса центрируется по высоте rect'а).
	// Заполняется слева направо. Цвет заполнения — обычное поле Color у Graphic.
	// Только отрисовка — что и насколько заполнять, задаёт, например, PlayerHealthBarUI.
	[RequireComponent(typeof(CanvasRenderer))]
	public class BarGraphic : MaskableGraphic
	{
		[Header("Shape")]
		[Tooltip("Толщина полосы, px")]
		[SerializeField] private float thickness = 6f;

		[Header("Colors")]
		[Tooltip("Цвет пустой части полосы. Прозрачный — подложки нет")]
		[SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.5f);
		[Tooltip("Цвет \"следа\" — части, которая только что убыла и ещё догоняет заполнение")]
		[SerializeField] private Color trailColor = new Color(1f, 1f, 1f, 0.6f);

		private float _fill = 1f;
		private float _trail = 1f;

		// заполненность 0..1
		public float Fill
		{
			get => _fill;
			set => SetValue(ref _fill, value);
		}

		// конец "следа" 0..1 — рисуется между Fill и Trail, если Trail > Fill
		public float Trail
		{
			get => _trail;
			set => SetValue(ref _trail, value);
		}

		public Color FillColor
		{
			get => color;
			set { if (color != value) color = value; }
		}

		private void SetValue(ref float field, float value)
		{
			value = Mathf.Clamp01(value);
			if (Mathf.Approximately(field, value)) return;
			field = value;
			SetVerticesDirty();
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			Rect rect = rectTransform.rect;
			float half = Mathf.Min(thickness, rect.height) * 0.5f;
			float yMin = rect.center.y - half;
			float yMax = rect.center.y + half;

			float fillX = Mathf.Lerp(rect.xMin, rect.xMax, _fill);
			float trailX = Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Max(_trail, _fill));

			if (backgroundColor.a > 0f && trailX < rect.xMax) AddQuad(vh, trailX, rect.xMax, yMin, yMax, backgroundColor);
			if (trailColor.a > 0f && trailX > fillX) AddQuad(vh, fillX, trailX, yMin, yMax, trailColor);
			if (fillX > rect.xMin) AddQuad(vh, rect.xMin, fillX, yMin, yMax, color);
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

#if UNITY_EDITOR
		protected override void OnValidate()
		{
			base.OnValidate();
			thickness = Mathf.Max(0f, thickness);
			SetVerticesDirty();
		}
#endif
	}
}
