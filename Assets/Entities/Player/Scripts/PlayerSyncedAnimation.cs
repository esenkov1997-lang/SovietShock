using System;
using Interactables;
using Inventory;
using StarterAssets;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Weapons;

namespace Player
{
	// Сторона игрока в синхронной анимации (собака вцепилась в руку и т.п.). Вешается на PlayerCapsule.
	//
	// На время анимации: управление выключено (тем же способом, что у PlayerCameraFocus — выключением
	// FirstPersonController), оружие убрано, вместо него показываются "руки без оружия" (Arms Prefab — тот же риг
	// рук, что у оружия, но прикреплён к ступням игрока, а не к камере), и на них играет клип взаимодействия.
	// Настоящая камера следует за костью камеры из клипа — рывки и взгляд вниз, поставленные в Blender, игрок видит как есть.
	//
	// Фазы:
	//   Aligning  — игрок разворачивается лицом к врагу, руки стоят на первом кадре клипа (на паузе),
	//               камера плавно перетекает из обычного взгляда в этот кадр. Длится, пока враг подтягивается в точку.
	//   Playing   — клип играет (StartClip вызывает враг, в один кадр со своей анимацией).
	//   Releasing — клип доигран: камера плавно возвращается в голову, мышь продолжает с того же взгляда.
	//
	// Ориентация кости камеры в клипе может отличаться от камеры Unity (оси Blender, развороты при экспорте),
	// поэтому за "взгляд прямо" принимается поза кости на ПЕРВОМ кадре клипа, а дальше берётся только изменение от неё.
	[RequireComponent(typeof(FirstPersonController))]
	public class PlayerSyncedAnimation : MonoBehaviour
	{
		[Tooltip("Префаб рук без оружия: модель FPHands с Animator (Avatar рук, Controller не нужен). " +
			"Создаётся один раз и прячется, пока не нужен")]
		[SerializeField] private GameObject armsPrefab;
		[Tooltip("Имя кости камеры в риге рук")]
		[SerializeField] private string cameraBoneName = "CameraBone";
		[Tooltip("Кривая плавности разворота игрока и переходов камеры")]
		[SerializeField] private AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		private enum Phase { Idle, Aligning, Playing, Releasing }

		private FirstPersonController _movement;
		private StarterAssetsInputs _input;
		private LedgeMantle _ledgeMantle;
		private PlayerCameraFocus _focus;
		private PlayerInteractor _interactor;
		private WeaponController[] _weapons;
		private Health _health;
		private Transform _cameraRoot;
		private Vector3 _headLocalPosition;

		private GameObject _arms;
		private Animator _armsAnimator;
		private Transform _cameraBone;
		private PlayableGraph _graph;
		private AnimationClipPlayable _clipPlayable;
		private float _clipLength;

		private Phase _phase = Phase.Idle;
		private float _alignElapsed;
		private float _alignDuration;
		private Quaternion _yawFrom;
		private Quaternion _yawTo;

		private float _blendInElapsed;
		private float _blendInDuration;
		private Vector3 _cameraStartPosition;
		private Quaternion _cameraStartRotation;
		private Quaternion _boneCorrection;
		private bool _boneCorrectionCaptured;

		private float _releaseElapsed;
		private float _releaseDuration;
		private Vector3 _releaseFromPosition;
		private Quaternion _releaseFromRotation;

		public bool IsBusy => _phase != Phase.Idle;
		// куда игрок будет смотреть после разворота — по этому направлению враг считает свою точку стыковки
		public Quaternion AlignedRotation => _phase == Phase.Idle ? transform.rotation : _yawTo;

		public event Action Started;
		public event Action Ended;

		// можно ли сейчас начать: стоит на земле, не в приседе (камера в клипе — на высоте стоящего),
		// не занят терминалом/залезанием, жив
		public bool CanStart =>
			_phase == Phase.Idle && _arms != null && _armsAnimator != null && _cameraBone != null &&
			_movement.enabled && _movement.Grounded && !_movement.IsCrouching &&
			(_focus == null || !_focus.IsBusy) &&
			(_ledgeMantle == null || !_ledgeMantle.IsClimbing) &&
			(_health == null || !_health.IsDead);

		private void Awake()
		{
			_movement = GetComponent<FirstPersonController>();
			_input = GetComponent<StarterAssetsInputs>();
			_ledgeMantle = GetComponent<LedgeMantle>();
			_focus = GetComponent<PlayerCameraFocus>();
			_health = GetComponent<Health>();
			_interactor = GetComponentInChildren<PlayerInteractor>(true);
			_weapons = GetComponentsInChildren<WeaponController>(true);

			if (_movement.CinemachineCameraTarget != null)
			{
				_cameraRoot = _movement.CinemachineCameraTarget.transform;
				_headLocalPosition = _cameraRoot.localPosition;
			}

			if (armsPrefab == null)
			{
				Debug.LogWarning($"{name}: у PlayerSyncedAnimation не назначен Arms Prefab — захваты врагов не сработают", this);
				return;
			}

			_arms = Instantiate(armsPrefab, transform);
			_arms.SetActive(false);
			_armsAnimator = _arms.GetComponentInChildren<Animator>(true);
			_cameraBone = FindDeep(_arms.transform, cameraBoneName);

			if (_armsAnimator == null)
				Debug.LogWarning($"{name}: в Arms Prefab нет Animator", this);
			if (_cameraBone == null)
				Debug.LogWarning($"{name}: в Arms Prefab нет кости \"{cameraBoneName}\"", this);
		}

		private void OnEnable()
		{
			if (_health != null) _health.OnDeath.AddListener(Abort);
		}

		private void OnDisable()
		{
			if (_health != null) _health.OnDeath.RemoveListener(Abort);
			Abort();
		}

		// Начать: игрок за alignDuration разворачивается лицом к faceTarget, руки встают на первый кадр клипа,
		// камера плавно переходит в него. Сам клип запустит StartClip — враг вызывает его в один кадр со своей анимацией.
		// groundHeight — мировая высота пола, на котором стоит враг: от неё ставятся руки. В Blender оба персонажа стоят
		// на одном полу (Z = 0), а точка отсчёта капсулы игрока в Unity — ~11 см над полом (низ капсулы ниже центра
		// на 7 см + Skin Width). Без этого руки оказались бы выше, чем в сцене Blender, относительно врага.
		public bool Begin(AnimationClip clip, Vector3 faceTarget, float groundHeight, float alignDuration, float armsYawOffset,
			float cameraBlendIn, float cameraBlendOut)
		{
			if (!CanStart || clip == null || _cameraRoot == null) return false;

			InventoryUI inventory = FindFirstObjectByType<InventoryUI>();
			if (inventory != null && inventory.IsOpen) inventory.Close();

			LockPlayer();

			_yawFrom = transform.rotation;
			Vector3 toTarget = faceTarget - transform.position;
			toTarget.y = 0f;
			_yawTo = toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget) : _yawFrom;
			_alignDuration = Mathf.Max(0f, alignDuration);
			_alignElapsed = 0f;

			_cameraRoot.GetPositionAndRotation(out _cameraStartPosition, out _cameraStartRotation);
			_blendInDuration = Mathf.Max(0f, cameraBlendIn);
			_blendInElapsed = 0f;
			_releaseDuration = Mathf.Max(0f, cameraBlendOut);
			_boneCorrectionCaptured = false;

			_arms.transform.localPosition = new Vector3(0f, groundHeight - transform.position.y, 0f);
			_arms.transform.localRotation = Quaternion.Euler(0f, armsYawOffset, 0f);
			_arms.SetActive(true);

			// клип играется напрямую (Playables) — отдельный Animator Controller под каждое взаимодействие не нужен
			_graph = PlayableGraph.Create($"{name}_SyncedAnimation");
			_graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
			AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Arms", _armsAnimator);
			_clipPlayable = AnimationClipPlayable.Create(_graph, clip);
			_clipPlayable.SetTime(0);
			_clipPlayable.SetSpeed(0); // на паузе на первом кадре до StartClip
			output.SetSourcePlayable(_clipPlayable);
			_graph.Play();
			_clipLength = clip.length;

			_phase = Phase.Aligning;
			Started?.Invoke();
			return true;
		}

		// враг закончил подтягиваться — клипы обоих стартуют в этот кадр
		public void StartClip()
		{
			if (_phase != Phase.Aligning) return;

			transform.rotation = _yawTo;
			_clipPlayable.SetTime(0);
			_clipPlayable.SetSpeed(1);
			_phase = Phase.Playing;
		}

		// прервать сразу (смерть игрока, враг погиб/исчез посреди захвата) — без плавного возврата камеры
		public void Abort()
		{
			if (_phase == Phase.Idle) return;

			StopClip();
			if (_cameraRoot != null)
			{
				Quaternion world = _cameraRoot.rotation;
				SyncLookAngles(world);
				_cameraRoot.localPosition = _headLocalPosition;
				_cameraRoot.localRotation = Quaternion.Euler(_movement.CameraPitch, 0f, 0f);
			}
			Finish();
		}

		private void Update()
		{
			if (_phase == Phase.Aligning)
			{
				_alignElapsed += Time.deltaTime;
				float t = _alignDuration > 0f ? blendCurve.Evaluate(Mathf.Clamp01(_alignElapsed / _alignDuration)) : 1f;
				transform.rotation = Quaternion.Slerp(_yawFrom, _yawTo, t);
			}
			else if (_phase == Phase.Playing && _clipPlayable.GetTime() >= _clipLength)
			{
				BeginRelease();
			}
		}

		// после того как аниматор выставил кости этого кадра
		private void LateUpdate()
		{
			switch (_phase)
			{
				case Phase.Aligning:
				case Phase.Playing:
					UpdateAnimatedCamera();
					break;
				case Phase.Releasing:
					UpdateRelease();
					break;
			}
		}

		private void UpdateAnimatedCamera()
		{
			// первый кадр после Begin — кость стоит на кадре 0 клипа: это и есть "взгляд прямо"
			if (!_boneCorrectionCaptured)
			{
				Quaternion boneLocal = Quaternion.Inverse(transform.rotation) * _cameraBone.rotation;
				_boneCorrection = Quaternion.Inverse(boneLocal);
				_boneCorrectionCaptured = true;
			}

			Vector3 animatedPosition = _cameraBone.position;
			Quaternion animatedRotation = _cameraBone.rotation * _boneCorrection;

			_blendInElapsed += Time.deltaTime;
			float w = _blendInDuration > 0f ? blendCurve.Evaluate(Mathf.Clamp01(_blendInElapsed / _blendInDuration)) : 1f;

			_cameraRoot.SetPositionAndRotation(
				Vector3.Lerp(_cameraStartPosition, animatedPosition, w),
				Quaternion.Slerp(_cameraStartRotation, animatedRotation, w));
		}

		// Клип доигран (руки ушли из кадра). Корпус игрока разворачивается туда, куда смотрит камера, а наклон
		// камеры передаётся контроллеру — мышь продолжит ровно с этого взгляда. Остаётся плавно вернуть камеру в голову.
		private void BeginRelease()
		{
			_cameraRoot.GetPositionAndRotation(out _releaseFromPosition, out _releaseFromRotation);
			StopClip();
			SyncLookAngles(_releaseFromRotation);
			_releaseElapsed = 0f;
			_phase = Phase.Releasing;
		}

		private void UpdateRelease()
		{
			_releaseElapsed += Time.deltaTime;
			float t = _releaseDuration > 0f ? blendCurve.Evaluate(Mathf.Clamp01(_releaseElapsed / _releaseDuration)) : 1f;

			Vector3 headPosition = transform.TransformPoint(_headLocalPosition);
			Quaternion headRotation = transform.rotation * Quaternion.Euler(_movement.CameraPitch, 0f, 0f);
			_cameraRoot.SetPositionAndRotation(
				Vector3.Lerp(_releaseFromPosition, headPosition, t),
				Quaternion.Slerp(_releaseFromRotation, headRotation, t));

			if (t < 1f) return;

			_cameraRoot.localPosition = _headLocalPosition;
			_cameraRoot.localRotation = Quaternion.Euler(_movement.CameraPitch, 0f, 0f);
			Finish();
		}

		// поворот капсулы — по yaw взгляда, наклон — в pitch контроллера (он сам ограничит его своими пределами)
		private void SyncLookAngles(Quaternion cameraWorldRotation)
		{
			Vector3 euler = cameraWorldRotation.eulerAngles;
			transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
			_movement.CameraPitch = Mathf.DeltaAngle(0f, euler.x);
		}

		private void StopClip()
		{
			if (_graph.IsValid()) _graph.Destroy();
			if (_arms != null) _arms.SetActive(false);
		}

		private void Finish()
		{
			_phase = Phase.Idle;
			UnlockPlayer();
			Ended?.Invoke();
		}

		// то же, что у PlayerCameraFocus: управление выключается выключением FirstPersonController,
		// оружие убирается, боевой ввод и обзор мышью глушатся
		private void LockPlayer()
		{
			_movement.StopMotion();
			_movement.enabled = false;
			if (_interactor != null) _interactor.enabled = false;
			foreach (WeaponController weapons in _weapons)
				if (weapons != null) weapons.AddHolsterRequest(this);

			if (_input == null) return;
			_input.move = Vector2.zero;
			_input.look = Vector2.zero;
			_input.jump = false;
			_input.sprint = false;
			_input.cursorInputForLook = false;
			_input.GameplayInputEnabled = false;
		}

		private void UnlockPlayer()
		{
			if (_input != null)
			{
				_input.look = Vector2.zero;
				_input.interact = false;
				_input.jump = false;
				_input.cursorInputForLook = true;
				_input.GameplayInputEnabled = true;
			}

			_movement.enabled = true;
			if (_interactor != null) _interactor.enabled = true;
			foreach (WeaponController weapons in _weapons)
				if (weapons != null) weapons.RemoveHolsterRequest(this);
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
	}
}
