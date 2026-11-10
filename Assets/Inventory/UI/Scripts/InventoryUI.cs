using System.Collections.Generic;
using Interactables;
using Menus;
using Player;
using StarterAssets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Localization;

namespace Inventory
{
	// Экран инвентаря: открывает/закрывает его по кнопке (см. toggleAction) вместе с курсором мыши и блокировкой
	// обзора/стрельбы, и связывает виды с данными игрока. Сам ничего не рисует: сетку показывает ItemGridView
	// (позже — ещё колесо оружия, вкладка улик, окно ящика).
	//
	// Вешается на объект, который всегда активен (например, на канвас инвентаря), а panel — дочерний объект со всем
	// содержимым экрана, который включается/выключается. Кнопка-крестик в окне: On Click → InventoryUI.Close
	public class InventoryUI : MonoBehaviour
	{
		[Header("Data Source")]
		[Tooltip("Можно оставить пустым — игрок спавнится в рантайме через PlayerStart, и InventoryHolder " +
			"подхватится автоматически по PlayerStart.PlayerSpawned. Задавай вручную только для сцен без PlayerStart")]
		[SerializeField] private InventoryHolder targetInventory;

		[Header("UI References")]
		[Tooltip("Корневая панель инвентаря — включается/выключается целиком")]
		[SerializeField] private GameObject panel;
		[Tooltip("Сетка инвентаря игрока (ItemGridView)")]
		[SerializeField] private ItemGridView gridView;
		[Tooltip("Меню действий по ПКМ (ItemContextMenu) — последним ребёнком Panel, поверх окон")]
		[SerializeField] private ItemContextMenu contextMenu;

		[Header("Context Menu Labels")]
		[SerializeField] private LocalizedString useLabel = new LocalizedString("UI", "inventory.action.use");
		[SerializeField] private LocalizedString dropLabel = new LocalizedString("UI", "inventory.action.drop");
		[SerializeField] private LocalizedString dropAllLabel = new LocalizedString("UI", "inventory.action.dropAll");

		[Header("Toggle")]
		[Tooltip("Действие открытия/закрытия инвентаря (StarterAssets → Player/Inventory, по умолчанию Tab / Select на геймпаде)")]
		[SerializeField] private InputActionReference toggleAction;
		[Tooltip("Не обязательно — если задано, при открытом инвентаре камера перестаёт вращаться от мыши")]
		[SerializeField] private StarterAssetsInputs input;

		private bool _isOpen;
		// фокус на терминале/рычаге (см. Interactables.PlayerCameraFocus): пока он идёт, инвентарь закрыт
		// и не открывается. Необязателен — у игрока без этого компонента инвентарь работает как раньше
		private PlayerCameraFocus _focus;

		public bool IsOpen => _isOpen;

		private void Awake()
		{
			if (panel == null)
			{
				Debug.LogError("InventoryUI: не назначен panel", this);
				return;
			}

			// частая ловушка: если panel — это тот же объект, на котором висит сам InventoryUI, то
			// SetOpen(false) ниже выключит этот GameObject, и Update (проверка Tab) больше никогда
			// не выполнится — снаружи это выглядит как "ничего не происходит". Скрипт должен жить
			// на объекте, который остаётся активным всегда (например, на самом Canvas), а panel —
			// ссылаться на отдельный дочерний объект, который и включается/выключается
			if (panel == gameObject)
			{
				Debug.LogError("InventoryUI: panel не должен быть тем же объектом, на котором висит этот " +
					"скрипт. Перевесь InventoryUI.cs на родителя (например, на Canvas), а в panel укажи " +
					"дочернюю панель.", this);
				return;
			}

			if (gridView == null) Debug.LogError("InventoryUI: не назначен Grid View", this);
			else
			{
				gridView.ItemDoubleClicked += HandleDoubleClick;
				gridView.ItemRightClicked += HandleRightClick;
				gridView.ItemDroppedOutside += HandleDroppedOutside;
			}

			SetOpen(false);
		}

		private void OnDestroy()
		{
			if (gridView == null) return;
			gridView.ItemDoubleClicked -= HandleDoubleClick;
			gridView.ItemRightClicked -= HandleRightClick;
			gridView.ItemDroppedOutside -= HandleDroppedOutside;
		}

		// ---------- Действия с предметами ----------

		// Двойной клик — быстрое действие. Своё окно без ящика: использовать (аптечка, еда); у предмета без
		// быстрого действия — ничего. (С открытым ящиком двойной клик будет переносить предмет между окнами)
		private void HandleDoubleClick(GridItemView view)
		{
			if (targetInventory == null || view.Item == null) return;
			if (targetInventory.CanUse(view.Item)) targetInventory.TryUse(view.Item);
		}

		// ПКМ — меню под курсором с тем, что можно сделать с этим предметом
		private void HandleRightClick(GridItemView view, PointerEventData eventData)
		{
			if (targetInventory == null || contextMenu == null || view.Item == null) return;

			ItemInstance item = view.Item;
			List<ItemContextMenu.Entry> entries = new List<ItemContextMenu.Entry>();

			if (targetInventory.CanUse(item))
			{
				entries.Add(new ItemContextMenu.Entry(useLabel, () => targetInventory.TryUse(item)));
			}
			if (targetInventory.CanDrop(item))
			{
				entries.Add(new ItemContextMenu.Entry(dropLabel, () => targetInventory.TryDrop(item, 1)));
				if (item.Count > 1) entries.Add(new ItemContextMenu.Entry(dropAllLabel, () => targetInventory.TryDrop(item, item.Count)));
			}

			contextMenu.Open(eventData.position, eventData.pressEventCamera, entries);
		}

		// бросили мимо окон — выбросить всю стопку
		private void HandleDroppedOutside(GridItemView view)
		{
			if (targetInventory == null || view.Item == null) return;
			targetInventory.TryDrop(view.Item, view.Item.Count);
		}

		private void OnEnable()
		{
			PlayerStart.PlayerSpawned += HandlePlayerSpawned;

			if (targetInventory != null)
			{
				SetTarget(targetInventory); // тот же путь, что и для рантайм-игрока: заодно подхватит input
			}
			else
			{
				// на случай, если игрок был заспавнен раньше, чем включился этот компонент
				// (порядок Awake между разными объектами сцены не гарантирован)
				InventoryHolder existing = FindFirstObjectByType<InventoryHolder>();
				if (existing != null) SetTarget(existing);
			}
		}

		private void OnDisable()
		{
			PlayerStart.PlayerSpawned -= HandlePlayerSpawned;
			SetFocus(null);
		}

		private void HandlePlayerSpawned(GameObject player)
		{
			// не GetComponent — PlayerStart спавнит Player.prefab, а InventoryHolder висит на его
			// дочернем PlayerCapsule, а не на самом корне
			InventoryHolder holder = player.GetComponentInChildren<InventoryHolder>();
			if (holder != null) SetTarget(holder);
		}

		// публичный — пригодится и для ручного вызова, если игрока где-то спавнит не PlayerStart
		public void SetTarget(InventoryHolder inventory)
		{
			targetInventory = inventory;
			if (targetInventory == null)
			{
				if (gridView != null) gridView.Bind(null);
				SetFocus(null);
				return;
			}

			// StarterAssetsInputs живёт на том же PlayerCapsule, что и InventoryHolder, и тоже не может
			// быть назначен в инспекторе заранее — игрок спавнится в рантайме
			if (input == null) input = targetInventory.GetComponentInChildren<StarterAssetsInputs>();

			// PlayerCameraFocus висит на том же PlayerCapsule — ищем вверх, на случай если InventoryHolder глубже
			SetFocus(targetInventory.GetComponentInParent<PlayerCameraFocus>());

			// инвентарь мог быть уже открыт к моменту появления игрока — переприменяем блокировку ввода
			ApplyInputLock();

			// вид сам следит за изменениями сетки — панель актуальна при первом же открытии
			if (gridView != null) gridView.Bind(targetInventory.Grid);
		}

		private void Update()
		{
			if (_focus != null && _focus.IsBusy) return;
			if (PauseMenu.IsPaused) return;
			// карту Player включает PlayerInput игрока (на паузе выключает) — сами действие не включаем
			if (toggleAction != null && toggleAction.action.WasPressedThisFrame()) SetOpen(!_isOpen);
		}

		// Menus.PauseMenu закрывает инвентарь по Escape вместо открытия паузы
		public void Close()
		{
			if (_isOpen) SetOpen(false);
		}

		private void SetFocus(PlayerCameraFocus focus)
		{
			if (_focus != null) _focus.FocusStarted -= HandleFocusStarted;
			_focus = focus;
			if (_focus != null) _focus.FocusStarted += HandleFocusStarted;
		}

		// игрок начал взаимодействие с открытым инвентарём — закрываем панель. PlayerCameraFocus вызывает
		// это ДО своей блокировки, так что курсор/ввод, которые SetOpen(false) здесь возвращает в игровой
		// режим, тут же перекрываются блокировкой фокуса
		private void HandleFocusStarted()
		{
			if (_isOpen) SetOpen(false);
		}

		private void SetOpen(bool open)
		{
			_isOpen = open;
			panel.SetActive(open);

			// деактивация панели гасит OnDisable у предметов (см. GridItemView), который сам подчищает
			// свой призрак, если тащил именно он — это на случай, если по какой-то причине не сработало
			if (!open)
			{
				GridItemView.DestroyActiveGhost();
				if (contextMenu != null) contextMenu.Close();
			}

			Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
			Cursor.visible = open;

			ApplyInputLock();
		}

		// пока открыт инвентарь — мышь управляет курсором, а не камерой и не оружием
		private void ApplyInputLock()
		{
			if (input == null) return;

			input.cursorInputForLook = !_isOpen;

			// огонь и ПКМ (прицел/ремонтный луч) глушатся прямо в обработчиках ввода — см.
			// StarterAssetsInputs.GameplayInputEnabled; уже зажатые кнопки там же сбрасываются
			input.GameplayInputEnabled = !_isOpen;

			// обнуляем накопленный взгляд: StarterAssetsInputs при выключенном cursorInputForLook просто
			// перестаёт записывать новые значения, а FirstPersonController продолжает крутить камеру
			// на последнем ненулевом look — иначе открытие инвентаря "в движении мышью" уводит камеру
			if (_isOpen) input.look = Vector2.zero;
		}
	}
}
