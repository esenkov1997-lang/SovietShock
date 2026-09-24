using UnityEngine;
using Weapons;

namespace Inventory
{
	// Мост между универсальным инвентарём и существующей системой оружия. Инвентарь и WeaponController
	// сознательно не знают друг о друге напрямую (как BaseTrigger не знает о TriggerAction).
	//
	// Подбор: подписывается на InventoryHolder.OnItemAdded и, если добавленный предмет — WeaponItemData,
	// сразу экипирует его через WeaponController. Слот в инвентаре при этом НЕ освобождается — оружие
	// одновременно занимает место в сетке (можно увидеть/переставить) и находится "в руках"; ammo/cooldown
	// по-прежнему считает только WeaponController, инвентарь лишь отображает факт владения.
	//
	// Выброс: WeaponController.DropCurrentWeapon сам не трогает InventoryHolder, поэтому здесь же слушаем
	// WeaponController.WeaponDropped и убираем соответствующий слот — иначе после выброса в инвентаре
	// осталась бы "призрачная" запись об оружии, которого уже нет ни в руках, ни в инвентаре по факту.
	//
	// Живёт на том же объекте, что и InventoryHolder (см. RequireComponent) — то есть на корне игрока
	// (PlayerCapsule), а WeaponController висит на дочернем WeaponHolder. Поэтому ищем его через
	// GetComponentInChildren (вниз по иерархии), а не GetComponentInParent (вверх) — WorldItem.Interact
	// и LockedDoorAction ищут InventoryHolder в обратную сторону именно потому, что стартуют ниже корня.
	[RequireComponent(typeof(InventoryHolder))]
	public class InventoryWeaponEquipHandler : MonoBehaviour
	{
		[SerializeField] private WeaponController weaponController;

		private InventoryHolder _inventory;

		private void Awake()
		{
			_inventory = GetComponent<InventoryHolder>();
			if (weaponController == null) weaponController = GetComponentInChildren<WeaponController>();
		}

		private void OnEnable()
		{
			_inventory.OnItemAdded += HandleItemAdded;
			if (weaponController != null) weaponController.WeaponDropped += HandleWeaponDropped;
		}

		private void OnDisable()
		{
			_inventory.OnItemAdded -= HandleItemAdded;
			if (weaponController != null) weaponController.WeaponDropped -= HandleWeaponDropped;
		}

		private void HandleItemAdded(ItemData item, int amount)
		{
			if (weaponController == null || !(item is WeaponItemData weaponItem)) return;

			weaponController.PickupWeapon(weaponItem.weaponData);
		}

		private void HandleWeaponDropped(WeaponData data)
		{
			foreach (ItemStack stack in _inventory.Items)
			{
				if (stack.item is WeaponItemData weaponItem && weaponItem.weaponData == data)
				{
					_inventory.RemoveItem(weaponItem, 1);
					return;
				}
			}
		}
	}
}
