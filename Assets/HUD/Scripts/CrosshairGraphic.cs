using UnityEngine;
using UnityEngine.UI;

namespace HUD
{
	// Рисует перекрестие сам (4 линии + точка) — без спрайтов и отдельных Image на каждую линию: форма
	// целиком настраивается числами в инспекторе, а разброс (Spread) раздвигает линии без пересборки иерархии.
	// Только отрисовка — поведение (разброс от движения/стрельбы, скрытие) задаёт CrosshairUI.
	// Цвет — обычное поле Color у Graphic. Вешается на пустой RectTransform по центру HUD-канваса.
	[RequireComponent(typeof(CanvasRenderer))]
	public class CrosshairGraphic : MaskableGraphic
	{
		[Header("Shape")]
		[Tooltip("Длина каждой линии, px")]
		[SerializeField] private float lineLength = 10f;
		[Tooltip("Толщина линий, px")]
		[SerializeField] private float thickness = 2f;
		[Tooltip("Базовый зазор от центра до начала линии, px — без учёта разброса")]
		[SerializeField] private float gap = 6f;
		[Tooltip("Рисовать верхнюю линию. Выключи для T-образного прицела")]
		[SerializeField] private bool topLine = true;
		[Tooltip("Точка в центре")]
		[SerializeField] private bool dot = true;
		[Tooltip("Размер точки, px")]
		[SerializeField] private float dotSize = 2f;

		private float _spread;
		private bool _linesVisible = true;
		private bool _dotForced;

		// дополнительный зазор поверх gap, px — задаёт CrosshairUI
		public float Spread
		{
			get => _spread;
			set
			{
				if (Mathf.Approximately(_spread, value)) return;
				_spread = value;
				SetVerticesDirty();
			}
		}

		// стиль "только точка" (например, для холодного оружия) — линии прячутся, точка рисуется всегда
		public void SetStyle(bool linesVisible, bool forceDot)
		{
			if (_linesVisible == linesVisible && _dotForced == forceDot) return;
			_linesVisible = linesVisible;
			_dotForced = forceDot;
			SetVerticesDirty();
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			Vector2 center = rectTransform.rect.center;
			float half = thickness * 0.5f;
			float inner = gap + _spread;
			float outer = inner + lineLength;

			if (_linesVisible)
			{
				if (topLine) AddQuad(vh, center, new Vector2(-half, inner), new Vector2(half, outer));
				AddQuad(vh, center, new Vector2(-half, -outer), new Vector2(half, -inner));
				AddQuad(vh, center, new Vector2(-outer, -half), new Vector2(-inner, half));
				AddQuad(vh, center, new Vector2(inner, -half), new Vector2(outer, half));
			}

			if (dot || _dotForced)
			{
				float d = dotSize * 0.5f;
				AddQuad(vh, center, new Vector2(-d, -d), new Vector2(d, d));
			}
		}

		private void AddQuad(VertexHelper vh, Vector2 center, Vector2 min, Vector2 max)
		{
			int start = vh.currentVertCount;
			Color32 c = color;
			vh.AddVert(center + new Vector2(min.x, min.y), c, Vector2.zero);
			vh.AddVert(center + new Vector2(min.x, max.y), c, Vector2.zero);
			vh.AddVert(center + new Vector2(max.x, max.y), c, Vector2.zero);
			vh.AddVert(center + new Vector2(max.x, min.y), c, Vector2.zero);
			vh.AddTriangle(start, start + 1, start + 2);
			vh.AddTriangle(start + 2, start + 3, start);
		}

#if UNITY_EDITOR
		protected override void OnValidate()
		{
			base.OnValidate();
			raycastTarget = false; // перекрестие никогда не должно перехватывать клики мыши (инвентарь и т.п.)
		}
#endif
	}
}
