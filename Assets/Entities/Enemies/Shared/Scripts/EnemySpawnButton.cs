using System.Collections.Generic;
using Interactables;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Localization;

namespace Enemies
{
	// Отладочная кнопка для тестовых сцен: по E спавнит врага в Spawn Point. Работает через обычную
	// систему интеракции (IInteractable) — повесь на любой объект с коллайдером, как DoorButton.
	// Старые экземпляры (в том числе трупы) удаляются, когда их больше Max Alive, — сцена не захламляется.
	public class EnemySpawnButton : MonoBehaviour, IInteractable
	{
		[Tooltip("Префаб врага (например, Dog)")]
		[SerializeField] private GameObject enemyPrefab;
		[Tooltip("Где и куда лицом появляется враг (синяя ось Z). Если пусто — в 2 м перед кнопкой")]
		[SerializeField] private Transform spawnPoint;
		[Tooltip("Сколько врагов от этой кнопки может быть в сцене одновременно. При превышении удаляется самый старый. 0 — без ограничения")]
		[SerializeField] private int maxAlive = 1;
		[Tooltip("Сразу после появления враг \"знает\", где игрок, и бежит к нему — удобно для теста погони/атаки/урона. " +
			"Выключи, чтобы тестировать спокойное поведение и обнаружение")]
		[SerializeField] private bool alertOnSpawn;
		[Tooltip("Подсказка при наведении. Ключ нужно добавить в таблицу UI")]
		[SerializeField] private LocalizedString prompt = new LocalizedString("UI", "prompt.debug.spawnEnemy");

		private readonly List<GameObject> _spawned = new List<GameObject>();

		public LocalizedString InteractionPrompt => prompt;

		private Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 2f;
		private Quaternion SpawnRotation => spawnPoint != null
			? Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f)
			: Quaternion.Euler(0f, transform.eulerAngles.y + 180f, 0f); // по умолчанию лицом к кнопке (к игроку)

		public void Interact(GameObject interactor)
		{
			if (enemyPrefab == null)
			{
				Debug.LogWarning($"{name}: не назначен Enemy Prefab", this);
				return;
			}

			// NavMeshAgent должен появиться точно на NavMesh, иначе он не сможет ходить ("Failed to create agent")
			if (!NavMesh.SamplePosition(SpawnPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
			{
				Debug.LogWarning($"{name}: рядом с точкой спавна нет NavMesh (в пределах 2 м) — враг не создан", this);
				return;
			}

			_spawned.RemoveAll(enemy => enemy == null);
			while (maxAlive > 0 && _spawned.Count >= maxAlive)
			{
				Destroy(_spawned[0]);
				_spawned.RemoveAt(0);
			}

			GameObject spawned = Instantiate(enemyPrefab, hit.position, SpawnRotation);
			_spawned.Add(spawned);

			if (alertOnSpawn)
			{
				EnemyPerception perception = spawned.GetComponent<EnemyPerception>();
				if (perception != null) perception.AlertToPlayer();
			}
		}

		// точка спавна видна всегда: красный круг и стрелка направления
		private void OnDrawGizmos()
		{
			Vector3 position = SpawnPosition;
			Vector3 forward = SpawnRotation * Vector3.forward;

			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(position + Vector3.up * 0.05f, 0.4f);
			Gizmos.DrawRay(position + Vector3.up * 0.05f, forward * 0.8f);
			Gizmos.DrawLine(transform.position, position);
		}
	}
}
