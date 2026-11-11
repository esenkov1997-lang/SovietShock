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
	// обзора/стрельбы, и связывает виды с данными игрока. Сам ничего не рисует: сетку показывает ItemGridView,
	// колесо — WeaponWheelView, улики — ClueListView, содержимое контейнера — LootWindowView. Здесь — какие действия
	// что значат (двойной клик, меню, перетаскивание между окнами); сами правила переноса — в InventoryHolder.
	//
	// Контейнер (LootContainer) открывает этот же экран с окном лута; Tab или любой крестик закрывает всё сразу,
	// отошёл от контейнера — закрывается только окно лута.
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
		[Tooltip("Колесо избранного оружия (WeaponWheelView). Лежит ВНЕ Panel: его же показывает быстрый выбор по Q " +
			"(WeaponWheelQuickSelect), а экран инвентаря включает его вместе с собой")]
		[SerializeField] private WeaponWheelView wheel;
		[Tooltip("Вкладки окна инвентаря: 0 — Предметы (сетка), 1 — Улики (список)")]
		[SerializeField] private TabGroup tabs;
		[Tooltip("Список улик (ClueListView) — страница вкладки \"Улики\"")]
		[SerializeField] private ClueListView clueList;
		[Tooltip("Окно чтения документа (DocumentReaderView) — внутри Panel, в центре, изначально выключено")]
		[SerializeField] private DocumentReaderView documentReader;

		[Header("Loot")]
		[Tooltip("Окно лута (LootWindowView) — внутри Panel, слева от окна инвентаря, изначально выключено")]
		[SerializeField] private LootWindowView lootWindow;
		[Tooltip("Окно инвентаря (InventoryWindow): пока над ним тащат предмет, вкладка переключается сама — " +
			"улика → \"Улики\", остальное → \"Предметы\"")]
		[SerializeField] private RectTransform inventoryWindow;
		[Tooltip("Размер сетки у всех контейнеров (ящики, трупы), в клетках")]
		[SerializeField] private Vector2Int lootGridSize = new Vector2Int(6, 4);
		[Tooltip("Окно лута закрывается, когда игрок отошёл от контейнера дальше этого (метры, от игрока до ближайшего " +
			"коллайдера контейнера). Должно быть больше дальности взаимодействия, иначе окно закроется сразу")]
		[Min(0.5f)]
		[SerializeField] private float lootCloseDistance = 4f;

		[Header("Context Menu Labels")]
		[SerializeField] private LocalizedString takeLabel = new LocalizedString("UI", "inventory.action.take");
		[SerializeField] private LocalizedString putLabel = new LocalizedString("UI", "inventory.action.put");
		[SerializeField] private LocalizedString useLabel = new LocalizedString("UI", "inventory.action.use");
		[SerializeField] private LocalizedString dropLabel = new LocalizedString("UI", "inventory.action.drop");
		[SerializeField] private LocalizedString dropAllLabel = new LocalizedString("UI", "inventory.action.dropAll");
		[SerializeField] private LocalizedString toFavoritesLabel = new LocalizedString("UI", "inventory.action.toFavorites");
		[Tooltip("{0} — номер сектора колеса")]
		[SerializeField] private LocalizedString replaceFavoriteLabel = new LocalizedString("UI", "inventory.action.replaceFavorite");
		[SerializeField] private LocalizedString fromFavoritesLabel = new LocalizedString("UI", "inventory.action.fromFavorites");
		[SerializeField] private LocalizedString readLabel = new LocalizedString("UI", "inventory.action.read");

		// номера вкладок в TabGroup
		public const int ItemsTab = 0;
		public const int CluesTab = 1;

		[Header("Toggle")]
		[Tooltip("Действие открытия/закрытия инвентаря (StarterAssets → Player/Inventory, по умолчанию Tab / Select на геймпаде)")]
		[SerializeField] private InputActionReference toggleAction;
		[Tooltip("Не обязательно — если задано, при открытом инвентаре камера перестаёт вращаться от мыши")]
		[SerializeField] private StarterAssetsInputs input;

		private bool _isOpen;
		// открытый контейнер и его сетка; null — окно лута закрыто (контейнер мог быть уничтожен — тогда _loot
		// "равен null" по правилам Unity, а _lootGrid — нет: по ней и проверяем, открыт ли лут)
		private LootContainer _loot;
		private ItemGrid _lootGrid;
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
				gridView.CanAcceptExternal = CanAcceptToGrid;
				gridView.AcceptExternal = AcceptToGrid;
			}

			if (wheel != null)
			{
				wheel.SlotRightClicked += HandleSlotRightClick;
				wheel.SlotDroppedOutside += HandleSlotDroppedOutside;
				wheel.CanAcceptDrop = CanAcceptToSlot;
				wheel.AcceptDrop = AcceptToSlot;
			}

			if (clueList != null)
			{
				clueList.EntryClicked += HandleClueClick;
				clueList.EntryRightClicked += HandleClueRightClick;
				clueList.AcceptDrop = AcceptToClues;
			}
			if (documentReader != null) documentReader.OpenChanged += HandleReaderOpenChanged;

			if (lootWindow != null)
			{
				lootWindow.TakeAllClicked += HandleTakeAll;
				lootWindow.CloseClicked += Close;

				ItemGridView lootGrid = lootWindow.GridView;
				if (lootGrid != null)
				{
					lootGrid.ItemDoubleClicked += HandleLootDoubleClick;
					lootGrid.ItemRightClicked += HandleLootRightClick;
					// из ящика в мир не выбрасываем: бросок мимо окон просто возвращает предмет на место
					lootGrid.CanAcceptExternal = CanAcceptToLoot;
					lootGrid.AcceptExternal = AcceptToLoot;
				}
				lootWindow.Hide();
			}

			SetOpen(false);
		}

		private void OnDestroy()
		{
			if (gridView != null)
			{
				gridView.ItemDoubleClicked -= HandleDoubleClick;
				gridView.ItemRightClicked -= HandleRightClick;
				gridView.ItemDroppedOutside -= HandleDroppedOutside;
			}
			if (wheel != null)
			{
				wheel.SlotRightClicked -= HandleSlotRightClick;
				wheel.SlotDroppedOutside -= HandleSlotDroppedOutside;
			}
			if (clueList != null)
			{
				clueList.EntryClicked -= HandleClueClick;
				clueList.EntryRightClicked -= HandleClueRightClick;
			}
			if (documentReader != null) documentReader.OpenChanged -= HandleReaderOpenChanged;
			if (lootWindow != null)
			{
				lootWindow.TakeAllClicked -= HandleTakeAll;
				lootWindow.CloseClicked -= Close;
				if (lootWindow.GridView != null)
				{
					lootWindow.GridView.ItemDoubleClicked -= HandleLootDoubleClick;
					lootWindow.GridView.ItemRightClicked -= HandleLootRightClick;
				}
			}
		}

		// ---------- Лут ----------

		private bool IsLootOpen => _lootGrid != null;
		private ItemGridView LootGridView => lootWindow != null ? lootWindow.GridView : null;

		// игрок нажал взаимодействие на контейнере (LootContainer.OpenRequested)
		private void HandleLootRequested(LootContainer container, GameObject interactor)
		{
			if (targetInventory == null || container == null || interactor == null) return;
			// контейнер открывает другой игрок (или не игрок) — не наш экран
			if (interactor.GetComponentInParent<InventoryHolder>() != targetInventory) return;
			if ((_focus != null && _focus.IsBusy) || PauseMenu.IsPaused || WeaponWheelQuickSelect.IsActive) return;

			OpenLoot(container);
		}

		// Открыть экран инвентаря с окном лута этого контейнера (уже открыт другой — сменится)
		public void OpenLoot(LootContainer container)
		{
			if (container == null) return;
			if (lootWindow == null)
			{
				Debug.LogError("InventoryUI: не назначен Loot Window — контейнер не открыть", this);
				return;
			}
			if (_loot == container && IsLootOpen && _isOpen) return;

			CloseLoot();
			_loot = container;
			_lootGrid = container.GetOrCreateGrid(lootGridSize);
			lootWindow.Show(container, _lootGrid);
			container.NotifyOpened();

			if (!_isOpen) SetOpen(true);
		}

		// закрыть только окно лута (отошли от ящика); экран инвентаря остаётся
		private void CloseLoot()
		{
			if (!IsLootOpen) return;

			// тащили предмет из ящика — бросок отменяется
			if (ItemDrag.IsActive && ItemDrag.SourceGrid != null && ItemDrag.SourceGrid == LootGridView) ItemDrag.Cancel();
			if (contextMenu != null) contextMenu.Close();

			LootContainer container = _loot;
			_loot = null;
			_lootGrid = null;
			if (lootWindow != null) lootWindow.Hide();
			if (container != null) container.NotifyClosed();
		}

		// контейнер уничтожили, выключили (труп убрали) или игрок отошёл — окно лута закрывается
		private void UpdateLootReach()
		{
			if (!IsLootOpen) return;

			bool reachable = _loot != null && _loot.IsAvailable && targetInventory != null &&
				_loot.DistanceTo(targetInventory.transform.position) <= lootCloseDistance;
			if (!reachable) CloseLoot();
		}

		private void HandleTakeAll()
		{
			if (targetInventory != null && IsLootOpen) targetInventory.TakeAll(_lootGrid);
		}

		// двойной клик в ящике — забрать (улику — в улики, остальное — в сетку)
		private void HandleLootDoubleClick(GridItemView view)
		{
			if (targetInventory != null && IsLootOpen && view.Item != null) targetInventory.TryTakeFrom(_lootGrid, view.Item);
		}

		// ПКМ в ящике: взять, прочитать (документ), в избранное (оружие)
		private void HandleLootRightClick(GridItemView view, PointerEventData eventData)
		{
			if (targetInventory == null || contextMenu == null || !IsLootOpen || view.Item == null) return;

			ItemInstance item = view.Item;
			ItemGrid loot = _lootGrid;
			List<ItemContextMenu.Entry> entries = new List<ItemContextMenu.Entry>();

			if (targetInventory.CanTakeFrom(loot, item))
			{
				entries.Add(new ItemContextMenu.Entry(takeLabel, () => targetInventory.TryTakeFrom(loot, item)));
			}
			if (item.Data is DocumentItemData document && documentReader != null)
			{
				entries.Add(new ItemContextMenu.Entry(readLabel, () => documentReader.Open(document)));
			}
			AddFavoriteEntries(loot, item, entries);

			contextMenu.Open(eventData.position, eventData.pressEventCamera, entries);
		}

		// в сетку ящика: из своей сетки — в клетку (или долить в стопку), из колеса — оружие в клетку
		private bool CanAcceptToLoot(ItemGridView view, Vector2Int cell)
		{
			if (targetInventory == null || !IsLootOpen || !ItemDrag.IsActive) return false;
			if (ItemDrag.IsFromSlot) return targetInventory.CanMoveFromSlot(ItemDrag.SourceSlot, _lootGrid, cell);
			return ItemDrag.SourceGrid == gridView && targetInventory.CanPutTo(_lootGrid, ItemDrag.Item, cell);
		}

		private bool AcceptToLoot(ItemGridView view, Vector2Int cell)
		{
			if (!CanAcceptToLoot(view, cell)) return false;
			if (ItemDrag.IsFromSlot) return targetInventory.TryMoveFromSlot(ItemDrag.SourceSlot, _lootGrid, cell);
			return targetInventory.TryPutTo(_lootGrid, ItemDrag.Item, cell);
		}

		// улику из ящика бросили на вкладку "Улики"
		private bool AcceptToClues()
		{
			if (targetInventory == null || !IsLootOpen || !ItemDrag.IsActive) return false;
			if (ItemDrag.SourceGrid == null || ItemDrag.SourceGrid != LootGridView) return false;

			ItemInstance item = ItemDrag.Item;
			return item.Data != null && item.Data.IsClue && targetInventory.TryTakeFrom(_lootGrid, item);
		}

		// тащат предмет над окном инвентаря — показываем вкладку, куда он ляжет
		private void UpdateDragTab()
		{
			if (tabs == null || inventoryWindow == null || !ItemDrag.IsActive || ItemDrag.Item.Data == null) return;
			if (!RectTransformUtility.RectangleContainsScreenPoint(inventoryWindow, ItemDrag.Position, ItemDrag.EventCamera)) return;

			int tab = ItemDrag.Item.Data.IsClue ? CluesTab : ItemsTab;
			if (tabs.Selected != tab) ShowTab(tab);
		}

		// ---------- Улики ----------

		// клик по улике: документ — открыть в окне чтения; ключ — пока ничего (описание — в подсказке, этап полировки)
		private void HandleClueClick(ItemInstance item, PointerEventData eventData)
		{
			if (item?.Data is DocumentItemData document && documentReader != null) documentReader.Open(document);
		}

		private void HandleClueRightClick(ItemInstance item, PointerEventData eventData)
		{
			if (contextMenu == null || !(item?.Data is DocumentItemData document) || documentReader == null) return;

			List<ItemContextMenu.Entry> entries = new List<ItemContextMenu.Entry>
			{
				new ItemContextMenu.Entry(readLabel, () => documentReader.Open(document))
			};
			contextMenu.Open(eventData.position, eventData.pressEventCamera, entries);
		}

		// документ стоит в центре, на месте колеса — пока он открыт, колесо спрятано
		private void HandleReaderOpenChanged(bool open) => UpdateWheelVisibility();

		private void UpdateWheelVisibility()
		{
			if (wheel == null) return;
			bool reading = documentReader != null && documentReader.IsOpen;
			wheel.gameObject.SetActive(_isOpen && !reading);
		}

		// переключить вкладку (например, при переносе улики из ящика — на "Улики")
		public void ShowTab(int index)
		{
			if (tabs != null) tabs.Select(index);
		}

		// ---------- Перенос между сеткой и колесом ----------

		// в свою сетку бросили что-то не из неё самой: оружие из колеса или предмет из ящика
		private bool CanAcceptToGrid(ItemGridView view, Vector2Int cell)
		{
			if (targetInventory == null || !ItemDrag.IsActive) return false;
			if (ItemDrag.IsFromSlot) return targetInventory.CanMoveFromSlot(ItemDrag.SourceSlot, view.Grid, cell);
			return IsLootOpen && ItemDrag.SourceGrid == LootGridView && targetInventory.CanTakeFrom(_lootGrid, ItemDrag.Item, cell);
		}

		private bool AcceptToGrid(ItemGridView view, Vector2Int cell)
		{
			if (!CanAcceptToGrid(view, cell)) return false;
			if (ItemDrag.IsFromSlot) return targetInventory.TryMoveFromSlot(ItemDrag.SourceSlot, view.Grid, cell);
			return targetInventory.TryTakeFrom(_lootGrid, ItemDrag.Item, cell);
		}

		// в сектор колеса: из другого сектора — обмен местами; из сетки — положить (занят — обмен, прежнее — в сетку)
		private bool CanAcceptToSlot(int slot)
		{
			if (targetInventory == null || !ItemDrag.IsActive) return false;
			if (ItemDrag.IsFromSlot) return ItemDrag.SourceSlot != slot;
			return ItemDrag.SourceGrid != null && targetInventory.CanMoveToSlot(ItemDrag.SourceGrid.Grid, ItemDrag.Item, slot);
		}

		private bool AcceptToSlot(int slot)
		{
			if (!CanAcceptToSlot(slot)) return false;
			if (ItemDrag.IsFromSlot) return targetInventory.TrySwapSlots(ItemDrag.SourceSlot, slot);
			return targetInventory.TryMoveToSlot(ItemDrag.SourceGrid.Grid, ItemDrag.Item, slot);
		}

		// ПКМ по сектору с оружием: убрать в сетку, выбросить
		private void HandleSlotRightClick(int slot, PointerEventData eventData)
		{
			if (targetInventory == null || contextMenu == null || targetInventory.Loadout?.GetItem(slot) == null) return;

			List<ItemContextMenu.Entry> entries = new List<ItemContextMenu.Entry>();
			ItemGrid grid = targetInventory.Grid;
			ItemGrid loot = _lootGrid;
			if (loot != null && targetInventory.CanMoveFromSlot(slot, loot, null))
			{
				entries.Add(new ItemContextMenu.Entry(putLabel, () => targetInventory.TryMoveFromSlot(slot, loot, null)));
			}
			if (targetInventory.CanMoveFromSlot(slot, grid, null))
			{
				entries.Add(new ItemContextMenu.Entry(fromFavoritesLabel, () => targetInventory.TryMoveFromSlot(slot, grid, null)));
			}
			if (targetInventory.CanDropFromSlot(slot))
			{
				entries.Add(new ItemContextMenu.Entry(dropLabel, () => targetInventory.TryDropFromSlot(slot)));
			}

			contextMenu.Open(eventData.position, eventData.pressEventCamera, entries);
		}

		private void HandleSlotDroppedOutside(int slot)
		{
			if (targetInventory != null) targetInventory.TryDropFromSlot(slot);
		}

		// "В избранное" для оружия в сетке: в первый пустой сектор; колесо полное — "Заменить N" для каждого сектора
		private void AddFavoriteEntries(ItemGrid grid, ItemInstance item, List<ItemContextMenu.Entry> entries)
		{
			if (!WeaponLoadout.CanHold(item) || targetInventory.Loadout == null) return;

			int empty = targetInventory.FirstEmptySlot();
			if (empty >= 0)
			{
				entries.Add(new ItemContextMenu.Entry(toFavoritesLabel, () => targetInventory.TryMoveToSlot(grid, item, empty)));
				return;
			}

			for (int i = 0; i < targetInventory.Loadout.SlotCount; i++)
			{
				int slot = i;
				if (!targetInventory.CanMoveToSlot(grid, item, slot)) continue;

				string number = (slot + 1).ToString();
				entries.Add(new ItemContextMenu.Entry(replaceFavoriteLabel,
					() => targetInventory.TryMoveToSlot(grid, item, slot),
					format => SafeFormat(format, number)));
			}
		}

		private static string SafeFormat(string format, string value)
		{
			try { return string.Format(format, value); }
			catch (System.FormatException) { return format; }
		}

		// ---------- Действия с предметами ----------

		// Двойной клик — быстрое действие. Открыт ящик — положить предмет в него; без ящика — использовать (аптечка,
		// еда); у предмета без быстрого действия — ничего
		private void HandleDoubleClick(GridItemView view)
		{
			if (targetInventory == null || view.Item == null) return;

			if (IsLootOpen) targetInventory.TryPutTo(_lootGrid, view.Item);
			else if (targetInventory.CanUse(view.Item)) targetInventory.TryUse(view.Item);
		}

		// ПКМ — меню под курсором с тем, что можно сделать с этим предметом
		private void HandleRightClick(GridItemView view, PointerEventData eventData)
		{
			if (targetInventory == null || contextMenu == null || view.Item == null) return;

			ItemInstance item = view.Item;
			List<ItemContextMenu.Entry> entries = new List<ItemContextMenu.Entry>();

			ItemGrid loot = _lootGrid;
			if (loot != null && targetInventory.CanPutTo(loot, item))
			{
				entries.Add(new ItemContextMenu.Entry(putLabel, () => targetInventory.TryPutTo(loot, item)));
			}
			if (targetInventory.CanUse(item))
			{
				entries.Add(new ItemContextMenu.Entry(useLabel, () => targetInventory.TryUse(item)));
			}
			AddFavoriteEntries(view.Owner.Grid, item, entries);
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
			LootContainer.OpenRequested += HandleLootRequested;

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
			LootContainer.OpenRequested -= HandleLootRequested;
			// окна здесь не трогаем (во время выключения иерархии их нельзя включать/выключать) — только сообщаем
			// контейнеру, что его больше не смотрят
			if (_loot != null) _loot.NotifyClosed();
			_loot = null;
			_lootGrid = null;
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
				if (wheel != null) wheel.Bind(null);
				if (clueList != null) clueList.Bind(null);
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
			if (wheel != null) wheel.Bind(targetInventory.Loadout);
			if (clueList != null) clueList.Bind(targetInventory.Clues);
		}

		private void Update()
		{
			if (_isOpen)
			{
				UpdateLootReach();
				UpdateDragTab();
			}

			if (_focus != null && _focus.IsBusy) return;
			if (PauseMenu.IsPaused) return;
			// открыто быстрое колесо (зажата Q) — инвентарь поверх не открываем, они делят ввод и само колесо
			if (WeaponWheelQuickSelect.IsActive) return;
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
			// закрыли посреди перетаскивания — бросок отменяется, предмет остаётся на месте; документ закрывается
			if (!open)
			{
				CloseLoot();
				ItemDrag.Cancel();
				if (contextMenu != null) contextMenu.Close();
				if (documentReader != null) documentReader.Close();
			}
			UpdateWheelVisibility();

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
