using Interactables;
using UnityEngine;
using UnityEngine.Localization;

namespace Inventory
{
	// Вешается на worldPrefab предмета, лежащего в сцене — единственный способ подобрать что-либо в игре,
	// включая оружие (см. WeaponItemData — просто ItemData со ссылкой на боевые данные WeaponController).
	// Коллайдер — обычный (не триггер): и для физики предмета на полу, и для рейкаста PlayerInteractor.
	[RequireComponent(typeof(Collider))]
	public class WorldItem : MonoBehaviour, IInteractable
	{
		[SerializeField] private ItemData item;
		[SerializeField] private int amount = 1;

		[Tooltip("Шаблон подсказки. Запись в таблице должна быть Smart String с переменной {item} — " +
			"туда подставится уже переведённое название предмета: \"Подобрать: {item}\" / \"Pick up: {item}\"")]
		[SerializeField] private LocalizedString pickupPrompt = new LocalizedString("UI", "prompt.pickup");

		// имя переменной в Smart String шаблона pickupPrompt
		private const string ItemVariable = "item";

		// защита от двойного подбора в один кадр: Destroy() откладывается до конца кадра, за это время
		// объект физически ещё существует и может быть подобран повторно (например, и по E, и по триггеру)
		private bool _pickedUp;

		public ItemData Item => item;

		// шаблон не задан — хотя бы просто название предмета
		public LocalizedString InteractionPrompt =>
			item == null ? null
			: pickupPrompt == null || pickupPrompt.IsEmpty ? item.itemName
			: pickupPrompt;

		private void Awake()
		{
			UpdatePromptItemVariable();
		}

		// используется при выбрасывании предмета из инвентаря, чтобы привязать данные к заспавненному объекту
		public void SetData(ItemData data, int stackAmount)
		{
			item = data;
			amount = stackAmount;
			UpdatePromptItemVariable();
		}

		// Название предмета — вложенная LocalizedString внутри шаблона: Localization сам переведёт её и
		// подставит в {item}, и оба текста обновятся при смене языка
		private void UpdatePromptItemVariable()
		{
			if (pickupPrompt == null) return;

			if (item != null) pickupPrompt[ItemVariable] = item.itemName;
			else pickupPrompt.Remove(ItemVariable);
		}

		public void Interact(GameObject interactor)
		{
			if (_pickedUp || item == null) return;

			InventoryHolder inventory = interactor.GetComponentInParent<InventoryHolder>();
			if (inventory == null || !inventory.AddItem(item, amount)) return;

			_pickedUp = true;
			Destroy(gameObject);
		}
	}
}
