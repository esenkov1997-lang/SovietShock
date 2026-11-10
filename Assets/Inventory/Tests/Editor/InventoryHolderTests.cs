using NUnit.Framework;
using UnityEngine;

namespace Inventory.Tests
{
	// Правила инвентаря игрока (InventoryHolder + WeaponLoadout): подбор, колесо избранного, перенос между сеткой
	// и колесом. Запуск: Window → General → Test Runner → EditMode → Run All.
	public class InventoryHolderTests
	{
		private InventoryTestUtils _utils;

		[SetUp] public void SetUp() => _utils = new InventoryTestUtils();
		[TearDown] public void TearDown() => _utils.Cleanup();

		private static void FillWheel(InventoryHolder player, WeaponItemData weapon)
		{
			for (int i = 0; i < player.Loadout.SlotCount; i++) Assert.IsTrue(player.TryPickup(weapon, 1));
		}

		// ---------- Подбор ----------

		[Test]
		public void PickupWeapon_GoesToFirstEmptySlot_NotGrid()
		{
			InventoryHolder player = _utils.Player(4, 2);
			WeaponItemData pistol = _utils.Weapon("Pistol");

			Assert.IsTrue(player.TryPickup(pistol, 1));
			Assert.IsNotNull(player.Loadout.GetItem(0));
			Assert.AreEqual(0, player.Grid.Items.Count);
		}

		[Test]
		public void PickupWeapon_WheelFull_GoesToGrid()
		{
			InventoryHolder player = _utils.Player(4, 2);
			WeaponItemData pistol = _utils.Weapon("Pistol");
			FillWheel(player, pistol);

			Assert.IsTrue(player.TryPickup(pistol, 1));
			Assert.AreEqual(1, player.Grid.Items.Count);
		}

		[Test]
		public void PickupWeapon_WheelAndGridFull_Refused()
		{
			InventoryHolder player = _utils.Player(1, 1);
			WeaponItemData rifle = _utils.Weapon("Rifle", 2, 1); // в сетку 1×1 не влезет
			FillWheel(player, rifle);

			Assert.IsFalse(player.TryPickup(rifle, 1));
		}

		[Test]
		public void PickupInstance_KeepsSameWeaponState()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol", magazine: 12), loadedAmmo: 5);

			Assert.IsTrue(player.TryPickup(pistol));
			Assert.AreSame(pistol, player.Loadout.GetItem(0), "подбирается тот же экземпляр, а не новый");
			Assert.AreEqual(5, player.Loadout.GetItem(0).Weapon.CurrentAmmo);
		}

		[Test]
		public void PickupStack_ToGrid()
		{
			InventoryHolder player = _utils.Player(2, 1);
			ItemData medkit = _utils.Item("Medkit", maxStack: 3);

			Assert.IsTrue(player.TryPickup(medkit, 5));
			Assert.AreEqual(5, player.GetTotalCount(medkit));
			Assert.IsTrue(player.HasItem(medkit, 5));
			Assert.IsTrue(player.RemoveItem(medkit, 2));
			Assert.AreEqual(3, player.GetTotalCount(medkit));
		}

		// ---------- Сетка → колесо ----------

		[Test]
		public void MoveToEmptySlot_LeavesGrid()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol"));
			player.Grid.TryAdd(pistol);

			Assert.IsTrue(player.TryMoveToSlot(player.Grid, pistol, 2));
			Assert.AreSame(pistol, player.Loadout.GetItem(2));
			Assert.IsFalse(player.Grid.Contains(pistol));
		}

		[Test]
		public void MoveToOccupiedSlot_PreviousTakesNewOnesPlace()
		{
			InventoryHolder player = _utils.Player(4, 2);
			WeaponItemData weapon = _utils.Weapon("Pistol", 2, 1);
			ItemInstance inWheel = new ItemInstance(weapon);
			ItemInstance inGrid = new ItemInstance(weapon);
			player.Loadout.TryPut(0, inWheel, out _);
			player.Grid.TryPlace(inGrid, new Vector2Int(2, 1));

			Assert.IsTrue(player.TryMoveToSlot(player.Grid, inGrid, 0));
			Assert.AreSame(inGrid, player.Loadout.GetItem(0));
			Assert.IsTrue(player.Grid.TryGetPosition(inWheel, out Vector2Int position));
			Assert.AreEqual(new Vector2Int(2, 1), position, "прежнее оружие встаёт ровно на место нового");
		}

		[Test]
		public void MoveToOccupiedSlot_PreviousDoesNotFitFreedPlace_GoesToAnyFreeSpot()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemInstance rifle = new ItemInstance(_utils.Weapon("Rifle", 3, 1));   // в колесе
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol", 1, 1)); // в сетке
			player.Loadout.TryPut(0, rifle, out _);
			player.Grid.TryPlace(pistol, new Vector2Int(3, 0)); // на месте пистолета 3×1 вылезет за край

			Assert.IsTrue(player.TryMoveToSlot(player.Grid, pistol, 0));
			Assert.IsTrue(player.Grid.TryGetPosition(rifle, out Vector2Int position));
			Assert.AreEqual(new Vector2Int(0, 0), position);
		}

		[Test]
		public void MoveToOccupiedSlot_NoSpaceForPrevious_RefusedAndNothingChanges()
		{
			InventoryHolder player = _utils.Player(1, 1);
			ItemInstance rifle = new ItemInstance(_utils.Weapon("Rifle", 2, 1));
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol", 1, 1));
			player.Loadout.TryPut(0, rifle, out _);
			player.Grid.TryAdd(pistol);

			Assert.IsFalse(player.CanMoveToSlot(player.Grid, pistol, 0));
			Assert.IsFalse(player.TryMoveToSlot(player.Grid, pistol, 0));
			Assert.AreSame(rifle, player.Loadout.GetItem(0));
			Assert.IsTrue(player.Grid.Contains(pistol));
		}

		[Test]
		public void MoveToSlot_NotWeapon_Refused()
		{
			InventoryHolder player = _utils.Player(2, 2);
			ItemInstance medkit = new ItemInstance(_utils.Item("Medkit"));
			player.Grid.TryAdd(medkit);

			Assert.IsFalse(player.TryMoveToSlot(player.Grid, medkit, 0));
			Assert.IsNull(player.Loadout.GetItem(0));
		}

		// ---------- Колесо → сетка, колесо → колесо ----------

		[Test]
		public void MoveFromSlot_ToFreeCell()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol", 2, 1));
			player.Loadout.TryPut(1, pistol, out _);

			Assert.IsTrue(player.TryMoveFromSlot(1, player.Grid, new Vector2Int(2, 1)));
			Assert.IsNull(player.Loadout.GetItem(1));
			Assert.IsTrue(player.Grid.TryGetPosition(pistol, out Vector2Int position));
			Assert.AreEqual(new Vector2Int(2, 1), position);
		}

		[Test]
		public void MoveFromSlot_ToOccupiedCell_Refused()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol", 2, 1));
			player.Loadout.TryPut(0, pistol, out _);
			player.Grid.TryPlace(new ItemInstance(_utils.Item("Box")), new Vector2Int(1, 0));

			Assert.IsFalse(player.TryMoveFromSlot(0, player.Grid, new Vector2Int(0, 0)));
			Assert.AreSame(pistol, player.Loadout.GetItem(0));
		}

		[Test]
		public void MoveFromSlot_AnyFreeSpot_NoSpace_Refused()
		{
			InventoryHolder player = _utils.Player(1, 1);
			ItemInstance rifle = new ItemInstance(_utils.Weapon("Rifle", 2, 1));
			player.Loadout.TryPut(0, rifle, out _);

			Assert.IsFalse(player.CanMoveFromSlot(0, player.Grid, null));
			Assert.IsFalse(player.TryMoveFromSlot(0, player.Grid, null));
			Assert.AreSame(rifle, player.Loadout.GetItem(0));
		}

		[Test]
		public void SwapSlots_IncludingEmpty()
		{
			InventoryHolder player = _utils.Player(2, 2);
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol"));
			player.Loadout.TryPut(0, pistol, out _);

			Assert.IsTrue(player.TrySwapSlots(0, 3));
			Assert.IsNull(player.Loadout.GetItem(0));
			Assert.AreSame(pistol, player.Loadout.GetItem(3));
		}

		[Test]
		public void FirstEmptySlot_Clockwise()
		{
			InventoryHolder player = _utils.Player(2, 2);
			WeaponItemData pistol = _utils.Weapon("Pistol");
			player.Loadout.TryPut(0, new ItemInstance(pistol), out _);
			player.Loadout.TryPut(2, new ItemInstance(pistol), out _);

			Assert.AreEqual(1, player.FirstEmptySlot());
		}

		// ---------- Выброс ----------

		[Test]
		public void Drop_WithoutDropperOrWorldPrefab_Refused_ItemStays()
		{
			InventoryHolder player = _utils.Player(2, 2);
			ItemInstance medkit = new ItemInstance(_utils.Item("Medkit"));
			player.Grid.TryAdd(medkit);

			// нет ItemDropper и World Prefab — предмет не должен пропасть из инвентаря "в никуда"
			Assert.IsFalse(player.TryDrop(medkit, 1));
			Assert.IsTrue(player.Grid.Contains(medkit));
		}
	}
}
