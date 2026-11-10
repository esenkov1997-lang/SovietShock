using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
	// Предмет, который сейчас перетаскивают мышью, — общий для всех мест инвентаря: сетки (своей и ящика) и колеса
	// избранного. Откуда взяли (SourceGrid или SourceSlot), за какую клетку держат, где курсор. Цели броска
	// (ItemGridView, WeaponWheelView) по этому решают, можно ли сюда положить, и подсвечивают место.
	// Одновременно перетаскивается только один предмет.
	//
	// За курсором летит "призрак" — копия иконки на корневом канвасе, поверх всех окон.
	public static class ItemDrag
	{
		public static ItemInstance Item { get; private set; }
		// откуда взяли: из сетки (вид сетки) или из слота колеса (номер); второе — null / -1
		public static ItemGridView SourceGrid { get; private set; }
		public static int SourceSlot { get; private set; } = -1;
		// клетка предмета (от его верхнего левого угла), которая под курсором: предмет кладётся так, чтобы она
		// оказалась в клетке под курсором
		public static Vector2Int GrabCell { get; private set; }
		public static Vector2 Position { get; private set; }
		public static Camera EventCamera { get; private set; }

		public static bool IsActive => Item != null;
		public static bool IsFromSlot => IsActive && SourceSlot >= 0;

		// бросок кто-то принял (даже если положить не удалось) — значит, это не выброс в мир
		public static bool Handled { get; private set; }
		public static void MarkHandled() => Handled = true;

		private static GameObject s_ghost;

		// ghostSource — Image, копия которого полетит за курсором (иконка предмета); та точка иконки, за которую
		// взяли, останется под курсором
		public static void Begin(ItemInstance item, ItemGridView sourceGrid, int sourceSlot, Vector2Int grabCell,
			PointerEventData eventData, Image ghostSource)
		{
			Cancel();

			Item = item;
			SourceGrid = sourceGrid;
			SourceSlot = sourceSlot;
			GrabCell = grabCell;
			Position = eventData.position;
			EventCamera = eventData.pressEventCamera;
			Handled = false;

			CreateGhost(ghostSource, eventData);
		}

		public static void Move(PointerEventData eventData)
		{
			Position = eventData.position;
			if (s_ghost != null) s_ghost.transform.position = eventData.position;
		}

		// конец жеста: true — бросили мимо всех окон (никто не принял и под курсором нет UI) — выбросить в мир
		public static bool End(PointerEventData eventData)
		{
			bool outside = IsActive && !Handled && eventData.pointerCurrentRaycast.gameObject == null;
			Cancel();
			return outside;
		}

		// прервать без последствий (окно закрыли посреди перетаскивания, вид предмета уничтожили)
		public static void Cancel()
		{
			Item = null;
			SourceGrid = null;
			SourceSlot = -1;
			Handled = false;

			if (s_ghost != null) Object.Destroy(s_ghost);
			s_ghost = null;
		}

		private static void CreateGhost(Image source, PointerEventData eventData)
		{
			if (source == null || source.sprite == null) return;

			Canvas canvas = source.canvas != null ? source.canvas.rootCanvas : null;
			if (canvas == null) return;

			// копия иконки прямо на корневом канвасе — поверх всего, без обрезки масками окон, не ловит лучи
			s_ghost = Object.Instantiate(source.gameObject, canvas.transform);
			s_ghost.name = "DragGhost";
			s_ghost.transform.SetAsLastSibling();
			foreach (Graphic graphic in s_ghost.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;

			RectTransform iconRect = source.rectTransform;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(iconRect, eventData.position, eventData.pressEventCamera, out Vector2 inIcon);
			Rect local = iconRect.rect;

			RectTransform ghost = (RectTransform)s_ghost.transform;
			ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
			ghost.sizeDelta = local.size;
			ghost.localRotation = iconRect.localRotation;
			ghost.pivot = new Vector2(
				Mathf.Clamp01((inIcon.x - local.xMin) / Mathf.Max(1f, local.width)),
				Mathf.Clamp01((inIcon.y - local.yMin) / Mathf.Max(1f, local.height)));
			ghost.position = eventData.position;
		}
	}
}
