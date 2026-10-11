using System;
using StarterAssets;
using UnityEngine;
using Weapons;

namespace Enemies
{
	// "Органы чувств" врага: зрение и слух. Сам ничего не решает — только сообщает мозгу (DogAI и т.п.):
	// вижу ли игрока сейчас, насколько я в нём уверен (Awareness 0..1), где видел его последний раз
	// и что услышал. Общий для всех врагов, отличаются только настройки.
	//
	// Зрение: игрок должен быть ближе View Distance, внутри конуса View Angle (или совсем рядом —
	// Proximity Radius, тогда угол не важен), и луч от глаз до игрока не должен упираться в стену.
	// Увидеть ≠ сразу заметить: пока игрок в поле зрения, растёт Awareness (вблизи быстро, вдали
	// медленно, в приседе ещё медленнее). Дошла до 1 — враг "заметил" игрока (IsAlerted).
	public class EnemyPerception : MonoBehaviour
	{
		private const int RaycastBufferSize = 16;

		[Header("Глаза")]
		[Tooltip("Точка, откуда враг смотрит и слушает. Создай пустой объект внутри кости головы — тогда глаза следуют за анимацией. " +
			"Если пусто — точка на высоте Default Eye Height над корнем")]
		[SerializeField] private Transform eyes;
		[Tooltip("Высота глаз над корнем (м), если Eyes не назначен")]
		[SerializeField] private float defaultEyeHeight = 0.6f;

		[Header("Зрение")]
		[Tooltip("Дальность зрения (м)")]
		[SerializeField] private float viewDistance = 15f;
		[Tooltip("Полный угол конуса обзора (градусы). 120 — видит перед собой и немного по бокам")]
		[Range(0f, 360f)]
		[SerializeField] private float viewAngle = 120f;
		[Tooltip("В этом радиусе (м) враг замечает игрока под любым углом — \"чует\" того, кто подкрался вплотную. Стены всё равно мешают")]
		[SerializeField] private float proximityRadius = 1.5f;
		[Tooltip("Слои, которые загораживают обзор (стены, ящики, двери). Стекло и сетки лучше исключить")]
		[SerializeField] private LayerMask sightBlockers = ~(1 << 2); // всё, кроме Ignore Raycast
		[Tooltip("Как часто (сек) проверять видимость лучами. Меньше — точнее, больше — дешевле")]
		[SerializeField] private float sightCheckInterval = 0.1f;

		[Header("Осведомлённость (Awareness)")]
		[Tooltip("Скорость роста Awareness в секунду, пока игрок виден. По X — расстояние (0 = вплотную, 1 = View Distance), " +
			"по Y — прирост в секунду. Значение 4 = заметит за 0.25 сек, 0.5 = за 2 сек")]
		[SerializeField] private AnimationCurve awarenessGainByDistance = new AnimationCurve(
			new Keyframe(0f, 4f), new Keyframe(1f, 0.5f));
		[Tooltip("Множитель скорости роста, если игрок в приседе")]
		[Range(0f, 1f)]
		[SerializeField] private float crouchGainMultiplier = 0.5f;
		[Tooltip("Скорость спада Awareness в секунду, пока игрок не виден")]
		[SerializeField] private float awarenessDecay = 0.2f;
		[Tooltip("Сколько секунд после потери из виду враг \"знает\", куда побежал игрок. Без этого собака, " +
			"потеряв игрока за углом, бежала бы к углу, а не за угол. Стандартный приём в играх")]
		[SerializeField] private float trackAfterLostSight = 1f;

		[Header("Слух")]
		[Tooltip("Чуткость слуха — множитель радиуса любого шума. Собака > 1, глухой зомби < 1")]
		[SerializeField] private float hearingMultiplier = 1f;
		[Tooltip("Во сколько раз уменьшается радиус шума, если между источником и врагом стена")]
		[Range(0f, 1f)]
		[SerializeField] private float wallOcclusion = 0.5f;

		[Header("Отладка (только просмотр)")]
		[SerializeField] private bool canSeePlayer;
		[Range(0f, 1f)]
		[SerializeField] private float awareness;

		private readonly RaycastHit[] _hits = new RaycastHit[RaycastBufferSize];
		private Transform _player;
		private CharacterController _playerCapsule;
		private FirstPersonController _playerMovement;
		private Health _playerHealth;
		private float _nextSightCheckTime;
		private float _lastSeenTime = float.NegativeInfinity;

		public Transform Player => _player;
		// мёртвого игрока враг не видит и не преследует
		public bool IsPlayerDead => _playerHealth != null && _playerHealth.IsDead;
		public bool CanSeePlayer => canSeePlayer;
		public float Awareness => awareness;
		public bool IsAlerted => awareness >= 1f;
		public bool HasLastKnownPosition { get; private set; }
		public Vector3 LastKnownPlayerPosition { get; private set; }
		// куда и как быстро двигался игрок в последний известный момент — поиск начинается в эту сторону
		public Vector3 LastKnownPlayerVelocity { get; private set; }

		// позиция услышанного шума — мозг решает, идти ли проверять
		public event Action<Vector3> NoiseHeard;

		// откуда смотрит игрок (его камера) — для проверки, видит ли он врага или его укрытие
		public Vector3 PlayerEyePosition
		{
			get
			{
				if (_playerMovement != null && _playerMovement.CinemachineCameraTarget != null)
					return _playerMovement.CinemachineCameraTarget.transform.position;
				GetPlayerSightPoints(out _, out Vector3 head);
				return head;
			}
		}

		public Vector3 EyePosition => eyes != null ? eyes.position : transform.position + Vector3.up * defaultEyeHeight;

		private void OnEnable()
		{
			NoiseSystem.NoiseEmitted += HandleNoise;
		}

		private void OnDisable()
		{
			NoiseSystem.NoiseEmitted -= HandleNoise;
		}

		private void Update()
		{
			RefreshPlayer();

			// игрок погиб — забываем о нём, враг возвращается к спокойному поведению
			if (IsPlayerDead)
			{
				canSeePlayer = false;
				if (awareness > 0f || HasLastKnownPosition) ForgetPlayer();
				return;
			}

			if (Time.time >= _nextSightCheckTime)
			{
				_nextSightCheckTime = Time.time + sightCheckInterval;
				canSeePlayer = _player != null && CheckSight();
			}

			if (canSeePlayer)
			{
				_lastSeenTime = Time.time;
				awareness = Mathf.Min(1f, awareness + GetAwarenessGain() * Time.deltaTime);
			}
			else if (Time.time - _lastSeenTime > trackAfterLostSight)
			{
				// спад начинается, только когда враг перестал "вести" игрока. Иначе тревога без зрения
				// (AlertToPlayer — от урона, при спавне) могла бы упасть ниже 1 раньше, чем мозг её заметит
				awareness = Mathf.Max(0f, awareness - awarenessDecay * Time.deltaTime);
			}

			// пока видим (и ещё немного после) — помним точную позицию игрока
			if (_player != null && Time.time - _lastSeenTime <= trackAfterLostSight)
			{
				LastKnownPlayerPosition = _player.position;
				LastKnownPlayerVelocity = _playerCapsule != null ? _playerCapsule.velocity : Vector3.zero;
				HasLastKnownPosition = true;
			}
		}

		// Мгновенная тревога без зрения — например, враг получил пулю. Считаем, что стрелял игрок:
		// враг сразу "знает", где он (дальше это знание ведётся Track After Lost Sight секунд, как после потери из виду)
		public void AlertToPlayer()
		{
			RefreshPlayer();
			if (_player == null || IsPlayerDead) return;

			awareness = 1f;
			_lastSeenTime = Time.time;
			LastKnownPlayerPosition = _player.position;
			LastKnownPlayerVelocity = _playerCapsule != null ? _playerCapsule.velocity : Vector3.zero;
			HasLastKnownPosition = true;
		}

		// мозг вызывает, когда сдался искать — следующая встреча начнётся "с нуля"
		public void ForgetPlayer()
		{
			awareness = 0f;
			HasLastKnownPosition = false;
			LastKnownPlayerVelocity = Vector3.zero;
			_lastSeenTime = float.NegativeInfinity;
		}

		private void RefreshPlayer()
		{
			Transform player = PlayerLocator.Player;
			if (player == _player) return;

			_player = player;
			_playerCapsule = player != null ? player.GetComponent<CharacterController>() : null;
			_playerMovement = player != null ? player.GetComponent<FirstPersonController>() : null;
			_playerHealth = player != null ? player.GetComponent<Health>() : null;
		}

		private bool CheckSight()
		{
			Vector3 eye = EyePosition;
			Vector3 toPlayer = _player.position - transform.position;
			toPlayer.y = 0f;
			float distance = toPlayer.magnitude;

			if (distance > viewDistance) return false;

			if (distance > proximityRadius)
			{
				Vector3 forward = transform.forward;
				forward.y = 0f;
				if (Vector3.Angle(forward, toPlayer) > viewAngle * 0.5f) return false;
			}

			// две точки на игроке — грудь и голова: игрок, присевший за низким ящиком, не виден,
			// а торчащая над укрытием голова — видна
			GetPlayerSightPoints(out Vector3 chest, out Vector3 head);
			return IsPathClear(eye, head, _player) || IsPathClear(eye, chest, _player);
		}

		private void GetPlayerSightPoints(out Vector3 chest, out Vector3 head)
		{
			if (_playerCapsule != null)
			{
				Bounds bounds = _playerCapsule.bounds;
				chest = bounds.center;
				head = new Vector3(bounds.center.x, bounds.max.y - 0.15f, bounds.center.z);
			}
			else
			{
				chest = _player.position + Vector3.up * 1f;
				head = _player.position + Vector3.up * 1.6f;
			}
		}

		private float GetAwarenessGain()
		{
			float distance = Vector3.Distance(transform.position, _player.position);
			float gain = awarenessGainByDistance.Evaluate(Mathf.Clamp01(distance / viewDistance));
			if (_playerMovement != null && _playerMovement.IsCrouching) gain *= crouchGainMultiplier;
			return gain;
		}

		private void HandleNoise(NoiseEvent noise)
		{
			Vector3 ears = EyePosition;
			float radius = noise.Radius * hearingMultiplier;
			float distance = Vector3.Distance(ears, noise.Position);
			if (distance > radius) return;

			Transform source = noise.Source != null ? noise.Source.transform : null;
			if (source != null && source.IsChildOf(transform)) return; // собственный шум не слышим

			if (!IsPathClear(noise.Position, ears, source))
			{
				radius *= wallOcclusion;
				if (distance > radius) return;
			}

			NoiseHeard?.Invoke(noise.Position);
		}

		// Свободен ли отрезок from→to от препятствий. Коллайдеры самого врага и ignoreRoot (игрок,
		// источник шума) не считаются препятствием. Триггеры игнорируются.
		private bool IsPathClear(Vector3 from, Vector3 to, Transform ignoreRoot)
		{
			Vector3 direction = to - from;
			float distance = direction.magnitude;
			if (distance < 0.001f) return true;

			int count = Physics.RaycastNonAlloc(from, direction / distance, _hits, distance, sightBlockers, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				Transform hit = _hits[i].transform;
				if (hit.IsChildOf(transform)) continue;
				if (ignoreRoot != null && hit.IsChildOf(ignoreRoot)) continue;
				return false;
			}
			return true;
		}

		private void OnValidate()
		{
			viewDistance = Mathf.Max(0f, viewDistance);
			proximityRadius = Mathf.Clamp(proximityRadius, 0f, viewDistance);
			sightCheckInterval = Mathf.Max(0f, sightCheckInterval);
		}

#if UNITY_EDITOR
		// Scene view при выделенном враге: голубой конус — зрение, голубой круг — "чутьё" вплотную,
		// линия до игрока — зелёная, если видит, красная, если нет. В Play Mode над головой шар Awareness:
		// белый — спокоен, жёлтый — что-то подозревает, красный — заметил.
		private void OnDrawGizmosSelected()
		{
			Vector3 eye = EyePosition;
			Vector3 forward = transform.forward;
			forward.y = 0f;
			forward.Normalize();

			UnityEditor.Handles.color = new Color(0.3f, 0.8f, 1f, 0.8f);
			Vector3 arcStart = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * forward;
			UnityEditor.Handles.DrawWireArc(eye, Vector3.up, arcStart, viewAngle, viewDistance);
			if (viewAngle < 360f)
			{
				Vector3 arcEnd = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * forward;
				UnityEditor.Handles.DrawLine(eye, eye + arcStart * viewDistance);
				UnityEditor.Handles.DrawLine(eye, eye + arcEnd * viewDistance);
			}
			UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, proximityRadius);

			if (Application.isPlaying && _player != null)
			{
				GetPlayerSightPoints(out Vector3 chest, out _);
				Gizmos.color = canSeePlayer ? Color.green : Color.red;
				Gizmos.DrawLine(eye, chest);

				if (HasLastKnownPosition)
				{
					Gizmos.color = Color.magenta;
					Gizmos.DrawWireCube(LastKnownPlayerPosition + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f));
				}
			}
		}

		private void OnDrawGizmos()
		{
			if (!Application.isPlaying) return;

			Gizmos.color = IsAlerted ? Color.red : Color.Lerp(Color.white, Color.yellow, awareness);
			Gizmos.DrawSphere(EyePosition + Vector3.up * 0.6f, 0.08f + awareness * 0.12f);
		}
#endif
	}
}
