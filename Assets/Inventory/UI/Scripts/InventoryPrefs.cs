using UnityEngine;

namespace Inventory
{
	// Настройки инвентаря, которые помнятся между запусками игры. Пока хранятся в PlayerPrefs; когда появится общий
	// экран настроек со своим сохранением — переедут туда, а этот класс останется единственным местом, которое
	// придётся поменять
	public static class InventoryPrefs
	{
		private const string TooltipsKey = "Inventory.TooltipsEnabled";

		// подсказка о предмете при наведении (переключается действием ItemTooltip, по умолчанию Alt). По умолчанию включена
		public static bool TooltipsEnabled
		{
			get => PlayerPrefs.GetInt(TooltipsKey, 1) != 0;
			set
			{
				PlayerPrefs.SetInt(TooltipsKey, value ? 1 : 0);
				PlayerPrefs.Save();
			}
		}
	}
}
