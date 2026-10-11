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
		[Tooltip("Root motion: скорость поворота на ходу к следующему углу пути (град/с)")]
		[SerializeField] private float rootMotionTurnSpeed = 300f;
		[Tooltip("Root motion: за сколько метров до цели начинать сбрасывать скорость, чтобы остановиться в точке, а не проскочить её")]
		[SerializeField] private float brakingDistance = 1f;

		[Header("Пересчёт пути")]
		[Tooltip("Путь пересчитывается, только если точка назначения сдвинулась дальше этого расстояния (м). " +
			"Экономит CPU при преследовании движущейся цели")]
		[SerializeField] private float repathDistance = 0.3f;

		private const float ArrivalTolerance = 0.2f;

		private NavMeshAgent _agent;
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

		// Дошли до точки: путь посчитан и осталось меньше stoppingDistance (+ небольшой допуск).
		// Если точка недостижима (за стеной, в воздухе), агент строит путь до ближайшего достижимого места —
		// тогда "дошли" означает "дошли до конца этого пути".
		public bool HasArrived =>
			_hasDestination && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + ArrivalTolerance;

		private void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_testPath = new NavMeshPath();

			if (useRootMotion)
			{
				// агент больше не двигает и не поворачивает объект сам — только считает путь
				_agent.updatePosition = false;
				_agent.updateRotation = false;
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
			if (!_hasDestination || _agent.isStopped || _agent.pathPending || HasArrived)
			{
				_desiredSpeed = 0f;
				return;
			}

			Vector3 toCorner = _agent.steeringTarget - transform.position;
			toCorner.y = 0f;
			if (toCorner.sqrMagnitude < 0.0001f)
			{
				_desiredSpeed = 0f;
				return;
			}

			Quaternion targetRotation = Quaternion.LookRotation(toCorner);
			transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rootMotionTurnSpeed * Time.deltaTime);

			// плавное торможение перед целью
			float distanceLeft = _agent.remainingDistance - _agent.stoppingDistance;
			float braking = brakingDistance > 0f ? Mathf.Clamp01(distanceLeft / brakingDistance) : 1f;

			// цель сбоку или сзади — сначала разворачиваемся, сбавив ход, иначе враг описывает широкую дугу
			float facing = Mathf.Clamp01(Vector3.Dot(transform.forward, toCorner.normalized));

			_desiredSpeed = _agent.speed * braking * facing;
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
		public bool FaceDirection(Vector3 direction, float toleranceAngle = 5f)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f) return true;

			Quaternion targetRotation = Quaternion.LookRotation(direction);
			transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnInPlaceSpeed * Time.deltaTime);
			return Quaternion.Angle(transform.rotation, targetRotation) <= toleranceAngle;
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
