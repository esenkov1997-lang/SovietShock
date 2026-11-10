using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Inventory
{
	// Инвентарь игрока. Вешается на корень игрока (PlayerCapsule). Состоит из двух мест хранения:
	//   - сетка (ItemGrid) — "тетрис" для всего, кроме избранного оружия;
	//   - колесо избранного оружия (WeaponLoadout на том же объекте, необязательно) — оружие в нём не занимает сетку.
	//
	// Подбор с пола (TryPickup): оружие — в первый свободный слот колеса и сразу в руки; колесо полное — в сетку;
	// не влезает и туда — предмет не подбирается. Остальное — в сетку (стопки доливаются).
	//
	// Счёт и списание (патроны, ключи, аптечки — HasItem/RemoveItem/GetTotalCount) идут по сетке.
	public class InventoryHolder : MonoBehaviour
	{
		[SerializeField] private int gridWidth = 6;
		[SerializeField] private int gridHeight = 4;
		[Tooltip("Что лежит в сетке при старте и в каких клетках (x, y — верхний левый угол предмета). " +
			"Не помещается в указанную клетку — кладётся в первое свободное место")]
		[FormerlySerializedAs("items")]
		[SerializeField] private List<ItemStack> startingItems = new List<ItemStack>();

		private ItemGrid _grid;
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

		// колесо избранного оружия; null — у игрока его нет (тогда оружие подбирается в сетку)
		public WeaponLoadout Loadout
		{
			get
			{
				EnsureInitialized();
				return _loadout;
			}
		}

		// изменилось хоть что-то — сетка или колесо (для UI: перерисовать целиком)
		public event Action OnInventoryChanged;

		private void Awake()
		{
			EnsureInitialized();
		}

		private void OnDestroy()
		{
			if (_grid != null) _grid.Changed -= RaiseChanged;
			if (_loadout != null) _loadout.SlotsChanged -= RaiseChanged;
		}

		private void EnsureInitialized()
		{
			if (_grid != null) return;

			_grid = new ItemGrid(gridWidth, gridHeight);
			foreach (ItemStack stack in startingItems)
			{
				if (stack.item == null || stack.count <= 0) continue;

				ItemInstance instance = new ItemInstance(stack.item, stack.count);
				if (_grid.TryPlace(instance, new Vector2Int(stack.x, stack.y)) || _grid.TryAdd(stack.item, stack.count)) continue;

				Debug.LogWarning($"{nameof(InventoryHolder)} на {name}: стартовый предмет {stack.item.name} ×{stack.count} " +
					"не поместился в сетку", this);
			}
			_grid.Changed += RaiseChanged;

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

			if (WeaponLoadout.CanHold(item) && _loadout != null && _loadout.TryAddFromPickup(item)) return true;
			return _grid.TryAdd(item);
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

		// ---------- Счёт и списание (по сетке) ----------

		public bool HasItem(ItemData item, int amount)
		{
			return item != null && GetTotalCount(item) >= amount;
		}

		public bool RemoveItem(ItemData item, int amount)
		{
			return Grid.TryRemove(item, amount);
		}

		public int GetTotalCount(ItemData item)
		{
			return item != null ? Grid.CountOf(item) : 0;
		}
	}
}
