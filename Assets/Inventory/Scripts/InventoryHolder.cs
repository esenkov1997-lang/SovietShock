using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
	// Классический "тетрис"-инвентарь: прямоугольная сетка gridWidth x gridHeight, каждый предмет занимает
	// прямоугольник ItemData.GridSize ячеек начиная с верхнего левого угла (ItemStack.x/y). Свободное место
	// нигде отдельно не хранится — это просто ячейки, не покрытые прямоугольником ни одного предмета
	// (см. Overlaps/FindOverlapping), поэтому items содержит только реально лежащие предметы, без заглушек
	// под пустые ячейки.
	public class InventoryHolder : MonoBehaviour
	{
		[SerializeField] private int gridWidth = 6;
		[SerializeField] private int gridHeight = 4;
		[SerializeField] private List<ItemStack> items = new List<ItemStack>();

		public int GridWidth => gridWidth;
		public int GridHeight => gridHeight;
		public IReadOnlyList<ItemStack> Items => items;

		// для UI (список/слоты обновляются целиком по этому событию)
		public event Action OnInventoryChanged;
		// для точечных реакций на конкретный предмет (например, эквип оружия) — без пересчёта всего инвентаря
		public event Action<ItemData, int> OnItemAdded;
		public event Action<ItemData, int> OnItemRemoved;

		public bool AddItem(ItemData item, int amount)
		{
			if (item == null || amount <= 0) return false;

			// работаем на копии и подменяем items только при полном успехе — иначе, не хватив места на
			// поздние стеки, мы бы уже необратимо доложили предмет в существующие стеки наполовину
			List<ItemStack> scratch = new List<ItemStack>(items);
			int maxStack = item.maxStack > 0 ? item.maxStack : int.MaxValue;
			Vector2Int size = item.GridSize;
			int remaining = amount;

			for (int i = 0; i < scratch.Count && remaining > 0; i++)
			{
				if (scratch[i].item != item || scratch[i].count >= maxStack) continue;

				int add = Mathf.Min(maxStack - scratch[i].count, remaining);
				ItemStack stack = scratch[i];
				stack.count += add;
				scratch[i] = stack;
				remaining -= add;
			}

			while (remaining > 0)
			{
				if (!TryFindFreePosition(scratch, size, out Vector2Int position)) return false;

				int add = Mathf.Min(maxStack, remaining);
				scratch.Add(new ItemStack(item, add, position.x, position.y));
				remaining -= add;
			}

			items = scratch;
			OnItemAdded?.Invoke(item, amount);
			OnInventoryChanged?.Invoke();
			return true;
		}

		public bool HasItem(ItemData item, int amount)
		{
			return item != null && GetTotalCount(item) >= amount;
		}

		public bool RemoveItem(ItemData item, int amount)
		{
			if (amount <= 0 || !HasItem(item, amount)) return false;

			int remaining = amount;
			for (int i = items.Count - 1; i >= 0 && remaining > 0; i--)
			{
				if (items[i].item != item) continue;

				ItemStack stack = items[i];
				int take = Mathf.Min(stack.count, remaining);
				stack.count -= take;
				remaining -= take;

				if (stack.count <= 0) items.RemoveAt(i);
				else items[i] = stack;
			}

			OnItemRemoved?.Invoke(item, amount);
			OnInventoryChanged?.Invoke();
			return true;
		}

		// Перетаскивание уже лежащего предмета (по его текущему индексу в Items) на новую клетку — вызывает
		// InventoryGridDropZone. Если целевой прямоугольник свободен — просто переносит; если его целиком
		// занимает ровно один другой предмет — меняет их местами (классический своп); если задета площадь
		// больше чем одного соседа, или новое место выходит за границы сетки — отказывает без изменений.
		public bool TryMoveItem(int index, Vector2Int newPosition)
		{
			if (index < 0 || index >= items.Count) return false;

			ItemStack moving = items[index];
			Vector2Int size = moving.item.GridSize;
			RectInt targetRect = new RectInt(newPosition.x, newPosition.y, size.x, size.y);
			if (!IsWithinGrid(targetRect)) return false;

			List<int> overlapping = FindOverlapping(targetRect, index);

			if (overlapping.Count == 0)
			{
				MoveTo(index, newPosition);
				OnInventoryChanged?.Invoke();
				return true;
			}

			if (overlapping.Count == 1)
			{
				int otherIndex = overlapping[0];
				ItemStack other = items[otherIndex];
				RectInt otherNewRect = new RectInt(moving.x, moving.y, other.item.GridSize.x, other.item.GridSize.y);

				// сосед должен ещё и сам поместиться на освобождающееся место moving — с учётом того,
				// что оба предмета сейчас "убраны" с доски (иначе otherNewRect всегда пересекался бы с moving)
				if (!IsWithinGrid(otherNewRect) || FindOverlapping(otherNewRect, index, otherIndex).Count > 0) return false;

				MoveTo(otherIndex, new Vector2Int(moving.x, moving.y));
				MoveTo(index, newPosition);
				OnInventoryChanged?.Invoke();
				return true;
			}

			return false;
		}

		private void MoveTo(int index, Vector2Int position)
		{
			ItemStack stack = items[index];
			stack.x = position.x;
			stack.y = position.y;
			items[index] = stack;
		}

		private bool TryFindFreePosition(List<ItemStack> scratch, Vector2Int size, out Vector2Int position)
		{
			for (int y = 0; y <= gridHeight - size.y; y++)
			{
				for (int x = 0; x <= gridWidth - size.x; x++)
				{
					if (!Overlaps(scratch, new RectInt(x, y, size.x, size.y), -1))
					{
						position = new Vector2Int(x, y);
						return true;
					}
				}
			}

			position = default;
			return false;
		}

		private List<int> FindOverlapping(RectInt rect, params int[] ignoreIndices)
		{
			List<int> result = new List<int>();
			for (int i = 0; i < items.Count; i++)
			{
				if (Array.IndexOf(ignoreIndices, i) >= 0) continue;
				if (rect.Overlaps(RectOf(items[i]))) result.Add(i);
			}
			return result;
		}

		private static bool Overlaps(List<ItemStack> scratch, RectInt rect, int ignoreIndex)
		{
			for (int i = 0; i < scratch.Count; i++)
			{
				if (i == ignoreIndex) continue;
				if (rect.Overlaps(RectOf(scratch[i]))) return true;
			}
			return false;
		}

		private static RectInt RectOf(ItemStack stack)
		{
			Vector2Int size = stack.item.GridSize;
			return new RectInt(stack.x, stack.y, size.x, size.y);
		}

		private bool IsWithinGrid(RectInt rect)
		{
			return rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= gridWidth && rect.yMax <= gridHeight;
		}

		public int GetTotalCount(ItemData item)
		{
			int total = 0;
			foreach (ItemStack stack in items)
			{
				if (stack.item == item) total += stack.count;
			}
			return total;
		}
	}
}
