using UnityEngine;
using UnityEngine.Localization;
using Weapons;

namespace Interactables
{
	// Кнопка: переключает дверь (открывает/закрывает по очереди) по нажатию E через существующую систему
	// интеракции (см. IInteractable, PlayerInteractor) — но только пока сама кнопка полностью исправна
	// (Repairable.IsActive). Сломанную кнопку (см. Repairable.OnBroken) сначала нужно починить оружием
	// с WeaponData.CanRepair — до этого нажатие E ничего не делает, в каком бы состоянии ни была дверь.
	[RequireComponent(typeof(Repairable))]
	public class DoorButton : MonoBehaviour, IInteractable
	{
		[SerializeField] private SlidingDoor door;
		// ключи по умолчанию — из общей таблицы UI; у конкретной кнопки можно выбрать другие в инспекторе
		[Tooltip("Подсказка, когда кнопка исправна и дверь закрыта")]
		[SerializeField] private LocalizedString openPrompt = new LocalizedString("UI", "prompt.door.open");
		[Tooltip("Подсказка, когда кнопка исправна и дверь открыта")]
		[SerializeField] private LocalizedString closePrompt = new LocalizedString("UI", "prompt.door.close");
		[Tooltip("Подсказка, когда кнопка повреждена и не реагирует на нажатие")]
		[SerializeField] private LocalizedString brokenPrompt = new LocalizedString("UI", "prompt.door.broken");

		private Repairable _repairable;

		public LocalizedString InteractionPrompt
		{
			get
			{
				if (!_repairable.IsActive) return brokenPrompt;
				return door.IsOpen ? closePrompt : openPrompt;
			}
		}

		private void Awake()
		{
			_repairable = GetComponent<Repairable>();
		}

		public void Interact(GameObject interactor)
		{
			if (!_repairable.IsActive) return;

			if (door.IsOpen) door.Close();
			else door.Open();
		}
	}
}
