using System;

namespace Weapons
{
	// Набор слотов оружия игрока ("колесо избранного"): что из оружия доступно в руках и под какими номерами.
	// WeaponController сам оружие не хранит — он показывает то, что лежит в слотах, и следит за их изменениями.
	// Реализация — Inventory.WeaponLoadout (WeaponController намеренно ничего не знает про Inventory).
	public interface IWeaponSlots
	{
		int SlotCount { get; }
		// null — слот пуст
		WeaponState GetSlot(int index);
		// после ЛЮБОЙ завершённой операции со слотами (положили, убрали, поменяли местами) — один раз, когда
		// всё уже на своих местах: промежуточных состояний ("оружие на секунду пропало") подписчики не видят
		event Action SlotsChanged;
	}
}
