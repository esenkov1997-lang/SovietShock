using Interactables;
using Inventory;
using UnityEngine;

namespace Triggers
{
	// Дверь, которая открывается через существующие триггеры, только если у вошедшего есть нужный
	// квестовый предмет — например, "Ключ-карта". Держи на объекте с BaseTrigger (обычно PlayerTrigger)
	// у самой двери. Проверка идёт через InventoryHolder.HasItem, то есть работает для любого ItemData,
	// не только квестового — но по смыслу сюда обычно кладут именно ItemType.Quest.
	public class LockedDoorAction : TriggerAction
	{
		[SerializeField] private Door door;
		[SerializeField] private ItemData requiredItem;
		[SerializeField] private int requiredAmount = 1;
		[Tooltip("Забирать ли предмет из инвентаря при открытии — выключи для ключа многоразового использования")]
		[SerializeField] private bool consumeItem;

		protected override void Execute(Collider other)
		{
			InventoryHolder inventory = other.GetComponentInParent<InventoryHolder>();
			if (inventory == null || !inventory.HasItem(requiredItem, requiredAmount)) return;

			if (consumeItem) inventory.RemoveItem(requiredItem, requiredAmount);
			door.Open();
		}
	}
}
