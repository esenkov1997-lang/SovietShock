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

		// повесь Animation Event с этим методом на ПОСЛЕДНИЙ кадр клипа Hide (оружие убрано из кадра) — в этот
		// момент старая модель удаляется и появляется новое оружие с Draw. Без события смена ждёт таймер-подстраховку
		public void WeaponHidden()
		{
			if (_weaponController != null) _weaponController.NotifyWeaponHidden();
		}

		// Метки в клипе подъёма на уступ (MantleGrab / MantleRelease) больше не используются кодом. Пустые методы
		// оставлены, чтобы Unity не ругался, если такие события остались в клипе — их можно удалить из клипа
		// вместе с этими методами
		public void MantleGrab() { }
		public void MantleRelease() { }

		// повесь Animation Event с этим методом на момент в клипе Reload, когда магазин уже вставлен
		public void ReloadComplete()
		{
			if (_weaponController != null) _weaponController.NotifyReloadComplete();
		}

		// повесь Animation Event с этим методом на КАДР КОНТАКТА во взмахе Melee-оружия (клип, который играется по
		// AttackTrigger) — здесь проверяется попадание: попал — урон и клип удара (ImpactTrigger), который должен
		// начинаться с позы этого кадра; мимо — взмах играет дальше. В клип удара MeleeHit ставить не нужно
		public void MeleeHit()
		{
			if (_weaponController != null) _weaponController.NotifyMeleeHit();
		}

		// повесь Animation Event с этим методом на кадр в КАЖДОМ клипе удара (и попадания, и промаха), начиная
		// с которого можно перейти в следующий удар комбо — обычно сразу после касания. Если игрок жмёт атаку
		// раньше, нажатие запоминается (MeleeWeaponData.InputBufferTime) и удар выйдет ровно в этом кадре.
		// Без события комбо не работает: следующий удар ждёт WeaponReady в конце клипа
		public void ComboWindow()
		{
			if (_weaponController != null) _weaponController.NotifyComboWindow();
		}

		// повесь Animation Event с этим методом на кадр в клипе Repair_Start, где горелка зажигается —
		// с этого момента появляются пламя и свет, тратится газ и идёт ремонт/прожиг
		public void RepairStart()
		{
			if (_weaponController != null) _weaponController.NotifyRepairStart();
		}

		// --- предмет в руках (аптечка, WeaponController.BeginItemAction). В конце клипа предмета, когда он убран
		// из кадра, ставится WeaponHidden — по нему в руки возвращается оружие ---

		// повесь Animation Event с этим методом на кадр, где предмет применён (игла вошла): с него начинается действие
		// (лечение). Параметр заполняет сам Unity — по нему находится ItemUseFinished в том же клипе
		public void ItemApplied(AnimationEvent animationEvent)
		{
			if (_weaponController != null) _weaponController.NotifyItemApplied(animationEvent);
		}

		// повесь Animation Event с этим методом на кадр, где действие заканчивается (лечение набрано полностью).
		// Может быть раньше конца клипа — дальше руки убирают предмет
		public void ItemUseFinished()
		{
			if (_weaponController != null) _weaponController.NotifyItemUseFinished();
		}
	}
}
