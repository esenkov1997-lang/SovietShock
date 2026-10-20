using UnityEngine;

namespace Weapons
{
	// Общая база данных оружия — только то, что есть у любого оружия. Конкретные ассеты создаются
	// из наследников: FirearmData (огнестрел) и MeleeWeaponData (холодное, в т.ч. ремонтный инструмент),
	// так что у холодного оружия в инспекторе нет полей огнестрела, и наоборот. Звуки — не здесь,
	// а в компоненте WeaponAudio на InHandPrefab.
	//
	// Чистые данные: не хранит рантайм-состояние (например, текущий боезапас), чтобы один и тот же ассет
	// не расшаривал состояние между разными игроками/подборами.
	public abstract class WeaponData : ScriptableObject
	{
		[Header("Identity")]
		public string WeaponId;
		public string WeaponName;

		[Header("Stats")]
		public int Damage = 10;
		[Tooltip("Максимальная дистанция поражения: для холодного — длина удара (и дальность горелки), для огнестрела — дальность хитскана")]
		public float Range = 100f;
		[Tooltip("Выстрелов (или ударов) в секунду")]
		public float FireRate = 5f;
		[Tooltip("Физический толчок при попадании (импульс) по объекту с Rigidbody — по направлению взгляда, в точку попадания. Зависит от массы цели: при Mass = 1 значения 2–5 дают заметный отлёт. 0 — без толчка")]
		public float HitForce = 3f;

		[Header("Animation")]
		[Tooltip("Имя Trigger-параметра в Animator Controller модели (InHandPrefab), включается в момент удара/выстрела. У холодного — анимация удара с попаданием")]
		public string AttackTrigger = "Attack";
		[Tooltip("Имя Trigger-параметра, включаемого при доставании оружия. Пусто — без анимации")]
		public string DrawTrigger = "Draw";

		[Header("VFX")]
		public VFXData VFX = new VFXData();

		[Header("Prefabs")]
		[Tooltip("Модель оружия в руках игрока (спавнится внутри WeaponHolder). Звуки оружия — компонент WeaponAudio на ней")]
		public GameObject InHandPrefab;
		[Tooltip("Объект оружия, лежащий на земле (спавнится при выбрасывании)")]
		public GameObject PickupPrefab;

		[Header("Hand Placement")]
		[Tooltip("Позиция по умолчанию, если на конкретном WeaponHolder (WeaponController.handPlacementOverrides) для этого оружия нет своего оверрайда")]
		public Vector3 HandPosition;
		[Tooltip("Поворот по умолчанию, если на конкретном WeaponHolder (WeaponController.handPlacementOverrides) для этого оружия нет своего оверрайда (в градусах)")]
		public Vector3 HandRotation;

		[Header("Animation Profile")]
		[Tooltip("Sway/Bobbing/Impact Spring для этого оружия — вынесены в отдельный переиспользуемый ассет (WeaponAnimationProfileSO), см. ProceduralWeaponAnimation. Один профиль можно назначить нескольким похожим по весу пушкам")]
		public WeaponAnimationProfileSO AnimationProfile;

		// Магазин оружия: у огнестрела — патроны, у ремонтного инструмента — баллон. null — оружию нечего
		// заряжать и перезаряжать (обычное холодное оружие). По нему работают WeaponController (боезапас,
		// перезарядка) и Inventory.InventoryReloadHandler (тип патронов в инвентаре)
		public abstract AmmoData Ammo { get; }
	}
}
