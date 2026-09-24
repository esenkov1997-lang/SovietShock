namespace Weapons
{
	// Реализуй на любом объекте (или его родителе — WeaponController ищет через GetComponentInParent),
	// который должен получать урон от оружия. Система оружия ничего не знает о конкретной реализации
	// здоровья — только об этом контракте.
	public interface IDamageable
	{
		void TakeDamage(int amount);
	}
}
