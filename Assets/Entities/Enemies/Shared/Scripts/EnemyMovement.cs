using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Enemies
{
	public enum EnemyGait
	{
		Walk,
		Run
	}

	// "Ноги" любого врага: обёртка над NavMeshAgent. Мозг (DogAI и т.п.) решает КУДА идти и с какой
	// походкой, а этот компонент — КАК: выставляет скорость, не пересчитывает путь без нужды,
	// умеет остановиться и развернуться к цели на месте.
	//
	// Два режима:
	//   Без root motion — NavMeshAgent сам двигает объект, анимация лишь подстраивается под его скорость.
	//   Root motion     — двигает АНИМАЦИЯ (root-кость клипа). Агент только прокладывает путь, говорит,
	//                     куда поворачивать и с какой скоростью идти (параметр Speed в аниматор), и не
	//                     даёт выйти за NavMesh. Лапы не скользят никогда, а рывки, выпады и отскоки
	//                     из боевых клипов перемещают врага по-настоящему. Перемещение из аниматора приносит
	//                     EnemyRootMotion (висит на модели) в ApplyRootMotion.
	[RequireComponent(typeof(NavMeshAgent))]
	public class EnemyMovement : MonoBehaviour
	{
		[Header("Скорости (м/с)")]
		[Tooltip("Скорость шага. Должна совпадать с порогом Walk в Blend Tree (в режиме root motion — " +
			"с реальной скоростью клипа: Blend Tree → Compute Thresholds → Speed)")]
		[SerializeField] private float walkSpeed = 1f;
		[Tooltip("Скорость бега. Должна совпадать с порогом Run в Blend Tree")]
		[SerializeField] private float runSpeed = 6f;

		[Header("Поворот на месте")]
		[Tooltip("Скорость разворота к цели, когда враг стоит (град/с). На ходу без root motion поворотом управляет Angular Speed агента")]
		[SerializeField] private float turnInPlaceSpeed = 360f;

		[Header("Root Motion")]
		[Tooltip("Перемещение задаёт анимация (root-кость), а не NavMeshAgent. Нужен EnemyRootMotion на модели " +
			"и Avatar с указанным Root Node — см. инструкцию")]
		[SerializeField] private bool useRootMotion;
		[Tooltip("Root motion: скорость поворота на ходу шагом (град/с). Меньше — шире дуга. Радиус дуги ≈ скорость / поворот")]
		[SerializeField] private float walkTurnSpeed = 150f;
		[Tooltip("Root motion: скорость поворота на бегу (град/с). Обычно меньше, чем шагом: на бегу разворачиваются по широкой дуге")]
		[SerializeField] private float runTurnSpeed = 100f;
		[Tooltip("Root motion: какую долю скорости враг сохраняет, пока разворачивается к цели сбоку/сзади. " +
			"0 — останавливается и крутится на месте (\"турель\"), 0.3–0.5 — уходит дугой. Возле самой цели доля " +
			"автоматически уменьшается, чтобы враг не нарезал круги вокруг точки")]
		[Range(0f, 1f)]
		[SerializeField] private float minTurnSpeedFactor = 0.4f;
		[Tooltip("Root motion: на сколько метров вперёд по пути враг \"смотрит\". Целится не в ближайший угол пути, а дальше — " +
			"углы срезаются плавной кривой, а не проходятся изломом. 0 — строго по углам")]
		[SerializeField] private float pathLookAhead = 1.5f;
		[Tooltip("Root motion: за сколько метров до цели начинать сбрасывать скорость, чтобы остановиться в точке, а не проскочить её")]
		[SerializeField] private float brakingDistance = 1f;

		[Header("Личное пространство")]
		[Tooltip("Радиус (м) \"личного пространства\" врага. Два врага не подходят друг к другу ближе суммы своих радиусов — " +
			"если оказались ближе, плавно расходятся. Для собаки ~половина длины корпуса. 0 — выключено")]
		[SerializeField] private float separationRadius = 0.45f;
		[Tooltip("Максимальная скорость (м/с), с которой враги расталкивают друг друга. Больше — жёстче, меньше — мягче, но дольше перекрываются")]
		[SerializeField] private float separationSpeed = 2f;

		[Header("Пересчёт пути")]
		[Tooltip("Путь пересчитывается, только если точка назначения сдвинулась дальше этого расстояния (м). " +
			"Экономит CPU при преследовании движущейся цели")]
		[SerializeField] private float repathDistance = 0.3f;

		private const float ArrivalTolerance = 0.2f;
		private const float CornersRefreshInterval = 0.2f;
		private const int MaxPathCorners = 32;

		private readonly Vector3[] _corners = new Vector3[MaxPathCorners];
		private int _cornerCount;
		private float _nextCornersRefreshTime;

		private NavMeshAgent _agent;
		private EnemyTurnInPlace _turnInPlace; // необязательный — повороты на месте анимацией
		private NavMeshPath _testPath;
		private Vector3 _lastDestination;
		private bool _hasDestination;
		private float _desiredSpeed;
		private Vector3 _rootMotionVelocity;

		public NavMeshAgent Agent => _agent;
		public bool UseRootMotion => useRootMotion;
		public float WalkSpeed => walkSpeed;
		public float RunSpeed => runSpeed;

		// фактическая скорость перемещения по земле (м/с) — для громкости шагов, инерции ragdoll и т.п.
		public Vector3 Velocity => useRootMotion ? _rootMotionVelocity : _agent.velocity;
		public float CurrentSpeed => Velocity.magnitude;

		// Что отдавать в параметр Speed аниматора. Без root motion — фактическая скорость агента (анимация
		// подстраивается под движение). С root motion — желаемая скорость (движение подстраивается под анимацию)
		public float AnimatorSpeed => useRootMotion ? _desiredSpeed : _agent.velocity.magnitude;

		// Root motion: с какой скоростью (град/с, + вправо, − влево) враг сейчас поворачивает на ходу —
		// для аниматора (наклон корпуса, клипы поворота в 2D Blend Tree)
		public float TurnRate { get; private set; }

		// закреплён на месте (синхронная атака) — личное пространство его не двигает, соседи обходят его целиком
		public bool IsPinned { get; private set; }

		// сейчас играет анимация поворота на месте (EnemyTurnInPlace)
		public bool IsTurningInPlace => _turnInPlace != null && _turnInPlace.IsTurning;

		// Дошли до точки: путь посчитан и осталось меньше stoppingDistance (+ небольшой допуск).
		// Если точка недостижима (за стеной, в воздухе), агент строит путь до ближайшего достижимого места —
		// тогда "дошли" означает "дошли до конца этого пути".
		public bool HasArrived =>
			_hasDestination && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + ArrivalTolerance;

		private void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_turnInPlace = GetComponent<EnemyTurnInPlace>();
			_testPath = new NavMeshPath();

			if (useRootMotion)
			{
				// агент больше не двигает и не поворачивает объект сам — только считает путь
				_agent.updatePosition = false;
				_agent.updateRotation = false;
			}
		}

		// все живые враги — для "личного пространства". Мёртвые выключают EnemyMovement (DisableAgent) и выпадают сами
		private static readonly List<EnemyMovement> _active = new List<EnemyMovement>();

		private void OnEnable()
		{
			_active.Add(this);
		}

		private void OnDisable()
		{
			_active.Remove(this);
		}

		// после перемещения этого кадра (root motion приходит в OnAnimatorMove, он раньше LateUpdate)
		private void LateUpdate()
		{
			ApplySeparation();
		}

		// Личное пространство: если соседний враг ближе суммы радиусов — отходим на половину перекрытия
		// (вторую половину отойдёт он сам), не быстрее Separation Speed. Сдвиг идёт через агента, поэтому
		// ограничен NavMesh — в стену не вытолкнет. Перебор всех врагов: на десятки врагов это копейки.
		private void ApplySeparation()
		{
			if (separationRadius <= 0f || !_agent.enabled || !_agent.isOnNavMesh || IsPinned) return;

			Vector3 push = Vector3.zero;
			Vector3 position = transform.position;
			foreach (EnemyMovement other in _active)
			{
				if (other == this || other.separationRadius <= 0f) continue;
				// закреплённого соседа не сдвинуть — отходим на всё перекрытие сами, а не на половину
				float share = other.IsPinned ? 1f : 0.5f;

				Vector3 offset = position - other.transform.position;
				offset.y = 0f;
				float minDistance = separationRadius + other.separationRadius;
				float sqrDistance = offset.sqrMagnitude;
				if (sqrDistance >= minDistance * minDistance) continue;

				float distance = Mathf.Sqrt(sqrDistance);
				// стоят точно в одной точке (заспавнили друг в друге) — расходимся в разные стороны, а не в никуда
				Vector3 direction = distance > 0.001f
					? offset / distance
					: Quaternion.Euler(0f, GetInstanceID() % 360, 0f) * Vector3.forward;

				push += direction * ((minDistance - distance) * share);
			}

			if (push.sqrMagnitude < 0.000001f) return;

			Vector3 step = Vector3.ClampMagnitude(push, separationSpeed * Time.deltaTime);
			if (useRootMotion)
			{
				_agent.nextPosition = position + step;
				transform.position = _agent.nextPosition;
			}
			else
			{
				_agent.Move(step);
			}
		}

		private void Update()
		{
			if (useRootMotion) UpdateRootMotionSteering();
		}

		// Root motion: решаем, с какой скоростью "хочет" идти враг и куда повернуться. Само перемещение
		// придёт из анимации, которая играет на этой скорости, в ApplyRootMotion.
		private void UpdateRootMotionSteering()
		{
			// играет клип поворота на месте — он сам вращает врага (root motion), не мешаем ему и не идём
			if (IsTurningInPlace)
			{
				_desiredSpeed = 0f;
				TurnRate = 0f;
				return;
			}

			if (!_hasDestination || _agent.isStopped || _agent.pathPending || HasArrived)
			{
				_desiredSpeed = 0f;
				TurnRate = 0f;
				return;
			}

			Vector3 toTarget = GetLookAheadPoint() - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude < 0.0001f)
			{
				_desiredSpeed = 0f;
				TurnRate = 0f;
				return;
			}

			// поворот на ходу: шагом резче, на бегу плавнее (скорость поворота — между Walk и Run Turn Speed)
			float gait = Mathf.InverseLerp(walkSpeed, runSpeed, _agent.speed);
			float turnSpeed = Mathf.Lerp(walkTurnSpeed, runTurnSpeed, gait);

			Quaternion before = transform.rotation;
			Quaternion targetRotation = Quaternion.LookRotation(toTarget);
			transform.rotation = Quaternion.RotateTowards(before, targetRotation, turnSpeed * Time.deltaTime);
			if (Time.deltaTime > 0f)
				TurnRate = Mathf.DeltaAngle(before.eulerAngles.y, transform.eulerAngles.y) / Time.deltaTime;

			// плавное торможение перед целью
			float distanceLeft = _agent.remainingDistance - _agent.stoppingDistance;
			float braking = brakingDistance > 0f ? Mathf.Clamp01(distanceLeft / brakingDistance) : 1f;

			// Цель сбоку или сзади — не стоим на месте, а уходим дугой, сохраняя часть скорости. Но если до цели
			// меньше пары радиусов дуги, дугой в неё не попасть (враг кружил бы вокруг точки) — там доворачиваемся на месте
			float turnRadius = _agent.speed / Mathf.Max(turnSpeed * Mathf.Deg2Rad, 0.01f);
			float roomToArc = Mathf.Clamp01(distanceLeft / (2f * turnRadius));
			float minFactor = minTurnSpeedFactor * roomToArc;
			float facing = Mathf.Clamp01(Vector3.Dot(transform.forward, toTarget.normalized));

			_desiredSpeed = _agent.speed * braking * Mathf.Lerp(minFactor, 1f, facing);
		}

		// Точка "чуть впереди по пути" (pure pursuit): идём от врага к ближайшему углу пути и, если он ближе
		// Path Look Ahead, продолжаем по следующему отрезку. Целясь в неё, враг заранее заворачивает и срезает угол.
		private Vector3 GetLookAheadPoint()
		{
			Vector3 steeringTarget = _agent.steeringTarget;
			if (pathLookAhead <= 0f) return steeringTarget;

			// углы пути обновляем не каждый кадр: свойство path каждый раз создаёт копию пути
			if (Time.time >= _nextCornersRefreshTime)
			{
				_nextCornersRefreshTime = Time.time + CornersRefreshInterval;
				_cornerCount = _agent.path.GetCornersNonAlloc(_corners);
			}

			int index = -1;
			for (int i = 0; i < _cornerCount; i++)
			{
				if ((_corners[i] - steeringTarget).sqrMagnitude < 0.01f)
				{
					index = i;
					break;
				}
			}
			if (index < 0) return steeringTarget;

			Vector3 from = transform.position;
			float remaining = pathLookAhead;
			for (int i = index; i < _cornerCount; i++)
			{
				float segment = Vector3.Distance(from, _corners[i]);
				if (segment >= remaining) return from + (_corners[i] - from) * (remaining / segment);
				remaining -= segment;
				from = _corners[i];
			}
			return _corners[_cornerCount - 1];
		}

		// Вызывается из EnemyRootMotion.OnAnimatorMove каждый кадр: перемещение и поворот root-кости из клипа.
		// Агент ограничивает точку NavMesh'ем — выпад в стену упрётся в стену, а не пройдёт сквозь неё.
		public void ApplyRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
		{
			if (!useRootMotion || !_agent.enabled) return;

			transform.rotation *= deltaRotation;

			Vector3 previous = transform.position;
			_agent.nextPosition = previous + deltaPosition;
			transform.position = _agent.nextPosition;

			if (Time.deltaTime > 0f)
				_rootMotionVelocity = (transform.position - previous) / Time.deltaTime;
		}

		public void MoveTo(Vector3 destination, EnemyGait gait)
		{
			_agent.speed = gait == EnemyGait.Run ? runSpeed : walkSpeed;
			_agent.isStopped = false;

			if (_hasDestination && (destination - _lastDestination).sqrMagnitude < repathDistance * repathDistance)
				return;

			if (!_agent.SetDestination(destination))
				Debug.LogWarning($"{name}: не удалось построить путь до {destination} — враг стоит на NavMesh?", this);

			_lastDestination = destination;
			_hasDestination = true;
			_nextCornersRefreshTime = 0f; // новый путь — углы перечитать сразу, как только он посчитается
		}

		public void SetPinned(bool pinned)
		{
			IsPinned = pinned;
		}

		// Поставить врага в точку с поворотом напрямую (подтягивание в точку стыковки синхронной атаки).
		// Через агента — точка остаётся на NavMesh, агент не "теряет" врага
		public void SetPose(Vector3 position, Quaternion rotation)
		{
			transform.rotation = rotation;
			if (!_agent.enabled)
			{
				transform.position = position;
				return;
			}

			if (useRootMotion)
			{
				_agent.nextPosition = position;
				transform.position = _agent.nextPosition;
			}
			else
			{
				_agent.Warp(position);
			}
		}

		// насовсем (смерть): агент больше не держит объект на NavMesh и не мешает физике ragdoll
		public void DisableAgent()
		{
			Stop();
			_agent.enabled = false;
			enabled = false;
		}

		public void Stop()
		{
			_desiredSpeed = 0f;
			if (!_agent.enabled) return;
			if (!_agent.isOnNavMesh) return;

			_agent.isStopped = true;
			_agent.ResetPath();
			_hasDestination = false;
		}

		// Разворот к точке, пока стоим (агент сам поворачивает только во время движения).
		// Возвращает true, когда уже смотрим на цель.
		public bool FaceTowards(Vector3 point, float toleranceAngle = 5f)
		{
			return FaceDirection(point - transform.position, toleranceAngle);
		}

		// То же, но к направлению (например, "смотреть туда, куда смотрит точка патруля")
		// turnSpeed — своя скорость поворота (град/с), например медленный осмотр; меньше 0 — Turn In Place Speed.
		// animated — большие углы поворачивать клипом (EnemyTurnInPlace), если он есть; остаток довести программно
		public bool FaceDirection(Vector3 direction, float toleranceAngle = 5f, float turnSpeed = -1f, bool animated = false)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f) return true;

			// клип поворота уже играет — вращает он, ждём
			if (IsTurningInPlace) return false;

			Quaternion targetRotation = Quaternion.LookRotation(direction);

			if (animated && _turnInPlace != null)
			{
				Vector3 forward = transform.forward;
				forward.y = 0f;
				float signedAngle = Vector3.SignedAngle(forward, direction, Vector3.up);
				if (Mathf.Abs(signedAngle) >= _turnInPlace.MinAngle && _turnInPlace.TryStartTurn(signedAngle)) return false;
			}

			float speed = turnSpeed >= 0f ? turnSpeed : turnInPlaceSpeed;
			transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, speed * Time.deltaTime);
			return Quaternion.Angle(transform.rotation, targetRotation) <= toleranceAngle;
		}

		// Можно ли дойти вплотную к target. Цель "достижима", если рядом с ней (в пределах допусков по горизонтали
		// и высоте) есть точка NavMesh, до которой ведёт полный путь. Игрок на высоком ящике — ближайший NavMesh
		// внизу, разница высот больше допуска → недостижим. Игрок на "острове" NavMesh без лестницы — путь неполный.
		public bool CanReach(Vector3 target, float horizontalTolerance, float verticalTolerance)
		{
			float sampleRadius = Mathf.Max(horizontalTolerance, verticalTolerance);
			if (!NavMesh.SamplePosition(target, out NavMeshHit hit, sampleRadius, _agent.areaMask)) return false;

			Vector3 offset = hit.position - target;
			if (Mathf.Abs(offset.y) > verticalTolerance) return false;
			offset.y = 0f;
			if (offset.magnitude > horizontalTolerance) return false;

			if (!NavMesh.CalculatePath(transform.position, hit.position, _agent.areaMask, _testPath)) return false;
			return _testPath.status == NavMeshPathStatus.PathComplete;
		}

		// Достижимая точка NavMesh рядом с desired (в пределах sampleRadius) — например, на кольце вокруг игрока
		public bool TryGetReachablePointNear(Vector3 desired, float sampleRadius, out Vector3 point)
		{
			point = desired;
			if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, sampleRadius, _agent.areaMask)) return false;
			if (!NavMesh.CalculatePath(transform.position, hit.position, _agent.areaMask, _testPath)) return false;
			if (_testPath.status != NavMeshPathStatus.PathComplete) return false;

			point = hit.position;
			return true;
		}

		// Точка для бегства от угрозы: примерно в distance метров, в сторону от threat. Перебираются направления
		// веером (прямо от угрозы, потом всё сильнее вбок) — из тупика враг убежит вдоль стены, а не в стену.
		// Выбирается достижимая точка, которая дальше всего от угрозы. false — убегать некуда (все ближе, чем сейчас).
		public bool TryFindPointAwayFrom(Vector3 threat, float distance, out Vector3 point)
		{
			Vector3 away = transform.position - threat;
			away.y = 0f;
			away = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;

			float[] angles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f };
			float bestDistance = (transform.position - threat).sqrMagnitude;
			bool found = false;
			point = transform.position;

			foreach (float angle in angles)
			{
				Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * away * distance;

				if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, _agent.areaMask)) continue;
				if (!NavMesh.CalculatePath(transform.position, hit.position, _agent.areaMask, _testPath)) continue;
				if (_testPath.status != NavMeshPathStatus.PathComplete) continue;

				float distanceFromThreat = (hit.position - threat).sqrMagnitude;
				if (distanceFromThreat <= bestDistance) continue;

				bestDistance = distanceFromThreat;
				point = hit.position;
				found = true;
			}

			return found;
		}

		// Случайная точка на NavMesh в радиусе от center, до которой реально можно дойти
		// (не на другом этаже без лестницы, не в закрытой комнате). false — за несколько попыток не нашли.
		public bool TryFindReachablePoint(Vector3 center, float radius, out Vector3 point)
		{
			const int attempts = 10;
			for (int i = 0; i < attempts; i++)
			{
				Vector2 offset = Random.insideUnitCircle * radius;
				Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);

				if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, _agent.areaMask)) continue;
				if (!NavMesh.CalculatePath(transform.position, hit.position, _agent.areaMask, _testPath)) continue;
				if (_testPath.status != NavMeshPathStatus.PathComplete) continue;

				point = hit.position;
				return true;
			}

			point = center;
			return false;
		}
	}
}
