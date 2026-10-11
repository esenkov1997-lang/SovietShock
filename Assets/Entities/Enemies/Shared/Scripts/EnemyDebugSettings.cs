using UnityEngine;

namespace Enemies
{
	// Глобальные отладочные переключатели для всех врагов (меняются из меню паузы → Отладка, см. Menus.EnemyDebugMenu).
	// Статические, потому что касаются всех врагов сразу, включая заспавненных позже.
	public static class EnemyDebugSettings
	{
		// "notarget": враги не видят и не слышат игрока (шум от брошенных предметов слышат — отвлечение можно тестировать),
		// попадания тоже не поднимают тревогу. Уже встревоженные враги забывают игрока и возвращаются к спокойному поведению
		public static bool PlayerIgnored { get; set; }

		// враги ведут себя как обычно (замечают, гонятся, кружат, прячутся), но не кусают
		public static bool AttacksDisabled { get; set; }

		// сброс при каждом запуске Play Mode (на случай отключённого Domain Reload)
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			PlayerIgnored = false;
			AttacksDisabled = false;
		}
	}
}
