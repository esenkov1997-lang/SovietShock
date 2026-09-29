using Player;
using StarterAssets;
using UnityEngine;
using Weapons;

namespace HUD
{
	// Общая привязка элементов HUD к игроку. HUD живёт в сессионном UIInterface и не может сослаться на игрока
	// в инспекторе — игрока спавнит PlayerStart уже в рантайме. Поэтому, как и InventoryUI, ловим
	// PlayerStart.PlayerSpawned, а если игрок уже появился раньше, чем включился этот компонент
	// (порядок Awake между объектами сцены не гарантирован), — находим его в сцене.
	public abstract class PlayerHUDBinding : MonoBehaviour
	{
		protected WeaponController Weapons { get; private set; }
		protected FirstPersonController Movement { get; private set; }

		protected virtual void OnEnable()
		{
			PlayerStart.PlayerSpawned += HandlePlayerSpawned;
			if (Weapons == null) Bind(FindFirstObjectByType<WeaponController>());
		}

		protected virtual void OnDisable()
		{
			PlayerStart.PlayerSpawned -= HandlePlayerSpawned;
			Bind(null);
		}

		private void HandlePlayerSpawned(GameObject player)
		{
			// не GetComponent — WeaponController висит глубоко внутри Player.prefab (на WeaponHolder)
			Bind(player.GetComponentInChildren<WeaponController>());
		}

		private void Bind(WeaponController weapons)
		{
			if (Weapons == weapons) return;

			if (Weapons != null) OnUnbind(Weapons);
			Weapons = weapons;
			Movement = weapons != null ? weapons.GetComponentInParent<FirstPersonController>() : null;
			if (Weapons != null) OnBind(Weapons);
		}

		// подписки на события WeaponController — здесь, а не в OnEnable: игрока в момент OnEnable может ещё не быть
		protected virtual void OnBind(WeaponController weapons) { }
		protected virtual void OnUnbind(WeaponController weapons) { }
	}
}
