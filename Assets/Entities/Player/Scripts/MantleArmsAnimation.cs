using StarterAssets;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Weapons;

namespace Player
{
	// Анимация рук и камеры при подъёме на уступ / перелезании (LedgeMantle). Вешается на PlayerCapsule рядом с LedgeMantle.
	//
	// Руки — один клип на обе руки, а играет его то, что сейчас в руках:
	//  • оружие — в его Animator Controller слой Mantle Layer Name (Override, маска СВОБОДНОЙ руки этого оружия,
	//    вес по умолчанию 0) с клипом подъёма по триггеру Climb/Vault Trigger. Какая рука свободна — решает маска
	//    в контроллере конкретного оружия, код не выбирает. Нет такого слоя — руки оружия не анимируются;
	//  • пустые руки — Empty Hands Prefab (риг рук без оружия, например InteractionArms): на время подъёма
	//    показывается у камеры так же, как руки оружия, и играет клип целиком (обе руки);
	//  • в руках физический предмет (ItemHoldController) — рук в кадре нет.
	// Высокий подъём (вис и подтягивание) — исключение: оружие на это время убирается (запрос WeaponController.
	// AddHolsterRequest, без Hide — подъём не ждёт), клип целиком играют пустые руки, в конце оружие достаётся с Draw.
	// Поэтому в первых кадрах клипа высокого подъёма рук в кадре быть не должно — иначе видна подмена.
	//
	// Камера — из кости камеры (CameraBone) того же клипа: её поворот/смещение относительно ПЕРВОГО кадра клипа
	// добавляется поверх обычной камеры (FirstPersonController.AnimatedCamera...). Клип для этого всегда проигрывается
	// на риге пустых рук — когда в руках оружие, риг просто не рисуется. Поэтому камера работает одинаково
	// с оружием, без оружия и даже с оружием без слоя Mantle.
	//
	// Тайминг: движение игрока идёт по скоростям LedgeMantle, клип играет поверх на своей скорости —
	// длину клипа подгоняй под подъём (ClimbSpeed / VaultSpeed).
	//
	// LateUpdate с ранним порядком: после того как Animator выставил кости этого кадра и после WeaponCameraAnchor,
	// но до FirstPersonController.LateUpdate, который применяет смещения к камере
	[DefaultExecutionOrder(-50)]
	[RequireComponent(typeof(LedgeMantle))]
	public class MantleArmsAnimation : MonoBehaviour, IMantleAnimation
	{
		[Header("Clips")]
		[Tooltip("Клип подъёма на уступ (обе руки + кость камеры) — для пустых рук и для камеры. Руки оружия берут клип из своего слоя Mantle")]
		[SerializeField] private AnimationClip climbClip;
		[Tooltip("Клип перелезания. Пусто — перелезание без анимации рук и камеры")]
		[SerializeField] private AnimationClip vaultClip;

		[Header("High Climb (движение тела из клипа)")]
		[Tooltip("Клип высокого подъёма (прыжок → вис → подтягивание) с анимацией КОРНЯ: тело игрока идёт ровно по корню клипа, " +
			"подогнанному под реальный уступ. Нужно событие Grab Event в кадре, где ладони легли на край. Пусто — высокие уступы как обычные")]
		[SerializeField] private AnimationClip highClimbClip;
		[Tooltip("С какой высоты уступа (от ног) включается высокий подъём, м. Ниже — обычный подъём (Climb Clip). Подгонка " +
			"растягивает/сжимает прыжок под реальную высоту — держи диапазон в разумных пределах вокруг высоты, под которую сделан клип")]
		[SerializeField] private float highClimbMinHeight = 2f;
		[Tooltip("Имя Animation Event в клипе высокого подъёма: ладони легли на край")]
		[SerializeField] private string grabEvent = "MantleGrab";
		[Tooltip("Имя корневой кости рига рук (её движение в клипе — движение тела игрока)")]
		[SerializeField] private string rootBoneName = "root";
		[Tooltip("Кости ладоней: в кадре захвата (Grab Event) они лежат на крае уступа — по ним край из клипа совмещается " +
			"с реальной передней гранью. Если ладони чуть не доходят или заходят за край, можно взять другую кость кисти")]
		[SerializeField] private string leftPalmBoneName = "middle_metacarpal_l";
		[SerializeField] private string rightPalmBoneName = "middle_metacarpal_r";

		[Header("Weapon Arms")]
		[Tooltip("Имя слоя подъёма в Animator Controller оружия (Override, маска свободной руки, Weight 0)")]
		[SerializeField] private string mantleLayerName = "Mantle";
		[Tooltip("Trigger-параметр, запускающий в этом слое клип подъёма")]
		[SerializeField] private string climbTrigger = "Mantle";
		[Tooltip("Имя отдельного слоя перелезания (так же: Override, маска, Weight 0). Если такого слоя нет — состояние " +
			"перелезания ищется в слое Mantle Layer Name")]
		[SerializeField] private string vaultLayerName = "Vault";
		[Tooltip("Trigger-параметр клипа перелезания. Если его нет в контроллере — играется клип подъёма (Climb Trigger)")]
		[SerializeField] private string vaultTrigger = "Vault";
		[Tooltip("За сколько секунд слой плавно включается — рука уходит из позы оружия в клип")]
		[SerializeField] private float layerBlendIn = 0.08f;
		[Tooltip("За сколько секунд слой плавно выключается в конце — рука возвращается к оружию")]
		[SerializeField] private float layerBlendOut = 0.15f;

		[Header("Empty Hands")]
		[Tooltip("Риг рук без оружия (FPHandsRig с Animator без контроллера — например, InteractionArms). " +
			"Ставится у камеры так же, как руки оружия. Нужен и для анимации камеры. Пусто — без рук и без камеры из клипа")]
		[SerializeField] private GameObject emptyHandsPrefab;

		[Header("Camera")]
		[Tooltip("Брать движение камеры из кости камеры клипа")]
		[SerializeField] private bool animateCamera = true;
		[Tooltip("Сколько поворота кости камеры передаётся камере: 1 — как в клипе, 0 — без поворота")]
		[Range(0f, 1f)]
		[SerializeField] private float cameraRotationWeight = 1f;
		[Tooltip("Сколько смещения кости камеры передаётся камере. Осторожно: если в клипе камера поднимается вместе с телом " +
			"на всю высоту уступа, а игрока и так поднимает LedgeMantle, подъём удвоится. Обычно 0 или небольшое значение " +
			"для \"живых\" движений, анимированных без самого подъёма")]
		[Range(0f, 1f)]
		[SerializeField] private float cameraPositionWeight = 0f;
		[Tooltip("За сколько секунд камера плавно возвращается в обычное положение после подъёма — если клип не закончился ровно в нуле")]
		[SerializeField] private float cameraBlendOut = 0.15f;

		private WeaponController _weapons;
		private ItemHoldController _itemHold;
		private FirstPersonController _movement;

		private GameObject _hands;
		private Animator _handsAnimator;
		private Renderer[] _handsRenderers;
		private Transform _handsCameraBone;
		private PlayableGraph _graph;
		// корень рига у камеры не двигается: движение корня в клипе — это движение тела, его делает LedgeMantle
		private Transform _handsRootBone;
		private Vector3 _handsRootRest;

		// источник движения тела для высокого подъёма: скрытый экземпляр рига у ног игрока, без поворота камеры —
		// так клип и строился в Blender. Клип ставится на нужное время вручную, корень читается в осях игрока
		private GameObject _rootSampler;
		private Transform _samplerRootBone;
		private Transform _samplerLeftPalm;
		private Transform _samplerRightPalm;
		private PlayableGraph _samplerGraph;
		private AnimationClipPlayable _samplerPlayable;
		private Vector3 _samplerRootStart;

		// что сейчас анимируется: слой Animator оружия и/или клип на риге пустых рук (видимом или нет)
		private WeaponController _animatingWeapons;
		// оружие убрано на время высокого подъёма (наш запрос в WeaponController)
		private bool _holsteringWeapons;
		private bool _rigPlaying;
		private bool _drivingCamera;

		// поза кости камеры на первом кадре клипа в пространстве WeaponHolder (= камеры) — это "взгляд прямо"
		// поза покоя: кость камеры относительно корня рига и сам корень рига в пространстве камеры (после выставления
		// по камере) — от неё считается поворот камеры из клипа и обратный поворот рук
		private Quaternion _cameraBoneRestRotation = Quaternion.identity;
		private Vector3 _cameraBoneRestPosition;
		private Quaternion _rigRestLocalRotation = Quaternion.identity;
		private Vector3 _rigRestLocalPosition;

		// последнее смещение камеры — с него плавно возвращаемся в ноль после подъёма
		private Quaternion _cameraRotation = Quaternion.identity;
		private Vector3 _cameraPosition;
		private Quaternion _fadeFromRotation = Quaternion.identity;
		private Vector3 _fadeFromPosition;
		private float _fadeProgress = 1f;

		public bool DrivesCamera => _drivingCamera;

		private void Awake()
		{
			_weapons = GetComponentInChildren<WeaponController>(true);
			_itemHold = GetComponentInChildren<ItemHoldController>(true);
			_movement = GetComponent<FirstPersonController>();
		}

		// после Awake WeaponController — нужны его слой рендера и смещения рук
		private void Start()
		{
			if (emptyHandsPrefab != null && _weapons != null) CreateEmptyHands();
		}

		private void OnDisable()
		{
			ReleaseWeapons();
			StopRig();
			StopSampler();
			ResetCamera();
		}

		private void OnDestroy()
		{
			StopSampler();
		}

		public bool TryBegin(bool isVault)
		{
			AnimationClip clip = isVault ? vaultClip : climbClip;
			if (clip == null) return false;

			// перелезание: свой слой → состояние Vault в слое подъёма → на крайний случай клип подъёма
			return BeginVisuals(clip, () => isVault
				? _weapons.BeginMantleLayer(vaultLayerName, vaultTrigger, layerBlendIn)
					|| _weapons.BeginMantleLayer(mantleLayerName, vaultTrigger, layerBlendIn)
					|| _weapons.BeginMantleLayer(mantleLayerName, climbTrigger, layerBlendIn)
				: _weapons.BeginMantleLayer(mantleLayerName, climbTrigger, layerBlendIn));
		}

		public bool TryBeginClimbMotion(float ledgeHeight, out MantleClipMotion motion)
		{
			motion = default;
			if (highClimbClip == null || ledgeHeight < highClimbMinHeight || _rootSampler == null || _samplerRootBone == null) return false;

			float grabTime = -1f;
			foreach (AnimationEvent e in highClimbClip.events)
			{
				if (e.functionName == grabEvent) grabTime = e.time;
			}
			if (grabTime < 0f)
			{
				Debug.LogWarning($"{name}: в клипе {highClimbClip.name} нет Animation Event \"{grabEvent}\" — высокий подъём не работает, " +
					"используется обычный", highClimbClip);
				return false;
			}

			StartSampler(highClimbClip);
			Vector3 rootAtGrab = SampleClimbRoot(grabTime);
			float edgeForward = PalmsForward(); // поза на кадре захвата — ладони на крае
			motion = new MantleClipMotion
			{
				Duration = highClimbClip.length,
				GrabTime = grabTime,
				RootAtGrab = rootAtGrab,
				RootAtEnd = SampleClimbRoot(highClimbClip.length),
				EdgeForwardAtGrab = edgeForward,
			};

			// оружие убирается сразу, без Hide (подъём не ждёт), — клип целиком играют пустые руки. Предмет в руках
			// (аптечка) этим же запросом прерывается. Движение тела работает и без рук
			if (_weapons != null)
			{
				_weapons.AddHolsterRequest(this);
				_holsteringWeapons = true;
			}
			BeginVisuals(highClimbClip, null);
			return true;
		}

		public Vector3 SampleClimbRoot(float time)
		{
			if (!_samplerGraph.IsValid() || _samplerRootBone == null) return Vector3.zero;

			_samplerPlayable.SetTime(time);
			_samplerGraph.Evaluate(0f);
			return transform.InverseTransformPoint(_samplerRootBone.position) - _samplerRootStart;
		}

		// где ладони в текущей позе источника по оси "вперёд" от старта — среднее двух (NaN, если костей нет)
		private float PalmsForward()
		{
			if (_samplerLeftPalm == null || _samplerRightPalm == null) return float.NaN;

			float left = (transform.InverseTransformPoint(_samplerLeftPalm.position) - _samplerRootStart).z;
			float right = (transform.InverseTransformPoint(_samplerRightPalm.position) - _samplerRootStart).z;
			return (left + right) * 0.5f;
		}

		// руки (слой оружия или пустые руки) и камера из клипа. beginWeaponLayer — как запустить клип на руках оружия
		// (null — оружие на этот подъём убрано, см. TryBeginClimbMotion)
		private bool BeginVisuals(AnimationClip clip, System.Func<bool> beginWeaponLayer)
		{
			// в руках физический предмет или предмет действия (аптечка доигрывает свой клип — у неё нет слоя подъёма)
			bool holdingItem = (_itemHold != null && _itemHold.IsHolding) || (_weapons != null && _weapons.IsUsingItem);
			bool hasWeapon = _weapons != null && _weapons.HasWeaponInHands && !_weapons.IsHolstered;

			// руки оружия — слой Mantle в его контроллере (если есть)
			bool weaponArms = false;
			if (hasWeapon && !holdingItem && beginWeaponLayer != null)
			{
				weaponArms = beginWeaponLayer();
				if (weaponArms) _animatingWeapons = _weapons;
			}

			// риг пустых рук: видим, если рук больше нет (ни оружия, ни предмета) или оружие убрано на этот подъём,
			// и нужен невидимым как источник камеры. Оружие, убранное кем-то другим (терминал), руки не освобождает
			bool weaponGone = _weapons != null && (!_weapons.HasWeaponInHands || _holsteringWeapons);
			bool showEmptyHands = _hands != null && !hasWeapon && !holdingItem && weaponGone;
			_drivingCamera = animateCamera && _hands != null && _handsCameraBone != null;
			if (showEmptyHands || _drivingCamera) PlayRig(clip, showEmptyHands);

			return weaponArms || showEmptyHands || _drivingCamera;
		}

		public void End()
		{
			if (_animatingWeapons != null)
			{
				_animatingWeapons.EndMantleLayer(layerBlendOut);
				_animatingWeapons = null;
			}

			ReleaseWeapons();
			StopRig();
			StopSampler();

			if (_drivingCamera)
			{
				// плавно гасим то смещение камеры, на котором остановился клип
				_fadeFromRotation = _cameraRotation;
				_fadeFromPosition = _cameraPosition;
				_fadeProgress = 0f;
				_drivingCamera = false;
			}
		}

		// снимаем свой запрос — если оружие больше никто не держит убранным, оно достаётся с Draw
		private void ReleaseWeapons()
		{
			if (!_holsteringWeapons) return;
			_holsteringWeapons = false;
			if (_weapons != null) _weapons.RemoveHolsterRequest(this);
		}

		private void LateUpdate()
		{
			if (_movement == null) return;

			// корень рига у камеры — на месте (Animator только что выставил его по клипу): руки стоят относительно камеры,
			// а движение тела делает LedgeMantle. У клипов без движения корня это ничего не меняет
			if (_rigPlaying && _handsRootBone != null) _handsRootBone.localPosition = _handsRootRest;

			if (_drivingCamera && _rigPlaying)
			{
				// поза кости камеры относительно корня рига — и её отклонение от ПОЗЫ ПОКОЯ: именно по позе покоя руки
				// выставлены к камере (AlignRigToCamera), поэтому и "взгляд прямо" — это покой. Считается в осях корня
				// рига, поэтому не зависит от того, как риг сейчас повёрнут (см. обратный поворот ниже). В оси камеры
				// игрока переводится через общее смещение рук (View Model Offset Rotation)
				Transform rigRoot = _hands.transform;
				Quaternion boneRotation = Quaternion.Inverse(rigRoot.rotation) * _handsCameraBone.rotation;
				Vector3 bonePosition = Vector3.Scale(rigRoot.localScale, rigRoot.InverseTransformPoint(_handsCameraBone.position));

				Quaternion offsetRotation = _weapons.ViewModelOffsetRotation;
				Quaternion localDelta = Quaternion.Inverse(_cameraBoneRestRotation) * boneRotation;
				Quaternion delta = offsetRotation * localDelta * Quaternion.Inverse(offsetRotation);
				Vector3 positionDelta = _rigRestLocalRotation * (bonePosition - _cameraBoneRestPosition);

				_cameraRotation = Quaternion.Slerp(Quaternion.identity, delta, cameraRotationWeight);
				_cameraPosition = positionDelta * cameraPositionWeight;
				ApplyCamera(_cameraRotation, _cameraPosition);
			}
			else if (_fadeProgress < 1f)
			{
				_fadeProgress = cameraBlendOut > 0f ? Mathf.Min(1f, _fadeProgress + Time.deltaTime / cameraBlendOut) : 1f;
				_cameraRotation = Quaternion.Slerp(_fadeFromRotation, Quaternion.identity, _fadeProgress);
				_cameraPosition = Vector3.Lerp(_fadeFromPosition, Vector3.zero, _fadeProgress);
				ApplyCamera(_cameraRotation, _cameraPosition);
			}
		}

		// Камера поворачивается по клипу относительно ТЕЛА. Руки висят на камере и повернулись бы вместе с ней — "прилипли"
		// бы к экрану, а не остались на крае уступа. Поэтому и пустые руки, и модель оружия поворачиваются обратно на тот
		// же угол вокруг камеры: руки на месте в пространстве тела, а камера смотрит вокруг них — как в Blender
		private void ApplyCamera(Quaternion rotation, Vector3 position)
		{
			_movement.AnimatedCameraRotationOffset = rotation;
			_movement.AnimatedCameraPositionOffset = position;

			if (_weapons != null) _weapons.SetAnimatedCameraOffset(rotation, position);

			if (_hands != null && _handsCameraBone != null)
			{
				Quaternion counter = Quaternion.Inverse(rotation);
				_hands.transform.localRotation = counter * _rigRestLocalRotation;
				_hands.transform.localPosition = counter * (_rigRestLocalPosition - position);
			}
		}

		private void ResetCamera()
		{
			_drivingCamera = false;
			_fadeProgress = 1f;
			_cameraRotation = Quaternion.identity;
			_cameraPosition = Vector3.zero;
			if (_movement != null) ApplyCamera(Quaternion.identity, Vector3.zero);
		}

		// пустые руки создаются один раз и прячутся; стоят в пространстве WeaponHolder — как руки оружия:
		// кость камеры рига совмещается с камерой, плюс общее смещение рук из WeaponController
		private void CreateEmptyHands()
		{
			Transform holder = _weapons.transform;
			_hands = Instantiate(emptyHandsPrefab, holder);
			_hands.name = $"{emptyHandsPrefab.name} (Mantle)";
			Transform root = _hands.transform;
			root.localPosition = Vector3.zero;
			root.localRotation = Quaternion.identity;

			_handsAnimator = _hands.GetComponentInChildren<Animator>(true);
			if (_handsAnimator == null)
			{
				Debug.LogWarning($"{name}: в Empty Hands Prefab нет Animator — подъём будет без анимации рук и камеры", this);
				Destroy(_hands);
				_hands = null;
				return;
			}

			// руки у самой камеры, а габариты мешей Unity считает по позе покоя рига — без этого он решает, что руки
			// вне кадра, и перестаёт их обновлять/рисовать (у рук оружия то же самое выставлено в их префабах)
			_handsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			foreach (SkinnedMeshRenderer skin in _hands.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				skin.updateWhenOffscreen = true;
			}
			_handsRenderers = _hands.GetComponentsInChildren<Renderer>(true);

			// если в клипе есть Animation Events (например, старые метки MantleGrab/MantleRelease) — у рук без
			// оружия нет WeaponAnimationEvents, нужен пустой получатель, иначе Unity ругается на каждое событие
			if (_handsAnimator.GetComponent<MantleEventSink>() == null) _handsAnimator.gameObject.AddComponent<MantleEventSink>();

			_handsCameraBone = FindDeep(root, _weapons.CameraBoneName);
			if (_handsCameraBone != null)
			{
				AlignRigToCamera(); // сразу после Instantiate — по позе покоя из модели

				// поза покоя — точка отсчёта поворота камеры из клипа и обратного поворота рук (см. LateUpdate, ApplyCamera)
				_cameraBoneRestRotation = Quaternion.Inverse(root.rotation) * _handsCameraBone.rotation;
				_cameraBoneRestPosition = Vector3.Scale(root.localScale, root.InverseTransformPoint(_handsCameraBone.position));
				_rigRestLocalRotation = root.localRotation;
				_rigRestLocalPosition = root.localPosition;
			}
			else
			{
				Debug.LogWarning($"{name}: в Empty Hands Prefab нет кости \"{_weapons.CameraBoneName}\" — руки встанут в WeaponHolder как есть, " +
					"камера из клипа не анимируется", this);
			}

			_handsRootBone = FindDeep(root, rootBoneName);
			if (_handsRootBone != null) _handsRootRest = _handsRootBone.localPosition;

			if (_weapons.WeaponLayer >= 0) SetLayerRecursively(_hands, _weapons.WeaponLayer);
			_hands.SetActive(false);

			if (highClimbClip != null) CreateRootSampler();
		}

		// скрытый экземпляр того же рига у ног игрока (ребёнок PlayerCapsule, без поворота) — источник движения тела
		// для высокого подъёма. Не рисуется; клип на нём ставится на нужное время вручную (SampleClimbRoot)
		private void CreateRootSampler()
		{
			_rootSampler = Instantiate(emptyHandsPrefab, transform);
			_rootSampler.name = $"{emptyHandsPrefab.name} (Mantle Root Sampler)";
			_rootSampler.transform.localPosition = Vector3.zero;
			_rootSampler.transform.localRotation = Quaternion.identity;

			foreach (Renderer r in _rootSampler.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

			Animator samplerAnimator = _rootSampler.GetComponentInChildren<Animator>(true);
			_samplerRootBone = FindDeep(_rootSampler.transform, rootBoneName);
			_samplerLeftPalm = FindDeep(_rootSampler.transform, leftPalmBoneName);
			_samplerRightPalm = FindDeep(_rootSampler.transform, rightPalmBoneName);
			if (samplerAnimator == null || _samplerRootBone == null)
			{
				Debug.LogWarning($"{name}: в Empty Hands Prefab нет Animator или кости \"{rootBoneName}\" — высокий подъём не работает", this);
				Destroy(_rootSampler);
				_rootSampler = null;
				return;
			}

			// ничего не рисуется — без этого Animator мог бы решить, что риг вне кадра, и не обновлять кости
			samplerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			if (samplerAnimator.GetComponent<MantleEventSink>() == null) samplerAnimator.gameObject.AddComponent<MantleEventSink>();

			// включается только на время высокого подъёма (StartSampler/StopSampler)
			_rootSampler.SetActive(false);
		}

		// клип на источнике движения: ручной режим — время ставит SampleClimbRoot. Запоминаем корень на первом кадре — от него
		// и считается движение тела
		// Источник — ребёнок капсулы: в сцене его корень оказывается на "подъём капсулы + подъём из клипа" и выглядит как
		// двойной подъём. На движение это не влияет — читается смещение относительно капсулы (SampleClimbRoot)
		private void StartSampler(AnimationClip clip)
		{
			StopSampler();
			_rootSampler.SetActive(true);

			Animator samplerAnimator = _rootSampler.GetComponentInChildren<Animator>(true);
			_samplerGraph = PlayableGraph.Create($"{name}_MantleRootSampler");
			_samplerGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
			AnimationPlayableOutput output = AnimationPlayableOutput.Create(_samplerGraph, "Root", samplerAnimator);
			_samplerPlayable = AnimationClipPlayable.Create(_samplerGraph, clip);
			output.SetSourcePlayable(_samplerPlayable);

			_samplerPlayable.SetTime(0);
			_samplerGraph.Evaluate(0f);
			_samplerRootStart = transform.InverseTransformPoint(_samplerRootBone.position);
		}

		// после подъёма — поза первого кадра (корень снова у ног) и выключить, пока не нужен
		private void StopSampler()
		{
			if (_samplerGraph.IsValid())
			{
				_samplerPlayable.SetTime(0);
				_samplerGraph.Evaluate(0f);
				_samplerGraph.Destroy();
			}
			if (_rootSampler != null) _rootSampler.SetActive(false);
		}

		// Один раз, по позе покоя из модели — так же, как руки оружия (WeaponController.AlignToCameraBone): кость камеры
		// рига совмещается с камерой игрока (WeaponHolder), плюс общее смещение рук. Дальше корень рига не двигается —
		// поэтому в клипах подъёма/перелезания кость камеры должна стоять на месте и только вращаться
		private void AlignRigToCamera()
		{
			Transform root = _hands.transform;
			Vector3 bonePosition = Vector3.Scale(root.localScale, root.InverseTransformPoint(_handsCameraBone.position));
			Quaternion inverseBoneRotation = Quaternion.Inverse(Quaternion.Inverse(root.rotation) * _handsCameraBone.rotation);

			Quaternion offsetRotation = _weapons.ViewModelOffsetRotation;
			root.localRotation = offsetRotation * inverseBoneRotation;
			root.localPosition = offsetRotation * -(inverseBoneRotation * bonePosition) + _weapons.ViewModelOffset;
		}

		// клип напрямую через Playables — Animator Controller для рига не нужен (как в PlayerSyncedAnimation).
		// visible = false — риг только как источник камеры: Animator работает, но ничего не рисуется
		private void PlayRig(AnimationClip clip, bool visible)
		{
			StopRig();

			_hands.SetActive(true);
			foreach (Renderer r in _handsRenderers)
			{
				if (r != null) r.enabled = visible;
			}

			_graph = PlayableGraph.Create($"{name}_Mantle");
			_graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
			AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Hands", _handsAnimator);
			AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
			playable.SetTime(0);
			output.SetSourcePlayable(playable);

			// ставим кости ровно на первый кадр и запоминаем позу кости камеры — от неё считается всё движение камеры
			_graph.Evaluate(0f);
			_graph.Play();
			_rigPlaying = true;
			_fadeProgress = 1f; // новый подъём начался посреди возврата камеры — возврат больше не нужен
		}

		private void StopRig()
		{
			if (_graph.IsValid()) _graph.Destroy();
			if (_hands != null) _hands.SetActive(false);
			_rigPlaying = false;
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

		private static void SetLayerRecursively(GameObject target, int layer)
		{
			target.layer = layer;
			foreach (Transform child in target.transform) SetLayerRecursively(child.gameObject, layer);
		}
	}
}
