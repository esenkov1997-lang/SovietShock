using NUnit.Framework;
using UnityEngine;

namespace Inventory.Tests
{
	// Обмен с контейнером (InventoryHolder, раздел "Лут"): взять, положить, "Взять все", перетаскивание в клетку,
	// оружие из ящика в колесо. Сетка ящика — просто ItemGrid, как у LootContainer
	public class LootTests
	{
		private InventoryTestUtils _utils;

		[SetUp] public void SetUp() => _utils = new InventoryTestUtils();
		[TearDown] public void TearDown() => _utils.Cleanup();

		// ---------- Взять / положить целиком ----------

		[Test]
		public void TakeClue_GoesToClues_AndLeavesLoot()
		{
			InventoryHolder player = _utils.Player(1, 1);
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance key = new ItemInstance(_utils.Clue("Key"));
			loot.TryAdd(key);

			Assert.IsTrue(player.TryTakeFrom(loot, key));
			Assert.AreEqual(1, player.Clues.Items.Count);
			Assert.AreEqual(0, loot.Items.Count);
		}

		[Test]
		public void TakeWeapon_GoesToGrid_NotWheel()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol"));
			loot.TryAdd(pistol);

			Assert.IsTrue(player.TryTakeFrom(loot, pistol));
			Assert.IsTrue(player.Grid.Contains(pistol));
			Assert.IsNull(player.Loadout.GetItem(0), "из ящика оружие не идёт в колесо само");
		}

		[Test]
		public void Take_DoesNotFitWhole_RefusedAndNothingChanges()
		{
			InventoryHolder player = _utils.Player(2, 1);
			player.Grid.TryAdd(new ItemInstance(_utils.Item("Box")));
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance rifle = new ItemInstance(_utils.Item("Rifle", 2, 1));
			loot.TryAdd(rifle);

			Assert.IsFalse(player.CanTakeFrom(loot, rifle));
			Assert.IsFalse(player.TryTakeFrom(loot, rifle));
			Assert.IsTrue(loot.Contains(rifle));
			Assert.AreEqual(1, player.Grid.Items.Count);
		}

		[Test]
		public void Take_StackMergesIntoExistingStack()
		{
			InventoryHolder player = _utils.Player(1, 1);
			ItemData ammo = _utils.Item("Ammo", maxStack: 30);
			player.Grid.TryAdd(ammo, 10);
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance lootAmmo = new ItemInstance(ammo, 15);
			loot.TryAdd(lootAmmo);

			Assert.IsTrue(player.TryTakeFrom(loot, lootAmmo));
			Assert.AreEqual(25, player.GetTotalCount(ammo));
			Assert.AreEqual(1, player.Grid.Items.Count);
			Assert.AreEqual(0, loot.Items.Count);
		}

		[Test]
		public void TakeAll_TakesWhatFits_RestStays()
		{
			InventoryHolder player = _utils.Player(1, 1);
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance medkit = new ItemInstance(_utils.Item("Medkit"));
			ItemInstance box = new ItemInstance(_utils.Item("Box"));
			ItemInstance note = new ItemInstance(_utils.Clue("Note", ItemType.Document));
			loot.TryAdd(medkit);
			loot.TryAdd(box);
			loot.TryAdd(note);

			Assert.IsFalse(player.TakeAll(loot), "сетка 1×1 — второй предмет не влезет");
			Assert.IsTrue(player.Grid.Contains(medkit));
			Assert.IsTrue(loot.Contains(box));
			Assert.AreEqual(1, player.Clues.Items.Count, "улики забираются всегда — им место не нужно");
		}

		[Test]
		public void Put_FromGridToLoot()
		{
			InventoryHolder player = _utils.Player(2, 2);
			ItemInstance medkit = new ItemInstance(_utils.Item("Medkit"));
			player.Grid.TryAdd(medkit);
			ItemGrid loot = new ItemGrid(4, 4);

			Assert.IsTrue(player.TryPutTo(loot, medkit));
			Assert.IsTrue(loot.Contains(medkit));
			Assert.IsFalse(player.Grid.Contains(medkit));
		}

		[Test]
		public void Put_LootFull_Refused()
		{
			InventoryHolder player = _utils.Player(2, 2);
			ItemInstance medkit = new ItemInstance(_utils.Item("Medkit"));
			player.Grid.TryAdd(medkit);
			ItemGrid loot = new ItemGrid(1, 1);
			loot.TryAdd(new ItemInstance(_utils.Item("Box")));

			Assert.IsFalse(player.TryPutTo(loot, medkit));
			Assert.IsTrue(player.Grid.Contains(medkit));
		}

		// ---------- Перетаскивание в клетку ----------

		[Test]
		public void DragFromLoot_ToFreeCell()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance medkit = new ItemInstance(_utils.Item("Medkit"));
			loot.TryAdd(medkit);

			Assert.IsTrue(player.TryTakeFrom(loot, medkit, new Vector2Int(3, 1)));
			Assert.IsTrue(player.Grid.TryGetPosition(medkit, out Vector2Int position));
			Assert.AreEqual(new Vector2Int(3, 1), position);
		}

		[Test]
		public void DragFromLoot_ToOccupiedCell_Refused()
		{
			InventoryHolder player = _utils.Player(4, 2);
			player.Grid.TryPlace(new ItemInstance(_utils.Item("Box")), new Vector2Int(0, 0));
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance medkit = new ItemInstance(_utils.Item("Medkit"));
			loot.TryAdd(medkit);

			Assert.IsFalse(player.TryTakeFrom(loot, medkit, new Vector2Int(0, 0)));
			Assert.IsTrue(loot.Contains(medkit));
		}

		[Test]
		public void DragClue_ToGridCell_Refused()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance key = new ItemInstance(_utils.Clue("Key"));
			loot.TryAdd(key);

			Assert.IsFalse(player.CanTakeFrom(loot, key, new Vector2Int(0, 0)), "улика не ложится в сетку — только в улики");
		}

		[Test]
		public void DragOntoSameStack_MergesWhatFits_RemainderStaysInSource()
		{
			InventoryHolder player = _utils.Player(4, 2);
			ItemData ammo = _utils.Item("Ammo", maxStack: 30);
			ItemInstance playerAmmo = new ItemInstance(ammo, 25);
			player.Grid.TryPlace(playerAmmo, new Vector2Int(0, 0));
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance lootAmmo = new ItemInstance(ammo, 10);
			loot.TryAdd(lootAmmo);

			Assert.IsTrue(player.TryTakeFrom(loot, lootAmmo, new Vector2Int(0, 0)));
			Assert.AreEqual(30, playerAmmo.Count);
			Assert.AreEqual(5, lootAmmo.Count);
			Assert.IsTrue(loot.Contains(lootAmmo), "не влезшее осталось в ящике");
		}

		// ---------- Колесо ↔ ящик ----------

		[Test]
		public void WeaponFromLoot_ToOccupiedSlot_PreviousGoesToLoot()
		{
			InventoryHolder player = _utils.Player(1, 1);
			ItemInstance inWheel = new ItemInstance(_utils.Weapon("Rifle"));
			player.Loadout.TryPut(0, inWheel, out _);
			ItemGrid loot = new ItemGrid(4, 4);
			ItemInstance fromLoot = new ItemInstance(_utils.Weapon("Pistol"));
			loot.TryPlace(fromLoot, new Vector2Int(1, 2));

			Assert.IsTrue(player.TryMoveToSlot(loot, fromLoot, 0));
			Assert.AreSame(fromLoot, player.Loadout.GetItem(0));
			Assert.IsTrue(loot.TryGetPosition(inWheel, out Vector2Int position), "прежнее оружие уходит в ящик");
			Assert.AreEqual(new Vector2Int(1, 2), position);
		}

		[Test]
		public void WeaponFromSlot_ToLootCell()
		{
			InventoryHolder player = _utils.Player(1, 1);
			ItemInstance pistol = new ItemInstance(_utils.Weapon("Pistol"));
			player.Loadout.TryPut(2, pistol, out _);
			ItemGrid loot = new ItemGrid(4, 4);

			Assert.IsTrue(player.TryMoveFromSlot(2, loot, new Vector2Int(2, 3)));
			Assert.IsNull(player.Loadout.GetItem(2));
			Assert.IsTrue(loot.Contains(pistol));
		}
	}
}
