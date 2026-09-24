using System;
using UnityEngine;

namespace Player
{
	// Точка спавна игрока (как PlayerStart в Unreal) — брось единственный экземпляр этого префаба
	// в сцену вместо всего готового рига игрока. При старте сцены сам заспавнит PlayerPrefab в своей
	// позиции/повороте и уничтожит маркер — он нужен только на этапе редактирования уровня.
	public class PlayerStart : MonoBehaviour
	{
		[Tooltip("Риг игрока (капсула + Cinemachine-камера), спавнится в позиции/повороте этого маркера")]
		[SerializeField] private GameObject playerPrefab;

		// Сессионный UI (Canvas с InventoryUI и т.п.) живёт в сцене отдельно от игрока и не может
		// заранее сослаться на него в инспекторе — игрока ещё не существует на этапе редактирования.
		// Такие системы подписываются на это событие и сами находят нужные компоненты на переданном объекте.
		public static event Action<GameObject> PlayerSpawned;

		private void Awake()
		{
			if (playerPrefab == null)
			{
				Debug.LogError("PlayerStart: не назначен playerPrefab", this);
				return;
			}

			GameObject player = Instantiate(playerPrefab, transform.position, transform.rotation);
			PlayerSpawned?.Invoke(player);
			Destroy(gameObject);
		}

		// упрощённый гуманоид-гизмо — виден в Scene view даже без выделения, чтобы точку спавна
		// было легко найти на уровне
		private void OnDrawGizmos()
		{
			Gizmos.color = Color.green;
			Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.35f);
			Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
			Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, transform.position + Vector3.up * 0.9f + transform.forward * 0.6f);
		}
	}
}
