namespace Weapons
{
	// Реализуй на любом объекте (или его родителе — WeaponController ищет через GetComponentInParent),
	// который можно чинить оружием с WeaponData.CanRepair (например, кнопка-триггер). Отдельно от
	// IDamageable, потому что чинить и наносить урон — разные объекты и разная реакция на попадание.
	public interface IRepairable
	{
		void Repair(int amount);
	}
}
