using Interactables;
using Inventory;
using Player;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Menus
{
	// Отладочный "свободный курсор": по клавише (F1) курсор появляется, а игра продолжает идти — можно кликать
	// по объектам в Scene view и Hierarchy, смотреть инспектор и гизмо врагов в движении. Камера в это время
	// не крутится от мыши, ЛКМ/ПКМ не стреляют, а WASD работает — можно ходить и наблюдать одновременно.
	// Вешается на объект, который всегда активен (например, корень UIInterface, рядом с PauseMenu).
	//
	// Курсором управляют и другие системы (пауза, инвентарь, терминал) и при закрытии возвращают "игровой" режим.
	// Поэтому, пока свободный курсор включён, его состояние подтверждается каждый кадр, а при выключении управление
	// возвращается игре, только если поверх не открыт другой UI — тот сам вернёт всё при своём закрытии.
	public class DebugFreeCursor : MonoBehaviour
	{
		[Tooltip("Клавиша включения/выключения свободного курсора")]
		[SerializeField] private Key toggleKey = Key.F1;
		[Tooltip("Работает только в редакторе и Development Build — в релизной сборке клавиша ничего не делает")]
		[SerializeField] private bool onlyInDevBuilds = true;

		private StarterAssetsInputs _input;
		private PlayerCameraFocus _focus;
		private InventoryUI _inventory;
		private bool _active;

		private void Awake()
		{
			if (onlyInDevBuilds && !Debug.isDebugBuild)
			{
				enabled = false;
				return;
			}

			_inventory = FindFirstObjectByType<InventoryUI>();
		}

		private void OnEnable()
		{
			PlayerStart.PlayerSpawned += HandlePlayerSpawned;
			if (_input == null) Bind(FindFirstObjectByType<StarterAssetsInputs>());
		}

		private void OnDisable()
		{
			PlayerStart.PlayerSpawned -= HandlePlayerSpawned;
			if (_active) SetActive(false);
		}

		private void HandlePlayerSpawned(GameObject player)
		{
			Bind(player.GetComponentInChildren<StarterAssetsInputs>());
		}

		private void Bind(StarterAssetsInputs input)
		{
			_input = input;
			_focus = input != null ? input.GetComponentInParent<PlayerCameraFocus>() : null;
		}

		private void Update()
		{
			Keyboard keyboard = Keyboard.current;
			if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) SetActive(!_active);
		}

		// после Update других систем: если пауза/инвентарь при закрытии снова захватили курсор — отпускаем обратно
		private void LateUpdate()
		{
			if (_active) ApplyFreeCursor();
		}

		private void SetActive(bool active)
		{
			_active = active;

			if (active)
			{
				ApplyFreeCursor();
				return;
			}

			if (_input != null) _input.cursorLocked = true;

			// поверх открыт другой UI — он сам вернёт курсор и ввод, когда закроется
			bool otherUIOpen = PauseMenu.IsPaused || (_inventory != null && _inventory.IsOpen) || (_focus != null && _focus.IsBusy);
			if (otherUIOpen) return;

			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
			if (_input == null) return;

			_input.look = Vector2.zero;
			_input.cursorInputForLook = true;
			_input.GameplayInputEnabled = true;
		}

		private void ApplyFreeCursor()
		{
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
			if (_input == null) return;

			// cursorLocked = false — иначе StarterAssetsInputs снова захватит курсор при возврате фокуса в Game view
			_input.cursorLocked = false;
			_input.cursorInputForLook = false;
			_input.look = Vector2.zero; // без этого камера продолжала бы крутиться на последнем движении мыши
			if (_input.GameplayInputEnabled) _input.GameplayInputEnabled = false; // клики по Game view не стреляют
		}
	}
}
