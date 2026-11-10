using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Weapons;

namespace Inventory.Tests
{
	// Общие заготовки для тестов инвентаря: временные ассеты предметов и объект игрока. Всё созданное
	// уничтожается в Cleanup — тесты не оставляют мусора ни в сцене, ни в проекте.
	public sealed class InventoryTestUtils
	{
		private readonly List<Object> _created = new List<Object>();

		// обычный предмет: размер в клетках и сколько штук в стопке (1 — не складывается)
		public ItemData Item(string name, int width = 1, int height = 1, int maxStack = 1)
		{
			ItemData data = Track(ScriptableObject.CreateInstance<ItemData>());
			data.name = name;
			data.maxStack = maxStack;
			SetGridSize(data, width, height);
			return data;
		}

		// оружие с магазином на magazine патронов (для проверки, что состояние экземпляра сохраняется)
		public WeaponItemData Weapon(string name, int width = 2, int height = 1, int magazine = 30)
		{
			FirearmData firearm = Track(ScriptableObject.CreateInstance<FirearmData>());
			firearm.name = name + "_Data";
			firearm.Magazine.MagazineSize = magazine;

			WeaponItemData data = Track(ScriptableObject.CreateInstance<WeaponItemData>());
			data.name = name;
			data.maxStack = 1;
			data.weaponData = firearm;
			SetGridSize(data, width, height);
			return data;
		}

		// игрок: InventoryHolder с сеткой width x height и колесом на slots слотов (без WeaponController —
		// колесо просто хранит оружие, в руки ничего не берётся)
		public InventoryHolder Player(int width, int height, int slots = 4)
		{
			GameObject go = Track(new GameObject("TestPlayer"));
			InventoryHolder holder = go.AddComponent<InventoryHolder>();
			SetInt(holder, "gridWidth", width);
			SetInt(holder, "gridHeight", height);

			WeaponLoadout loadout = go.AddComponent<WeaponLoadout>();
			SetInt(loadout, "slotCount", slots);
			return holder;
		}

		public void Cleanup()
		{
			foreach (Object obj in _created)
			{
				if (obj != null) Object.DestroyImmediate(obj);
			}
			_created.Clear();
		}

		private T Track<T>(T obj) where T : Object
		{
			_created.Add(obj);
			return obj;
		}

		// приватные [SerializeField] — так же, как их выставил бы инспектор
		private static void SetGridSize(ItemData data, int width, int height)
		{
			SerializedObject so = new SerializedObject(data);
			so.FindProperty("gridSize").vector2IntValue = new Vector2Int(width, height);
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void SetInt(Object target, string field, int value)
		{
			SerializedObject so = new SerializedObject(target);
			so.FindProperty(field).intValue = value;
			so.ApplyModifiedPropertiesWithoutUndo();
		}
	}
}
