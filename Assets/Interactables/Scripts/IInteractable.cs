using UnityEngine;
using UnityEngine.Localization;

namespace Interactables
{
	// Контракт для любого объекта, с которым игрок может взаимодействовать по кнопке (см. Player.PlayerInteractor).
	// Реализуй на любом активируемом объекте — например, Inventory.WorldItem (любой предмет, включая
	// оружие, см. Inventory.WeaponItemData) — PlayerInteractor работает только через этот интерфейс
	// и не знает о конкретных типах.
	public interface IInteractable
	{
		// Текст всплывающей подсказки ("Подобрать: Аптечка") — ссылка на запись таблицы локализации, а не
		// готовая строка: PlayerInteractor сам привязывает её к тексту на экране (перевод подтянется
		// и обновится при смене языка) и добавляет префикс с кнопкой ("[F] " — клавиша из биндинга Interact, см. Player.InteractKey).
		// Можно возвращать разные строки в зависимости от состояния (DoorButton: открыть/закрыть) —
		// PlayerInteractor заметит смену и перепривяжет текст, даже если игрок продолжает смотреть на объект.
		LocalizedString InteractionPrompt { get; }

		// Вызывается PlayerInteractor по нажатию кнопки взаимодействия, пока луч наведён на этот объект.
		// interactor — корневой GameObject того, кто взаимодействует (обычно игрок), чтобы реализация
		// сама нашла нужные компоненты (InventoryHolder, WeaponController и т.п.) через GetComponentInParent.
		void Interact(GameObject interactor);
	}
}
