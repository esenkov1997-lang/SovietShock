using UnityEngine;

namespace Weapons
{
	// Всё, что нужно WeaponController, чтобы показать модель в руках: префаб рук с предметом, как его ставить
	// относительно камеры и чем раскачивать. Реализуют оружие (WeaponData) и предметы, которые на время
	// берутся в руки вместо оружия (аптечка — Inventory.HealingItemData, см. WeaponController.BeginItemAction).
	// Префабы устроены одинаково: риг рук FPHands, предмет на кости w_base, звуки/события — как у оружия
	public interface IViewModelSource
	{
		// имя ассета — для сообщений в консоль
		string name { get; }
		GameObject InHandPrefab { get; }
		bool AlignArmsToCameraBone { get; }
		Vector3 HandPosition { get; }
		Vector3 HandRotation { get; }
		WeaponAnimationProfileSO AnimationProfile { get; }
	}

	// Получатель действия с предметом в руках (WeaponController.BeginItemAction): узнаёт о ключевых моментах
	// клипа из Animation Event (ItemApplied, ItemUseFinished — см. WeaponAnimationEvents) и о прерывании
	public interface IItemActionHandler
	{
		// предмет применён (игла вошла). activeDuration — сколько секунд от этого момента до ItemUseFinished
		// по самому клипу (с учётом скорости состояния); 0 — события конца в клипе нет
		void OnItemApplied(float activeDuration);
		// клип дошёл до ItemUseFinished — дальше руки убирают предмет, оружие возвращается
		void OnItemFinished();
		// действие прервано (руки убраны: терминал, захват, смерть) — до ItemUseFinished
		void OnItemCancelled();
	}
}
