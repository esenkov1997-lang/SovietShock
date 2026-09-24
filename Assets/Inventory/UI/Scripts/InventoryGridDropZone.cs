using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
	// Живёт на Content (том же объекте, где Grid Layout Group задаёт размер/отступы одной ячейки).
	// Предметы (InventorySlotUI) сами по себе больше не ловят OnDrop — они могут быть размером больше
	// 1x1 и лежат вперемешку с пустым местом, поэтому единственная точка, гарантированно покрывающая
	// всю сетку целиком (в том числе пустые клетки без собственного IDropHandler), — сам Content.
	// Пересчитывает точку отпускания в координаты ячейки и просит InventoryHolder передвинуть туда
	// перетаскиваемый предмет (см. InventoryHolder.TryMoveItem).
	[RequireComponent(typeof(RectTransform), typeof(GridLayoutGroup))]
	public class InventoryGridDropZone : MonoBehaviour, IDropHandler
	{
		private RectTransform _rect;
		private GridLayoutGroup _grid;

		private void Awake()
		{
			_rect = (RectTransform)transform;
			_grid = GetComponent<GridLayoutGroup>();
		}

		public void OnDrop(PointerEventData eventData)
		{
			if (eventData.pointerDrag == null) return;
			if (!eventData.pointerDrag.TryGetComponent(out InventorySlotUI draggedSlot)) return;
			if (draggedSlot.Inventory == null) return;

			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
			{
				return;
			}

			Vector2Int cell = LocalPointToCell(local);
			draggedSlot.Inventory.TryMoveItem(draggedSlot.SlotIndex, cell);
		}

		// та же арифметика, которой Grid Layout Group сама раскладывает детей при UpperLeft/Horizontal
		// (см. InventoryUI.RedrawUI, где предметы позиционируются вручную по этой же формуле в обратную
		// сторону) — локальная точка отсчитывается от pivot RectTransform, здесь это верхний левый угол
		private Vector2Int LocalPointToCell(Vector2 local)
		{
			float cellStepX = _grid.cellSize.x + _grid.spacing.x;
			float cellStepY = _grid.cellSize.y + _grid.spacing.y;

			float xFromLeft = local.x - _rect.rect.xMin - _grid.padding.left;
			float yFromTop = _rect.rect.yMax - local.y - _grid.padding.top;

			int cellX = cellStepX > 0f ? Mathf.FloorToInt(xFromLeft / cellStepX) : 0;
			int cellY = cellStepY > 0f ? Mathf.FloorToInt(yFromTop / cellStepY) : 0;

			return new Vector2Int(cellX, cellY);
		}
	}
}
