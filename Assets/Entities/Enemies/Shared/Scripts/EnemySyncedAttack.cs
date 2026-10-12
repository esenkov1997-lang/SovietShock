using Player;
using UnityEngine;
using UnityEngine.AI;
using Weapons;

namespace Enemies
{
	// Синхронная (парная) атака врага — например, собака вцепляется в руку игрока. Вешается на корень врага рядом
	// с EnemyAttack; все настройки — в ассете SyncedAttackData. Мозг (DogAI) в момент обычной атаки спрашивает
	// TryStart — и с шансом из ассета вместо укуса начинается захват.
	//
	// Последовательность:
	//   1. Проверки: игрок может (стоит, не занят), враг перед ним, высота одна, в точке стыковки есть NavMesh
	//      и место за спиной для отскока.
	//   2. Выравнивание (Align Duration): игрок разворачивается к врагу, враг подтягивается в точку стыковки —
	//      туда, где он стоял относительно игрока в сцене Blender (Forward/Side/Yaw Offset).
	//   3. Оба клипа стартуют в один кадр: враг — состояние EnemyStateName в своём аниматоре, игрок — PlayerSyncedAnimation.
	//      Урон — по Animation Event "GrabBite" в клипе врага. Игрок получает управление, когда доиграет свой клип;
	//      враг доигрывает свой (отпускает, отскакивает) и, выйдя из состояния с тегом EnemyStateTag, возвращается к ИИ.
	[RequireComponent(typeof(EnemyMovement))]
	public class EnemySyncedAttack : MonoBehaviour
	{
		[Tooltip("Ассет с настройками захвата (Assets/Create/Enemies/Synced Attack)")]
		[SerializeField] private SyncedAttackData data;

		// если аниматор так и не вошёл в состояние с тегом за это время — состояние/тег не настроены
		private const float EnterTimeout = 0.5f;

		private enum Phase { Idle, Aligning, Playing }

		private EnemyMovement _movement;
		private Animator _animator;
		private EnemyAnimationEvents _events;
		private EnemyHitReaction _hitReaction;
		private int _stateHash;

		private Phase _phase = Phase.Idle;
		private float _phaseTime;
		private bool _enteredState;
		private float _nextAllowedTime;

		private PlayerSyncedAnimation _player;
		private IDamageable _playerDamageable;
		private Vector3 _alignFromPosition;
		private Quaternion _alignFromRotation;
		private Vector3 _alignToPosition;
		private Quaternion _alignToRotation;

		public bool IsActive => _phase != Phase.Idle;

		private void Awake()
		{
			_movement = GetComponent<EnemyMovement>();
			_animator = GetComponentInChildren<Animator>();
			_events = GetComponentInChildren<EnemyAnimationEvents>();
			_hitReaction = GetComponent<EnemyHitReaction>();

			if (data == null)
			{
				Debug.LogWarning($"{name}: у EnemySyncedAttack не назначен Data — захватов не будет", this);
				enabled = false;
				return;
			}

			_stateHash = Animator.StringToHash(data.EnemyStateName);
			bool hasState = _animator != null &&
				(_animator.HasState(0, _stateHash) || _animator.HasState(0, Animator.StringToHash("Base Layer." + data.EnemyStateName)));
			if (!hasState)
			{
				Debug.LogWarning($"{name}: в Animator Controller нет состояния \"{data.EnemyStateName}\" на базовом слое — захватов не будет", this);
				enabled = false;
			}
		}

		private void OnEnable()
		{
			if (_events != null) _events.GrabBiteReceived += HandleBite;
		}

		private void OnDisable()
		{
			if (_events != null) _events.GrabBiteReceived -= HandleBite;
			Abort();
		}

		// Вызывается мозгом в момент, когда он собирается укусить. Бросает шанс и проверяет условия;
		// true — захват начат (обычную атаку не делать)
		public bool TryStart(Transform player)
		{
			if (!enabled || IsActive || player == null || Time.time < _nextAllowedTime) return false;
			if (EnemyDebugSettings.AttacksDisabled || EnemyDebugSettings.PlayerIgnored) return false;
			if (Random.value > data.Chance) return false;

			PlayerSyncedAnimation playerAnimation = player.GetComponent<PlayerSyncedAnimation>();
			if (playerAnimation == null || !playerAnimation.CanStart) return false;
			if (!IsInFrontOfPlayer(playerAnimation)) return false;
			if (Mathf.Abs(transform.position.y - player.position.y) > data.MaxHeightDifference) return false;

			// точка стыковки — относительно игрока, уже развёрнутого к нам
			Vector3 toEnemy = transform.position - player.position;
			toEnemy.y = 0f;
			Quaternion playerFacing = toEnemy.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toEnemy) : player.rotation;
			Vector3 desired = player.position + playerFacing * new Vector3(data.SideOffset, 0f, data.ForwardOffset);
			if (!_movement.TryGetReachablePointNear(desired, 0.3f, out Vector3 alignPoint)) return false;

			Quaternion alignRotation = playerFacing * Quaternion.Euler(0f, data.YawOffset, 0f);
			if (!HasSpaceBehind(alignPoint, alignRotation)) return false;

			if (!playerAnimation.Begin(data.PlayerClip, transform.position, data.AlignDuration,
				data.PlayerArmsYawOffset, data.CameraBlendIn, data.CameraBlendOut))
				return false;

			_player = playerAnimation;
			_playerDamageable = player.GetComponent<IDamageable>();
			_alignFromPosition = transform.position;
			_alignFromRotation = transform.rotation;
			_alignToPosition = alignPoint;
			_alignToRotation = alignRotation;

			_movement.Stop();
			_movement.SetPinned(true); // другие враги не растолкают нас из точки стыковки
			if (_hitReaction != null) _hitReaction.enabled = false; // вздрагивание сорвало бы синхронную анимацию

			_phase = Phase.Aligning;
			_phaseTime = 0f;
			return true;
		}

		// прервать сразу (враг погиб и т.п.) — игрок тоже сразу получает управление
		public void Abort()
		{
			if (!IsActive) return;
			if (_player != null && _player.IsBusy) _player.Abort();
			Finish();
		}

		private void Update()
		{
			if (!IsActive) return;
			_phaseTime += Time.deltaTime;

			if (_phase == Phase.Aligning)
			{
				float t = data.AlignDuration > 0f ? Mathf.Clamp01(_phaseTime / data.AlignDuration) : 1f;
				t = Mathf.SmoothStep(0f, 1f, t);
				_movement.SetPose(Vector3.Lerp(_alignFromPosition, _alignToPosition, t),
					Quaternion.Slerp(_alignFromRotation, _alignToRotation, t));
				if (t < 1f) return;

				// оба клипа — в один кадр
				_animator.CrossFadeInFixedTime(_stateHash, data.EnemyCrossFade, 0, 0f);
				_player.StartClip();
				_phase = Phase.Playing;
				_phaseTime = 0f;
				_enteredState = false;
				return;
			}

			// Playing: ждём, пока аниматор выйдет из состояния захвата (как у поворотов — по тегу)
			bool inState = _animator.IsInTransition(0)
				? _animator.GetNextAnimatorStateInfo(0).IsTag(data.EnemyStateTag)
				: _animator.GetCurrentAnimatorStateInfo(0).IsTag(data.EnemyStateTag);

			if (inState)
			{
				_enteredState = true;
			}
			else if (_enteredState)
			{
				Finish();
				return;
			}
			else if (_phaseTime > EnterTimeout)
			{
				Debug.LogWarning($"{name}: аниматор не перешёл в состояние захвата с тегом \"{data.EnemyStateTag}\" — " +
					"проверь Tag у состояния", this);
				Finish();
				return;
			}

			if (_phaseTime > data.MaxDuration) Abort();
		}

		private void HandleBite()
		{
			if (_phase != Phase.Playing || _playerDamageable == null) return;
			_playerDamageable.TakeDamage(data.DamagePerBite);
		}

		private void Finish()
		{
			_phase = Phase.Idle;
			_player = null;
			_playerDamageable = null;
			_movement.SetPinned(false);
			if (_hitReaction != null) _hitReaction.enabled = true;
			_nextAllowedTime = Time.time + data.Cooldown;
		}

		// игрок смотрит на врага: угол между взглядом камеры (по горизонтали) и направлением на врага
		private bool IsInFrontOfPlayer(PlayerSyncedAnimation player)
		{
			Transform view = Camera.main != null ? Camera.main.transform : player.transform;
			Vector3 forward = view.forward;
			forward.y = 0f;
			Vector3 toEnemy = transform.position - player.transform.position;
			toEnemy.y = 0f;
			if (forward.sqrMagnitude < 0.0001f || toEnemy.sqrMagnitude < 0.0001f) return true;
			return Vector3.Angle(forward, toEnemy) <= data.MaxAngleFromPlayerView;
		}

		// за спиной врага в точке стыковки свободный NavMesh на Required Space Behind — для отскока в конце клипа
		private bool HasSpaceBehind(Vector3 point, Quaternion rotation)
		{
			if (data.RequiredSpaceBehind <= 0f) return true;
			Vector3 behind = point - rotation * Vector3.forward * data.RequiredSpaceBehind;
			return !NavMesh.Raycast(point, behind, out _, NavMesh.AllAreas);
		}
	}
}
