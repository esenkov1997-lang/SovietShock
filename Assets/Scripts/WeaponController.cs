using System.Collections.Generic;
using Sound;
using StarterAssets;
using UnityEngine;

namespace Weapons
{
	// Вешается на WeaponHolder — точку в руках игрока, где появляется модель текущего оружия.
	// Данные оружия — WeaponData и его наследники FirearmData/MeleeWeaponData, звуки — WeaponAudio на модели в руках.
	public class WeaponController : MonoBehaviour
	{
		// Позволяет переопределить HandPosition/HandRotation из WeaponData для конкретного WeaponHolder —
		// например, у разных персонажей/риг разная длина руки, и одно и то же оружие нужно держать по-разному.
		// Если для оружия нет записи здесь, используются значения по умолчанию из WeaponData.
		[System.Serializable]
		private class WeaponHandPlacementOverride
		{
			public WeaponData Weapon;
			public Vector3 HandPosition;
			public Vector3 HandRotation;
		}

		[Header("Starting Loadout")]
		[Tooltip("Оружие, которое уже есть у игрока при старте (удобно для тестов)")]
		[SerializeField] private List<WeaponData> startingWeapons = new List<WeaponData>();

		[Header("Hand Placement Overrides")]
		[Tooltip("Позиция/поворот в руках для конкретного оружия на этом WeaponHolder. Если оружия здесь нет — берутся HandPosition/HandRotation из его WeaponData")]
		[SerializeField] private List<WeaponHandPlacementOverride> handPlacementOverrides = new List<WeaponHandPlacementOverride>();

		private readonly Dictionary<WeaponData, WeaponHandPlacementOverride> _handPlacementByWeapon = new Dictionary<WeaponData, WeaponHandPlacementOverride>();

		[Header("Hit Sounds")]
		[Tooltip("Звуки попадания для поверхностей без компонента SoundSurface (стены и прочее окружение) — тот же принцип, что декаль по умолчанию в WeaponData. Пусто — по таким поверхностям попадание беззвучное")]
		[SerializeField] private SurfaceSoundSet defaultHitSurface;

		[Header("Drop Settings")]
		[Tooltip("Точка/направление выброса оружия. Если не задано — используется сам WeaponHolder")]
		[SerializeField] private Transform dropPoint;
		[SerializeField] private float dropForce = 4f;

		[Header("Rendering (Anti-Clip)")]
		[Tooltip("Слой, на который переносится вся модель оружия в руках (сама пушка + руки) при экипировке. Нужен отдельной камере оружия (см. WeaponAimZoom), которая рендерит только этот слой с крошечным near clip — так модель не клипается в стены без уменьшения. Оставь пустым, чтобы не менять слой")]
		[SerializeField] private string weaponLayerName = "FirstPersonWeapon";

		// Данные (WeaponData) + рантайм-состояние (боезапас, кулдаун стрельбы) разделены: сам ScriptableObject
		// не мутируем, иначе два игрока с одним и тем же WeaponData делили бы один счётчик патронов.
		private class WeaponSlot
		{
			public readonly WeaponData Data;
			public int CurrentAmmo;
			public float NextFireTime;

			// loadedAmmo < 0 — полный магазин. Оружие без магазина (обычное холодное) — всегда 0
			public WeaponSlot(WeaponData data, int loadedAmmo)
			{
				Data = data;
				int magazineSize = data.Ammo != null ? data.Ammo.MagazineSize : 0;
				CurrentAmmo = loadedAmmo < 0 ? magazineSize : Mathf.Min(loadedAmmo, magazineSize);
			}
		}

		// подстраховка на случай, если Animation Event (WeaponReady/ReloadComplete) забыли проставить в клипе —
		// не обязана быть точной, это просто "не застрять навсегда", а не основной механизм тайминга
		private const float FallbackAnimationDuration = 3f;

		private readonly List<WeaponSlot> _inventory = new List<WeaponSlot>();
		private int _currentIndex = -1;
		private GameObject _currentVisual;
		private WeaponMuzzle _currentMuzzle;
		private MuzzleFlash _currentMuzzleFlash;
		private Animator _currentAnimator;
		private WeaponAudio _currentAudio;
		private bool _firePreviouslyHeld;
		private float _readyToFireTime;
		private bool _isReloading;
		private float _reloadFinishTime;

		// отдача: "очередь" определяется по времени с последнего выстрела, а не по факту зажатия
		// кнопки — так быстрые одиночные клики тоже накапливают увод, как непрерывное удержание
		private FirstPersonController _movement;
		private float _lastShotTime = float.NegativeInfinity;
		private float _burstDrift;
		private float _burstDriftDirection = 1f;

		// прицел (ПКМ): 0 = от бедра, 1 = полностью в прицеле, между ними — плавный Lerp/Slerp
		private bool _isAiming;
		private float _aimBlend;
		// читает, например, WeaponAimZoom, чтобы синхронно с HandPosition/AimPosition менять FOV камеры
		public float AimBlend => _aimBlend;

		// ремонт/прожиг (ПКМ у Melee-оружия с CanRepair): копят дробный остаток между кадрами —
		// см. ApplyAccumulated, иначе на высоком FPS Repair/DamagePerSecond срабатывали бы каждый кадр
		private float _gasAccumulator;
		private float _repairAccumulator;
		private float _burnAccumulator;

		// горелка "зажглась": ставится Animation Event RepairStart в клипе Repair_Start (см. NotifyRepairStart),
		// сбрасывается, как только ПКМ отпущена/кончился газ. До этого момента играет только анимация
		// замаха — пламя, свет, расход газа и ремонт ждут события. _repairStartFallbackTime — подстраховка
		// на случай, если событие в клипе не проставлено (тот же принцип, что FallbackAnimationDuration)
		private bool _repairFlameActive;
		private float _repairStartFallbackTime = float.PositiveInfinity;

		// эффекты горелки в точке касания луча: искры (Repair.SparksPrefab), пока объект чинится, и дым
		// (Repair.SmokePrefab) по всему остальному — не чинимая поверхность или уже починенный объект.
		// Одновременно горит только один из них, второй догорает
		private readonly RepairSurfaceEffect _repairSparks = new RepairSurfaceEffect();
		private readonly RepairSurfaceEffect _repairSmoke = new RepairSurfaceEffect();

		// Опционально подключается извне (см. Inventory.InventoryReloadHandler): CompleteReload спрашивает
		// здесь, сколько патронов реально удалось взять из пула инвентаря (0..amountRequested), и добавляет
		// в магазин ровно столько — если патронов в пуле меньше, чем нужно, перезарядка выйдет частичной.
		// Если не назначено — перезарядка всегда даёт запрошенное количество целиком (старое поведение,
		// для сцен без интеграции с инвентарём). WeaponController намеренно ничего не знает про Inventory —
		// та же идея, что и с TriggerAction/BaseTrigger.
		public System.Func<WeaponData, int, int> ConsumeAmmo;

		// Тоже из InventoryReloadHandler: сколько патронов под это оружие сейчас лежит в пуле (ничего не списывает).
		// Reload смотрит сюда ДО запуска анимации — без патронов в инвентаре перезарядка не начинается вовсе,
		// а не проигрывает клип вхолостую. Не назначено — считаем, что патроны есть (сцены без инвентаря)
		public System.Func<WeaponData, int> GetAvailableAmmo;

		// Тоже опционально слушается извне (см. Inventory.InventoryWeaponEquipHandler) — сигнал "это оружие
		// больше не при мне", чтобы соответствующий слот в инвентаре тоже освободился. WeaponController
		// сам не трогает InventoryHolder — по той же причине, что и ConsumeAmmo выше
		// Кроме самого оружия передаёт заспавненный PickupPrefab (null, если он не задан) и сколько патронов
		// оставалось в магазине — слушатель записывает их в лежащий предмет, чтобы при повторном подборе
		// магазин был тем же, а не снова полным
		public event System.Action<WeaponData, GameObject, int> WeaponDropped;

		// данные текущего оружия — читает, например, ProceduralWeaponAnimation для AnimationProfile
		public WeaponData CurrentWeaponData => _currentIndex >= 0 ? _inventory[_currentIndex].Data : null;

		// --- состояние для HUD (только чтение) ---

		// патронов/газа в магазине текущего оружия; 0, если оружия нет или магазина у него нет
		public int CurrentAmmo => _currentIndex >= 0 ? _inventory[_currentIndex].CurrentAmmo : 0;
		public bool IsReloading => _isReloading;
		public bool IsAiming => _isAiming;
		// что сейчас чинит горелка: пламя горит и луч попадает в IRepairable. null — ничего не чинится.
		// Прогресс ремонта — IRepairable.RepairProgress (см. HUD.RepairProgressUI)
		public IRepairable RepairTarget { get; private set; }

		// сколько патронов под текущее оружие лежит в инвентаре (через GetAvailableAmmo). -1 — неизвестно
		// или бесконечно (сцена без инвентаря, у оружия не задан AmmoId); 0 — если у оружия нет магазина
		public int ReserveAmmo
		{
			get
			{
				WeaponData data = CurrentWeaponData;
				if (data == null || data.Ammo == null) return 0;
				if (GetAvailableAmmo == null) return -1;

				int available = GetAvailableAmmo(data);
				return available == int.MaxValue ? -1 : available;
			}
		}

		// каждый выстрел огнестрела и начало каждого взмаха холодного оружия — например, для раскрытия прицела
		public event System.Action<WeaponData> Fired;

		// pivot, который каждый кадр ставится в HandPosition/AimPosition текущего оружия (см. UpdateAimPose).
		// Модель оружия (_currentVisual) висит на нём с нулевым локальным смещением, поэтому
		// ProceduralWeaponAnimation, вращая/двигая именно VisualRoot, делает это вокруг точки, где
		// оружие реально держится, а не вокруг pivot'а самого WeaponHolder — без рычага при повороте.
		private Transform _visualRoot;
		public Transform VisualRoot => _visualRoot;

		private int _weaponLayer = -1;

		private void Awake()
		{
			_visualRoot = new GameObject("WeaponVisualRoot").transform;
			_visualRoot.SetParent(transform, false);

			if (!string.IsNullOrEmpty(weaponLayerName)) _weaponLayer = LayerMask.NameToLayer(weaponLayerName);
		}

		private void Start()
		{
			_movement = GetComponentInParent<FirstPersonController>();

			foreach (WeaponHandPlacementOverride placement in handPlacementOverrides)
			{
				if (placement.Weapon != null) _handPlacementByWeapon[placement.Weapon] = placement;
			}

			foreach (WeaponData data in startingWeapons)
			{
				PickupWeapon(data);
			}
		}

		// позиция/поворот в руках для этого WeaponHolder: сначала смотрим оверрайд (handPlacementOverrides),
		// иначе — значения по умолчанию из самого WeaponData
		private Vector3 GetHandPosition(WeaponData data) =>
			_handPlacementByWeapon.TryGetValue(data, out WeaponHandPlacementOverride placement) ? placement.HandPosition : data.HandPosition;

		private Vector3 GetHandRotation(WeaponData data) =>
			_handPlacementByWeapon.TryGetValue(data, out WeaponHandPlacementOverride placement) ? placement.HandRotation : data.HandRotation;

		private void Update()
		{
			// подстраховка (см. NotifyReloadComplete): если Animation Event в клипе не сработал,
			// магазин всё равно пополнится по таймеру
			if (_isReloading && Time.time >= _reloadFinishTime)
			{
				CompleteReload();
			}

			RecoverDrift();
			UpdateAimPose();
		}

		// вызывать каждый кадр с текущим состоянием ПКМ. Прицел есть только у огнестрела
		public void SetAiming(bool held)
		{
			if (IsHolstered) held = false;
			_isAiming = held && CurrentWeaponData is FirearmData;
		}

		// вызывать каждый кадр с сырым (не toggle) состоянием ПКМ — ремонт/прожиг это удержание,
		// а не прицел, поэтому сюда всегда передаётся именно "зажата ли кнопка сейчас".
		// Расходует CurrentAmmo слота — баллон горелки (Repair.Tank) это тот же "магазин", что у огнестрела,
		// просто тратится со своей скоростью Repair.AmmoPerSecond
		public void TickHeal(bool held)
		{
			if (IsHolstered) return;

			WeaponSlot slot = _currentIndex >= 0 ? _inventory[_currentIndex] : null;
			MeleeWeaponData tool = slot?.Data as MeleeWeaponData;
			bool canRepair = tool != null && tool.CanRepair;

			// оружие ещё достаётся/перезаряжается — как и обычная стрельба, лечить в этот момент нельзя
			bool wantsToHeal = held && canRepair && Time.time >= _readyToFireTime;
			bool healing = wantsToHeal && slot.CurrentAmmo > 0;

			// параметр актуален только для ремонтного инструмента — у остальных Animator Controller
			// про него ничего не знает, SetBool на несуществующий параметр каждый кадр сыпал бы ошибку в консоль
			if (_currentAnimator != null && canRepair && !string.IsNullOrEmpty(tool.Repair.HealBoolParam))
			{
				_currentAnimator.SetBool(tool.Repair.HealBoolParam, healing);
			}

			if (!healing)
			{
				SetRepairFlame(false);
				_repairStartFallbackTime = float.PositiveInfinity;
				return;
			}

			// начало ремонта — ждём RepairStart из анимации; таймер только чтобы не застрять без пламени навсегда
			if (float.IsPositiveInfinity(_repairStartFallbackTime)) _repairStartFallbackTime = Time.time + FallbackAnimationDuration;
			if (!_repairFlameActive && Time.time >= _repairStartFallbackTime) SetRepairFlame(true);

			if (_repairFlameActive) PerformHealTick(tool, slot);
		}

		// вызывается из WeaponAnimationEvents по Animation Event RepairStart — кадр в Repair_Start, где горелка зажигается.
		// Если ПКМ к этому моменту уже отпустили, событие игнорируется: анимация всё равно уходит в Repair_End
		public void NotifyRepairStart()
		{
			if (IsHolstered || float.IsPositiveInfinity(_repairStartFallbackTime)) return;
			SetRepairFlame(true);
		}

		private void SetRepairFlame(bool on)
		{
			if (_repairFlameActive == on) return;
			_repairFlameActive = on;

			if (_currentMuzzleFlash != null) _currentMuzzleFlash.SetContinuous(on);
			if (_currentMuzzle != null) _currentMuzzle.SetContinuous(on);

			if (_currentAudio != null) _currentAudio.SetRepairLoop(on);
			if (!on)
			{
				StopRepairEffects();
				RepairTarget = null;
			}
		}

		// искры — пока объект реально чинится; дым — если луч упёрся во что-то, что не чинится (стена, враг)
		// или в уже полностью починенный объект. Прогресс неизвестен (RepairProgress < 0) — считаем, что чинится
		private void UpdateRepairEffects(RepairToolData repair, RaycastHit hit, IRepairable repairable)
		{
			bool repairing = repairable != null && repairable.RepairProgress < 1f;

			if (repairing)
			{
				_repairSmoke.Stop();
				_repairSparks.Play(repair.SparksPrefab, hit);
			}
			else
			{
				_repairSparks.Stop();
				_repairSmoke.Play(repair.SmokePrefab, hit);
			}
		}

		private void StopRepairEffects()
		{
			_repairSparks.Stop();
			_repairSmoke.Stop();
		}

		private void OnDestroy()
		{
			_repairSparks.Dispose();
			_repairSmoke.Dispose();
		}

		// расход баллона идёт всегда, пока зажата кнопка и есть газ — как у настоящего баллона: жмёшь
		// и он травит газ, даже если светишь "в воздух" и никуда не попадаешь. Попадание в цель —
		// это отдельный вопрос, что этот газ дальше делает (лечит IRepairable / жжёт IDamageable)
		private void PerformHealTick(MeleeWeaponData tool, WeaponSlot slot)
		{
			RepairToolData repair = tool.Repair;

			// газ тратится по своей ставке (AmmoPerSecond), ремонт и урон — по своим, независимо от расхода
			ApplyAccumulated(ref _gasAccumulator, repair.AmmoPerSecond,
				spend => slot.CurrentAmmo = Mathf.Max(0, slot.CurrentAmmo - spend));

			if (_movement == null || _movement.CinemachineCameraTarget == null) return;

			Transform origin = _movement.CinemachineCameraTarget.transform;
			if (!Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, tool.Range, ~0, QueryTriggerInteraction.Ignore))
			{
				// горелка светит "в воздух" — до поверхности не достаёт, ни искр, ни дыма
				StopRepairEffects();
				RepairTarget = null;
				return;
			}

			IRepairable repairable = hit.collider.GetComponentInParent<IRepairable>();
			RepairTarget = repairable;
			UpdateRepairEffects(repair, hit, repairable);
			if (repairable != null)
			{
				ApplyAccumulated(ref _repairAccumulator, repair.RepairPerSecond, repairable.Repair);
				return;
			}

			IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
			if (damageable != null)
			{
				ApplyAccumulated(ref _burnAccumulator, repair.DamagePerSecond, damageable.TakeDamage);
			}
		}

		// копит дробный остаток ставки-в-секунду между кадрами и применяет только набежавшую целую часть —
		// без этого Mathf.CeilToInt(rate * deltaTime) на каждом кадре округлялся бы вверх минимум до 1
		private static void ApplyAccumulated(ref float accumulator, float ratePerSecond, System.Action<int> apply)
		{
			accumulator += ratePerSecond * Time.deltaTime;
			int whole = Mathf.FloorToInt(accumulator);
			if (whole <= 0) return;

			accumulator -= whole;
			apply(whole);
		}

		// переносит всю модель оружия (и вложенные меши/кости — руки, дуло и т.п.) на отдельный слой,
		// который видит только WeaponCamera — так модель не клипается в стены без уменьшения: она
		// рендерится отдельным проходом с крошечным near clip, а не вместе с общей мировой геометрией
		private static void SetLayerRecursively(GameObject root, int layer)
		{
			root.layer = layer;
			foreach (Transform child in root.transform)
			{
				SetLayerRecursively(child.gameObject, layer);
			}
		}

		// плавно тянет VisualRoot между HandPosition/HandRotation (от бедра) и AimPosition/AimRotation.
		// Это база, поверх которой ProceduralWeaponAnimation в LateUpdate добавляет свои офсеты —
		// Update гарантированно отрабатывает раньше LateUpdate, так что база всегда чистая к их началу.
		private void UpdateAimPose()
		{
			if (_currentVisual == null) return;

			WeaponData data = CurrentWeaponData;
			if (data == null) return;

			// без прицела (холодное оружие) — всегда поза от бедра
			AimData aim = (data as FirearmData)?.Aim;
			if (aim == null)
			{
				_aimBlend = 0f;
				_visualRoot.localPosition = GetHandPosition(data);
				_visualRoot.localRotation = Quaternion.Euler(GetHandRotation(data));
				return;
			}

			float targetBlend = _isAiming ? 1f : 0f;
			_aimBlend = Mathf.MoveTowards(_aimBlend, targetBlend, aim.TransitionSpeed * Time.deltaTime);

			_visualRoot.localPosition = Vector3.Lerp(GetHandPosition(data), aim.Position, _aimBlend);
			_visualRoot.localRotation = Quaternion.Slerp(
				Quaternion.Euler(GetHandRotation(data)), Quaternion.Euler(aim.Rotation), _aimBlend);
		}

		public void Next()
		{
			// при одном оружии в инвентаре (currentIndex + 1) % 1 всегда даёт 0 — переключение
			// на самого себя без надобности переигрывало бы Draw-анимацию
			if (IsHolstered || _inventory.Count <= 1) return;
			EquipByIndex((_currentIndex + 1) % _inventory.Count);
		}

		public void Previous()
		{
			if (IsHolstered || _inventory.Count <= 1) return;
			EquipByIndex((_currentIndex - 1 + _inventory.Count) % _inventory.Count);
		}

		// спавнит InHandPrefab выбранного оружия внутри VisualRoot, убирая старый визуал
		public void EquipByIndex(int index)
		{
			if (index < 0 || index >= _inventory.Count) return;

			// гасим горелку ещё со старым оружием (_currentIndex не сменён) — выключится его пламя/свет,
			// остановится луп и сыграет его звук затухания
			SetRepairFlame(false);
			_repairStartFallbackTime = float.PositiveInfinity;

			_currentIndex = index;

			if (_currentVisual != null) Destroy(_currentVisual);

			WeaponData data = _inventory[_currentIndex].Data;
			_currentMuzzle = null;
			_currentMuzzleFlash = null;
			_currentAnimator = null;
			_currentAudio = null;
			_isReloading = false; // смена оружия отменяет незаконченную перезарядку предыдущего
			_aimBlend = 0f; // смена оружия сбрасывает прицел — новое оружие всегда начинается от бедра

			if (data.InHandPrefab != null)
			{
				_currentVisual = Instantiate(data.InHandPrefab, _visualRoot);
				_currentVisual.transform.localPosition = Vector3.zero;
				_currentVisual.transform.localRotation = Quaternion.identity;
				if (_weaponLayer >= 0) SetLayerRecursively(_currentVisual, _weaponLayer);
				_currentMuzzle = _currentVisual.GetComponentInChildren<WeaponMuzzle>();
				_currentMuzzleFlash = _currentVisual.GetComponentInChildren<MuzzleFlash>();
				_currentAnimator = _currentVisual.GetComponentInChildren<Animator>();
				_currentAudio = _currentVisual.GetComponentInChildren<WeaponAudio>();
			}
			else
			{
				Debug.LogWarning($"{data.name}: не задан In Hand Prefab — оружие экипировано, но в руках ничего не появится", data);
			}

			if (_currentAudio != null) _currentAudio.PlayDraw();

			// готовность обычно приходит раньше через Animation Event (см. NotifyWeaponReady/WeaponAnimationEvents) —
			// FallbackAnimationDuration здесь лишь подстраховка на случай, если событие в клипе не проставлено
			_readyToFireTime = Time.time + FallbackAnimationDuration;
			if (_currentAnimator != null && !string.IsNullOrEmpty(data.DrawTrigger))
			{
				_currentAnimator.SetTrigger(data.DrawTrigger);
			}
		}

		// оружие убрано из рук (например, пока игрок работает с терминалом — см. Interactables.PlayerCameraFocus):
		// визуал скрыт, стрелять/перезаряжаться/переключаться/выбрасывать нельзя (см. гарды ниже и WeaponInput)
		public bool IsHolstered { get; private set; }

		public void SetHolstered(bool holstered)
		{
			if (IsHolstered == holstered) return;
			IsHolstered = holstered;

			if (holstered)
			{
				_isAiming = false;
				_aimBlend = 0f; // FOV (WeaponAimZoom) сразу возвращается к базовому, а не "доезжает" из прицела
				_isReloading = false; // незаконченная перезарядка отменяется — патроны из пула ещё не списаны

				// ремонтный луч мог гореть в момент блокировки — TickHeal больше не вызывается и сам его не погасит
				SetRepairFlame(false);
				_repairStartFallbackTime = float.PositiveInfinity;

				_visualRoot.gameObject.SetActive(false);
			}
			else
			{
				_visualRoot.gameObject.SetActive(true);
				// заново "достаём" текущее оружие — с Draw-анимацией и задержкой перед первым выстрелом
				if (_currentIndex >= 0) EquipByIndex(_currentIndex);
			}
		}

		// вызывается из WeaponAnimationEvents по Animation Event WeaponReady — универсальное "действие закончено,
		// можно снова действовать": в конце клипа Draw, Reload и удара Melee. Готовность наступает раньше таймера
		public void NotifyWeaponReady()
		{
			_readyToFireTime = Time.time;
		}

		// начинает перезарядку текущего оружия (кнопка R)
		public void Reload()
		{
			if (IsHolstered || _currentIndex < 0 || _isReloading || Time.time < _readyToFireTime) return;

			WeaponSlot slot = _inventory[_currentIndex];
			WeaponData data = slot.Data;
			AmmoData ammo = data.Ammo;

			// у обычного холодного оружия магазина нет — перезаряжать нечего. У огнестрела это патроны,
			// у ремонтного инструмента — баллон горелки
			if (ammo == null || slot.CurrentAmmo >= ammo.MagazineSize) return;

			// в инвентаре нечем перезаряжаться — не играем анимацию впустую
			if (GetAvailableAmmo != null && GetAvailableAmmo(data) <= 0) return;

			_isReloading = true;
			_reloadFinishTime = Time.time + FallbackAnimationDuration; // подстраховка, см. FallbackAnimationDuration
			_readyToFireTime = Mathf.Max(_readyToFireTime, _reloadFinishTime); // на время перезарядки стрелять нельзя

			if (_currentAnimator != null && !string.IsNullOrEmpty(ammo.ReloadTrigger))
			{
				_currentAnimator.SetTrigger(ammo.ReloadTrigger);
			}
			if (_currentAudio != null) _currentAudio.PlayReload();
		}

		// вызывается из WeaponAnimationEvents по Animation Event в клипе Reload — магазин пополняется раньше таймера
		public void NotifyReloadComplete()
		{
			if (_isReloading) CompleteReload();
		}

		// вызывается из WeaponAnimationEvents по Animation Event в клипе удара (кадр, где оружие реально касается цели) —
		// это единственное место, где Melee-оружие наносит урон
		public void NotifyMeleeHit()
		{
			if (_currentIndex < 0) return;

			WeaponData data = _inventory[_currentIndex].Data;
			if (!(data is MeleeWeaponData)) return;

			PerformHitscan(data);
		}

		private void CompleteReload()
		{
			_isReloading = false;
			if (_currentIndex < 0) return;

			WeaponSlot slot = _inventory[_currentIndex];
			WeaponData data = slot.Data;
			AmmoData ammo = data.Ammo;
			if (ammo == null) return;

			// спрашиваем пул патронов именно сейчас (не в момент нажатия R) — если между началом
			// и концом перезарядки сменили оружие, _isReloading уже сброшен в EquipByIndex и сюда
			// не дойдёт, так что патроны из пула не спишутся впустую за незавершённую перезарядку
			int needed = ammo.MagazineSize - slot.CurrentAmmo;
			int given = ConsumeAmmo != null ? ConsumeAmmo(data, needed) : needed;
			slot.CurrentAmmo += Mathf.Clamp(given, 0, needed);

			Debug.Log($"{data.WeaponName}: перезарядка завершена, патроны {slot.CurrentAmmo}/{ammo.MagazineSize}");
		}

		// добавляет оружие в инвентарь и сразу берёт его в руки. loadedAmmo — сколько патронов в магазине,
		// < 0 — полный магазин (стартовое оружие, свежий предмет на уровне)
		public void PickupWeapon(WeaponData data, int loadedAmmo = -1)
		{
			if (data == null) return;

			_inventory.Add(new WeaponSlot(data, loadedAmmo));
			EquipByIndex(_inventory.Count - 1);
		}

		// выбрасывает текущее оружие перед игроком с физическим импульсом
		public void DropCurrentWeapon()
		{
			if (IsHolstered || _currentIndex < 0) return;

			// выбросили прямо во время ремонта — гасим горелку, пока оружие ещё текущее
			SetRepairFlame(false);
			_repairStartFallbackTime = float.PositiveInfinity;

			WeaponSlot slot = _inventory[_currentIndex];
			WeaponData data = slot.Data;
			Transform origin = dropPoint != null ? dropPoint : transform;

			GameObject dropped = null;
			if (data.PickupPrefab != null)
			{
				dropped = Instantiate(data.PickupPrefab, origin.position + origin.forward, origin.rotation);

				if (dropped.TryGetComponent(out Rigidbody rb))
				{
					rb.AddForce((origin.forward + Vector3.up * 0.5f) * dropForce, ForceMode.Impulse);
				}
			}

			_inventory.RemoveAt(_currentIndex);
			WeaponDropped?.Invoke(data, dropped, slot.CurrentAmmo);

			if (_inventory.Count == 0)
			{
				_currentIndex = -1;
				if (_currentVisual != null) Destroy(_currentVisual);
			}
			else
			{
				EquipByIndex(Mathf.Min(_currentIndex, _inventory.Count - 1));
			}
		}

		// вызывать каждый кадр с текущим состоянием кнопки огня — сам решает, когда стрелять,
		// исходя из FireMode и FireRate текущего оружия
		public void TickFire(bool triggerHeld)
		{
			bool triggerPressed = triggerHeld && !_firePreviouslyHeld;
			_firePreviouslyHeld = triggerHeld;

			// оружие ещё достаётся/перезаряжается (_readyToFireTime) — не тратим впустую кулдаун FireRate
			if (IsHolstered || _currentIndex < 0 || Time.time < _readyToFireTime) return;

			WeaponSlot slot = _inventory[_currentIndex];
			bool automatic = slot.Data is FirearmData firearm && firearm.Mode == FireMode.Auto;
			bool wantsToFire = automatic ? triggerHeld : triggerPressed;
			if (!wantsToFire || Time.time < slot.NextFireTime) return;

			slot.NextFireTime = Time.time + (slot.Data.FireRate > 0f ? 1f / slot.Data.FireRate : 0f);
			Shoot();
		}

		// тратит патрон текущего оружия (кроме Melee), спавнит визуализацию пули и логирует результат
		public void Shoot()
		{
			// защита и для прямых вызовов Shoot() в обход TickFire — то же ограничение по доставанию
			if (IsHolstered || _currentIndex < 0 || Time.time < _readyToFireTime) return;

			WeaponSlot slot = _inventory[_currentIndex];
			WeaponData data = slot.Data;

			if (data is MeleeWeaponData melee)
			{
				// взмах — такое же "занятое" действие, как Draw и Reload: следующий удар не начнётся, пока
				// Animation Event WeaponReady в конце клипа удара не вернёт готовность (см. NotifyWeaponReady).
				// Без этого единственным ограничителем был бы FireRate, который не связан с длиной клипа —
				// и Attack-триггер перезапускал бы взмах с нуля каждые 1/FireRate секунд
				_readyToFireTime = Time.time + FallbackAnimationDuration;

				// попал/промах решается сейчас, в момент начала взмаха: анимацию нельзя выбрать задним числом,
				// когда клип уже играет. Проверка — тот же луч, что и у реального урона (TryRaycastFromCamera)
				bool willHit = TryRaycastFromCamera(data, out _);

				// сам урон наносится не здесь, а в NotifyMeleeHit — по Animation Event в нужном кадре клипа
				// попадания. На клипе промаха MeleeHit ставить не нужно
				Debug.Log($"{data.WeaponName}: удар начат ({(willHit ? "попадание" : "промах")})");
				PlayMeleeAnimation(melee, willHit);
				if (_currentAudio != null) _currentAudio.PlayAttack();
				Fired?.Invoke(data);
				return;
			}

			if (!(data is FirearmData firearm)) return;

			if (slot.CurrentAmmo <= 0)
			{
				Debug.Log($"{data.WeaponName}: патронов нет, нужна перезарядка");
				if (_currentAudio != null) _currentAudio.PlayDryFire();
				return;
			}

			slot.CurrentAmmo--;
			Debug.Log($"{data.WeaponName}: выстрел, урон {data.Damage}, патроны {slot.CurrentAmmo}/{firearm.Magazine.MagazineSize}");
			SpawnBulletVisual(firearm);
			PlayAttackAnimation(data);
			if (_currentAudio != null) _currentAudio.PlayAttack();
			if (_currentMuzzleFlash != null) _currentMuzzleFlash.Flash();
			if (_currentMuzzle != null) _currentMuzzle.Flash();
			ApplyRecoil(firearm);
			PerformHitscan(data);
			Fired?.Invoke(data);
		}

		// мгновенное попадание лучом от камеры — визуальная пуля (SpawnBulletVisual) чисто декоративна
		// и на реальное попадание не влияет, ровно как договаривались (без физики пули)
		private void PerformHitscan(WeaponData data)
		{
			if (!TryRaycastFromCamera(data, out RaycastHit hit)) return;

			SpawnImpactDecal(data, hit);
			PlayHitSound(data, hit);
			ApplyHitForce(data, hit);

			IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
			if (damageable != null)
			{
				damageable.TakeDamage(data.Damage);
				Debug.Log($"{data.WeaponName}: попадание в {hit.collider.name}, урон {data.Damage}");
			}
			else
			{
				Debug.Log($"{data.WeaponName}: попадание в {hit.collider.name} (без IDamageable, урон не нанесён)");
			}
		}

		// толкает Rigidbody цели в точку попадания по направлению взгляда — ящики, обломки и т.п. отлетают от удара/пули.
		// Вызывается до урона: если попадание добивает объект, толчок всё равно успевает примениться.
		// Kinematic-тела (в т.ч. замороженные обломки BreakableDebris) AddForce просто игнорируют
		private void ApplyHitForce(WeaponData data, RaycastHit hit)
		{
			if (data.HitForce <= 0f || hit.rigidbody == null) return;

			Vector3 direction = _movement.CinemachineCameraTarget.transform.forward;
			hit.rigidbody.AddForceAtPosition(direction * data.HitForce, hit.point, ForceMode.Impulse);
		}

		// единственное место, где определяется "есть ли перед оружием что-то с коллизией" — общий луч
		// от камеры на WeaponData.Range (враг или проп, не важно; триггеры игнорируются). Им пользуются и
		// реальный урон (PerformHitscan), и выбор анимации попадание/промах у Melee: если бы у них были
		// разные проверки, анимация могла бы показать попадание там, где урон промахнулся, и наоборот.
		// У Melee вместо тонкого луча — SphereCast радиусом MeleeHitRadius: удар прощает неточное наведение.
		// Стрелковое оружие остаётся на тонком луче — сфера на 100 м работала бы как автоприцел
		private bool TryRaycastFromCamera(WeaponData data, out RaycastHit hit)
		{
			hit = default;
			if (_movement == null || _movement.CinemachineCameraTarget == null) return false;

			Transform origin = _movement.CinemachineCameraTarget.transform;

			// SphereCast не замечает коллайдеры, которые сфера перекрывает уже в начальной точке — это как
			// раз и отсекает собственную капсулу игрока (камера внутри неё), отдельного фильтра не нужно
			if (data is MeleeWeaponData melee && melee.HitRadius > 0f)
			{
				return Physics.SphereCast(origin.position, melee.HitRadius, origin.forward, out hit, data.Range, ~0, QueryTriggerInteraction.Ignore);
			}

			return Physics.Raycast(origin.position, origin.forward, out hit, data.Range, ~0, QueryTriggerInteraction.Ignore);
		}

		// звук попадания, как и декаль, зависит от того, во что попали, а не от оружия: одна и та же пуля по дереву
		// и по металлу звучит по-разному. Звук самого выстрела/взмаха — WeaponAudio.Attack на модели в руках,
		// играется отдельно. Для холодного сюда попадаем из NotifyMeleeHit, т.е. ровно в кадр касания в клипе
		private void PlayHitSound(WeaponData data, RaycastHit hit)
		{
			SurfaceSoundSet surface = SoundSurface.Find(hit.collider);
			if (surface == null) surface = defaultHitSurface;

			HitType type = data is MeleeWeaponData ? HitType.Melee : HitType.Bullet;
			SurfaceSoundManager.Instance.PlayHit(surface, type, hit.point);
		}

		// декаль зависит от того, во что попали, а не от оружия: если на цели есть ImpactSurface
		// (например, кровь на манекене) — используем её, иначе берём декаль по умолчанию из WeaponData
		// (для окружения вроде стен, на которые не хочется вешать компонент на каждый объект)
		private void SpawnImpactDecal(WeaponData data, RaycastHit hit)
		{
			ImpactSurface surface = hit.collider.GetComponentInParent<ImpactSurface>();
			GameObject decalPrefab = surface != null && surface.DecalPrefab != null ? surface.DecalPrefab : data.VFX.DecalPrefab;
			if (decalPrefab == null) return;

			float lifetime = surface != null && surface.DecalLifetimeOverride > 0f ? surface.DecalLifetimeOverride : data.VFX.DecalLifetime;

			// forward декали смотрит "в" поверхность (-normal) — так ориентируется URP Decal Projector;
			// для простого квада с текстурой вместо -hit.normal может понадобиться hit.normal (см. пояснение)
			GameObject decal = Instantiate(decalPrefab, hit.point, Quaternion.LookRotation(-hit.normal));
			decal.transform.SetParent(hit.transform); // едет вместе с поверхностью, если та движется

			Destroy(decal, lifetime);
		}

		// подброс по X — постоянная добавка к прицелу за каждый выстрел; увод по Y — копится внутри
		// очереди в случайно выбранную при её начале сторону, до Recoil.MaxDrift
		private void ApplyRecoil(FirearmData data)
		{
			if (_movement == null) return;

			RecoilSettings recoil = _isAiming ? data.Recoil.Aim : data.Recoil.Hip;
			float kick = recoil.Kick;
			float driftPerShot = recoil.DriftPerShot;
			float maxDrift = recoil.MaxDrift;

			float burstGap = data.FireRate > 0f ? (1f / data.FireRate) * 1.5f : 0.5f;
			bool continuingBurst = (Time.time - _lastShotTime) <= burstGap;
			_lastShotTime = Time.time;

			if (!continuingBurst)
			{
				_burstDrift = 0f;
				_burstDriftDirection = Random.value < 0.5f ? -1f : 1f;
			}

			_movement.AddPitchKick(kick);

			float previousDrift = _burstDrift;
			_burstDrift = Mathf.Min(_burstDrift + driftPerShot, maxDrift);
			_movement.AddYaw((_burstDrift - previousDrift) * _burstDriftDirection);
		}

		// плавно тянет накопленный увод к нулю после того, как очередь закончилась
		// (с последнего выстрела прошло больше времени, чем занимает пауза между выстрелами)
		private void RecoverDrift()
		{
			if (_burstDrift <= 0f || _movement == null) return;

			if (!(CurrentWeaponData is FirearmData data)) return;

			float burstGap = data.FireRate > 0f ? (1f / data.FireRate) * 1.5f : 0.5f;
			if (Time.time - _lastShotTime < burstGap) return; // очередь ещё активна, ждём

			float recoverySpeed = (_isAiming ? data.Recoil.Aim : data.Recoil.Hip).DriftRecoverySpeed;

			float previousDrift = _burstDrift;
			_burstDrift = Mathf.MoveTowards(_burstDrift, 0f, recoverySpeed * Time.deltaTime);
			_movement.AddYaw((_burstDrift - previousDrift) * _burstDriftDirection);
		}

		// включает Trigger-параметр (WeaponData.AttackTrigger) в Animator текущей модели в руках
		private void PlayAttackAnimation(WeaponData data)
		{
			if (_currentAnimator != null && !string.IsNullOrEmpty(data.AttackTrigger))
			{
				_currentAnimator.SetTrigger(data.AttackTrigger);
			}
		}

		// холодное: анимация попадания (AttackTrigger) или промаха (AttackMissTrigger). Если отдельный
		// триггер промаха не задан — играется обычная анимация удара
		private void PlayMeleeAnimation(MeleeWeaponData data, bool hit)
		{
			string trigger = !hit && !string.IsNullOrEmpty(data.AttackMissTrigger) ? data.AttackMissTrigger : data.AttackTrigger;

			if (_currentAnimator != null && !string.IsNullOrEmpty(trigger))
			{
				_currentAnimator.SetTrigger(trigger);
			}
		}

		private void SpawnBulletVisual(FirearmData data)
		{
			BulletVisualData bulletData = data.Bullet;
			if (bulletData.Prefab == null) return;

			Transform origin = _currentMuzzle != null ? _currentMuzzle.transform : transform;
			GameObject bullet = Instantiate(bulletData.Prefab, origin.position, origin.rotation);

			if (bullet.TryGetComponent(out Rigidbody rb))
			{
				rb.linearVelocity = origin.forward * bulletData.Speed;
			}

			Destroy(bullet, bulletData.Lifetime);
		}
	}
}
