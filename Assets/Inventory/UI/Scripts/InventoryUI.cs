using System.Collections.Generic;
using Interactables;
using Player;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory
{
	// Вешается на объект InventoryUI (панель на Canvas). Держит панель в актуальном состоянии по событию
	// InventoryHolder.OnInventoryChanged и переключает её видимость по кнопке (см. toggleKey) — вместе
	// с курсором мыши, ровно как это уже делает StarterAssetsInputs при потере фокуса окна.
	//
	// Сетка "тетрис"-стиля: contentParent (Grid Layout Group) задаёт размер/отступы ОДНОЙ ячейки и
	// рисует фон gridWidth x gridHeight — этот фон статичен, перерисовывается один раз при получении
	// инвентаря. Сами предметы — отдельные объекты (slotPrefab), которые Grid Layout Group не трогает
	// (LayoutElement.ignoreLayout), а RedrawUI вручную ставит им размер/позицию под ItemData.GridSize.
	public class InventoryUI : MonoBehaviour
	{
		[Header("Data Source")]
		[Tooltip("Можно оставить пустым — игрок спавнится в рантайме через PlayerStart, и InventoryHolder " +
			"подхватится автоматически по PlayerStart.PlayerSpawned. Задавай вручную только для сцен без PlayerStart")]
		[SerializeField] private InventoryHolder targetInventory;

		[Header("UI References")]
		[Tooltip("Объект с Grid Layout Group — задаёт размер и отступы одной ячейки. Сюда спавнятся и фон, и сами предметы")]
		[SerializeField] private RectTransform contentParent;
		[Tooltip("Предмет (InventorySlotPrefab)")]
		[SerializeField] private GameObject slotPrefab;
		[Tooltip("Необязательно — фоновая клетка 1x1. Если не задано, клетка рисуется простым полупрозрачным квадратом в коде")]
		[SerializeField] private GameObject backgroundCellPrefab;
		[Tooltip("Корневая панель инвентаря — включается/выключается целиком")]
		[SerializeField] private GameObject panel;

		[Header("Toggle")]
		[SerializeField] private KeyCode toggleKey = KeyCode.Tab;
		[Tooltip("Не обязательно — если задано, при открытом инвентаре камера перестаёт вращаться от мыши")]
		[SerializeField] private StarterAssetsInputs input;

		private bool _isOpen;
		private bool _backgroundBuilt;
		private GridLayoutGroup _grid;
		// фокус на терминале/рычаге (см. Interactables.PlayerCameraFocus): пока он идёт, инвентарь закрыт
		// и не открывается. Необязателен — у игрока без этого компонента инвентарь работает как раньше
		private PlayerCameraFocus _focus;
		private readonly List<GameObject> _itemViews = new List<GameObject>();

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

			_grid = contentParent != null ? contentParent.GetComponent<GridLayoutGroup>() : null;
			if (_grid == null)
			{
				Debug.LogError("InventoryUI: на contentParent нет Grid Layout Group", this);
			}

			SetOpen(false);
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

			if (targetInventory != null) targetInventory.OnInventoryChanged -= RedrawUI;
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
			if (targetInventory != null) targetInventory.OnInventoryChanged -= RedrawUI;
			targetInventory = inventory;
			if (targetInventory == null)
			{
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

			Subscribe(targetInventory);
		}

		private void Subscribe(InventoryHolder inventory)
		{
			inventory.OnInventoryChanged += RedrawUI;
			RedrawUI(); // чтобы панель не была пустой при первом же открытии
		}

		private void Update()
		{
			if (_focus != null && _focus.IsBusy) return;
			if (Input.GetKeyDown(toggleKey)) SetOpen(!_isOpen);
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

			// деактивация панели гасит OnDisable у предметов (см. InventorySlotUI), который сам подчищает
			// свой призрак, если тащил именно он — это на случай, если по какой-то причине не сработало
			if (!open) InventorySlotUI.DestroyActiveGhost();

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

		// фон рисуется один раз на весь размер сетки (gridWidth x gridHeight) и никогда не перерисовывается —
		// это просто разметка, она не зависит от того, что лежит в инвентаре
		private void EnsureBackground()
		{
			if (_backgroundBuilt || targetInventory == null || _grid == null) return;
			_backgroundBuilt = true;

			int total = targetInventory.GridWidth * targetInventory.GridHeight;
			for (int i = 0; i < total; i++)
			{
				if (backgroundCellPrefab != null) Instantiate(backgroundCellPrefab, contentParent);
				else CreatePlainBackgroundCell();
			}

			// единая точка приёма OnDrop на весь Content, включая пустые клетки без своего IDropHandler
			if (contentParent.GetComponent<InventoryGridDropZone>() == null)
			{
				contentParent.gameObject.AddComponent<InventoryGridDropZone>();
			}
		}

		private void CreatePlainBackgroundCell()
		{
			GameObject cell = new GameObject("BackgroundCell", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			cell.transform.SetParent(contentParent, false);

			Image image = cell.GetComponent<Image>();
			image.color = new Color(1f, 1f, 1f, 0.06f); // едва заметная клетка — просто разметка сетки
			image.raycastTarget = false; // клики по фону не нужны — сброс ловит InventoryGridDropZone на Content
		}

		// сносим старые виды предметов и спавним по одному на каждый ItemStack — простая полная
		// перерисовка вместо точечного апдейта: инвентарь не настолько большой, чтобы это было проблемой.
		// Фон (см. EnsureBackground) в этой перерисовке не участвует — он статичен
		private void RedrawUI()
		{
			if (_grid == null) return;

			EnsureBackground();

			foreach (GameObject view in _itemViews) Destroy(view);
			_itemViews.Clear();

			IReadOnlyList<ItemStack> stacks = targetInventory.Items;
			for (int i = 0; i < stacks.Count; i++)
			{
				ItemStack stack = stacks[i];
				_itemViews.Add(CreateItemView(stack, i));
			}
		}

		private GameObject CreateItemView(ItemStack stack, int index)
		{
			GameObject view = Instantiate(slotPrefab, contentParent);

			// иначе Grid Layout Group тут же вернёт объект на свою обычную 1x1 позицию по порядку —
			// ему разрешено определять только размер/отступы ячейки, а не расставлять предметы самому
			if (!view.TryGetComponent(out LayoutElement layoutElement)) layoutElement = view.AddComponent<LayoutElement>();
			layoutElement.ignoreLayout = true;

			RectTransform rect = (RectTransform)view.transform;
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);

			Vector2Int size = stack.item.GridSize;
			float cellStepX = _grid.cellSize.x + _grid.spacing.x;
			float cellStepY = _grid.cellSize.y + _grid.spacing.y;

			rect.sizeDelta = new Vector2(
				size.x * _grid.cellSize.x + (size.x - 1) * _grid.spacing.x,
				size.y * _grid.cellSize.y + (size.y - 1) * _grid.spacing.y);
			rect.anchoredPosition = new Vector2(
				_grid.padding.left + stack.x * cellStepX,
				-(_grid.padding.top + stack.y * cellStepY));

			view.GetComponent<InventorySlotUI>().SetupSlot(stack.item, stack.count, index, targetInventory);
			return view;
		}
	}
}
