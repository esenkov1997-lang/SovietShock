using System.Collections.Generic;
using Sound;
using StarterAssets;
using UnityEngine;

namespace Weapons
{
	// Вешается на WeaponHolder — точку в руках игрока, где появляется модель текущего оружия.
	[RequireComponent(typeof(AudioSource))]
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

			public WeaponSlot(WeaponData data)
			{
				Data = data;
				CurrentAmmo = data.MagazineSize;
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
		private bool _firePreviouslyHeld;
		private float _readyToFireTime;
		private AudioSource _audioSource;
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
		private float _healAccumulator;
		private float _burnAccumulator;

		// Опционально подключается извне (см. Inventory.InventoryReloadHandler): CompleteReload спрашивает
		// здесь, сколько патронов реально удалось взять из пула инвентаря (0..amountRequested), и добавляет
		// в магазин ровно столько — если патронов в пуле меньше, чем нужно, перезарядка выйдет частичной.
		// Если не назначено — перезарядка всегда даёт запрошенное количество целиком (старое поведение,
		// для сцен без интеграции с инвентарём). WeaponController намеренно ничего не знает про Inventory —
		// та же идея, что и с TriggerAction/BaseTrigger.
		public System.Func<WeaponData, int, int> ConsumeAmmo;

		// Тоже опционально слушается извне (см. Inventory.InventoryWeaponEquipHandler) — сигнал "это оружие
		// больше не при мне", чтобы соответствующий слот в инвентаре тоже освободился. WeaponController
		// сам не трогает InventoryHolder — по той же причине, что и ConsumeAmmo выше
		public event System.Action<WeaponData> WeaponDropped;

		// данные текущего оружия — читает, например, ProceduralWeaponAnimation для AnimationProfile
		public WeaponData CurrentWeaponData => _currentIndex >= 0 ? _inventory[_currentIndex].Data : null;

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
			_audioSource = GetComponent<AudioSource>();
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

		// вызывать каждый кадр с текущим состоянием ПКМ. Melee никогда не переходит в прицел
		public void SetAiming(bool held)
		{
			if (IsHolstered) held = false;
			WeaponData data = CurrentWeaponData;
			_isAiming = held && data != null && data.Mode != FireMode.Melee;
		}

		// вызывать каждый кадр с сырым (не toggle) состоянием ПКМ — ремонт/прожиг это удержание,
		// а не прицел, поэтому сюда всегда передаётся именно "зажата ли кнопка сейчас".
		// Расходует MagazineSize/CurrentAmmo того же слота, что и обычная стрельба — баллон горелки
		// это тот же "магазин", просто тратится по 1 заряду за каждую единицу лечения/урона
		public void TickHeal(bool held)
		{
			if (IsHolstered) return;

			WeaponSlot slot = _currentIndex >= 0 ? _inventory[_currentIndex] : null;
			WeaponData data = slot?.Data;

			// оружие ещё достаётся/перезаряжается — как и обычная стрельба, лечить в этот момент нельзя
			bool wantsToHeal = held && data != null && data.CanRepair && Time.time >= _readyToFireTime;
			bool healing = wantsToHeal && slot.CurrentAmmo > 0;

			// параметр актуален только для оружия с CanRepair — у остальных Animator Controller
			// про него ничего не знает, SetBool на несуществующий параметр каждый кадр сыпал бы ошибку в консоль
			if (_currentAnimator != null && data != null && data.CanRepair && !string.IsNullOrEmpty(data.HealBoolParam))
			{
				_currentAnimator.SetBool(data.HealBoolParam, healing);
			}

			if (_currentMuzzleFlash != null) _currentMuzzleFlash.SetContinuous(healing);
			if (_currentMuzzle != null) _currentMuzzle.SetContinuous(healing);

			if (healing) PerformHealTick(data, slot);
		}

		// расход баллона идёт всегда, пока зажата кнопка и есть газ — как у настоящего баллона: жмёшь
		// и он травит газ, даже если светишь "в воздух" и никуда не попадаешь. Попадание в цель —
		// это отдельный вопрос, что этот газ дальше делает (лечит IRepairable / жжёт IDamageable)
		private void PerformHealTick(WeaponData data, WeaponSlot slot)
		{
			_healAccumulator += data.RepairAmountPerSecond * Time.deltaTime;
			int whole = Mathf.FloorToInt(_healAccumulator);
			if (whole <= 0) return;
			_healAccumulator -= whole;

			int spend = Mathf.Min(whole, slot.CurrentAmmo);
			slot.CurrentAmmo -= spend;
			if (spend <= 0) return; // баллон уже пуст в момент этого тика — дальше некуда бить/лечить

			if (_movement == null || _movement.CinemachineCameraTarget == null) return;

			Transform origin = _movement.CinemachineCameraTarget.transform;
			if (!Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, data.Range, ~0, QueryTriggerInteraction.Ignore)) return;

			IRepairable repairable = hit.collider.GetComponentInParent<IRepairable>();
			if (repairable != null)
			{
				repairable.Repair(spend); // 1 потраченный заряд = 1 очко ремонта — та же ставка RepairAmountPerSecond
				return;
			}

			IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
			if (damageable != null)
			{
				// урон идёт по своей отдельной ставке (RepairDamagePerSecond), газ на него уже потрачен выше
				ApplyAccumulated(ref _burnAccumulator, data.RepairDamagePerSecond, damageable.TakeDamage);
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

			float targetBlend = _isAiming ? 1f : 0f;
			_aimBlend = Mathf.MoveTowards(_aimBlend, targetBlend, data.AimTransitionSpeed * Time.deltaTime);

			_visualRoot.localPosition = Vector3.Lerp(GetHandPosition(data), data.AimPosition, _aimBlend);
			_visualRoot.localRotation = Quaternion.Slerp(
				Quaternion.Euler(GetHandRotation(data)), Quaternion.Euler(data.AimRotation), _aimBlend);
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

			_currentIndex = index;

			if (_currentVisual != null) Destroy(_currentVisual);

			WeaponData data = _inventory[_currentIndex].Data;
			_currentMuzzle = null;
			_currentMuzzleFlash = null;
			_currentAnimator = null;
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
			}

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
				if (_currentMuzzleFlash != null) _currentMuzzleFlash.SetContinuous(false);
				if (_currentMuzzle != null) _currentMuzzle.SetContinuous(false);

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

			// обычный Melee (удар) патронов не тратит — перезаряжать нечего. Но Melee с CanRepair
			// (баллон горелки) — тратит, и его как раз нужно уметь перезарядить
			if ((data.Mode == FireMode.Melee && !data.CanRepair) || slot.CurrentAmmo >= data.MagazineSize) return;

			_isReloading = true;
			_reloadFinishTime = Time.time + FallbackAnimationDuration; // подстраховка, см. FallbackAnimationDuration
			_readyToFireTime = Mathf.Max(_readyToFireTime, _reloadFinishTime); // на время перезарядки стрелять нельзя

			if (_currentAnimator != null && !string.IsNullOrEmpty(data.ReloadTrigger))
			{
				_currentAnimator.SetTrigger(data.ReloadTrigger);
			}
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
			if (data.Mode != FireMode.Melee) return;

			PerformHitscan(data);
		}

		private void CompleteReload()
		{
			_isReloading = false;
			if (_currentIndex < 0) return;

			WeaponSlot slot = _inventory[_currentIndex];
			WeaponData data = slot.Data;

			// спрашиваем пул патронов именно сейчас (не в момент нажатия R) — если между началом
			// и концом перезарядки сменили оружие, _isReloading уже сброшен в EquipByIndex и сюда
			// не дойдёт, так что патроны из пула не спишутся впустую за незавершённую перезарядку
			int needed = data.MagazineSize - slot.CurrentAmmo;
			int given = ConsumeAmmo != null ? ConsumeAmmo(data, needed) : needed;
			slot.CurrentAmmo += Mathf.Clamp(given, 0, needed);

			Debug.Log($"{data.WeaponName}: перезарядка завершена, патроны {slot.CurrentAmmo}/{data.MagazineSize}");
		}

		// добавляет оружие в инвентарь и сразу берёт его в руки — всегда с полным магазином
		public void PickupWeapon(WeaponData data)
		{
			if (data == null) return;

			_inventory.Add(new WeaponSlot(data));
			EquipByIndex(_inventory.Count - 1);
		}

		// выбрасывает текущее оружие перед игроком с физическим импульсом
		public void DropCurrentWeapon()
		{
			if (IsHolstered || _currentIndex < 0) return;

			WeaponData data = _inventory[_currentIndex].Data;
			Transform origin = dropPoint != null ? dropPoint : transform;

			if (data.PickupPrefab != null)
			{
				GameObject dropped = Instantiate(data.PickupPrefab, origin.position + origin.forward, origin.rotation);

				if (dropped.TryGetComponent(out Rigidbody rb))
				{
					rb.AddForce((origin.forward + Vector3.up * 0.5f) * dropForce, ForceMode.Impulse);
				}
			}

			_inventory.RemoveAt(_currentIndex);
			WeaponDropped?.Invoke(data);

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
			bool wantsToFire = slot.Data.Mode == FireMode.Auto ? triggerHeld : triggerPressed;
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

			if (data.Mode == FireMode.Melee)
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
				PlayMeleeAnimation(data, willHit);
				PlayAttackSound(data);
				return;
			}

			if (slot.CurrentAmmo <= 0)
			{
				Debug.Log($"{data.WeaponName}: патронов нет, нужна перезарядка");
				return;
			}

			slot.CurrentAmmo--;
			Debug.Log($"{data.WeaponName}: выстрел, урон {data.Damage}, патроны {slot.CurrentAmmo}/{data.MagazineSize}");
			SpawnBulletVisual(data);
			PlayAttackAnimation(data);
			PlayAttackSound(data);
			if (_currentMuzzleFlash != null) _currentMuzzleFlash.Flash();
			if (_currentMuzzle != null) _currentMuzzle.Flash();
			ApplyRecoil(data);
			PerformHitscan(data);
		}

		// мгновенное попадание лучом от камеры — визуальная пуля (SpawnBulletVisual) чисто декоративна
		// и на реальное попадание не влияет, ровно как договаривались (без физики пули)
		private void PerformHitscan(WeaponData data)
		{
			if (!TryRaycastFromCamera(data, out RaycastHit hit)) return;

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
			if (data.Mode == FireMode.Melee && data.MeleeHitRadius > 0f)
			{
				return Physics.SphereCast(origin.position, data.MeleeHitRadius, origin.forward, out hit, data.Range, ~0, QueryTriggerInteraction.Ignore);
			}

			return Physics.Raycast(origin.position, origin.forward, out hit, data.Range, ~0, QueryTriggerInteraction.Ignore);
		}

		// звук попадания, как и декаль, зависит от того, во что попали, а не от оружия: одна и та же пуля по дереву
		// и по металлу звучит по-разному. AttackSound из WeaponData — это звук самого выстрела/взмаха и играется
		// отдельно (PlayAttackSound). Для Melee сюда попадаем из NotifyMeleeHit, т.е. ровно в кадр касания в клипе
		private void PlayHitSound(WeaponData data, RaycastHit hit)
		{
			SurfaceSoundSet surface = SoundSurface.Find(hit.collider);
			if (surface == null) surface = defaultHitSurface;

			HitType type = data.Mode == FireMode.Melee ? HitType.Melee : HitType.Bullet;
			SurfaceSoundManager.Instance.PlayHit(surface, type, hit.point);
		}

		// декаль зависит от того, во что попали, а не от оружия: если на цели есть ImpactSurface
		// (например, кровь на манекене) — используем её, иначе берём декаль по умолчанию из WeaponData
		// (для окружения вроде стен, на которые не хочется вешать компонент на каждый объект)
		private void SpawnImpactDecal(WeaponData data, RaycastHit hit)
		{
			ImpactSurface surface = hit.collider.GetComponentInParent<ImpactSurface>();
			GameObject decalPrefab = surface != null && surface.DecalPrefab != null ? surface.DecalPrefab : data.DecalPrefab;
			if (decalPrefab == null) return;

			float lifetime = surface != null && surface.DecalLifetimeOverride > 0f ? surface.DecalLifetimeOverride : data.DecalLifetime;

			// forward декали смотрит "в" поверхность (-normal) — так ориентируется URP Decal Projector;
			// для простого квада с текстурой вместо -hit.normal может понадобиться hit.normal (см. пояснение)
			GameObject decal = Instantiate(decalPrefab, hit.point, Quaternion.LookRotation(-hit.normal));
			decal.transform.SetParent(hit.transform); // едет вместе с поверхностью, если та движется

			Destroy(decal, lifetime);
		}

		// подброс по X — постоянная добавка к прицелу за каждый выстрел; увод по Y — копится внутри
		// очереди в случайно выбранную при её начале сторону, до WeaponData.MaxDrift
		private void ApplyRecoil(WeaponData data)
		{
			if (_movement == null) return;

			float kick = _isAiming ? data.AimRecoilKick : data.RecoilKick;
			float driftPerShot = _isAiming ? data.AimRecoilDriftPerShot : data.RecoilDriftPerShot;
			float maxDrift = _isAiming ? data.AimMaxDrift : data.MaxDrift;

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

			WeaponData data = CurrentWeaponData;
			if (data == null) return;

			float burstGap = data.FireRate > 0f ? (1f / data.FireRate) * 1.5f : 0.5f;
			if (Time.time - _lastShotTime < burstGap) return; // очередь ещё активна, ждём

			float recoverySpeed = _isAiming ? data.AimDriftRecoverySpeed : data.DriftRecoverySpeed;

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

		// Melee: анимация попадания (AttackTrigger) или промаха (AttackMissTrigger). Если отдельный
		// триггер промаха не задан в WeaponData — играется обычная анимация удара, как раньше
		private void PlayMeleeAnimation(WeaponData data, bool hit)
		{
			string trigger = !hit && !string.IsNullOrEmpty(data.AttackMissTrigger) ? data.AttackMissTrigger : data.AttackTrigger;

			if (_currentAnimator != null && !string.IsNullOrEmpty(trigger))
			{
				_currentAnimator.SetTrigger(trigger);
			}
		}

		// PlayOneShot, а не Play — так звуки не обрывают друг друга при быстрой стрельбе/очереди
		private void PlayAttackSound(WeaponData data)
		{
			if (data.AttackSound == null) return;

			_audioSource.pitch = Random.Range(data.AttackPitchRange.x, data.AttackPitchRange.y);
			_audioSource.PlayOneShot(data.AttackSound);
		}

		private void SpawnBulletVisual(WeaponData data)
		{
			if (data.BulletPrefab == null) return;

			Transform origin = _currentMuzzle != null ? _currentMuzzle.transform : transform;
			GameObject bullet = Instantiate(data.BulletPrefab, origin.position, origin.rotation);

			if (bullet.TryGetComponent(out Rigidbody rb))
			{
				rb.linearVelocity = origin.forward * data.BulletSpeed;
			}

			Destroy(bullet, data.BulletLifetime);
		}
	}
}
