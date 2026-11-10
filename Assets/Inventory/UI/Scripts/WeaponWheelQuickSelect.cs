using Interactables;
using Menus;
using Player;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inventory
{
	// Быстрый выбор оружия колесом прямо в игре: зажал клавишу (Player/WeaponWheel, по умолчанию Q) — на экране то
	// же колесо, что и в инвентаре; движение мыши (или стик) выбирает сектор, камера в это время не крутится;
	// отпустил — в руки берётся выбранное, пустой сектор — убрать оружие (пустые руки). Отпустил в центре — ничего
	// не меняется. Время не замедляется: мир живёт дальше.
	//
	// Вешается на объект, который всегда активен (например, на канвас инвентаря). Колесо (WeaponWheelView) должно
	// лежать ВНЕ панели инвентаря — его включает и экран инвентаря, и этот компонент.
	public class WeaponWheelQuickSelect : MonoBehaviour
	{
		[Tooltip("Действие колеса (StarterAssets → Player/WeaponWheel): пока зажато — колесо на экране")]
		[SerializeField] private InputActionReference wheelAction;
		[Tooltip("Действие выбора сектора (StarterAssets → Player/WeaponWheelSelect): движение мыши без инверсии и масштаба " +
			"(не Look — у того настройки камеры: инверсия Y, чувствительность) и правый стик")]
		[SerializeField] private InputActionReference selectAction;
		[Tooltip("Колесо (WeaponWheelView) — объект вне панели инвентаря")]
		[SerializeField] private WeaponWheelView wheel;
		[Tooltip("Экран инвентаря — пока он открыт, быстрое колесо не открывается. Пусто — ищется в сцене")]
		[SerializeField] private InventoryUI inventoryUI;

		[Header("Selection")]
		[Tooltip("Насколько надо сдвинуть мышь от центра, чтобы выбрать сектор (в пикселях движения мыши). Меньше — центр, ничего не выбрано")]
		[Min(0f)]
		[SerializeField] private float deadZone = 30f;
		[Tooltip("Дальше этого \"указатель\" не уходит — чтобы вернуться к другому сектору не пришлось долго вести мышь обратно")]
		[Min(1f)]
		[SerializeField] private float maxDistance = 100f;
		[Tooltip("Стик геймпада: насколько отклонить, чтобы выбрать сектор (0..1)")]
		[Range(0.1f, 1f)]
		[SerializeField] private float stickThreshold = 0.5f;

		// колесо открыто прямо сейчас (экран инвентаря по этому не открывается поверх)
		public static bool IsActive { get; private set; }

		private StarterAssetsInputs _input;
		private PlayerCameraFocus _focus;
		private InventoryHolder _inventory;
		// "указатель" колеса: накопленное движение мыши от центра (или направление стика)
		private Vector2 _pointer;

		private void Awake()
		{
			if (inventoryUI == null) inventoryUI = FindFirstObjectByType<InventoryUI>();
			if (wheel == null) Debug.LogError($"{nameof(WeaponWheelQuickSelect)} на {name}: не назначено колесо (Wheel)", this);
			if (wheelAction == null) Debug.LogError($"{nameof(WeaponWheelQuickSelect)} на {name}: не назначено действие Wheel Action", this);
		}

		private void OnEnable()
		{
			PlayerStart.PlayerSpawned += HandlePlayerSpawned;
			if (_input == null) Bind(FindFirstObjectByType<StarterAssetsInputs>());
		}

		private void OnDisable()
		{
			PlayerStart.PlayerSpawned -= HandlePlayerSpawned;
			if (IsActive) Close(select: false);
		}

		private void HandlePlayerSpawned(GameObject player)
		{
			Bind(player.GetComponentInChildren<StarterAssetsInputs>());
		}

		private void Bind(StarterAssetsInputs input)
		{
			_input = input;
			_focus = input != null ? input.GetComponentInParent<PlayerCameraFocus>() : null;
			_inventory = input != null ? input.GetComponentInParent<InventoryHolder>() : null;
		}

		private void Update()
		{
			if (wheel == null || wheelAction == null) return;

			if (!IsActive)
			{
				if (wheelAction.action.WasPressedThisFrame() && CanOpen()) Open();
				return;
			}

			// поверх открылось что-то другое (пауза, терминал, смерть) — закрываемся, ничего не выбирая
			if (OtherUIOpen())
			{
				Close(select: false);
				return;
			}

			if (!wheelAction.action.IsPressed())
			{
				Close(select: true);
				return;
			}

			TrackPointer();
			wheel.SetDirection(_pointer, deadZone);
		}

		private bool OtherUIOpen()
		{
			return PauseMenu.IsPaused || DeathScreen.IsActive || (_focus != null && _focus.IsBusy) ||
				(inventoryUI != null && inventoryUI.IsOpen);
		}

		private bool CanOpen() => _input != null && _inventory != null && _inventory.Loadout != null && !OtherUIOpen();

		private void Open()
		{
			IsActive = true;
			_pointer = Vector2.zero;

			wheel.Bind(_inventory.Loadout);
			wheel.gameObject.SetActive(true);
			wheel.SetDirection(_pointer, deadZone);

			// мышь выбирает сектор, а не крутит камеру; стрелять с открытым колесом нельзя
			_input.cursorInputForLook = false;
			_input.look = Vector2.zero; // иначе камера продолжила бы вращаться на последнем движении мыши
			_input.GameplayInputEnabled = false;
		}

		private void Close(bool select)
		{
			int slot = wheel.HoverSlot;
			IsActive = false;
			wheel.SetDirection(null, deadZone);
			wheel.gameObject.SetActive(false);

			// ввод возвращаем, только если его не забрал кто-то другой — тот вернёт его сам, когда закроется
			if (_input != null && !OtherUIOpen())
			{
				_input.look = Vector2.zero;
				_input.cursorInputForLook = true;
				_input.GameplayInputEnabled = true;
			}

			if (select && slot >= 0 && _inventory != null && _inventory.Loadout != null) _inventory.Loadout.Select(slot);
		}

		// Мышь: накапливаем движение от центра (курсор зажат, поэтому "указатель" свой), не дальше Max Distance.
		// Стик: направление отклонения; отпущенный стик не сбрасывает выбор — иначе на отпускании кнопки выбора бы не было
		private void TrackPointer()
		{
			if (selectAction == null || selectAction.action == null) return;

			InputAction look = selectAction.action;
			Vector2 value = look.ReadValue<Vector2>();
			bool isPointer = look.activeControl == null || look.activeControl.device is Pointer;

			if (isPointer)
			{
				_pointer = Vector2.ClampMagnitude(_pointer + value, maxDistance);
			}
			else if (value.magnitude >= stickThreshold)
			{
				_pointer = value.normalized * maxDistance;
			}
		}
	}
}
