using UnityEngine;
using Weapons;

namespace Inventory
{
	// Связывает предмет инвентаря с уже существующими "боевыми" данными оружия (WeaponData), которые
	// использует WeaponController. Статы оружия не дублируются в инвентаре — здесь только ссылка.
	// Вешается на worldPrefab оружия вместе с WorldItem — единый путь подбора для любого предмета,
	// включая оружие. См. InventoryWeaponEquipHandler — он превращает подбор такого предмета в эквип.
	[CreateAssetMenu(fileName = "NewWeaponItemData", menuName = "Inventory/Weapon Item Data")]
	public class WeaponItemData : ItemData
	{
		public WeaponData weaponData;

		// оружие в инвентаре всегда типа Weapon — незачем давать выставить это вручную и промахнуться
		private void OnValidate()
		{
			type = ItemType.Weapon;
			maxStack = 1;
		}
	}
}
