using UnityEngine;

namespace Weapons
{
	public enum FireMode
	{
		Single, // один выстрел за нажатие, следующий — только после отпускания и нового нажатия
		Auto,   // очередь, пока зажата кнопка, с темпом FireRate
		Melee   // ближний бой: без патронов и без пули, просто удар
	}

	// Чистые данные оружия. Не хранит рантайм-состояние (например, текущий боезапас),
	// чтобы один и тот же ассет не расшаривал состояние между разными игроками/подборами.
	[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Weapons/Weapon Data")]
	public class WeaponData : ScriptableObject
	{
		[Header("Identity")]
		public string WeaponId;
		public string WeaponName;

		[Header("Stats")]
		public int Damage = 10;
		public int MagazineSize = 30;
		[Tooltip("Максимальная дистанция поражения: для Melee — длина удара, для остального оружия — дальность хитскана")]
		public float Range = 100f;
		[Tooltip("Только Melee: радиус сферы, которой проверяется попадание (SphereCast от камеры на Range). Делает удар снисходительнее к тонким целям и краям пропов. 0 — обычный тонкий луч. Стрелковое оружие всегда использует тонкий луч и это поле игнорирует")]
		public float MeleeHitRadius = 0.15f;

		[Tooltip("Тип патронов/газа для перезарядки — должен совпадать с Inventory.AmmoItemData.ammoId. Пусто = перезарядка не тратит инвентарь (старое поведение)")]
		public string AmmoId;

		[Header("Fire Mode")]
		public FireMode Mode = FireMode.Single;
		[Tooltip("Выстрелов (или ударов) в секунду")]
		public float FireRate = 5f;

		[Header("Animation")]
		[Tooltip("Имя Trigger-параметра в Animator Controller модели (InHandPrefab), включается в момент удара/выстрела. У Melee — анимация удара, когда в зоне поражения есть цель (попадание)")]
		public string AttackTrigger = "Attack";
		[Tooltip("Только Melee: Trigger-параметр анимации удара, когда перед оружием ничего нет (промах). Пусто — промаха как отдельной анимации нет, всегда играется AttackTrigger. Параметр должен существовать в Animator Controller, иначе Unity будет сыпать ошибку на каждом ударе")]
		public string AttackMissTrigger = "";
		[Tooltip("Имя Trigger-параметра, включаемого при доставании оружия. Можно оставить пустым, если анимации доставания нет")]
		public string DrawTrigger = "Draw";
		[Tooltip("Имя Trigger-параметра, включаемого при начале перезарядки. Не используется для Melee")]
		public string ReloadTrigger = "Reload";

		[Header("Recoil")]
		[Tooltip("Подброс прицела вверх за один выстрел, градусы")]
		public float RecoilKick = 1.5f;
		[Tooltip("Прирост увода в сторону (Y) за каждый выстрел внутри одной очереди, градусы")]
		public float RecoilDriftPerShot = 0.5f;
		[Tooltip("Максимальный накопленный увод по Y за одну очередь, градусы")]
		public float MaxDrift = 4f;
		[Tooltip("Скорость возврата увода к нулю после окончания очереди, градусы/сек")]
		public float DriftRecoverySpeed = 10f;

		[Header("Aim Recoil (ПКМ)")]
		[Tooltip("То же самое, но при стрельбе через прицел — обычно меньше, чем при стрельбе от бедра")]
		public float AimRecoilKick = 0.8f;
		public float AimRecoilDriftPerShot = 0.25f;
		public float AimMaxDrift = 2f;
		public float AimDriftRecoverySpeed = 10f;

		[Header("Aim Down Sights (ПКМ)")]
		[Tooltip("Не используется для Melee")]
		public Vector3 AimPosition;
		public Vector3 AimRotation;
		[Tooltip("Скорость перехода в прицел и обратно")]
		public float AimTransitionSpeed = 8f;
		[Tooltip("FOV камеры в прицеле — см. WeaponAimZoom. Не используется для Melee")]
		public float AimFOV = 40f;

		[Header("Repair / Burn (ПКМ, только для Melee)")]
		[Tooltip("Включает удержание ПКМ как луч ремонта/прожига для этого Melee-оружия (например, ключ-горелка). Обычный ADS по ПКМ на Melee и так не действует, это отдельная механика")]
		public bool CanRepair;
		[Tooltip("Прочности в секунду объектам с IRepairable (например, кнопке-триггеру), пока наведён на них и зажата ПКМ")]
		public float RepairAmountPerSecond = 20f;
		[Tooltip("Урона в секунду целям с IDamageable (например, врагам), если навести ПКМ на них вместо ремонтируемого объекта")]
		public float RepairDamagePerSecond = 5f;
		[Tooltip("Имя Bool-параметра в Animator Controller модели — включается, пока зажата ПКМ и идёт ремонт/прожиг, выключается при отпускании. Можно оставить пустым, если анимации нет")]
		public string HealBoolParam = "Healing";

		[Header("Audio")]
		[Tooltip("Звук самого выстрела или взмаха (у Melee играется в начале удара, независимо от попадания). Звук попадания по поверхности задаётся не здесь, а в SurfaceSoundSet цели")]
		public AudioClip AttackSound;
		[Tooltip("Разброс питча звука за выстрел, чтобы очередь не звучала одинаково-механически")]
		public Vector2 AttackPitchRange = new Vector2(0.95f, 1.05f);

		[Header("Decals")]
		[Tooltip("Префаб декали (след от пули/удара), спавнится в точке попадания хитскана")]
		public GameObject DecalPrefab;
		[Tooltip("Через сколько секунд декаль будет уничтожена")]
		public float DecalLifetime = 15f;

		[Header("Bullet Visualization")]
		[Tooltip("Префаб визуализации пули. Не используется для Melee")]
		public GameObject BulletPrefab;
		[Tooltip("Скорость пули, м/с")]
		public float BulletSpeed = 40f;
		[Tooltip("Через сколько секунд пуля будет уничтожена")]
		public float BulletLifetime = 3f;

		[Header("Prefabs")]
		[Tooltip("Модель оружия в руках игрока (спавнится внутри WeaponHolder)")]
		public GameObject InHandPrefab;
		[Tooltip("Объект оружия, лежащий на земле (спавнится при выбрасывании)")]
		public GameObject PickupPrefab;

		[Header("Hand Placement")]
		[Tooltip("Позиция по умолчанию, если на конкретном WeaponHolder (WeaponController.handPlacementOverrides) для этого оружия нет своего оверрайда")]
		public Vector3 HandPosition;
		[Tooltip("Поворот по умолчанию, если на конкретном WeaponHolder (WeaponController.handPlacementOverrides) для этого оружия нет своего оверрайда (в градусах)")]
		public Vector3 HandRotation;

		[Header("Animation Profile")]
		[Tooltip("Sway/Bobbing/Impact Spring для этого оружия — вынесены в отдельный переиспользуемый ассет (WeaponAnimationProfileSO), см. Weapons/Scripts/ProceduralWeaponAnimation. Один профиль можно назначить нескольким похожим по весу пушкам")]
		public WeaponAnimationProfileSO AnimationProfile;
	}
}
