using Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace Menus
{
	// Отладочные переключатели врагов для страницы отладки меню паузы. Вешается на DebugPage (или любой объект меню).
	// Переключатели (UI Toggle) подключаются в инспекторе: On Value Changed → этот компонент → метод из раздела
	// "Dynamic bool" (SetPlayerIgnored / SetAttacksDisabled). Ссылки на сами Toggle нужны, чтобы при открытии
	// страницы галочки показывали текущее состояние, а не то, что было сохранено в префабе.
	public class EnemyDebugMenu : MonoBehaviour
	{
		[Tooltip("Переключатель \"Враги не замечают игрока\"")]
		[SerializeField] private Toggle playerIgnoredToggle;
		[Tooltip("Переключатель \"Враги не атакуют\"")]
		[SerializeField] private Toggle attacksDisabledToggle;

		private void OnEnable()
		{
			// без вызова On Value Changed — только показать текущее состояние
			if (playerIgnoredToggle != null) playerIgnoredToggle.SetIsOnWithoutNotify(EnemyDebugSettings.PlayerIgnored);
			if (attacksDisabledToggle != null) attacksDisabledToggle.SetIsOnWithoutNotify(EnemyDebugSettings.AttacksDisabled);
		}

		public void SetPlayerIgnored(bool ignored)
		{
			EnemyDebugSettings.PlayerIgnored = ignored;
		}

		public void SetAttacksDisabled(bool disabled)
		{
			EnemyDebugSettings.AttacksDisabled = disabled;
		}
	}
}
