using UnityEngine;
using Weapons;

namespace Inventory
{
	// Конкретный предмет у игрока, в ящике или в слоте колеса: что это (ItemData — общий ассет, не меняется),
	// сколько штук в стопке и собственное состояние экземпляра. Сейчас состояние есть только у оружия — магазин
	// (WeaponState): пистолет помнит свои патроны, где бы он ни лежал. Сюда же потом ляжет износ, заряд и т.п.
	//
	// Где лежит предмет (клетка сетки, слот колеса), хранит не он, а то место, куда его положили (ItemGrid,
	// WeaponLoadout): один и тот же экземпляр переносится между ними без копирования.
	public sealed class ItemInstance
	{
		public ItemData Data { get; }
		public int Count { get; private set; }
		// состояние оружия; null — предмет не оружие (или у WeaponItemData не задан Weapon Data)
		public WeaponState Weapon { get; }

		// count — сколько штук в стопке (у предметов с состоянием, например оружия, всегда 1).
		// loadedAmmo — только для оружия: патронов в магазине, < 0 — полный
		public ItemInstance(ItemData data, int count = 1, int loadedAmmo = -1)
		{
			Data = data;
			if (data is WeaponItemData weapon && weapon.weaponData != null) Weapon = new WeaponState(weapon.weaponData, loadedAmmo);

			// экземпляр с собственным состоянием не может быть стопкой: у двух пистолетов разные магазины
			Count = HasState ? 1 : Mathf.Clamp(count, 1, MaxStack);
		}

		// у предмета есть собственное состояние — такие не складываются в стопку
		public bool HasState => Weapon != null;
		// maxStack <= 0 в ассете — без ограничения
		public int MaxStack => HasState ? 1 : (Data != null && Data.maxStack > 0 ? Data.maxStack : int.MaxValue);
		public bool IsStackable => MaxStack > 1;
		public int SpaceLeft => MaxStack - Count;
		public Vector2Int GridSize => Data != null ? Data.GridSize : Vector2Int.one;

		// можно ли долить other в эту стопку (место при этом не проверяется — см. SpaceLeft)
		public bool CanStackWith(ItemInstance other)
		{
			return other != null && other != this && other.Data == Data && IsStackable && other.IsStackable;
		}

		// количество меняют только хранилища (ItemGrid), снаружи — через их методы, чтобы они знали об изменении
		internal void SetCount(int count)
		{
			Count = Mathf.Clamp(count, 0, MaxStack);
		}
	}
}
