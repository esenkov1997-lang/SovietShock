using UnityEngine;

namespace Inventory
{
	// Размещение всплывающих окошек (меню по ПКМ, окно количества) под курсором
	internal static class PopupPlacement
	{
		// камера для перевода экранных координат в координаты канваса этого объекта: null — канвас Screen Space Overlay
		public static Camera CanvasCamera(Component inCanvas)
		{
			Canvas canvas = inCanvas != null ? inCanvas.GetComponentInParent<Canvas>() : null;
			if (canvas == null) return null;

			Canvas root = canvas.rootCanvas;
			return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
		}

		// Поставить popup верхним левым углом под курсор внутри area; не влезает вправо/вниз — прижимается к краю.
		// Размер popup должен быть уже посчитан (после LayoutRebuilder.ForceRebuildLayoutImmediate).
		// Позиция округляется до целого пикселя — пиксельный UI не размывается
		// offset — сдвиг угла от курсора в пикселях интерфейса (x вправо, y вверх), например чтобы подсказка не лезла под курсор
		public static void PlaceAt(RectTransform area, RectTransform popup, Vector2 screenPosition, Camera eventCamera,
			Vector2 offset = default)
		{
			if (area == null || popup == null) return;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPosition, eventCamera, out Vector2 local)) return;
			local += offset;

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
