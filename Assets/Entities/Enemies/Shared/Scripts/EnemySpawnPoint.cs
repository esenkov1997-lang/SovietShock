using UnityEngine;
using UnityEngine.AI;

namespace Enemies
{
	// Точка спавна врага (как PlayerStart для игрока): маркер на уровне, на месте которого появляется враг —
	// на NavMesh, лицом туда, куда смотрит маркер (синяя ось Z). Пост собаки (если нет маршрута) — это место и направление.
	//
	// Когда спавнить:
	//   Spawn On Start — при старте сцены;
	//   иначе — по вызову Spawn(): например, из системы триггеров (BaseTrigger → On Entered → EnemySpawnPoint.Spawn),
	//   "вошёл в комнату — появились собаки".
	// Маршрут патруля задаётся здесь, а не в префабе врага: маршрут — часть уровня, префаб — один на все маршруты.
	public class EnemySpawnPoint : MonoBehaviour
	{
		[Tooltip("Префаб врага (например, Dog)")]
		[SerializeField] private GameObject enemyPrefab;
		[Tooltip("Спавнить при старте сцены. Выключи, чтобы спавнить по триггеру (вызовом Spawn)")]
		[SerializeField] private bool spawnOnStart = true;
		[Tooltip("Спавнить только один раз — повторные вызовы Spawn (повторный заход в триггер) игнорируются")]
		[SerializeField] private bool spawnOnce = true;

		[Header("Поведение")]
		[Tooltip("Маршрут патруля для этого врага. Пусто — охраняет пост (место и направление этой точки)")]
		[SerializeField] private PatrolRoute patrolRoute;
		[Tooltip("Враг появляется уже \"знающим\", где игрок, и сразу бежит к нему — например, для засады по триггеру")]
		[SerializeField] private bool alertOnSpawn;

		[Header("Отладка")]
		[Tooltip("Цвет маркера в Scene view")]
		[SerializeField] private Color gizmoColor = new Color(1f, 0.3f, 0.2f);

		private bool _spawned;

		public GameObject SpawnedEnemy { get; private set; }

		private void Start()
		{
			if (spawnOnStart) Spawn();
		}

		// публичный — для триггеров и любых событий уровня
		public void Spawn()
		{
			if (spawnOnce && _spawned) return;

			if (enemyPrefab == null)
			{
				Debug.LogWarning($"{name}: у EnemySpawnPoint не назначен Enemy Prefab", this);
				return;
			}

			// NavMeshAgent должен появиться точно на NavMesh, иначе не сможет ходить ("Failed to create agent")
			if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
			{
				Debug.LogWarning($"{name}: рядом с точкой спавна нет NavMesh (в пределах 2 м) — враг не создан", this);
				return;
			}

			_spawned = true;
			Quaternion rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
			SpawnedEnemy = Instantiate(enemyPrefab, hit.position, rotation);

			// маршрут назначается до Start врага — враг сразу начнёт патруль, а не встанет на пост
			if (patrolRoute != null)
			{
				IPatrolAgent agent = SpawnedEnemy.GetComponent<IPatrolAgent>();
				if (agent != null) agent.SetPatrolRoute(patrolRoute);
				else Debug.LogWarning($"{name}: у {enemyPrefab.name} нет компонента, принимающего маршрут (IPatrolAgent)", this);
			}

			if (alertOnSpawn)
			{
				EnemyPerception perception = SpawnedEnemy.GetComponent<EnemyPerception>();
				if (perception != null) perception.AlertToPlayer();
			}
		}

		// маркер виден всегда: "силуэт" врага, стрелка направления, линия к маршруту
		private void OnDrawGizmos()
		{
			Vector3 position = transform.position;
			Quaternion rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
			Vector3 forward = rotation * Vector3.forward;

			Gizmos.color = gizmoColor;
			Gizmos.matrix = Matrix4x4.TRS(position + Vector3.up * 0.4f, rotation, Vector3.one);
			Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.4f, 0.8f, 1.1f));
			Gizmos.matrix = Matrix4x4.identity;
			Gizmos.DrawRay(position + Vector3.up * 0.4f, forward * 1f);

			if (patrolRoute != null && patrolRoute.Count > 0)
			{
				Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.4f);
				Gizmos.DrawLine(position, patrolRoute.GetPoint(0).position);
			}

#if UNITY_EDITOR
			string label = enemyPrefab != null ? enemyPrefab.name : "нет префаба";
			if (!spawnOnStart) label += " (по триггеру)";
			UnityEditor.Handles.Label(position + Vector3.up * 1.1f, label);
#endif
		}
	}
}
