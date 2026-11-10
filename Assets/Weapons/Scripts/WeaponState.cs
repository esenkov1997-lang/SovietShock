using UnityEngine;

namespace Weapons
{
	// Рантайм-состояние ОДНОГО экземпляра оружия: сколько патронов (газа) в магазине и когда можно стрелять снова.
	// Данные (WeaponData) — общий ScriptableObject и не мутируются, иначе два одинаковых пистолета делили бы
	// один счётчик патронов. Экземпляр живёт вместе с предметом (Inventory.ItemInstance): лежит пистолет в
	// сетке, в ящике или в слоте колеса — его магазин тот же
	public sealed class WeaponState
	{
		public WeaponData Data { get; }
		public int CurrentAmmo;
		public float NextFireTime;

		// loadedAmmo < 0 — полный магазин. Оружие без магазина (обычное холодное) — всегда 0
		public WeaponState(WeaponData data, int loadedAmmo = -1)
		{
			Data = data;
			int magazineSize = MagazineSize;
			CurrentAmmo = loadedAmmo < 0 ? magazineSize : Mathf.Min(loadedAmmo, magazineSize);
		}

		public int MagazineSize => Data != null && Data.Ammo != null ? Data.Ammo.MagazineSize : 0;
	}
}
