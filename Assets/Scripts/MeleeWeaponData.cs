using UnityEngine;

namespace Weapons
{
	// Холодное оружие: удар с попаданием/промахом по Animation Event MeleeHit, без патронов и пули.
	// Опционально — ремонтный инструмент по ПКМ (Repair), тогда у оружия появляется баллон (Repair.Tank)
	[CreateAssetMenu(fileName = "NewMeleeWeapon", menuName = "Weapons/Melee Weapon")]
	public class MeleeWeaponData : WeaponData
	{
		[Header("Melee")]
		[Tooltip("Радиус сферы, которой проверяется попадание (SphereCast от камеры на Range). Делает удар снисходительнее к тонким целям и краям пропов. 0 — обычный тонкий луч")]
		public float HitRadius = 0.15f;
		[Tooltip("Trigger-параметр анимации удара, когда перед оружием ничего нет (промах). Пусто — всегда играется AttackTrigger. Параметр должен существовать в Animator Controller, иначе Unity будет сыпать ошибку на каждом ударе")]
		public string AttackMissTrigger = "";

		[Header("Repair / Burn (ПКМ)")]
		public RepairToolData Repair = new RepairToolData();

		public bool CanRepair => Repair.CanRepair;

		public override AmmoData Ammo => Repair.CanRepair ? Repair.Tank : null;
	}
}
