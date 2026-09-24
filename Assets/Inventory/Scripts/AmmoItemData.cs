using UnityEngine;

namespace Inventory
{
	// Стекающийся предмет-пул патронов/газа. ammoId должен совпадать с Weapons.WeaponData.AmmoId у
	// оружия, для которого эти патроны подходят — сопоставление идёт строкой, а не прямой ссылкой,
	// чтобы WeaponData не зависел от Inventory (та же логика, что и WeaponId у самого оружия).
	// Подбирается и хранится как обычный ItemData через WorldItem/InventoryHolder — своей логики
	// подбора не требует.
	[CreateAssetMenu(fileName = "NewAmmoItemData", menuName = "Inventory/Ammo Item Data")]
	public class AmmoItemData : ItemData
	{
		[Tooltip("Должен совпадать с WeaponData.AmmoId у оружия, для которого эти патроны/газ подходят")]
		public string ammoId;

		// патроны всегда типа Ammo — незачем давать выставить это вручную и промахнуться
		private void OnValidate()
		{
			type = ItemType.Ammo;
		}
	}
}
