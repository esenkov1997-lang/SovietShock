using UnityEngine;
using UnityEngine.UI;

namespace HUD
{
	// Рисует круговой индикатор сам — кольцо-подложку и заполняемую дугу поверх неё, без спрайтов: радиус,
	// толщина и цвета настраиваются числами в инспекторе. Дуга растёт от верхней точки по часовой стрелке.
	// Только отрисовка — когда показывать и насколько заполнять, задаёт RepairProgressUI.
	// Цвет дуги — обычное поле Color у Graphic. Вешается на пустой RectTransform по центру HUD-канваса.
	[RequireComponent(typeof(CanvasRenderer))]
	public class RadialProgressGraphic : MaskableGraphic
	{
		[Header("Shape")]
		[Tooltip("Внешний радиус кольца, px")]
		[SerializeField] private float radius = 16f;
		[Tooltip("Толщина кольца, px")]
		[SerializeField] private float thickness = 3f;
		[Tooltip("Количество сегментов на полный круг — больше = глаже")]
		[Range(12, 128)]
		[SerializeField] private int segments = 64;
		[Tooltip("Цвет незаполненной части кольца. Прозрачный — подложки нет")]
		[SerializeField] private Color backgroundColor = new Color(1f, 1f, 1f, 0.25f);

		private float _fill;

		// заполненность 0..1 — задаёт RepairProgressUI
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

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			Vector2 center = rectTransform.rect.center;
			float inner = Mathf.Max(0f, radius - thickness);

			if (backgroundColor.a > 0f && _fill < 1f) AddArc(vh, center, inner, _fill, 1f, backgroundColor);
			if (_fill > 0f) AddArc(vh, center, inner, 0f, _fill, color);
		}

		// дуга кольца от доли from до доли to полного круга (0 — верхняя точка, по часовой стрелке)
		private void AddArc(VertexHelper vh, Vector2 center, float inner, float from, float to, Color32 c)
		{
			int steps = Mathf.Max(1, Mathf.CeilToInt(segments * (to - from)));
			int start = vh.currentVertCount;

			for (int i = 0; i <= steps; i++)
			{
				float t = Mathf.Lerp(from, to, i / (float)steps);
				float angle = Mathf.PI * 0.5f - t * Mathf.PI * 2f;
				Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
				vh.AddVert(center + direction * inner, c, Vector2.zero);
				vh.AddVert(center + direction * radius, c, Vector2.zero);
			}

			for (int i = 0; i < steps; i++)
			{
				int v = start + i * 2;
				vh.AddTriangle(v, v + 1, v + 3);
				vh.AddTriangle(v + 3, v + 2, v);
			}
		}

#if UNITY_EDITOR
		protected override void OnValidate()
		{
			base.OnValidate();
			raycastTarget = false; // индикатор никогда не должен перехватывать клики мыши (инвентарь и т.п.)
		}
#endif
	}
}
