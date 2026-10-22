using UnityEngine;

namespace Weapons
{
	// Холодное оружие: бесконечное комбо ударов (1-2-...-N-1-2...), у каждого удара — вариант попадания и промаха,
	// урон по Animation Event MeleeHit, без патронов и пули.
	// Тайминг задают события в клипах удара: MeleeHit (касание, только в клипах попадания), ComboWindow (с этого
	// кадра можно переходить в следующий удар), WeaponReady (конец клипа — комбо сбрасывается). Код только помнит
	// нажатие (буфер) и выставляет в Animator номер удара (ComboStepParam) + триггер AttackTrigger/AttackMissTrigger.
	// Опционально — ремонтный инструмент по ПКМ (Repair), тогда у оружия появляется баллон (Repair.Tank)
	[CreateAssetMenu(fileName = "NewMeleeWeapon", menuName = "Weapons/Melee Weapon")]
	public class MeleeWeaponData : WeaponData
	{
		[Header("Melee")]
		[Tooltip("Радиус сферы, которой проверяется попадание (SphereCast от камеры на Range). Делает удар снисходительнее к тонким целям и краям пропов. 0 — обычный тонкий луч")]
		public float HitRadius = 0.15f;
		[Tooltip("Trigger-параметр анимации удара, когда перед оружием ничего нет (промах). Пусто — всегда играется AttackTrigger. Номер удара в комбо (ComboStepParam) выставляется для обоих вариантов. Параметр должен существовать в Animator Controller, иначе Unity будет сыпать ошибку на каждом ударе")]
		public string AttackMissTrigger = "";

		[Header("Combo")]
		[Tooltip("Сколько разных ударов в комбо. После последнего снова идёт первый")]
		[Min(1)]
		public int ComboLength = 2;
		[Tooltip("Int-параметр в Animator Controller с номером удара: 0 — первый, 1 — второй... Выставляется перед триггером AttackTrigger. Пусто — без комбо, всегда один и тот же удар")]
		public string ComboStepParam = "ComboStep";
		[Tooltip("Сколько секунд помнится нажатие атаки. Нажал чуть раньше, чем открылось окно комбо (ComboWindow), — следующий удар всё равно выйдет, как только окно откроется")]
		public float InputBufferTime = 0.25f;

		[Header("Repair / Burn (ПКМ)")]
		public RepairToolData Repair = new RepairToolData();

		public bool CanRepair => Repair.CanRepair;

		public override AmmoData Ammo => Repair.CanRepair ? Repair.Tank : null;
	}
}
