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

		[Header("Weapon Arms")]
		[Tooltip("Имя слоя в Animator Controller оружия (Override, маска свободной руки, Weight 0)")]
		[SerializeField] private string mantleLayerName = "Mantle";
		[Tooltip("Trigger-параметр, запускающий в этом слое клип подъёма")]
		[SerializeField] private string climbTrigger = "Mantle";
		[Tooltip("Trigger-параметр клипа перелезания. Если его нет в контроллере — используется Climb Trigger")]
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

		// что сейчас анимируется: слой Animator оружия и/или клип на риге пустых рук (видимом или нет)
		private WeaponController _animatingWeapons;
		private bool _rigPlaying;
		private bool _drivingCamera;

		// поза кости камеры на первом кадре клипа в пространстве WeaponHolder (= камеры) — это "взгляд прямо"
		private Quaternion _cameraBoneStartRotation;
		private Vector3 _cameraBoneStartPosition;

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
			StopRig();
			ResetCamera();
		}

		public bool TryBegin(bool isVault)
		{
			AnimationClip clip = isVault ? vaultClip : climbClip;
			if (clip == null) return false;

			bool holdingItem = _itemHold != null && _itemHold.IsHolding;
			bool hasWeapon = _weapons != null && _weapons.HasWeaponInHands && !_weapons.IsHolstered;

			// руки оружия — слой Mantle в его контроллере (если есть)
			bool weaponArms = false;
			if (hasWeapon && !holdingItem)
			{
				string trigger = isVault ? vaultTrigger : climbTrigger;
				weaponArms = _weapons.BeginMantleLayer(mantleLayerName, trigger, layerBlendIn)
					|| (isVault && _weapons.BeginMantleLayer(mantleLayerName, climbTrigger, layerBlendIn));
				if (weaponArms) _animatingWeapons = _weapons;
			}

			// риг пустых рук: видим, если рук больше нет (ни оружия, ни предмета), и нужен невидимым как источник камеры
			bool showEmptyHands = _hands != null && !hasWeapon && !holdingItem && _weapons != null && !_weapons.HasWeaponInHands;
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

			StopRig();

			if (_drivingCamera)
			{
				// плавно гасим то смещение камеры, на котором остановился клип
				_fadeFromRotation = _cameraRotation;
				_fadeFromPosition = _cameraPosition;
				_fadeProgress = 0f;
				_drivingCamera = false;
			}
		}

		private void LateUpdate()
		{
			if (_movement == null) return;

			if (_drivingCamera && _rigPlaying)
			{
				// поза кости камеры в пространстве камеры (WeaponHolder) — и её изменение от первого кадра клипа
				Transform holder = _weapons.transform;
				Quaternion boneRotation = Quaternion.Inverse(holder.rotation) * _handsCameraBone.rotation;
				Vector3 bonePosition = holder.InverseTransformPoint(_handsCameraBone.position);

				Quaternion delta = boneRotation * Quaternion.Inverse(_cameraBoneStartRotation);
				_cameraRotation = Quaternion.Slerp(Quaternion.identity, delta, cameraRotationWeight);
				_cameraPosition = (bonePosition - _cameraBoneStartPosition) * cameraPositionWeight;
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

		private void ApplyCamera(Quaternion rotation, Vector3 position)
		{
			_movement.AnimatedCameraRotationOffset = rotation;
			_movement.AnimatedCameraPositionOffset = position;
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
				// поза кости камеры относительно корня рига (сразу после Instantiate — поза покоя из модели)
				Vector3 bonePosition = Vector3.Scale(root.localScale, root.InverseTransformPoint(_handsCameraBone.position));
				Quaternion inverseBoneRotation = Quaternion.Inverse(Quaternion.Inverse(root.rotation) * _handsCameraBone.rotation);

				Quaternion offsetRotation = _weapons.ViewModelOffsetRotation;
				root.localRotation = offsetRotation * inverseBoneRotation;
				root.localPosition = offsetRotation * -(inverseBoneRotation * bonePosition) + _weapons.ViewModelOffset;
			}
			else
			{
				Debug.LogWarning($"{name}: в Empty Hands Prefab нет кости \"{_weapons.CameraBoneName}\" — руки встанут в WeaponHolder как есть, " +
					"камера из клипа не анимируется", this);
			}

			if (_weapons.WeaponLayer >= 0) SetLayerRecursively(_hands, _weapons.WeaponLayer);
			_hands.SetActive(false);
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
			if (_handsCameraBone != null)
			{
				Transform holder = _weapons.transform;
				_cameraBoneStartRotation = Quaternion.Inverse(holder.rotation) * _handsCameraBone.rotation;
				_cameraBoneStartPosition = holder.InverseTransformPoint(_handsCameraBone.position);
			}

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
