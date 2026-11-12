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
	// Перетаскивание (общее для всего инвентаря — ItemDrag): за курсором летит "призрак" иконки, сам предмет остаётся
	// на месте, пока цель (сетка, колесо) не решит, куда его положить. Иконку держат за ту клетку, за которую взяли.
	//
	// Клики (ЛКМ дважды — "быстрое действие", ПКМ — контекстное меню) и бросок за пределы окон предмет передаёт своей
	// сетке (ItemGridView), а что они значат — решает экран инвентаря.
	[RequireComponent(typeof(RectTransform))]
	public class GridItemView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler,
		IPointerEnterHandler, IPointerExitHandler
	{
		public enum IconFit { NativeSize, FitInside }

		[SerializeField] private Image icon;
		[SerializeField] private TMP_Text countText;
		[SerializeField] private IconFit iconFit = IconFit.NativeSize;
		[Tooltip("Только для Fit Inside: отступ иконки от края предмета, в пикселях интерфейса")]
		[Min(0)]
		[SerializeField] private int iconPadding = 2;

		public ItemInstance Item { get; private set; }
		public ItemGridView Owner { get; private set; }

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

		// ---------- Клики и перетаскивание ----------

		// этот предмет сейчас перетаскивают (жест начат с него)
		private bool _dragging;

		private void OnDisable()
		{
			// вид уничтожили посреди перетаскивания (сетка перерисовалась, окно закрыли) — OnEndDrag уже не придёт
			if (_dragging) ItemDrag.Cancel();
			_dragging = false;
			ItemHover.Exit(this);
		}

		// наведение — для подсказки (ItemTooltipView)
		public void OnPointerEnter(PointerEventData eventData) => ItemHover.Enter(this, Item);
		public void OnPointerExit(PointerEventData eventData) => ItemHover.Exit(this);

		public void OnPointerClick(PointerEventData eventData)
		{
			if (Owner == null || eventData.dragging) return;

			if (eventData.button == PointerEventData.InputButton.Right) Owner.NotifyRightClick(this, eventData);
			else if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2) Owner.NotifyDoubleClick(this);
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			// перетаскивают только левой кнопкой — правая открывает меню
			if (eventData.button != PointerEventData.InputButton.Left || Item == null)
			{
				eventData.pointerDrag = null;
				return;
			}

			// за какую клетку взяли: от верхнего левого угла предмета
			RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
			Rect rect = Rect.rect;
			Vector2 fromTopLeft = new Vector2(local.x - rect.xMin, rect.yMax - local.y);
			int cell = Owner != null ? Owner.CellSize : 1;
			Vector2Int grabCell = new Vector2Int(
				Mathf.Clamp(Mathf.FloorToInt(fromTopLeft.x / cell), 0, Item.GridSize.x - 1),
				Mathf.Clamp(Mathf.FloorToInt(fromTopLeft.y / cell), 0, Item.GridSize.y - 1));

			ItemDrag.Begin(Item, Owner, -1, grabCell, eventData, icon);
			_dragging = true;
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (_dragging) ItemDrag.Move(eventData);
		}

		// OnDrop у целей приходит раньше OnEndDrag — к этому моменту уже известно, принял ли кто-то предмет
		public void OnEndDrag(PointerEventData eventData)
		{
			if (!_dragging) return;
			_dragging = false;

			ItemInstance item = Item;
			if (ItemDrag.End(eventData) && Owner != null && item == Item) Owner.NotifyDroppedOutside(this);
		}
	}
}
