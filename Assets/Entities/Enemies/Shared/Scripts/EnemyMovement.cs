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
	// походкой, а этот компонент — КАК: выставляет скорость агента, не пересчитывает путь без нужды,
	// умеет остановиться и развернуться к цели на месте. Сам NavMeshAgent отвечает за поиск пути
	// по запечённому NavMesh, обход углов и ускорение/торможение.
	[RequireComponent(typeof(NavMeshAgent))]
	public class EnemyMovement : MonoBehaviour
	{
		[Header("Скорости (м/с)")]
		[Tooltip("Скорость шага. Должна совпадать с порогом Walk в Blend Tree, иначе лапы будут скользить")]
		[SerializeField] private float walkSpeed = 1.5f;
		[Tooltip("Скорость бега. Должна совпадать с порогом Run в Blend Tree")]
		[SerializeField] private float runSpeed = 6f;

		[Header("Поворот на месте")]
		[Tooltip("Скорость разворота к цели, когда враг стоит (град/с). На ходу поворотом управляет Angular Speed агента")]
		[SerializeField] private float turnInPlaceSpeed = 360f;

		[Header("Пересчёт пути")]
		[Tooltip("Путь пересчитывается, только если точка назначения сдвинулась дальше этого расстояния (м). " +
			"Экономит CPU при преследовании движущейся цели")]
		[SerializeField] private float repathDistance = 0.3f;

		private NavMeshAgent _agent;
		private NavMeshPath _testPath;
		private Vector3 _lastDestination;
		private bool _hasDestination;

		public NavMeshAgent Agent => _agent;
		// фактическая скорость относительно земли — её и отдаём в аниматор
		public float CurrentSpeed => _agent.velocity.magnitude;
		public float WalkSpeed => walkSpeed;
		public float RunSpeed => runSpeed;

		// Дошли до точки: путь посчитан и осталось меньше stoppingDistance (+ небольшой допуск).
		// Если точка недостижима (за стеной, в воздухе), агент строит путь до ближайшего достижимого места —
		// тогда "дошли" означает "дошли до конца этого пути".
		public bool HasArrived =>
			_hasDestination && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + ArrivalTolerance;

		private const float ArrivalTolerance = 0.2f;

		private void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_testPath = new NavMeshPath();
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
