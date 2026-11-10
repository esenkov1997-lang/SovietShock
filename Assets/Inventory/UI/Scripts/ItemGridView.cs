using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
	// Показывает одну сетку предметов (ItemGrid): инвентарь игрока, позже — ящик. Сама сетка — это картинка:
	// Image с Image Type = Tiled и спрайтом одной клетки (Background), видна и правится прямо в редакторе.
	// Код только задаёт её размер (клеток × Cell Size) и раскладывает поверх предметы (GridItemView).
	//
	// Объект — верхний левый угол сетки, всё внутри считается от него. Размер в игре берётся из данных
	// (InventoryHolder: Grid Width/Height), в редакторе — из Preview Size, чтобы вёрстку было видно до запуска.
	// Все размеры и позиции целые, поэтому при целом масштабе канваса (PixelPerfectCanvasScaler) картинка чёткая.
	//
	// Сюда же приходит "отпускание" перетаскиваемого предмета (OnDrop): клетки фона и сами предметы — внутри этого
	// объекта, поэтому событие всплывает до него, куда бы ни бросили. Background должен ловить лучи (Raycast Target).
	[RequireComponent(typeof(RectTransform))]
	public class ItemGridView : MonoBehaviour, IDropHandler, ILayoutElement
	{
		[Header("Grid")]
		[Tooltip("Размер одной клетки в пикселях интерфейса. Спрайт клетки фона — того же размера")]
		[Min(1)]
		[SerializeField] private int cellSize = 32;
		[Tooltip("Только для редактора: сколько клеток показать при вёрстке (в игре размер берётся из данных сетки)")]
		[SerializeField] private Vector2Int previewSize = new Vector2Int(8, 6);

		[Header("References")]
		[Tooltip("Фон сетки: Image, Image Type = Tiled, спрайт одной клетки, Raycast Target включён. " +
			"Растягивается на всю сетку (якоря stretch-stretch, отступы 0)")]
		[SerializeField] private Image background;
		[Tooltip("Куда спавнятся предметы: пустой объект на всю сетку (stretch-stretch, отступы 0) поверх фона. " +
			"Пусто — прямо в этот объект")]
		[SerializeField] private RectTransform itemsRoot;
		[Tooltip("Префаб предмета (с компонентом GridItemView)")]
		[SerializeField] private GridItemView itemViewPrefab;

		private ItemGrid _grid;
		private readonly List<GridItemView> _views = new List<GridItemView>();

		public ItemGrid Grid => _grid;
		public int CellSize => cellSize;
		private RectTransform ItemsRoot => itemsRoot != null ? itemsRoot : (RectTransform)transform;

		private void Awake()
		{
			// верхний левый угол — точка отсчёта: размер сетки меняется вправо-вниз, не сдвигая её, и угол всегда
			// на целом пикселе. С другим pivot сетка нечётного размера съехала бы на полпикселя
			if (((RectTransform)transform).pivot != new Vector2(0f, 1f))
			{
				Debug.LogWarning($"{nameof(ItemGridView)} на {name}: поставь Pivot = (0, 1) — верхний левый угол", this);
			}

			// до привязки к данным (игрок ещё не появился) — размер как при вёрстке, чтобы окно не схлопнулось
			if (_grid == null) ApplySize(previewSize);

			if (itemViewPrefab == null) Debug.LogError($"{nameof(ItemGridView)} на {name}: не назначен Item View Prefab", this);
		}

		private void OnDestroy()
		{
			if (_grid != null) _grid.Changed -= Redraw;
		}

		// Показать сетку (null — отвязать). Вид перерисовывается сам при каждом изменении сетки,
		// в том числе пока окно закрыто — при открытии всё уже актуально
		public void Bind(ItemGrid grid)
		{
			if (_grid == grid) return;

			if (_grid != null) _grid.Changed -= Redraw;
			_grid = grid;
			if (_grid != null) _grid.Changed += Redraw;

			ApplySize(_grid != null ? new Vector2Int(_grid.Width, _grid.Height) : previewSize);
			Redraw();
		}

		// ---------- Размер (ILayoutElement) ----------

		// Сетка сообщает раскладке свой точный размер (клеток × Cell Size): окно с Vertical Layout Group и
		// Content Size Fitter подстраивается под неё само, а растягивать или сжимать сетку раскладка не будет
		private Vector2Int _cells = Vector2Int.one;
		private Vector2 PixelSize => new Vector2(_cells.x * cellSize, _cells.y * cellSize);

		public float minWidth => PixelSize.x;
		public float preferredWidth => PixelSize.x;
		public float flexibleWidth => 0f;
		public float minHeight => PixelSize.y;
		public float preferredHeight => PixelSize.y;
		public float flexibleHeight => 0f;
		public int layoutPriority => 1;
		public void CalculateLayoutInputHorizontal() { }
		public void CalculateLayoutInputVertical() { }

		private void ApplySize(Vector2Int cells)
		{
			_cells = Vector2Int.Max(cells, Vector2Int.one);
			RectTransform rect = (RectTransform)transform;
			rect.sizeDelta = PixelSize;
			// окно вокруг сетки может подстраиваться под неё (Layout Group + Content Size Fitter) — сообщаем ему,
			// что размер изменился: сам по себе размер обычного объекта раскладку не будит
			LayoutRebuilder.MarkLayoutForRebuild(rect);
		}

		// полная перерисовка: сетка маленькая, точечные обновления не окупаются
		private void Redraw()
		{
			foreach (GridItemView view in _views)
			{
				if (view != null) Destroy(view.gameObject);
			}
			_views.Clear();

			if (_grid == null || itemViewPrefab == null) return;

			foreach (GridItem item in _grid.Items)
			{
				GridItemView view = Instantiate(itemViewPrefab, ItemsRoot);
				view.Setup(item, this);
				_views.Add(view);
			}
		}

		// ---------- Координаты ----------

		// позиция верхнего левого угла клетки относительно верхнего левого угла сетки (y вниз — отрицательный)
		public Vector2 CellToLocal(Vector2Int cell) => new Vector2(cell.x * cellSize, -cell.y * cellSize);

		// клетка под точкой экрана; false — точка вне сетки
		public bool ScreenToCell(Vector2 screenPoint, Camera eventCamera, out Vector2Int cell)
		{
			cell = default;
			RectTransform root = ItemsRoot;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, eventCamera, out Vector2 local)) return false;

			Rect rect = root.rect;
			float fromLeft = local.x - rect.xMin;
			float fromTop = rect.yMax - local.y;
			if (fromLeft < 0f || fromTop < 0f || fromLeft >= rect.width || fromTop >= rect.height) return false;

			cell = new Vector2Int(Mathf.FloorToInt(fromLeft / cellSize), Mathf.FloorToInt(fromTop / cellSize));
			return true;
		}

		// ---------- Перетаскивание ----------

		// Предмет бросили над этой сеткой. Пока — только перекладывание внутри неё же (перенос между сетками,
		// колесом и миром — этап "взаимодействие"). Клетка, куда встанет верхний левый угол предмета, — та, что под
		// курсором, минус клетка предмета, за которую его взяли: предмет ложится ровно туда, где его видно
		public void OnDrop(PointerEventData eventData)
		{
			if (_grid == null || eventData.pointerDrag == null) return;
			if (!eventData.pointerDrag.TryGetComponent(out GridItemView dragged) || dragged.Owner != this) return;
			if (!ScreenToCell(eventData.position, eventData.pressEventCamera, out Vector2Int cell)) return;

			_grid.TryMove(dragged.Item, cell - dragged.GrabCell);
		}

#if UNITY_EDITOR
		// предпросмотр размера при вёрстке: меняешь Preview Size или Cell Size — сетка в сцене сразу меняется.
		// Размер RectTransform нельзя менять прямо в OnValidate (Unity ругается) — откладываем на следующий тик редактора
		private void OnValidate()
		{
			previewSize = Vector2Int.Max(previewSize, Vector2Int.one);
			if (Application.isPlaying) return;

			UnityEditor.EditorApplication.delayCall += () =>
			{
				if (this == null || Application.isPlaying) return;
				UnityEditor.Undo.RecordObject(transform, "Item Grid Preview Size");
				ApplySize(previewSize);
			};
		}
#endif
	}
}
