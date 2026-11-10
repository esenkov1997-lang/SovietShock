using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Inventory
{
	// Инвентарь игрока. Вешается на корень игрока (PlayerCapsule). Состоит из трёх мест хранения:
	//   - сетка (ItemGrid) — "тетрис" для всего, кроме избранного оружия и улик;
	//   - колесо избранного оружия (WeaponLoadout на том же объекте, необязательно) — оружие в нём не занимает сетку;
	//   - улики (ClueCollection) — ключи и документы (ItemData.IsClue): список без места, не выбрасываются.
	//
	// Подбор с пола (TryPickup): улика — в улики; оружие — в первый свободный слот колеса и сразу в руки; колесо
	// полное — в сетку; не влезает и туда — предмет не подбирается. Остальное — в сетку (стопки доливаются).
	//
	// Счёт и списание (патроны, ключи, аптечки — HasItem/RemoveItem/GetTotalCount): улики — по уликам, остальное —
	// по сетке.
	public class InventoryHolder : MonoBehaviour
	{
		[SerializeField] private int gridWidth = 6;
		[SerializeField] private int gridHeight = 4;
		[Tooltip("Что лежит у игрока при старте. Обычные предметы — в сетке, в указанной клетке (x, y — верхний левый " +
			"угол; не помещается — в первое свободное место). Улики (ключи, документы) — во вкладке улик, клетка не важна")]
		[FormerlySerializedAs("items")]
		[SerializeField] private List<ItemStack> startingItems = new List<ItemStack>();

		private ItemGrid _grid;
		private ClueCollection _clues;
		private WeaponLoadout _loadout;

		// сетка создаётся при первом обращении: UI может подписаться раньше, чем у этого компонента прошёл Awake
		public ItemGrid Grid
		{
			get
			{
				EnsureInitialized();
				return _grid;
			}
		}

		public ClueCollection Clues
		{
			get
			{
				EnsureInitialized();
				return _clues;
			}
		}

		// колесо избранного оружия; null — у игрока его нет (тогда оружие подбирается в сетку)
		public WeaponLoadout Loadout
		{
			get
			{
				EnsureInitialized();
				return _loadout;
			}
		}

		// изменилось хоть что-то — сетка, колесо или улики (для UI: перерисовать целиком)
		public event Action OnInventoryChanged;

		private void Awake()
		{
			EnsureInitialized();
		}

		private void OnDestroy()
		{
			if (_grid != null) _grid.Changed -= RaiseChanged;
			if (_clues != null) _clues.Changed -= RaiseChanged;
			if (_loadout != null) _loadout.SlotsChanged -= RaiseChanged;
		}

		private void EnsureInitialized()
		{
			if (_grid != null) return;

			_grid = new ItemGrid(gridWidth, gridHeight);
			_clues = new ClueCollection();
			foreach (ItemStack stack in startingItems)
			{
				if (stack.item == null || stack.count <= 0) continue;

				if (stack.item.IsClue)
				{
					_clues.TryAdd(stack.item, stack.count);
					continue;
				}

				ItemInstance instance = new ItemInstance(stack.item, stack.count);
				if (_grid.TryPlace(instance, new Vector2Int(stack.x, stack.y)) || _grid.TryAdd(stack.item, stack.count)) continue;

				Debug.LogWarning($"{nameof(InventoryHolder)} на {name}: стартовый предмет {stack.item.name} ×{stack.count} " +
					"не поместился в сетку", this);
			}
			_grid.Changed += RaiseChanged;
			_clues.Changed += RaiseChanged;

			_loadout = GetComponent<WeaponLoadout>();
			if (_loadout != null) _loadout.SlotsChanged += RaiseChanged;
		}

		private void RaiseChanged() => OnInventoryChanged?.Invoke();

		// ---------- Подбор ----------

		// amount — сколько штук. loadedAmmo — только для оружия: патронов в магазине, < 0 — полный.
		// false — некуда положить, предмет остаётся лежать
		public bool TryPickup(ItemData data, int amount, int loadedAmmo = -1)
		{
			if (data == null || amount <= 0) return false;
			EnsureInitialized();

			if (data.IsClue) return _clues.TryAdd(data, amount);

			ItemInstance probe = new ItemInstance(data, 1, loadedAmmo);
			if (!probe.HasState) return _grid.TryAdd(data, amount);

			// у каждого экземпляра с состоянием (оружия) свой магазин — стопкой их не подобрать
			if (amount > 1)
			{
				Debug.LogWarning($"{data.name}: предмет с собственным состоянием (оружие) не может лежать в мире " +
					$"стопкой ×{amount} — подбирается одна штука. Поставь Amount = 1 в WorldItem", data);
			}

			return TryPickup(probe);
		}

		// Подобрать уже существующий экземпляр (выброшенный ранее из инвентаря — со своим состоянием)
		public bool TryPickup(ItemInstance item)
		{
			if (item == null || item.Data == null) return false;
			EnsureInitialized();

			if (item.Data.IsClue) return _clues.TryAdd(item);
			if (WeaponLoadout.CanHold(item) && _loadout != null && _loadout.TryAddFromPickup(item)) return true;
			return _grid.TryAdd(item);
		}

		// ---------- Колесо избранного ----------
		//
		// Правила переноса между сеткой (своей или ящика — source) и колесом. Can* — проверка без изменений
		// (подсветка при перетаскивании, пункты меню), Try* — сам перенос; оба по одним и тем же правилам.

		// Положить оружие из сетки source в слот. Слот занят — обмен: прежнее оружие уходит в source — на место
		// нового, если влезает, иначе в любое свободное место. Места нет — отказ, ничего не меняется
		public bool CanMoveToSlot(ItemGrid source, ItemInstance item, int slot)
		{
			return PlanMoveToSlot(source, item, slot, out _, out _);
		}

		public bool TryMoveToSlot(ItemGrid source, ItemInstance item, int slot)
		{
			if (!PlanMoveToSlot(source, item, slot, out ItemInstance previous, out Vector2Int previousPosition)) return false;

			source.Remove(item);
			_loadout.TryPut(slot, item, out _);
			if (previous != null) source.TryPlace(previous, previousPosition);
			return true;
		}

		private bool PlanMoveToSlot(ItemGrid source, ItemInstance item, int slot, out ItemInstance previous,
			out Vector2Int previousPosition)
		{
			previous = null;
			previousPosition = default;
			if (Loadout == null || source == null || !source.Contains(item) || !WeaponLoadout.CanHold(item)) return false;
			if (slot < 0 || slot >= _loadout.SlotCount) return false;

			previous = _loadout.GetItem(slot);
			if (previous == null) return true;

			// прежнему оружию — сначала место нового (оно освободится), иначе первое свободное
			source.TryGetPosition(item, out Vector2Int freed);
			RectInt atFreed = new RectInt(freed, previous.GridSize);
			if (source.IsInside(atFreed) && source.IsFree(atFreed, item))
			{
				previousPosition = freed;
				return true;
			}
			return source.TryFindFreePosition(previous.GridSize, out previousPosition, item);
		}

		// "В избранное" из меню: первый пустой слот по часовой стрелке. -1 — колесо полное (тогда — "Заменить N")
		public int FirstEmptySlot() => Loadout != null ? _loadout.FirstEmptySlot() : -1;

		// Убрать оружие из слота в сетку target: в клетку cell (null — в первое свободное место). Нет места — отказ
		public bool CanMoveFromSlot(int slot, ItemGrid target, Vector2Int? cell)
		{
			return PlanMoveFromSlot(slot, target, cell, out _);
		}

		public bool TryMoveFromSlot(int slot, ItemGrid target, Vector2Int? cell)
		{
			if (!PlanMoveFromSlot(slot, target, cell, out Vector2Int position)) return false;

			ItemInstance item = _loadout.Take(slot);
			return target.TryPlace(item, position);
		}

		private bool PlanMoveFromSlot(int slot, ItemGrid target, Vector2Int? cell, out Vector2Int position)
		{
			position = default;
			ItemInstance item = Loadout != null ? _loadout.GetItem(slot) : null;
			if (item == null || target == null) return false;

			if (cell.HasValue)
			{
				position = cell.Value;
				return target.CanPlace(item, position);
			}
			return target.TryFindFreePosition(item.GridSize, out position);
		}

		// Поменять два слота колеса местами (перетаскивание внутри колеса)
		public bool TrySwapSlots(int a, int b) => Loadout != null && _loadout.Swap(a, b);

		// Выбросить оружие из слота в мир
		public bool CanDropFromSlot(int slot)
		{
			return Loadout != null && GetComponent<ItemDropper>() != null && ItemDropper.CanDrop(_loadout.GetItem(slot));
		}

		public bool TryDropFromSlot(int slot)
		{
			if (!CanDropFromSlot(slot)) return false;
			return GetComponent<ItemDropper>().Drop(_loadout.Take(slot));
		}

		// ---------- Использование и выброс ----------

		// можно ли предмет использовать (есть ли на игроке, кто его применяет) — для пункта меню "Использовать"
		public bool CanUse(ItemInstance item)
		{
			if (item == null) return false;
			foreach (IItemUseHandler handler in GetComponentsInChildren<IItemUseHandler>())
			{
				if (handler.CanUse(item)) return true;
			}
			return false;
		}

		// использовать (аптечку — вколоть и т.п.). Списывает предмет сам обработчик — когда действие реально случилось
		public bool TryUse(ItemInstance item)
		{
			if (item == null || !Grid.Contains(item)) return false;
			foreach (IItemUseHandler handler in GetComponentsInChildren<IItemUseHandler>())
			{
				if (handler.CanUse(item)) return handler.TryUse(item);
			}
			return false;
		}

		// можно ли выбросить предмет из сетки в мир (есть ItemDropper на игроке и World Prefab у предмета)
		public bool CanDrop(ItemInstance item)
		{
			return item != null && Grid.Contains(item) && GetComponent<ItemDropper>() != null && ItemDropper.CanDrop(item);
		}

		// Выбросить amount штук из стопки в мир (всю стопку — сам экземпляр со своим состоянием).
		// Сначала проверяется, что выбросить получится, — предмет не может пропасть из инвентаря "в никуда"
		public bool TryDrop(ItemInstance item, int amount)
		{
			if (!CanDrop(item) || amount <= 0) return false;

			ItemDropper dropper = GetComponent<ItemDropper>();
			if (!Grid.TryTake(item, amount, out ItemInstance taken)) return false;
			return dropper.Drop(taken);
		}

		// ---------- Счёт и списание ----------
		// улики (ключ для двери) — по уликам, остальное (патроны, аптечки) — по сетке

		public bool HasItem(ItemData item, int amount)
		{
			return item != null && GetTotalCount(item) >= amount;
		}

		public bool RemoveItem(ItemData item, int amount)
		{
			if (item == null) return false;
			return item.IsClue ? Clues.TryRemove(item, amount) : Grid.TryRemove(item, amount);
		}

		public int GetTotalCount(ItemData item)
		{
			if (item == null) return 0;
			return item.IsClue ? Clues.CountOf(item) : Grid.CountOf(item);
		}
	}
}
