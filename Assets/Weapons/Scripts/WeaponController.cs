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

		[Header("Arms Rig")]
		[Tooltip("Имя кости камеры в риге рук — для оружия с галкой Align Arms To Camera Bone в WeaponData: руки ставятся так, " +
			"чтобы эта кость в позе покоя совпала с камерой игрока")]
		[SerializeField] private string cameraBoneName = "CameraBone";
		[Tooltip("Для того же режима: кость, вокруг которой раскачивается оружие (sway, bob, отдача) — корень оружия в риге рук. " +
			"Берётся её текущее анимированное положение, вручную точку раскачки задавать не нужно")]
		[SerializeField] private string swayPivotBoneName = "w_base";
		[Tooltip("Для того же режима: смещение рук и оружия относительно камеры — общее для всего оружия (м, по осям камеры: " +
			"X вправо, Y вверх, Z вперёд). Подстройка без переделки анимаций, например под другой FOV. " +
			"Своё смещение у каждого оружия — Hand Position/Rotation в его WeaponData, складывается с этим")]
		[SerializeField] private Vector3 viewModelOffset;
		[Tooltip("Поворот рук и оружия относительно камеры, общий для всего оружия (градусы)")]
		[SerializeField] private Vector3 viewModelOffsetRotation;

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
		// то же для клипа предмета в руках (аптечка): он длиннее клипов оружия, ждём дольше
		private const float FallbackItemActionDuration = 10f;

		private readonly List<WeaponSlot> _inventory = new List<WeaponSlot>();
		private int _currentIndex = -1;
		private GameObject _currentVisual;
		// что сейчас показано в руках: текущее оружие или предмет действия (BeginItemAction)
		private IViewModelSource _currentViewModel;
		private WeaponMuzzle _currentMuzzle;
		private MuzzleFlash _currentMuzzleFlash;
		private Animator _currentAnimator;
		private WeaponAudio _currentAudio;
		private bool _firePreviouslyHeld;
		private float _readyToFireTime;

		// комбо холодного оружия (см. TickMelee): удар идёт, пока не пришёл WeaponReady в конце клипа; в окне
		// ComboWindow можно начать следующий. _comboStep — номер последнего начатого удара
		private bool _swingActive;
		private bool _comboWindowOpen;
		private int _comboStep;
		private float _attackBufferedUntil = float.NegativeInfinity;
		// идёт последний удар полного комбо (MeleeWeaponData.ComboCooldown > 0) и до какого времени после него нельзя бить
		private bool _comboCompleted;
		private float _meleeCooldownEndTime;

		// смена оружия с анимацией Hide (см. SwitchTo): старое оружие доигрывает Hide, новое появляется только
		// после Animation Event WeaponHidden (или по таймеру-подстраховке). Храним сам слот, а не индекс —
		// индексы съезжают, если инвентарь меняется во время смены
		private WeaponSlot _pendingSlot;
		private float _hideFinishTime;
		private bool _currentHasHideTrigger;
		// у холодного оружия есть клип удара (ImpactTrigger) — проверяется при экипировке, как и Hide
		private bool _currentHasImpactTrigger;

		// идёт смена: старое оружие убирается — стрелять, бить, перезаряжаться, чинить и выбрасывать нельзя
		public bool IsSwitching => _pendingSlot != null;

		// действие с предметом в руках (BeginItemAction): оружие доигрывает Hide, затем в руках предмет со своим клипом,
		// в конце клипа (WeaponHidden) оружие возвращается с Draw. Всё это время оружием пользоваться нельзя
		private IViewModelSource _itemSource;
		private string _itemTrigger;
		private IItemActionHandler _itemHandler;
		// предмет уже в руках (до этого доигрывает Hide оружия)
		private bool _itemSpawned;
		// клип уже дошёл до ItemUseFinished
		private bool _itemFinished;
		private float _itemFallbackTime;

		public bool IsUsingItem => _itemHandler != null;

		// общая постановка рук — чтобы другие риги рук (например, пустые руки при подъёме, Player.MantleArmsAnimation)
		// стояли относительно камеры ровно так же, как руки оружия
		public string CameraBoneName => cameraBoneName;
		public Vector3 ViewModelOffset => viewModelOffset;
		public Quaternion ViewModelOffsetRotation => Quaternion.Euler(viewModelOffsetRotation);
		// слой рендера моделей в руках (отдельная камера оружия, без клиппинга в стены); -1 — слой не меняется
		public int WeaponLayer => _weaponLayer;
		public bool HasWeaponInHands => _currentIndex >= 0 && _currentVisual != null;

		// слой анимации подъёма на уступ в Animator текущего оружия (см. BeginMantleLayer) — вес слоя ведёт код:
		// плавно поднимает в начале и опускает в конце, без снапа руки в позу покоя
		private int _mantleLayer = -1;
		private float _mantleWeight;
		private float _mantleWeightTarget;
		private float _mantleBlendSpeed;
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

		// данные выбранного оружия. Во время действия с предметом (IsUsingItem) — то, что вернётся в руки после него
		public WeaponData CurrentWeaponData => _currentIndex >= 0 ? _inventory[_currentIndex].Data : null;
		// что сейчас реально в руках (оружие или предмет) — читает, например, ProceduralWeaponAnimation для AnimationProfile
		public IViewModelSource CurrentViewModel => _currentVisual != null ? _currentViewModel : null;

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
		// риг "в реальном положении" (WeaponData.AlignArmsToCameraBone): обратная поза кости камеры относительно корня префаба
		private bool _alignedToCameraBone;
		private Vector3 _cameraBoneInversePosition;
		private Quaternion _cameraBoneInverseRotation = Quaternion.identity;
		private Transform _swayPivotBone;
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

			// стартовый набор — сразу в инвентарь, в руки берём последнее без Hide/Draw-цепочки на каждом оружии
			foreach (WeaponData data in startingWeapons)
			{
				if (data != null) _inventory.Add(new WeaponSlot(data, -1));
			}
			if (_inventory.Count > 0) EquipByIndex(_inventory.Count - 1);
		}

		// позиция/поворот в руках для этого WeaponHolder: сначала смотрим оверрайд (handPlacementOverrides, только для оружия),
		// иначе — значения по умолчанию из самих данных
		private Vector3 GetHandPosition(IViewModelSource source) =>
			source is WeaponData weapon && _handPlacementByWeapon.TryGetValue(weapon, out WeaponHandPlacementOverride placement)
				? placement.HandPosition : source.HandPosition;

		private Vector3 GetHandRotation(IViewModelSource source) =>
			source is WeaponData weapon && _handPlacementByWeapon.TryGetValue(weapon, out WeaponHandPlacementOverride placement)
				? placement.HandRotation : source.HandRotation;

		private void Update()
		{
			// подстраховка (см. NotifyReloadComplete): если Animation Event в клипе не сработал,
			// магазин всё равно пополнится по таймеру
			if (_isReloading && Time.time >= _reloadFinishTime)
			{
				CompleteReload();
			}

			// подстраховка (см. NotifyWeaponHidden): событие в конце клипа Hide не пришло — меняем по таймеру
			if (IsSwitching && Time.time >= _hideFinishTime)
			{
				Debug.LogWarning($"{CurrentWeaponData?.name}: смена оружия по таймеру — в клипе Hide нет Animation Event WeaponHidden", CurrentWeaponData);
				FinishSwitch();
			}

			// подстраховка для действия с предметом: нет WeaponHidden в конце Hide оружия или в конце клипа предмета
			if (IsUsingItem && Time.time >= _itemFallbackTime)
			{
				Debug.LogWarning($"{_itemSource.name}: действие с предметом продолжено по таймеру — в клипе " +
					$"{(_itemSpawned ? "предмета" : "Hide оружия")} нет Animation Event WeaponHidden", _itemSource as Object);
				if (_itemSpawned) FinishItemAction();
				else SpawnItem();
			}

			UpdateMantleLayer();
			RecoverDrift();
			UpdateAimPose();
		}

		// Поворот камеры из анимации (например, подъём на уступ — Player.MantleArmsAnimation) относительно тела. Модель
		// в руках висит на камере и сама повернулась бы вместе с ней — тогда руки "прилипли" бы к экрану, а не остались
		// в пространстве тела (на крае уступа). Поэтому модель поворачивается обратно на тот же угол вокруг камеры:
		// руки на месте, а камера смотрит вокруг них, как в Blender. Без анимации камеры — identity, ничего не меняется
		//
		// rotation/offset — поворот и смещение камеры из анимации (в её осях) В ЭТОМ КАДРЕ. Вызывать раз за кадр в LateUpdate
		// до ProceduralWeaponAnimation и FirstPersonController (как делает MantleArmsAnimation): базовая поза VisualRoot
		// на этот кадр уже выставлена в Update, обратный поворот ложится поверх неё сразу — без отставания от камеры на кадр
		public void SetAnimatedCameraOffset(Quaternion rotation, Vector3 offset)
		{
			if (_currentVisual == null || (rotation == Quaternion.identity && offset == Vector3.zero)) return;

			Quaternion counter = Quaternion.Inverse(rotation);
			_visualRoot.localPosition = counter * (_visualRoot.localPosition - offset);
			_visualRoot.localRotation = counter * _visualRoot.localRotation;
		}

		// Анимация подъёма на уступ на руках текущего оружия: в его Animator Controller должен быть слой layerName
		// (Override, с маской свободной руки, вес по умолчанию 0) и Trigger trigger, запускающий в нём клип подъёма.
		// В маске — ТОЛЬКО кости руки: без root (движение тела делает капсула) и без CameraBone (камерой управляет
		// анимация подъёма; иначе WeaponCameraAnchor на кости камеры оружия перенесёт её движение на камеру — двойной подъём).
		// false — у оружия такого слоя/триггера нет (или руки сейчас скрыты): анимировать нечем
		public bool BeginMantleLayer(string layerName, string trigger, float blendIn)
		{
			if (IsHolstered || _currentAnimator == null || string.IsNullOrEmpty(layerName)) return false;

			int layer = _currentAnimator.GetLayerIndex(layerName);
			if (layer < 0 || !HasTrigger(_currentAnimator, trigger)) return false;

			// прошлый подъём мог ещё доигрывать спуск веса на другом слое — обнуляем его
			if (_mantleLayer >= 0 && _mantleLayer != layer) _currentAnimator.SetLayerWeight(_mantleLayer, 0f);

			_mantleLayer = layer;
			_mantleWeightTarget = 1f;
			_mantleBlendSpeed = blendIn > 0f ? 1f / blendIn : float.PositiveInfinity;
			_currentAnimator.SetTrigger(trigger);
			return true;
		}

		public void EndMantleLayer(float blendOut)
		{
			if (_mantleLayer < 0) return;
			_mantleWeightTarget = 0f;
			_mantleBlendSpeed = blendOut > 0f ? 1f / blendOut : float.PositiveInfinity;
		}

		private void UpdateMantleLayer()
		{
			if (_mantleLayer < 0 || _currentAnimator == null) return;

			_mantleWeight = Mathf.MoveTowards(_mantleWeight, _mantleWeightTarget, _mantleBlendSpeed * Time.deltaTime);
			_currentAnimator.SetLayerWeight(_mantleLayer, _mantleWeight);

			if (_mantleWeight <= 0f && _mantleWeightTarget <= 0f) _mantleLayer = -1;
		}

		// вызывать каждый кадр с текущим состоянием ПКМ. Прицел есть только у огнестрела
		public void SetAiming(bool held)
		{
			if (IsHolstered || IsSwitching || IsUsingItem) held = false;
			_isAiming = held && CurrentWeaponData is FirearmData;
		}

		// вызывать каждый кадр с сырым (не toggle) состоянием ПКМ — ремонт/прожиг это удержание,
		// а не прицел, поэтому сюда всегда передаётся именно "зажата ли кнопка сейчас".
		// Расходует CurrentAmmo слота — баллон горелки (Repair.Tank) это тот же "магазин", что у огнестрела,
		// просто тратится со своей скоростью Repair.AmmoPerSecond
		public void TickHeal(bool held)
		{
			// в руках предмет — Animator принадлежит ему, а горелку погасил BeginItemAction
			if (IsHolstered || IsUsingItem) return;

			WeaponSlot slot = _currentIndex >= 0 ? _inventory[_currentIndex] : null;
			MeleeWeaponData tool = slot?.Data as MeleeWeaponData;
			bool canRepair = tool != null && tool.CanRepair;

			// оружие ещё достаётся/перезаряжается — как и обычная стрельба, лечить в этот момент нельзя
			bool wantsToHeal = held && canRepair && !IsSwitching && Time.time >= _readyToFireTime;
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
			if (IsHolstered || IsUsingItem || float.IsPositiveInfinity(_repairStartFallbackTime)) return;
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

			if (!CastFromCamera(tool.Range, 0f, out RaycastHit hit))
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
		// Для рига "в реальном положении": запоминает позу кости камеры в префабе и кость оружия (точку раскачки),
		// дальше UpdateAlignedPose каждый кадр держит кость камеры в камере игрока (WeaponHolder).
		// Вызывается сразу после Instantiate — Animator ещё не отработал, кости стоят в позе покоя из модели.
		private void AlignToCameraBone(Transform visual, IViewModelSource data)
		{
			Transform cameraBone = FindDeep(visual, cameraBoneName);
			if (cameraBone == null)
			{
				Debug.LogWarning($"{data.name}: включён Align Arms To Camera Bone, но в In Hand Prefab нет кости \"{cameraBoneName}\"", data as Object);
				return;
			}

			// поза кости камеры относительно корня префаба (в его единицах — с учётом масштаба корня); запоминаем
			// обратную — она постоянна для этого префаба, а поза от бедра может меняться (подстройка в Play Mode)
			Vector3 bonePosition = Vector3.Scale(visual.localScale, visual.InverseTransformPoint(cameraBone.position));
			Quaternion boneRotation = Quaternion.Inverse(visual.rotation) * cameraBone.rotation;
			_cameraBoneInverseRotation = Quaternion.Inverse(boneRotation);
			_cameraBoneInversePosition = -(_cameraBoneInverseRotation * bonePosition);
			_alignedToCameraBone = true;

			_swayPivotBone = FindDeep(visual, swayPivotBoneName);
			if (_swayPivotBone == null)
				Debug.LogWarning($"{data.name}: в In Hand Prefab нет кости \"{swayPivotBoneName}\" — точка раскачки берётся из Hand Position", data as Object);

			UpdateAlignedPose(data);
		}

		// Риг "в реальном положении", каждый кадр (из UpdateAimPose):
		//   VisualRoot (точка раскачки) ставится в текущее положение кости оружия (w_base) — sway, bob и отдача
		//   ProceduralWeaponAnimation крутят оружие вокруг него, вручную точку задавать не нужно;
		//   корень префаба сдвигается обратно на ту же величину — руки остаются ровно по кости камеры, как в Blender;
		//   прицел — смещение VisualRoot от точки раскачки (Aim Position/Rotation), оно сдвигает руки.
		// Положение кости берётся с прошлого кадра (Animator обновляет кости после Update) — запаздывание незаметно.
		private void UpdateAlignedPose(IViewModelSource data)
		{
			Transform visual = _currentVisual.transform;

			// смещение рук относительно "как в Blender": общее для всего оружия + своё у этого (Hand Position/Rotation)
			Quaternion offsetRotation = Quaternion.Euler(viewModelOffsetRotation) * Quaternion.Euler(GetHandRotation(data));
			Vector3 offsetPosition = viewModelOffset + Quaternion.Euler(viewModelOffsetRotation) * GetHandPosition(data);

			// корень префаба в пространстве камеры: кость камеры в камере, затем смещение
			Quaternion rootRotation = offsetRotation * _cameraBoneInverseRotation;
			Vector3 rootPosition = offsetRotation * _cameraBoneInversePosition + offsetPosition;

			// точка раскачки — кость оружия в пространстве камеры (с учётом смещения)
			Vector3 pivot = offsetPosition;
			if (_swayPivotBone != null)
			{
				Vector3 boneInPrefab = Vector3.Scale(visual.localScale, visual.InverseTransformPoint(_swayPivotBone.position));
				pivot = rootRotation * boneInPrefab + rootPosition;
			}

			// прицел — смещение от точки раскачки
			AimData aim = (data as FirearmData)?.Aim;
			Vector3 aimOffset = Vector3.zero;
			Quaternion aimRotation = Quaternion.identity;
			if (aim != null)
			{
				_aimBlend = Mathf.MoveTowards(_aimBlend, _isAiming ? 1f : 0f, aim.TransitionSpeed * Time.deltaTime);
				aimOffset = aim.Position * _aimBlend;
				aimRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(aim.Rotation), _aimBlend);
			}
			else
			{
				_aimBlend = 0f;
			}

			_visualRoot.localPosition = pivot + aimOffset;
			_visualRoot.localRotation = aimRotation;

			// корень префаба относительно VisualRoot (от бедра он стоит в pivot без поворота)
			visual.localRotation = rootRotation;
			visual.localPosition = rootPosition - pivot;
		}

		private static Transform FindDeep(Transform parent, string childName)
		{
			if (parent.name == childName) return parent;
			foreach (Transform child in parent)
			{
				Transform found = FindDeep(child, childName);
				if (found != null) return found;
			}
			return null;
		}

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
			IViewModelSource data = CurrentViewModel;
			if (data == null) return;

			if (_alignedToCameraBone)
			{
				UpdateAlignedPose(data);
				return;
			}

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
			if (IsHolstered || IsUsingItem || _inventory.Count <= 1) return;
			SwitchTo((SelectedIndex + 1) % _inventory.Count);
		}

		public void Previous()
		{
			if (IsHolstered || IsUsingItem || _inventory.Count <= 1) return;
			SwitchTo((SelectedIndex - 1 + _inventory.Count) % _inventory.Count);
		}

		// оружие, на которое игрок "листает": во время Hide это уже цель смены, а не то, что ещё в руках —
		// иначе два щелчка колесом подряд выбрали бы одно и то же оружие
		private int SelectedIndex => IsSwitching ? _inventory.IndexOf(_pendingSlot) : _currentIndex;

		// смена оружия с анимацией: текущее играет Hide, новое появится по событию WeaponHidden в конце клипа.
		// Если у текущего оружия анимации Hide нет (нет модели/Animator/параметра) — меняем мгновенно
		public void SwitchTo(int index)
		{
			if (index < 0 || index >= _inventory.Count) return;

			WeaponSlot target = _inventory[index];

			// в руках предмет (например, подобрали оружие во время лечения) — меняем только то, что вернётся в руки после него
			if (IsUsingItem)
			{
				_currentIndex = index;
				return;
			}

			// листают дальше, пока старое ещё убирается — меняем только цель, Hide не перезапускаем
			if (IsSwitching)
			{
				_pendingSlot = target;
				return;
			}

			if (index == _currentIndex && _currentVisual != null) return;

			// нечего убирать (руки пустые — например, выбросили последнее оружие) или у оружия нет анимации Hide
			if (_currentIndex < 0 || _currentVisual == null || _currentAnimator == null || !_currentHasHideTrigger)
			{
				EquipByIndex(index);
				return;
			}

			// всё, что делало старое оружие, прерываем: ремонт (вместе с пламенем и звуком), перезарядку, комбо, прицел.
			// Горелку дополнительно гасит TickHeal — пока идёт смена, он считает, что ПКМ не зажата
			SetRepairFlame(false);
			_repairStartFallbackTime = float.PositiveInfinity;
			_isReloading = false;
			ResetCombo();
			_attackBufferedUntil = float.NegativeInfinity;
			_isAiming = false;

			_pendingSlot = target;
			_hideFinishTime = Time.time + FallbackAnimationDuration;
			_currentAnimator.SetTrigger(CurrentWeaponData.HideTrigger);
		}

		// вызывается из WeaponAnimationEvents по Animation Event WeaponHidden в конце клипа Hide — и в конце клипа
		// предмета в руках (аптечка): предмет убран из кадра, оружие возвращается
		public void NotifyWeaponHidden()
		{
			if (IsUsingItem)
			{
				if (_itemSpawned) FinishItemAction();
				else SpawnItem();
				return;
			}

			if (IsSwitching) FinishSwitch();
		}

		private void FinishSwitch()
		{
			int index = _inventory.IndexOf(_pendingSlot);
			_pendingSlot = null;
			if (index >= 0) EquipByIndex(index);
		}

		private static bool HasTrigger(Animator animator, string parameter)
		{
			if (animator == null || string.IsNullOrEmpty(parameter)) return false;

			foreach (AnimatorControllerParameter p in animator.parameters)
			{
				if (p.type == AnimatorControllerParameterType.Trigger && p.name == parameter) return true;
			}
			return false;
		}

		// мгновенно спавнит InHandPrefab выбранного оружия внутри VisualRoot, убирая старый визуал — без анимации
		// Hide (для обычной смены с анимацией — SwitchTo). Отменяет незаконченную смену и действие с предметом, если они шли
		public void EquipByIndex(int index)
		{
			if (index < 0 || index >= _inventory.Count) return;

			_pendingSlot = null;
			AbortItemAction();

			// гасим горелку ещё со старым оружием (_currentIndex не сменён) — выключится его пламя/свет,
			// остановится луп и сыграет его звук затухания
			SetRepairFlame(false);
			_repairStartFallbackTime = float.PositiveInfinity;

			_currentIndex = index;

			WeaponData data = _inventory[_currentIndex].Data;
			_isReloading = false; // смена оружия отменяет незаконченную перезарядку предыдущего
			ResetCombo(); // и недоигранное комбо — новое оружие начинает с первого удара
			_meleeCooldownEndTime = 0f; // пауза после комбо была у прошлого оружия
			_attackBufferedUntil = float.NegativeInfinity;
			_aimBlend = 0f; // смена оружия сбрасывает прицел — новое оружие всегда начинается от бедра

			SpawnViewModel(data);

			// проверяем один раз при экипировке: SetTrigger на несуществующий параметр сыпал бы предупреждение
			// в консоль, а смена ждала бы таймер-подстраховку — у оружия без Hide она просто мгновенная
			_currentHasHideTrigger = HasTrigger(_currentAnimator, data.HideTrigger);
			_currentHasImpactTrigger = data is MeleeWeaponData melee && HasTrigger(_currentAnimator, melee.ImpactTrigger);

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
				ResetCombo();
				_attackBufferedUntil = float.NegativeInfinity;

				// убрали посреди смены — смену завершаем без анимации: при возвращении в руки появится уже новое оружие
				// (ветка else ниже делает EquipByIndex(_currentIndex) — с Draw)
				if (IsSwitching)
				{
					int target = _inventory.IndexOf(_pendingSlot);
					if (target >= 0) _currentIndex = target;
					_pendingSlot = null;
				}

				// ремонтный луч мог гореть в момент блокировки — TickHeal больше не вызывается и сам его не погасит
				SetRepairFlame(false);
				_repairStartFallbackTime = float.PositiveInfinity;

				// руки убрали посреди действия с предметом (терминал, захват, смерть) — действие прерывается,
				// при возвращении в руки появится оружие
				CancelItemAction();

				_visualRoot.gameObject.SetActive(false);
			}
			else
			{
				_visualRoot.gameObject.SetActive(true);
				// заново "достаём" текущее оружие — с Draw-анимацией и задержкой перед первым выстрелом
				if (_currentIndex >= 0) EquipByIndex(_currentIndex);
			}
		}

		// Взять предмет в руки на время его анимации (аптечка): текущее оружие играет Hide, после WeaponHidden в руках
		// появляется item.InHandPrefab и запускается trigger (пусто — клип играет как состояние по умолчанию контроллера).
		// Ключевые моменты клипа приходят в handler (Animation Event ItemApplied / ItemUseFinished), в конце клипа
		// (WeaponHidden) оружие возвращается с Draw. false — сейчас нельзя (руки убраны, уже идёт действие, нет префаба)
		public bool BeginItemAction(IViewModelSource item, string trigger, IItemActionHandler handler)
		{
			if (item == null || handler == null || IsHolstered || IsUsingItem) return false;
			if (item.InHandPrefab == null)
			{
				Debug.LogWarning($"{item.name}: не задан In Hand Prefab — предмет нечем показать в руках", item as Object);
				return false;
			}

			// всё, что делало оружие, прерываем — как при смене оружия
			SetRepairFlame(false);
			_repairStartFallbackTime = float.PositiveInfinity;
			_isReloading = false;
			ResetCombo();
			_attackBufferedUntil = float.NegativeInfinity;
			_isAiming = false;

			_itemSource = item;
			_itemTrigger = trigger;
			_itemHandler = handler;
			_itemSpawned = false;
			_itemFinished = false;
			_itemFallbackTime = Time.time + FallbackAnimationDuration;

			// шла смена — старое оружие уже играет Hide: дожидаемся его, а после предмета в руки вернётся новое
			if (IsSwitching)
			{
				int target = _inventory.IndexOf(_pendingSlot);
				if (target >= 0) _currentIndex = target;
				_pendingSlot = null;
				return true;
			}

			// убирать нечего (пустые руки) или у оружия нет Hide — предмет сразу
			if (_currentViewModel is WeaponData shown && _currentAnimator != null && _currentHasHideTrigger)
			{
				_currentAnimator.SetTrigger(shown.HideTrigger);
			}
			else
			{
				SpawnItem();
			}
			return true;
		}

		// прервать действие с предметом: предмет убирается сразу, без доигрывания клипа, оружие возвращается с Draw
		// (если руки не убраны). Всё, что успело произойти до этого (например, лечение), остаётся
		public void CancelItemAction()
		{
			bool spawned = _itemSpawned;
			if (!AbortItemAction()) return;

			if (IsHolstered)
			{
				// при возвращении в руки EquipByIndex всё равно заменит визуал — но без оружия предмет остался бы висеть
				if (spawned) ClearVisual();
				return;
			}
			ReturnWeapon();
		}

		// вызывается из WeaponAnimationEvents по Animation Event ItemApplied в клипе предмета
		public void NotifyItemApplied(AnimationEvent animationEvent)
		{
			if (!IsUsingItem || !_itemSpawned) return;
			_itemHandler.OnItemApplied(ActiveDurationUntilFinish(animationEvent));
		}

		// вызывается из WeaponAnimationEvents по Animation Event ItemUseFinished в клипе предмета
		public void NotifyItemUseFinished()
		{
			if (!IsUsingItem || !_itemSpawned || _itemFinished) return;
			_itemFinished = true;
			_itemHandler.OnItemFinished();
		}

		// оружие убрано (WeaponHidden) — показываем предмет
		private void SpawnItem()
		{
			_itemSpawned = true;
			_itemFallbackTime = Time.time + FallbackItemActionDuration;
			_aimBlend = 0f;
			SpawnViewModel(_itemSource);

			if (string.IsNullOrEmpty(_itemTrigger) || _currentAnimator == null) return;
			if (HasTrigger(_currentAnimator, _itemTrigger)) _currentAnimator.SetTrigger(_itemTrigger);
			else Debug.LogWarning($"{_itemSource.name}: в Animator Controller предмета нет Trigger \"{_itemTrigger}\"", _itemSource as Object);
		}

		// клип предмета закончился (WeaponHidden) — возвращаем оружие
		private void FinishItemAction()
		{
			IItemActionHandler handler = _itemHandler;
			bool finished = _itemFinished;
			ClearItemAction();

			// в клипе нет ItemUseFinished — окно действия закрывается вместе с клипом
			if (!finished) handler.OnItemFinished();
			ReturnWeapon();
		}

		// сбрасывает действие с предметом и сообщает о прерывании; false — действия не было
		private bool AbortItemAction()
		{
			if (!IsUsingItem) return false;

			IItemActionHandler handler = _itemHandler;
			ClearItemAction();
			handler.OnItemCancelled();
			return true;
		}

		private void ClearItemAction()
		{
			_itemSource = null;
			_itemTrigger = null;
			_itemHandler = null;
			_itemSpawned = false;
			_itemFinished = false;
		}

		private void ReturnWeapon()
		{
			if (_currentIndex >= 0 && _currentIndex < _inventory.Count) EquipByIndex(_currentIndex);
			else ClearVisual();
		}

		// сколько секунд от события ItemApplied до ItemUseFinished в том же клипе — по времени событий в самом клипе
		// и скорости состояния, чтобы тайминг правился только в анимации
		private float ActiveDurationUntilFinish(AnimationEvent applied)
		{
			AnimationClip clip = applied.animatorClipInfo.clip;
			if (clip != null)
			{
				foreach (AnimationEvent e in clip.events)
				{
					if (e.functionName != nameof(WeaponAnimationEvents.ItemUseFinished) || e.time < applied.time) continue;

					AnimatorStateInfo state = applied.animatorStateInfo;
					float speed = Mathf.Abs(state.speed * state.speedMultiplier * (_currentAnimator != null ? _currentAnimator.speed : 1f));
					return speed > 0f ? (e.time - applied.time) / speed : 0f;
				}
			}

			Debug.LogWarning($"{_itemSource.name}: в клипе {(clip != null ? clip.name : "?")} нет Animation Event ItemUseFinished " +
				"после ItemApplied — действие применится сразу целиком", _itemSource as Object);
			return 0f;
		}

		// спавнит модель в руках (оружие или предмет) внутри VisualRoot, убирая старую — без анимаций
		private void SpawnViewModel(IViewModelSource source)
		{
			ClearVisual();
			_currentViewModel = source;

			if (source.InHandPrefab == null)
			{
				Debug.LogWarning($"{source.name}: не задан In Hand Prefab — в руках ничего не появится", source as Object);
				return;
			}

			_currentVisual = Instantiate(source.InHandPrefab, _visualRoot);
			_currentVisual.transform.localPosition = Vector3.zero;
			_currentVisual.transform.localRotation = Quaternion.identity;
			if (source.AlignArmsToCameraBone) AlignToCameraBone(_currentVisual.transform, source);
			if (_weaponLayer >= 0) SetLayerRecursively(_currentVisual, _weaponLayer);
			_currentMuzzle = _currentVisual.GetComponentInChildren<WeaponMuzzle>();
			_currentMuzzleFlash = _currentVisual.GetComponentInChildren<MuzzleFlash>();
			_currentAnimator = _currentVisual.GetComponentInChildren<Animator>();
			_currentAudio = _currentVisual.GetComponentInChildren<WeaponAudio>();
		}

		private void ClearVisual()
		{
			if (_currentVisual != null) Destroy(_currentVisual);
			_currentVisual = null;
			_currentViewModel = null;
			_alignedToCameraBone = false;
			_swayPivotBone = null;
			_currentMuzzle = null;
			_currentMuzzleFlash = null;
			_currentAnimator = null;
			_currentAudio = null;
			_currentHasHideTrigger = false;
			_currentHasImpactTrigger = false;
			_mantleLayer = -1; // слой подъёма был у старого Animator — у нового вес по умолчанию из контроллера
			_mantleWeight = 0f;
		}

		// вызывается из WeaponAnimationEvents по Animation Event WeaponReady — универсальное "действие закончено,
		// можно снова действовать": в конце клипа Draw, Reload и удара Melee. Готовность наступает раньше таймера
		public void NotifyWeaponReady()
		{
			_readyToFireTime = Time.time;
			// у холодного это конец удара: комбо не продолжили — следующий удар снова первый
			EndSwingSequence();
		}

		// вызывается из WeaponAnimationEvents по Animation Event ComboWindow — кадр в клипе удара, начиная с которого
		// можно перейти в следующий удар комбо, не дожидаясь конца клипа. После последнего удара полного комбо
		// с паузой (MeleeWeaponData.ComboCooldown) окно не открывается — комбо закончено
		public void NotifyComboWindow()
		{
			if (_swingActive && !_comboCompleted) _comboWindowOpen = true;
		}

		// удар закончился (WeaponReady или таймер-подстраховка): если это был последний удар полного комбо — пауза
		private void EndSwingSequence()
		{
			if (_comboCompleted && CurrentWeaponData is MeleeWeaponData melee)
			{
				_meleeCooldownEndTime = Time.time + melee.ComboCooldown;
			}
			ResetCombo();
		}

		private void ResetCombo()
		{
			_swingActive = false;
			_comboWindowOpen = false;
			_comboCompleted = false;
		}

		// сколько секунд осталось до конца паузы после полного комбо (0 — бить можно) — например, для HUD
		public float MeleeCooldownRemaining => Mathf.Max(0f, _meleeCooldownEndTime - Time.time);

		// начинает перезарядку текущего оружия (кнопка R)
		public void Reload()
		{
			if (IsHolstered || IsSwitching || IsUsingItem || _currentIndex < 0 || _isReloading || Time.time < _readyToFireTime) return;

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

		// вызывается из WeaponAnimationEvents по Animation Event MeleeHit в кадре контакта взмаха — здесь и только здесь
		// решается, попал ли удар: по тому, куда игрок смотрит ИМЕННО СЕЙЧАС (нажал и отвернулся — промах).
		// Попал — урон и клип удара (ImpactTrigger); промахнулся — взмах играет дальше, это и есть промах
		public void NotifyMeleeHit()
		{
			if (_currentIndex < 0) return;
			if (!(_inventory[_currentIndex].Data is MeleeWeaponData melee)) return;

			if (!PerformHitscan(melee))
			{
				Debug.Log($"{melee.WeaponName}: удар {_comboStep + 1} — промах");
				return;
			}

			PlayImpactAnimation(melee);
		}

		private void PlayImpactAnimation(MeleeWeaponData data)
		{
			if (_currentAnimator == null || !_currentHasImpactTrigger) return;

			// триггер взмаха, если вдруг не "съеден" переходом, не должен перезапустить взмах после клипа удара
			if (!string.IsNullOrEmpty(data.AttackTrigger)) _currentAnimator.ResetTrigger(data.AttackTrigger);
			_currentAnimator.SetTrigger(data.ImpactTrigger);
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

		// добавляет оружие в инвентарь и берёт его в руки — текущее при этом убирается анимацией Hide (см. SwitchTo).
		// loadedAmmo — сколько патронов в магазине, < 0 — полный магазин (свежий предмет на уровне)
		public void PickupWeapon(WeaponData data, int loadedAmmo = -1)
		{
			if (data == null) return;

			_inventory.Add(new WeaponSlot(data, loadedAmmo));
			SwitchTo(_inventory.Count - 1);
		}

		// выбрасывает текущее оружие перед игроком с физическим импульсом
		public void DropCurrentWeapon()
		{
			if (IsHolstered || IsSwitching || IsUsingItem || _currentIndex < 0) return;

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
				ClearVisual();
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

			if (IsHolstered || IsSwitching || IsUsingItem || _currentIndex < 0) return;

			// у холодного своя логика — комбо с буфером нажатия, см. TickMelee
			if (_inventory[_currentIndex].Data is MeleeWeaponData melee)
			{
				TickMelee(melee, triggerPressed);
				return;
			}

			// оружие ещё достаётся/перезаряжается (_readyToFireTime) — не тратим впустую кулдаун FireRate
			if (Time.time < _readyToFireTime) return;

			WeaponSlot slot = _inventory[_currentIndex];
			bool automatic = slot.Data is FirearmData firearm && firearm.Mode == FireMode.Auto;
			bool wantsToFire = automatic ? triggerHeld : triggerPressed;
			if (!wantsToFire || Time.time < slot.NextFireTime) return;

			slot.NextFireTime = Time.time + (slot.Data.FireRate > 0f ? 1f / slot.Data.FireRate : 0f);
			Shoot();
		}

		// Холодное: нажатие запоминается на InputBufferTime, а удар начинается, как только это разрешено — сразу,
		// если оружие свободно, или в окне комбо (ComboWindow) текущего удара. Так спам ЛКМ даёт ровное 1-2-1-2,
		// а нажатие чуть раньше окна не теряется
		private void TickMelee(MeleeWeaponData melee, bool triggerPressed)
		{
			if (triggerPressed) _attackBufferedUntil = Time.time + melee.InputBufferTime;
			if (Time.time > _attackBufferedUntil || !CanStartSwing()) return;

			_attackBufferedUntil = float.NegativeInfinity;
			StartSwing(melee);
		}

		private bool CanStartSwing()
		{
			if (_isReloading || Time.time < _inventory[_currentIndex].NextFireTime) return false;

			// подстраховка: WeaponReady в конце клипа не пришёл — удар считается законченным по таймеру
			if (_swingActive && Time.time >= _readyToFireTime) EndSwingSequence();

			// пауза после полного комбо
			if (!_swingActive && Time.time < _meleeCooldownEndTime) return false;

			// идёт удар — следующий только в окне комбо; иначе — когда оружие готово (достали, перезарядили)
			return _swingActive ? _comboWindowOpen : Time.time >= _readyToFireTime;
		}

		private void StartSwing(MeleeWeaponData data)
		{
			WeaponSlot slot = _inventory[_currentIndex];
			slot.NextFireTime = Time.time + (data.FireRate > 0f ? 1f / data.FireRate : 0f);

			// продолжаем комбо — следующий удар по кругу; начинаем заново — первый
			_comboStep = _swingActive ? (_comboStep + 1) % Mathf.Max(1, data.ComboLength) : 0;
			// последний удар полного комбо — если у оружия есть пауза после комбо, продолжения не будет
			_comboCompleted = data.ComboCooldown > 0f && _comboStep == Mathf.Max(1, data.ComboLength) - 1;
			_swingActive = true;
			_comboWindowOpen = false;

			// взмах — "занятое" действие, как Draw и Reload: остальное (перезарядка, горелка) ждёт WeaponReady
			// в конце клипа, таймер — подстраховка на случай, если событие не проставлено
			_readyToFireTime = Time.time + FallbackAnimationDuration;

			// взмах всегда один и тот же — попал или нет, решится в кадре контакта (NotifyMeleeHit)
			Debug.Log($"{data.WeaponName}: удар {_comboStep + 1}");
			PlayMeleeAnimation(data, _comboStep);
			if (_currentAudio != null) _currentAudio.PlayAttack();
			Fired?.Invoke(data);
		}

		// тратит патрон текущего оружия, спавнит визуализацию пули и логирует результат. Холодное — удар комбо
		public void Shoot()
		{
			if (IsHolstered || IsSwitching || IsUsingItem || _currentIndex < 0) return;

			WeaponSlot slot = _inventory[_currentIndex];
			WeaponData data = slot.Data;

			if (data is MeleeWeaponData melee)
			{
				if (CanStartSwing()) StartSwing(melee);
				return;
			}

			// защита и для прямых вызовов Shoot() в обход TickFire — то же ограничение по доставанию
			if (Time.time < _readyToFireTime) return;

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
		// true — попали во что-то с коллизией (враг или проп)
		private bool PerformHitscan(WeaponData data)
		{
			if (!TryRaycastFromCamera(data, out RaycastHit hit)) return false;

			SpawnImpactDecal(data, hit);
			PlayHitSound(data, hit);

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

			ApplyHitForce(data, hit);
			return true;
		}

		// толкает Rigidbody цели в точку попадания по направлению взгляда — ящики, обломки и т.п. отлетают от удара/пули.
		// Вызывается ПОСЛЕ урона: если попадание убивает врага, его ragdoll включается прямо внутри TakeDamage
		// (kinematic-кости становятся физическими) — и толчок добивающего выстрела отбрасывает тело.
		// Destroy() срабатывает только в конце кадра, так что уничтожаемые объекты толчок тоже получают.
		// Kinematic-тела (в т.ч. замороженные обломки BreakableDebris и кости живого врага) AddForce просто игнорируют
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
			float radius = data is MeleeWeaponData melee ? melee.HitRadius : 0f;
			return CastFromCamera(data.Range, radius, out hit);
		}

		// буфер под все попадания одного луча — без аллокаций на каждый выстрел/кадр горелки
		private readonly RaycastHit[] _castHits = new RaycastHit[16];

		// луч (radius = 0) или сфера от камеры вперёд — ближайшее попадание, НЕ считая коллайдеры самого игрока.
		// Камера стоит у края капсулы игрока: при движении назад капсула за кадр сдвигается так, что точка
		// камеры оказывается снаружи перед ней, и обычный Raycast попадал в собственную капсулу — оружие
		// наносило урон игроку. Поэтому берём все попадания по дистанции и пропускаем свои
		private bool CastFromCamera(float range, float radius, out RaycastHit hit)
		{
			hit = default;
			if (_movement == null || _movement.CinemachineCameraTarget == null) return false;

			Transform origin = _movement.CinemachineCameraTarget.transform;
			int count = radius > 0f
				? Physics.SphereCastNonAlloc(origin.position, radius, origin.forward, _castHits, range, ~0, QueryTriggerInteraction.Ignore)
				: Physics.RaycastNonAlloc(origin.position, origin.forward, _castHits, range, ~0, QueryTriggerInteraction.Ignore);

			float closest = float.PositiveInfinity;
			Transform self = _movement.transform;
			for (int i = 0; i < count; i++)
			{
				RaycastHit candidate = _castHits[i];
				if (candidate.distance >= closest || candidate.collider.transform.IsChildOf(self)) continue;
				// SphereCastNonAlloc, в отличие от SphereCast, отдаёт и коллайдеры, перекрытые сферой уже в стартовой
				// точке — с distance 0 и без точки/нормали попадания. Как и раньше, такие не считаем попаданием
				if (radius > 0f && candidate.distance <= 0f) continue;

				closest = candidate.distance;
				hit = candidate;
			}

			return closest < float.PositiveInfinity;
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

		// холодное: номер удара в комбо (ComboStepParam) + триггер взмаха (AttackTrigger). Клип удара при попадании
		// запускается позже, в кадре контакта (PlayImpactAnimation)
		private void PlayMeleeAnimation(MeleeWeaponData data, int comboStep)
		{
			if (_currentAnimator == null) return;

			if (!string.IsNullOrEmpty(data.ComboStepParam)) _currentAnimator.SetInteger(data.ComboStepParam, comboStep);

			// сбрасываем триггеры: нажатие или попадание, не "съеденное" переходом (например, пришло во время Draw),
			// иначе осталось бы висеть и запустило бы лишний взмах или клип удара позже
			if (!string.IsNullOrEmpty(data.AttackTrigger)) _currentAnimator.ResetTrigger(data.AttackTrigger);
			if (_currentHasImpactTrigger) _currentAnimator.ResetTrigger(data.ImpactTrigger);

			if (!string.IsNullOrEmpty(data.AttackTrigger)) _currentAnimator.SetTrigger(data.AttackTrigger);
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
