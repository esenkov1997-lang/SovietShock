using Interactables;
using UnityEngine;

namespace Triggers
{
	// Автоподбор без нажатия E — например, для патронов/аптечек, разложенных в зоне. Работает поверх
	// любого IInteractable на этом же объекте (WorldItem, оружие и т.п.) — тем же путём, которым
	// PlayerInteractor подбирает предметы по кнопке E, просто без ожидания нажатия.
	public class PickupTriggerAction : TriggerAction
	{
		private IInteractable _interactable;

		protected override void Awake()
		{
			base.Awake();
			_interactable = GetComponent<IInteractable>();
			if (_interactable == null)
			{
				Debug.LogError($"{nameof(PickupTriggerAction)} на {name}: не найден компонент IInteractable", this);
			}
		}

		protected override void Execute(Collider other)
		{
			_interactable?.Interact(other.gameObject);
		}
	}
}
