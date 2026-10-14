using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;

namespace Interactables
{
	// Универсальная кнопка: по E (через обычную систему интеракции — подсказка, подсветка) вызывает событие
	// On Pressed, к которому в инспекторе подключается что угодно: заспавнить врага (EnemySpawnPoint.Spawn),
	// включить свет, вызвать лифт, поднять тревогу. Без нового кода под каждую задачу.
	// Для дверей с починкой и подсказкой "открыть/закрыть" по состоянию — отдельный DoorButton.
	public class EventButton : MonoBehaviour, IInteractable
	{
		[Tooltip("Подсказка при наведении (ключ из таблицы локализации)")]
		[SerializeField] private LocalizedString prompt = new LocalizedString("UI", "prompt.button.press");
		[Tooltip("Что происходит при нажатии")]
		[SerializeField] private UnityEvent onPressed;
		[Tooltip("Минимальная пауза (сек) между нажатиями — защита от двойного срабатывания")]
		[SerializeField] private float cooldown = 0.3f;
		[Tooltip("Сработать только один раз — после этого кнопка перестаёт реагировать")]
		[SerializeField] private bool singleUse;

		private float _nextAllowedTime;
		private bool _used;

		public LocalizedString InteractionPrompt => prompt;

		// использованная одноразовая кнопка больше не подсвечивается и не показывает подсказку
		public bool CanInteractFrom(Vector3 viewerPosition) => !(singleUse && _used);

		public void Interact(GameObject interactor)
		{
			if (singleUse && _used) return;
			if (Time.time < _nextAllowedTime) return;

			_nextAllowedTime = Time.time + cooldown;
			_used = true;
			onPressed?.Invoke();
		}
	}
}
