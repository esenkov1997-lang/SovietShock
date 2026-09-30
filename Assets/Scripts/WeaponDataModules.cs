using UnityEngine;

namespace Weapons
{
	// Модули настроек оружия — [System.Serializable]-классы, из которых собираются FirearmData и MeleeWeaponData.
	// В инспекторе каждый модуль — отдельный сворачиваемый блок, и у оружия есть только те блоки, что ему нужны:
	// у холодного нет отдачи/прицела/пули, у огнестрела — ремонта.

	// Магазин и перезарядка. У огнестрела — патроны, у ремонтного инструмента — баллон газа (тот же механизм)
	[System.Serializable]
	public class AmmoData
	{
		[Tooltip("Ёмкость магазина/баллона")]
		public int MagazineSize = 30;
		[Tooltip("Тип патронов/газа для перезарядки — должен совпадать с Inventory.AmmoItemData.ammoId. Пусто = перезарядка не тратит инвентарь (бесконечные патроны)")]
		public string AmmoId;
		[Tooltip("Имя Trigger-параметра в Animator Controller, включаемого при начале перезарядки. Пусто — без анимации")]
		public string ReloadTrigger = "Reload";
	}

	// Отдача за один режим стрельбы (от бедра или в прицеле)
	[System.Serializable]
	public class RecoilSettings
	{
		[Tooltip("Подброс прицела вверх за один выстрел, градусы")]
		public float Kick = 1.5f;
		[Tooltip("Прирост увода в сторону (Y) за каждый выстрел внутри одной очереди, градусы")]
		public float DriftPerShot = 0.5f;
		[Tooltip("Максимальный накопленный увод по Y за одну очередь, градусы")]
		public float MaxDrift = 4f;
		[Tooltip("Скорость возврата увода к нулю после окончания очереди, градусы/сек")]
		public float DriftRecoverySpeed = 10f;
	}

	[System.Serializable]
	public class RecoilData
	{
		[Tooltip("Отдача при стрельбе от бедра")]
		public RecoilSettings Hip = new RecoilSettings();
		[Tooltip("Отдача при стрельбе через прицел (ПКМ) — обычно меньше, чем от бедра")]
		public RecoilSettings Aim = new RecoilSettings { Kick = 0.8f, DriftPerShot = 0.25f, MaxDrift = 2f };
	}

	// Прицеливание (ПКМ): поза оружия в прицеле и FOV камеры
	[System.Serializable]
	public class AimData
	{
		[Tooltip("Позиция VisualRoot в прицеле (от бедра — HandPosition)")]
		public Vector3 Position;
		[Tooltip("Поворот VisualRoot в прицеле, градусы")]
		public Vector3 Rotation;
		[Tooltip("Скорость перехода в прицел и обратно")]
		public float TransitionSpeed = 8f;
		[Tooltip("FOV камеры в прицеле — см. WeaponAimZoom")]
		public float FOV = 40f;
	}

	// Визуальные эффекты попадания — общие для любого оружия
	[System.Serializable]
	public class VFXData
	{
		[Tooltip("Префаб декали (след от пули/удара) по умолчанию — спавнится в точке попадания, если у цели нет своего ImpactSurface")]
		public GameObject DecalPrefab;
		[Tooltip("Через сколько секунд декаль будет уничтожена")]
		public float DecalLifetime = 15f;
	}

	// Декоративная пуля огнестрела — на попадание не влияет, урон считается хитсканом
	[System.Serializable]
	public class BulletVisualData
	{
		[Tooltip("Префаб визуализации пули. Пусто — пуля не рисуется")]
		public GameObject Prefab;
		[Tooltip("Скорость пули, м/с")]
		public float Speed = 40f;
		[Tooltip("Через сколько секунд пуля будет уничтожена")]
		public float Lifetime = 3f;
	}

	// Ремонт/прожиг по удержанию ПКМ (например, ключ-горелка). Звуки горелки — в компоненте WeaponAudio на префабе в руках
	[System.Serializable]
	public class RepairToolData
	{
		[Tooltip("Включает удержание ПКМ как луч ремонта/прожига для этого оружия")]
		public bool CanRepair;
		[Tooltip("Баллон: ёмкость, тип газа для перезарядки из инвентаря и триггер анимации перезарядки")]
		public AmmoData Tank = new AmmoData { MagazineSize = 100 };
		[Tooltip("Прочности в секунду объектам с IRepairable (например, кнопке-триггеру), пока наведён на них и горит горелка")]
		public float RepairPerSecond = 20f;
		[Tooltip("Расход газа в секунду, пока горит горелка — независимо от того, попадаешь ли во что-то. Меньше — баллона хватает дольше")]
		public float AmmoPerSecond = 5f;
		[Tooltip("Урона в секунду целям с IDamageable (например, врагам), если навести горелку на них вместо ремонтируемого объекта")]
		public float DamagePerSecond = 5f;
		[Tooltip("Имя Bool-параметра в Animator Controller — включён, пока зажата ПКМ и идёт ремонт/прожиг. Пусто — без анимации")]
		public string HealBoolParam = "Healing";
		[Tooltip("Префаб зацикленной системы частиц (искры), которая горит в точке, где луч горелки касается объекта, пока он чинится " +
			"(IRepairable, ещё не полностью починен) и до него хватает Range. Ось Z префаба смотрит по нормали поверхности (от неё). Simulation Space — World, " +
			"чтобы искры не ехали за точкой при движении. Пусто — без искр")]
		public GameObject SparksPrefab;
		[Tooltip("Префаб зацикленного дыма — вместо искр, когда луч горелки попадает во что-то, что не чинится (стена, враг), " +
			"или в уже полностью починенный объект. Настраивается так же, как Sparks Prefab. Пусто — в этих случаях без эффекта")]
		public GameObject SmokePrefab;
	}
}
