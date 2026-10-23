using UnityEngine;

namespace Weapons
{
	// Холодное оружие: бесконечное комбо ударов (1-2-...-N-1-2...), урон по Animation Event MeleeHit, без патронов и пули.
	//
	// Попадание решается не в начале взмаха, а в КАДРЕ КОНТАКТА — по тому, куда игрок смотрит именно в этот момент:
	//  • взмах (AttackTrigger) — один клип на удар: замах → контакт → рука пролетает дальше → возврат. Это и есть промах;
	//  • в кадре контакта (событие MeleeHit) проверяется попадание. Попал — урон и сразу клип удара (ImpactTrigger),
	//    который начинается ровно с позы кадра контакта: остановка, дрожание, возврат.
	//    Промахнулся — взмах просто играет дальше.
	// Тайминг задают события в клипах: MeleeHit (контакт, во взмахе), ComboWindow (с этого кадра можно переходить в
	// следующий удар — во взмахе и в клипе удара), WeaponReady (конец обоих клипов — комбо сбрасывается).
	// Номер удара в комбо (ComboStepParam) выставляется перед взмахом и действует и для клипа удара.
	// Опционально — ремонтный инструмент по ПКМ (Repair), тогда у оружия появляется баллон (Repair.Tank)
	[CreateAssetMenu(fileName = "NewMeleeWeapon", menuName = "Weapons/Melee Weapon")]
	public class MeleeWeaponData : WeaponData
	{
		[Header("Melee")]
		[Tooltip("Радиус сферы, которой проверяется попадание (SphereCast от камеры на Range). Делает удар снисходительнее к тонким целям и краям пропов. 0 — обычный тонкий луч")]
		public float HitRadius = 0.15f;
		[Tooltip("Trigger-параметр клипа удара (попадания) — включается в кадре контакта, если удар попал. Клип должен начинаться с позы кадра контакта во взмахе. Пусто — при попадании взмах просто играет дальше")]
		public string ImpactTrigger = "Impact";

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
