using UnityEngine;

namespace Weapons
{
	// Вешается на тот же объект, где Animator (обычно на InHandPrefab или его дочерний меш).
	// Методы отсюда вызываются напрямую Animation Event из клипа и транслируются в WeaponController,
	// который обычно находится выше по иерархии, на WeaponHolder.
	public class WeaponAnimationEvents : MonoBehaviour
	{
		private WeaponController _weaponController;

		private void Awake()
		{
			_weaponController = GetComponentInParent<WeaponController>();
			if (_weaponController == null)
			{
				Debug.LogWarning($"WeaponAnimationEvents на {name}: не нашёл WeaponController среди родителей — событие сработает, но будет проигнорировано", this);
			}
		}

		// повесь Animation Event с этим методом в КОНЕЦ клипа, после которого оружие снова можно применять:
		// Draw (достали), Reload (перезарядили) и удара Melee (взмах закончен). Пока событие не пришло,
		// WeaponController не даст начать следующее действие — таймер FallbackAnimationDuration лишь подстраховка
		public void WeaponReady()
		{
			if (_weaponController != null) _weaponController.NotifyWeaponReady();
		}

		// повесь Animation Event с этим методом на момент в клипе Reload, когда магазин уже вставлен
		public void ReloadComplete()
		{
			if (_weaponController != null) _weaponController.NotifyReloadComplete();
		}

		// повесь Animation Event с этим методом на кадр в клипе ПОПАДАНИЯ Melee-оружия, где оружие касается цели —
		// именно тут наносится урон. На клип промаха его ставить не нужно (там нечему наносить урон), а вот
		// WeaponReady в конце клипа промаха нужен обязательно, иначе следующий удар ждёт таймер-подстраховку
		public void MeleeHit()
		{
			if (_weaponController != null) _weaponController.NotifyMeleeHit();
		}
	}
}
