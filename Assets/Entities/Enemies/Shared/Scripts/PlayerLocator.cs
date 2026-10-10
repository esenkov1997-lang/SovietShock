using Player;
using StarterAssets;
using UnityEngine;

namespace Enemies
{
	// Единая точка, через которую враги узнают, где игрок. Игрока спавнит PlayerStart в рантайме,
	// поэтому сослаться на него в инспекторе врага нельзя. Ловим PlayerStart.PlayerSpawned, а если
	// игрок уже был в сцене до подписки (или сцена без PlayerStart) — ищем его сами, не чаще раза в секунду.
	public static class PlayerLocator
	{
		private const float SearchInterval = 1f;

		private static Transform _player;
		private static float _nextSearchTime;

		// Transform капсулы игрока (объект с FirstPersonController) или null, если игрока нет
		public static Transform Player
		{
			get
			{
				if (_player == null && Time.time >= _nextSearchTime)
				{
					_nextSearchTime = Time.time + SearchInterval;
					FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
					if (controller != null) _player = controller.transform;
				}
				return _player;
			}
		}

		// сброс статики при каждом запуске Play Mode (на случай отключённого Domain Reload)
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			_player = null;
			_nextSearchTime = 0f;
			PlayerStart.PlayerSpawned -= HandlePlayerSpawned;
			PlayerStart.PlayerSpawned += HandlePlayerSpawned;
		}

		private static void HandlePlayerSpawned(GameObject player)
		{
			// FirstPersonController висит на PlayerCapsule внутри Player.prefab, не на корне
			FirstPersonController controller = player.GetComponentInChildren<FirstPersonController>();
			if (controller != null) _player = controller.transform;
		}
	}
}
