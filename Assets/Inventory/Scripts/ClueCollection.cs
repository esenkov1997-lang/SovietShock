using System;
using System.Collections.Generic;

namespace Inventory
{
	// Улики игрока (ключи и документы — ItemData.IsClue): простой список без места и размеров, в порядке подбора.
	// Одинаковые предметы складываются в одну запись (два одинаковых ключа — "Ключ ×2").
	public sealed class ClueCollection
	{
		private readonly List<ItemInstance> _items = new List<ItemInstance>();

		public IReadOnlyList<ItemInstance> Items => _items;
		public event Action Changed;

		public bool Contains(ItemInstance item) => _items.Contains(item);

		public int CountOf(ItemData data)
		{
			ItemInstance entry = Find(data);
			return entry != null ? entry.Count : 0;
		}

		public bool TryAdd(ItemData data, int amount)
		{
			if (data == null || !data.IsClue || amount <= 0) return false;

			ItemInstance entry = Find(data);
			if (entry != null) entry.SetCount(entry.Count + amount);
			else _items.Add(new ItemInstance(data, amount));

			Changed?.Invoke();
			return true;
		}

		// уже существующий экземпляр (выброшенный когда-то или из ящика) — сохраняется он сам, если такого ещё нет
		public bool TryAdd(ItemInstance item)
		{
			if (item == null || item.Data == null || !item.Data.IsClue || Contains(item)) return false;

			ItemInstance entry = Find(item.Data);
			if (entry != null) entry.SetCount(entry.Count + item.Count);
			else _items.Add(item);

			Changed?.Invoke();
			return true;
		}

		// списать (ключ, который тратится на дверь). Не хватает — ничего не списывается
		public bool TryRemove(ItemData data, int amount)
		{
			ItemInstance entry = Find(data);
			if (entry == null || amount <= 0 || entry.Count < amount) return false;

			entry.SetCount(entry.Count - amount);
			if (entry.Count <= 0) _items.Remove(entry);

			Changed?.Invoke();
			return true;
		}

		private ItemInstance Find(ItemData data)
		{
			foreach (ItemInstance item in _items)
			{
				if (item.Data == data) return item;
			}
			return null;
		}
	}
}
