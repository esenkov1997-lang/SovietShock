using NUnit.Framework;
using UnityEngine;

namespace Inventory.Tests
{
	// Логика "тетрис"-сетки (ItemGrid): раскладка, стопки, перенос, обмен, списание. Запуск: Window → General →
	// Test Runner → EditMode → Run All.
	public class ItemGridTests
	{
		private InventoryTestUtils _utils;

		[SetUp] public void SetUp() => _utils = new InventoryTestUtils();
		[TearDown] public void TearDown() => _utils.Cleanup();

		private static Vector2Int Pos(ItemGrid grid, ItemInstance item)
		{
			Assert.IsTrue(grid.TryGetPosition(item, out Vector2Int position), "предмета нет в сетке");
			return position;
		}

		// ---------- Добавление ----------

		[Test]
		public void Add_PlacesInFirstFreeCell_LeftToRightTopToBottom()
		{
			ItemGrid grid = new ItemGrid(3, 2);
			ItemData box = _utils.Item("Box", 2, 1);

			ItemInstance first = new ItemInstance(box);
			ItemInstance second = new ItemInstance(box);
			Assert.IsTrue(grid.TryAdd(first));
			Assert.IsTrue(grid.TryAdd(second));

			Assert.AreEqual(new Vector2Int(0, 0), Pos(grid, first));
			Assert.AreEqual(new Vector2Int(0, 1), Pos(grid, second), "в первом ряду осталась одна клетка — 2×1 уходит во второй");
		}

		[Test]
		public void Add_NoSpace_FailsAndChangesNothing()
		{
			ItemGrid grid = new ItemGrid(2, 1);
			ItemData big = _utils.Item("Big", 2, 1);
			Assert.IsTrue(grid.TryAdd(new ItemInstance(big)));

			int changes = 0;
			grid.Changed += () => changes++;

			Assert.IsFalse(grid.TryAdd(new ItemInstance(big)));
			Assert.AreEqual(1, grid.Items.Count);
			Assert.AreEqual(0, changes, "при отказе сетка не должна сообщать об изменении");
		}

		[Test]
		public void Add_FillsExistingStacksFirst_ThenNewStacks()
		{
			ItemGrid grid = new ItemGrid(3, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 5);

			Assert.IsTrue(grid.TryAdd(ammo, 3));
			Assert.IsTrue(grid.TryAdd(ammo, 4));

			Assert.AreEqual(2, grid.Items.Count);
			Assert.AreEqual(5, grid.Items[0].Instance.Count, "первая стопка долита до максимума");
			Assert.AreEqual(2, grid.Items[1].Instance.Count, "остаток — новой стопкой");
			Assert.AreEqual(7, grid.CountOf(ammo));
		}

		[Test]
		public void Add_AmountDoesNotFit_IsAtomic()
		{
			ItemGrid grid = new ItemGrid(1, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 5);
			Assert.IsTrue(grid.TryAdd(ammo, 3));

			// влезет только 2 из 6 — не кладётся ничего
			Assert.IsFalse(grid.TryAdd(ammo, 6));
			Assert.AreEqual(3, grid.CountOf(ammo), "существующая стопка не должна была доливаться наполовину");
		}

		[Test]
		public void AddInstance_FullyMergedIntoExistingStack_IsNotPlacedItself()
		{
			ItemGrid grid = new ItemGrid(2, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 10);
			Assert.IsTrue(grid.TryAdd(ammo, 4));

			ItemInstance incoming = new ItemInstance(ammo, 3);
			Assert.IsTrue(grid.TryAdd(incoming));

			Assert.AreEqual(1, grid.Items.Count);
			Assert.AreEqual(7, grid.CountOf(ammo));
			Assert.AreEqual(0, incoming.Count);
			Assert.IsFalse(grid.Contains(incoming));
		}

		[Test]
		public void WeaponInstances_NeverStack()
		{
			WeaponItemData pistol = _utils.Weapon("Pistol", 1, 1);
			pistol.maxStack = 10; // даже если в ассете ошибочно разрешена стопка

			ItemInstance a = new ItemInstance(pistol, 5);
			Assert.AreEqual(1, a.Count, "у предмета с состоянием всегда одна штука");

			ItemGrid grid = new ItemGrid(2, 1);
			Assert.IsTrue(grid.TryAdd(a));
			Assert.IsTrue(grid.TryAdd(new ItemInstance(pistol)));
			Assert.AreEqual(2, grid.Items.Count, "два пистолета — две отдельные клетки, а не стопка");
		}

		// ---------- Перенос ----------

		[Test]
		public void Move_ToFreeCell()
		{
			ItemGrid grid = new ItemGrid(3, 3);
			ItemInstance item = new ItemInstance(_utils.Item("Box"));
			grid.TryAdd(item);

			Assert.IsTrue(grid.TryMove(item, new Vector2Int(2, 2)));
			Assert.AreEqual(new Vector2Int(2, 2), Pos(grid, item));
		}

		[Test]
		public void Move_OutsideGrid_Refused()
		{
			ItemGrid grid = new ItemGrid(3, 3);
			ItemInstance item = new ItemInstance(_utils.Item("Long", 2, 1));
			grid.TryAdd(item);

			Assert.IsFalse(grid.TryMove(item, new Vector2Int(2, 0)), "2×1 с x = 2 вылезает за правый край");
			Assert.AreEqual(Vector2Int.zero, Pos(grid, item));
		}

		[Test]
		public void Move_OntoOtherItem_SwapsPlaces()
		{
			ItemGrid grid = new ItemGrid(3, 1);
			ItemInstance a = new ItemInstance(_utils.Item("A"));
			ItemInstance b = new ItemInstance(_utils.Item("B"));
			grid.TryPlace(a, new Vector2Int(0, 0));
			grid.TryPlace(b, new Vector2Int(2, 0));

			Assert.IsTrue(grid.TryMove(a, new Vector2Int(2, 0)));
			Assert.AreEqual(new Vector2Int(2, 0), Pos(grid, a));
			Assert.AreEqual(new Vector2Int(0, 0), Pos(grid, b));
		}

		[Test]
		public void Move_SwapWouldOverlapMovedItem_Refused()
		{
			// [a][BB] → a на (1,0): для обмена BB встал бы на место a (0,0), но он шириной 2 и наложился бы на a,
			// который уже стоит в (1,0). Обмена быть не может — отказ
			ItemGrid grid = new ItemGrid(3, 1);
			ItemInstance a = new ItemInstance(_utils.Item("a"));
			ItemInstance bb = new ItemInstance(_utils.Item("BB", 2, 1));
			grid.TryPlace(a, new Vector2Int(0, 0));
			grid.TryPlace(bb, new Vector2Int(1, 0));

			Assert.IsFalse(grid.TryMove(a, new Vector2Int(1, 0)));
			Assert.AreEqual(new Vector2Int(0, 0), Pos(grid, a));
			Assert.AreEqual(new Vector2Int(1, 0), Pos(grid, bb));
		}

		[Test]
		public void Move_OverTwoItems_Refused()
		{
			ItemGrid grid = new ItemGrid(3, 2);
			ItemInstance wide = new ItemInstance(_utils.Item("Wide", 2, 1));
			ItemInstance a = new ItemInstance(_utils.Item("a"));
			ItemInstance b = new ItemInstance(_utils.Item("b"));
			grid.TryPlace(wide, new Vector2Int(0, 1));
			grid.TryPlace(a, new Vector2Int(0, 0));
			grid.TryPlace(b, new Vector2Int(1, 0));

			Assert.IsFalse(grid.TryMove(wide, new Vector2Int(0, 0)), "широкий предмет задел бы сразу два");
		}

		[Test]
		public void Move_OntoSameStack_Merges_RemainderStays()
		{
			ItemGrid grid = new ItemGrid(2, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 10);
			ItemInstance target = new ItemInstance(ammo, 8);
			ItemInstance dragged = new ItemInstance(ammo, 5);
			grid.TryPlace(target, new Vector2Int(0, 0));
			grid.TryPlace(dragged, new Vector2Int(1, 0));

			Assert.AreEqual(ItemGrid.MoveKind.Merge, grid.EvaluateMove(dragged, new Vector2Int(0, 0), out _));
			Assert.IsTrue(grid.TryMove(dragged, new Vector2Int(0, 0)));

			Assert.AreEqual(10, target.Count);
			Assert.AreEqual(3, dragged.Count);
			Assert.AreEqual(new Vector2Int(1, 0), Pos(grid, dragged), "остаток остаётся на старом месте");
		}

		[Test]
		public void Move_OntoSameStack_FullMerge_RemovesDragged()
		{
			ItemGrid grid = new ItemGrid(2, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 10);
			ItemInstance target = new ItemInstance(ammo, 2);
			ItemInstance dragged = new ItemInstance(ammo, 3);
			grid.TryPlace(target, new Vector2Int(0, 0));
			grid.TryPlace(dragged, new Vector2Int(1, 0));

			Assert.IsTrue(grid.TryMove(dragged, new Vector2Int(0, 0)));
			Assert.AreEqual(5, target.Count);
			Assert.IsFalse(grid.Contains(dragged));
		}

		// ---------- Забрать / списать ----------

		[Test]
		public void Take_PartOfStack_ReturnsNewInstance()
		{
			ItemGrid grid = new ItemGrid(1, 1);
			ItemInstance stack = new ItemInstance(_utils.Item("Medkit", maxStack: 5), 3);
			grid.TryAdd(stack);

			Assert.IsTrue(grid.TryTake(stack, 1, out ItemInstance taken));
			Assert.AreNotSame(stack, taken);
			Assert.AreEqual(1, taken.Count);
			Assert.AreEqual(2, stack.Count);
			Assert.IsTrue(grid.Contains(stack));
		}

		[Test]
		public void Take_WholeStack_ReturnsSameInstance()
		{
			ItemGrid grid = new ItemGrid(1, 1);
			ItemInstance stack = new ItemInstance(_utils.Item("Medkit", maxStack: 5), 3);
			grid.TryAdd(stack);

			Assert.IsTrue(grid.TryTake(stack, 3, out ItemInstance taken));
			Assert.AreSame(stack, taken, "вся стопка уходит тем же экземпляром — со своим состоянием");
			Assert.IsFalse(grid.Contains(stack));
		}

		[Test]
		public void Remove_NotEnough_RemovesNothing()
		{
			ItemGrid grid = new ItemGrid(2, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 10);
			grid.TryAdd(ammo, 4);

			Assert.IsFalse(grid.TryRemove(ammo, 5));
			Assert.AreEqual(4, grid.CountOf(ammo));
		}

		[Test]
		public void Remove_AcrossStacks_DropsEmptyStacks()
		{
			ItemGrid grid = new ItemGrid(2, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 5);
			grid.TryAdd(ammo, 7); // стопки 5 и 2

			Assert.IsTrue(grid.TryRemove(ammo, 3));
			Assert.AreEqual(4, grid.CountOf(ammo));
			Assert.AreEqual(1, grid.Items.Count, "опустевшая стопка убрана");
		}
	}
}
