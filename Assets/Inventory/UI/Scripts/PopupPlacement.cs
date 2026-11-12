using UnityEngine;

namespace Inventory
{
	// Размещение всплывающих окошек (меню по ПКМ, окно количества) под курсором
	internal static class PopupPlacement
	{
		// Поставить popup верхним левым углом под курсор внутри area; не влезает вправо/вниз — прижимается к краю.
		// Размер popup должен быть уже посчитан (после LayoutRebuilder.ForceRebuildLayoutImmediate).
		// Позиция округляется до целого пикселя — пиксельный UI не размывается
		public static void PlaceAt(RectTransform area, RectTransform popup, Vector2 screenPosition, Camera eventCamera)
		{
			if (area == null || popup == null) return;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPosition, eventCamera, out Vector2 local)) return;

			Rect bounds = area.rect;
			Vector2 size = popup.rect.size;

			float x = Mathf.Clamp(local.x, bounds.xMin, bounds.xMax - size.x);
			float y = Mathf.Clamp(local.y, bounds.yMin + size.y, bounds.yMax);

			popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
			popup.pivot = new Vector2(0f, 1f);
			Vector2 center = bounds.center;
			popup.anchoredPosition = new Vector2(Mathf.Round(x - center.x), Mathf.Round(y - center.y));
		}
	}
}
