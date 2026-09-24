using System.Collections.Generic;
using UnityEngine;
using Weapons;

namespace Inventory
{
	// Мост между инвентарём и перезарядкой оружия — та же идея, что и InventoryWeaponEquipHandler,
	// только в другую сторону: WeaponController.CompleteReload спрашивает через ConsumeAmmo, сколько
	// патронов реально можно взять из пула (AmmoItemData), и получает не больше, чем есть в инвентаре,
	// и не больше, чем реально нужно магазину — WeaponController сам не знает про Inventory вообще.
	//
	// Живёт на том же объекте, что и InventoryHolder (корень игрока), WeaponController ищем в детях —
	// та же причина, что и в InventoryWeaponEquipHandler (см. его комментарий).
	[RequireComponent(typeof(InventoryHolder))]
	public class InventoryReloadHandler : MonoBehaviour
	{
		[Tooltip("Все типы патронов/газа в игре — сопоставляются с WeaponData.AmmoId по совпадению строки")]
		[SerializeField] private List<AmmoItemData> ammoTypes = new List<AmmoItemData>();
		[SerializeField] private WeaponController weaponController;

		private InventoryHolder _inventory;

		private void Awake()
		{
			_inventory = GetComponent<InventoryHolder>();
			if (weaponController == null) weaponController = GetComponentInChildren<WeaponController>();
		}

		private void OnEnable()
		{
			if (weaponController != null) weaponController.ConsumeAmmo = TryTakeFromPool;
		}

		private void OnDisable()
		{
			if (weaponController != null) weaponController.ConsumeAmmo = null;
		}

		// сколько реально взяли из пула (0..amountRequested) — WeaponController сам добавит это в магазин
		private int TryTakeFromPool(WeaponData data, int amountRequested)
		{
			if (amountRequested <= 0) return 0;

			// AmmoId не задан — оружие ещё не подключено к системе патронов, старое бесконечное поведение
			if (string.IsNullOrEmpty(data.AmmoId)) return amountRequested;

			AmmoItemData ammo = FindAmmo(data.AmmoId);
			if (ammo == null) return 0;

			int available = _inventory.GetTotalCount(ammo);
			int take = Mathf.Min(amountRequested, available);
			if (take <= 0) return 0;

			_inventory.RemoveItem(ammo, take);
			return take;
		}

		private AmmoItemData FindAmmo(string ammoId)
		{
			foreach (AmmoItemData ammo in ammoTypes)
			{
				if (ammo != null && ammo.ammoId == ammoId) return ammo;
			}
			return null;
		}
	}
}
