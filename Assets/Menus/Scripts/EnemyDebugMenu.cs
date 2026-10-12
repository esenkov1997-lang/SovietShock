using Enemies;
using UnityEngine;
using UnityEngine.UI;
using Weapons;

namespace Menus
{
	// Отладочные переключатели (враги и бессмертие игрока) для страницы отладки меню паузы. Вешается на DebugPage (или любой объект меню).
	// Переключатели (UI Toggle) подключаются в инспекторе: On Value Changed → этот компонент → метод из раздела
	// "Dynamic bool" (SetPlayerIgnored / SetAttacksDisabled / SetPlayerInvulnerable). Ссылки на сами Toggle нужны, чтобы при открытии
	// страницы галочки показывали текущее состояние, а не то, что было сохранено в префабе.
	public class EnemyDebugMenu : MonoBehaviour
	{
		[Tooltip("Переключатель \"Враги не замечают игрока\"")]
		[SerializeField] private Toggle playerIgnoredToggle;
		[Tooltip("Переключатель \"Враги не атакуют\"")]
		[SerializeField] private Toggle attacksDisabledToggle;
		[Tooltip("Переключатель \"Бессмертие игрока\"")]
		[SerializeField] private Toggle playerInvulnerableToggle;

		private void OnEnable()
		{
			// без вызова On Value Changed — только показать текущее состояние
			if (playerIgnoredToggle != null) playerIgnoredToggle.SetIsOnWithoutNotify(EnemyDebugSettings.PlayerIgnored);
			if (attacksDisabledToggle != null) attacksDisabledToggle.SetIsOnWithoutNotify(EnemyDebugSettings.AttacksDisabled);
			if (playerInvulnerableToggle != null)
			{
				Health health = GetPlayerHealth();
				playerInvulnerableToggle.SetIsOnWithoutNotify(health != null && health.Invulnerable);
			}
		}

		public void SetPlayerIgnored(bool ignored)
		{
			EnemyDebugSettings.PlayerIgnored = ignored;
		}

		public void SetAttacksDisabled(bool disabled)
		{
			EnemyDebugSettings.AttacksDisabled = disabled;
		}

		// враги нападают и кусают как обычно (захваты, анимации — всё играет), но урон игроку не проходит
		public void SetPlayerInvulnerable(bool invulnerable)
		{
			Health health = GetPlayerHealth();
			if (health == null)
			{
				Debug.LogWarning("EnemyDebugMenu: у игрока нет Health — бессмертие не включить", this);
				return;
			}
			health.Invulnerable = invulnerable;
		}

		private static Health GetPlayerHealth()
		{
			Transform player = PlayerLocator.Player;
			return player != null ? player.GetComponent<Health>() : null;
		}
	}
}
