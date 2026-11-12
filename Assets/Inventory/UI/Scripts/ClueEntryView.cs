using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
	// Строка списка улик (префаб для ClueListView): иконка, название, количество (только если больше одного).
	// Клик ЛКМ и ПКМ передаёт списку — что они значат (открыть документ, меню), решает экран инвентаря.
	// Для кликов нужен Graphic с Raycast Target на всю строку (например, фон строки на корне).
	public class ClueEntryView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
	{
		[SerializeField] private Image icon;
		[SerializeField] private TMP_Text nameText;
		[Tooltip("Необязательно: количество (\"×2\") — показывается, только если одинаковых улик больше одной")]
		[SerializeField] private TMP_Text countText;

		public ItemInstance Item { get; private set; }
		private ClueListView _owner;

		public void Setup(ItemInstance item, ClueListView owner)
		{
			Item = item;
			_owner = owner;

			if (icon != null)
			{
				icon.sprite = item.Data != null ? item.Data.icon : null;
				icon.enabled = icon.sprite != null;
				icon.preserveAspect = true;
			}

			if (nameText != null)
			{
				if (item.Data != null) LocalizedTextBinding.Bind(nameText, item.Data.itemName);
				else LocalizedTextBinding.SetPlain(nameText, string.Empty);
			}

			if (countText != null)
			{
				countText.gameObject.SetActive(item.Count > 1);
				countText.text = "×" + item.Count;
			}
		}

		// наведение — для подсказки (ItemTooltipView)
		public void OnPointerEnter(PointerEventData eventData) => ItemHover.Enter(this, Item);
		public void OnPointerExit(PointerEventData eventData) => ItemHover.Exit(this);
		private void OnDisable() => ItemHover.Exit(this);

		public void OnPointerClick(PointerEventData eventData)
		{
			if (_owner == null || Item == null) return;

			if (eventData.button == PointerEventData.InputButton.Left) _owner.NotifyClick(this, eventData);
			else if (eventData.button == PointerEventData.InputButton.Right) _owner.NotifyRightClick(this, eventData);
		}
	}
}
