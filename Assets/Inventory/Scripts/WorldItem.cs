using Interactables;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.SmartFormat.PersistentVariables;

namespace Inventory
{
	// Вешается на worldPrefab предмета, лежащего в сцене — единственный способ подобрать что-либо в игре,
	// включая оружие (см. WeaponItemData — просто ItemData со ссылкой на боевые данные WeaponController).
	// Коллайдер — обычный (не триггер): и для физики предмета на полу, и для рейкаста PlayerInteractor.
	// Несколько штук одного предмета (ящик шприцов, пачка патронов) — тот же предмет с Amount > 1: другая модель
	// в мире, в инвентарь попадает Amount штук этого предмета
	[RequireComponent(typeof(Collider))]
	public class WorldItem : MonoBehaviour, IInteractable
	{
		[SerializeField] private ItemData item;
		[Tooltip("Сколько штук предмета попадёт в инвентарь при подборе. Больше 1 — подсказка с количеством (Pickup Amount Prompt)")]
		[SerializeField] private int amount = 1;

		[Tooltip("Только для оружия: сколько патронов в магазине при подборе. -1 — полный магазин (ёмкость магазина из WeaponData). " +
			"При выбрасывании оружия сюда автоматически записывается, сколько в нём оставалось")]
		[SerializeField] private int loadedAmmo = -1;

		[Tooltip("Шаблон подсказки. Запись в таблице должна быть Smart String с переменной {item} — " +
			"туда подставится уже переведённое название предмета: \"Подобрать: {item}\" / \"Pick up: {item}\"")]
		[SerializeField] private LocalizedString pickupPrompt = new LocalizedString("UI", "prompt.pickup");
		[Tooltip("Шаблон подсказки, когда Amount больше 1. Smart String с переменными {item} и {amount}: " +
			"\"Подобрать: {item} ×{amount}\" / \"Pick up: {item} ×{amount}\". Пусто — обычный шаблон без количества")]
		[SerializeField] private LocalizedString pickupAmountPrompt = new LocalizedString("UI", "prompt.pickup.amount");

		// имена переменных в Smart String шаблонов
		private const string ItemVariable = "item";
		private const string AmountVariable = "amount";

		// количество — переменная шаблона: при смене Amount (SetData) подсказка обновится сама
		private readonly IntVariable _amountVariable = new IntVariable();

		// защита от двойного подбора в один кадр: Destroy() откладывается до конца кадра, за это время
		// объект физически ещё существует и может быть подобран повторно (например, и по E, и по триггеру)
		private bool _pickedUp;

		public ItemData Item => item;

		// шаблон не задан — хотя бы просто название предмета
		public LocalizedString InteractionPrompt
		{
			get
			{
				if (item == null) return null;
				if (amount > 1 && pickupAmountPrompt != null && !pickupAmountPrompt.IsEmpty) return pickupAmountPrompt;
				return pickupPrompt == null || pickupPrompt.IsEmpty ? item.itemName : pickupPrompt;
			}
		}

		private void Awake()
		{
			if (pickupAmountPrompt != null) pickupAmountPrompt[AmountVariable] = _amountVariable;
			UpdatePromptVariables();
		}

		// используется при выбрасывании предмета из инвентаря, чтобы привязать данные к заспавненному объекту
		public void SetData(ItemData data, int stackAmount)
		{
			item = data;
			amount = stackAmount;
			UpdatePromptVariables();
		}

		// выброшенное оружие помнит, сколько патронов в нём оставалось (см. InventoryWeaponEquipHandler)
		public void SetLoadedAmmo(int ammo)
		{
			loadedAmmo = ammo;
		}

		// Название предмета — вложенная LocalizedString внутри шаблона: Localization сам переведёт её и
		// подставит в {item}, и оба текста обновятся при смене языка
		private void UpdatePromptVariables()
		{
			_amountVariable.Value = amount;
			SetItemVariable(pickupPrompt);
			SetItemVariable(pickupAmountPrompt);
		}

		private void SetItemVariable(LocalizedString prompt)
		{
			if (prompt == null) return;

			if (item != null) prompt[ItemVariable] = item.itemName;
			else prompt.Remove(ItemVariable);
		}

		public void Interact(GameObject interactor)
		{
			if (_pickedUp || item == null) return;

			InventoryHolder inventory = interactor.GetComponentInParent<InventoryHolder>();
			if (inventory == null || !inventory.AddItem(item, amount, loadedAmmo)) return;

			_pickedUp = true;
			Destroy(gameObject);
		}
	}
}
