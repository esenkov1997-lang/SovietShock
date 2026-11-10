using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
	// Один предмет, лежащий в сетке (префаб для ItemGridView). Размер и позицию выставляет код: предмет занимает
	// ровно свои клетки (ItemData.GridSize × Cell Size). Префаб отвечает только за вид:
	//   - Icon — Image иконки (Raycast Target включён: за него предмет берут мышью);
	//   - Count — текст количества, только у стопок больше одной штуки. Положение задаётся в префабе
	//     (обычно правый нижний угол, якорь bottom-right).
	//
	// Иконка (Icon Fit):
	//   - Native Size — спрайт в своём настоящем размере, по центру предмета, на целом пикселе. Для пиксель-арта:
	//     рисуй иконку под размер предмета (1×1 при клетке 32 — до 32×32 px; 2×4 — до 64×128 px);
	//   - Fit Inside — вписать в предмет с отступом Icon Padding, сохраняя пропорции. Для временных и
	//     детализированных иконок (масштабируются — пиксели будут неровными).
	//
	// Перетаскивание: за курсором летит "призрак" иконки, сам предмет остаётся на месте до решения сетки, куда его
	// положить (ItemGridView.OnDrop). Иконку держат за ту клетку, за которую взяли (GrabCell).
	[RequireComponent(typeof(RectTransform))]
	public class GridItemView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		public enum IconFit { NativeSize, FitInside }

		[SerializeField] private Image icon;
		[SerializeField] private TMP_Text countText;
		[SerializeField] private IconFit iconFit = IconFit.NativeSize;
		[Tooltip("Только для Fit Inside: отступ иконки от края предмета, в пикселях интерфейса")]
		[Min(0)]
		[SerializeField] private int iconPadding = 2;

		// один призрак на всё сразу: если жест прервали не отпусканием кнопки (закрыли окно, перерисовка посреди
		// перетаскивания), следующее перетаскивание или закрытие инвентаря подчистят оставшийся
		private static GameObject s_activeGhost;

		private RectTransform _dragGhost;

		public ItemInstance Item { get; private set; }
		public ItemGridView Owner { get; private set; }
		// клетка предмета (от его верхнего левого угла), за которую его взяли мышью
		public Vector2Int GrabCell { get; private set; }

		private RectTransform Rect => (RectTransform)transform;

		public void Setup(GridItem item, ItemGridView owner)
		{
			Item = item.Instance;
			Owner = owner;

			int cell = owner.CellSize;
			RectTransform rect = Rect;
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.sizeDelta = new Vector2(item.Size.x * cell, item.Size.y * cell);
			rect.anchoredPosition = owner.CellToLocal(item.Position);

			SetupIcon(Item.Data != null ? Item.Data.icon : null, rect.sizeDelta);

			if (countText != null)
			{
				bool showCount = Item.IsStackable && Item.Count > 1;
				countText.gameObject.SetActive(showCount);
				if (showCount) countText.text = Item.Count.ToString();
			}
		}

		private void SetupIcon(Sprite sprite, Vector2 itemSize)
		{
			if (icon == null) return;

			icon.sprite = sprite;
			icon.enabled = sprite != null;
			if (sprite == null) return;

			RectTransform iconRect = icon.rectTransform;
			if (iconFit == IconFit.NativeSize)
			{
				icon.preserveAspect = false;
				icon.SetNativeSize();

				// по центру, но с округлением вниз до целого пикселя — без этого иконка нечётного размера в предмете
				// чётного размера встала бы на полпикселя и потеряла линию
				Vector2 iconSize = iconRect.sizeDelta;
				iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
				iconRect.pivot = new Vector2(0f, 1f);
				iconRect.anchoredPosition = new Vector2(
					Mathf.Floor((itemSize.x - iconSize.x) * 0.5f),
					-Mathf.Floor((itemSize.y - iconSize.y) * 0.5f));
			}
			else
			{
				icon.preserveAspect = true;
				iconRect.anchorMin = Vector2.zero;
				iconRect.anchorMax = Vector2.one;
				iconRect.pivot = new Vector2(0.5f, 0.5f);
				iconRect.offsetMin = new Vector2(iconPadding, iconPadding);
				iconRect.offsetMax = new Vector2(-iconPadding, -iconPadding);
			}
		}

		// ---------- Перетаскивание ----------

		private void OnDisable()
		{
			// объект выключили или уничтожили посреди перетаскивания (перерисовка, закрытие окна) — OnEndDrag в таком
			// случае не придёт, подчищаем здесь
			DestroyOwnGhost();
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (s_activeGhost != null) Destroy(s_activeGhost); // подстраховка от утёкшего с прошлого раза

			Canvas canvas = GetComponentInParent<Canvas>();
			if (canvas == null || icon == null || icon.sprite == null) return;
			canvas = canvas.rootCanvas;

			// за какую клетку взяли: от верхнего левого угла предмета
			RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
			Rect rect = Rect.rect;
			Vector2 fromTopLeft = new Vector2(local.x - rect.xMin, rect.yMax - local.y);
			int cell = Owner != null ? Owner.CellSize : 1;
			GrabCell = new Vector2Int(
				Mathf.Clamp(Mathf.FloorToInt(fromTopLeft.x / cell), 0, Item.GridSize.x - 1),
				Mathf.Clamp(Mathf.FloorToInt(fromTopLeft.y / cell), 0, Item.GridSize.y - 1));

			// призрак — копия иконки прямо на корневом канвасе (поверх всего, без обрезки масками окон)
			GameObject ghost = Instantiate(icon.gameObject, canvas.transform);
			ghost.name = "DragGhost";
			ghost.transform.SetAsLastSibling();
			foreach (Graphic graphic in ghost.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;

			// та же точка иконки под курсором, что и в момент захвата
			_dragGhost = (RectTransform)ghost.transform;
			RectTransform iconRect = icon.rectTransform;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(iconRect, eventData.position, eventData.pressEventCamera, out Vector2 inIcon);
			Rect iconLocal = iconRect.rect;
			_dragGhost.anchorMin = _dragGhost.anchorMax = new Vector2(0.5f, 0.5f);
			_dragGhost.sizeDelta = iconLocal.size;
			_dragGhost.pivot = new Vector2(
				(inIcon.x - iconLocal.xMin) / Mathf.Max(1f, iconLocal.width),
				(inIcon.y - iconLocal.yMin) / Mathf.Max(1f, iconLocal.height));
			_dragGhost.position = eventData.position;

			s_activeGhost = ghost;
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (_dragGhost != null) _dragGhost.position = eventData.position;
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			DestroyOwnGhost();
		}

		private void DestroyOwnGhost()
		{
			if (_dragGhost == null) return;

			Destroy(_dragGhost.gameObject);
			if (s_activeGhost == _dragGhost.gameObject) s_activeGhost = null;
			_dragGhost = null;
		}

		// для окна инвентаря при закрытии: перетаскивание могли прервать не отпусканием кнопки
		public static void DestroyActiveGhost()
		{
			if (s_activeGhost == null) return;

			Destroy(s_activeGhost);
			s_activeGhost = null;
		}
	}
}
