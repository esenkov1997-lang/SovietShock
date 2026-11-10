using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
	// Предмет, лежащий в сетке: сам экземпляр и верхняя левая клетка занимаемого прямоугольника
	public readonly struct GridItem
	{
		public readonly ItemInstance Instance;
		public readonly Vector2Int Position;

		public GridItem(ItemInstance instance, Vector2Int position)
		{
			Instance = instance;
			Position = position;
		}

		public Vector2Int Size => Instance.GridSize;
		public RectInt Rect => new RectInt(Position, Size);
	}

	// "Тетрис"-сетка без привязки к игроку: прямоугольник Width x Height клеток, каждый предмет занимает
	// прямоугольник своего ItemData.GridSize, предметы не поворачиваются. Свободное место нигде отдельно не
	// хранится — это клетки, не покрытые ни одним предметом. Одна и та же логика у инвентаря игрока
	// (InventoryHolder) и у контейнеров в мире.
	//
	// Все операции атомарные: либо выполняются целиком, либо ничего не меняют (и возвращают false).
	// Changed вызывается один раз после каждой успешной операции.
	public sealed class ItemGrid
	{
		private readonly List<GridItem> _items = new List<GridItem>();

		public int Width { get; }
		public int Height { get; }
		public IReadOnlyList<GridItem> Items => _items;

		public event Action Changed;

		public ItemGrid(int width, int height)
		{
			Width = Mathf.Max(1, width);
			Height = Mathf.Max(1, height);
		}

		// ---------- Запросы ----------

		public bool Contains(ItemInstance instance) => IndexOf(instance) >= 0;

		public bool TryGetPosition(ItemInstance instance, out Vector2Int position)
		{
			int index = IndexOf(instance);
			position = index >= 0 ? _items[index].Position : default;
			return index >= 0;
		}

		public bool IsInside(RectInt rect)
		{
			return rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= Width && rect.yMax <= Height;
		}

		// свободен ли прямоугольник (ignore — предмет, которого "нет на доске", например тот, что сейчас переносят)
		public bool IsFree(RectInt rect, ItemInstance ignore = null)
		{
			foreach (GridItem item in _items)
			{
				if (item.Instance != ignore && rect.Overlaps(item.Rect)) return false;
			}
			return true;
		}

		// можно ли положить предмет ровно в эту клетку (сам предмет, если он уже в сетке, не мешает)
		public bool CanPlace(ItemInstance instance, Vector2Int position)
		{
			if (instance == null) return false;
			RectInt rect = new RectInt(position, instance.GridSize);
			return IsInside(rect) && IsFree(rect, instance);
		}

		// первая свободная клетка под размер — слева направо, сверху вниз
		public bool TryFindFreePosition(Vector2Int size, out Vector2Int position, ItemInstance ignore = null)
		{
			for (int y = 0; y <= Height - size.y; y++)
			{
				for (int x = 0; x <= Width - size.x; x++)
				{
					if (IsFree(new RectInt(x, y, size.x, size.y), ignore))
					{
						position = new Vector2Int(x, y);
						return true;
					}
				}
			}

			position = default;
			return false;
		}

		// предмет, занимающий клетку; null — клетка пустая или вне сетки
		public ItemInstance GetAt(Vector2Int cell)
		{
			foreach (GridItem item in _items)
			{
				if (item.Rect.Contains(cell)) return item.Instance;
			}
			return null;
		}

		public int CountOf(ItemData data)
		{
			int total = 0;
			foreach (GridItem item in _items)
			{
				if (item.Instance.Data == data) total += item.Instance.Count;
			}
			return total;
		}

		// первый предмет, подходящий под условие (например, первая аптечка) — в порядке, в котором их клали
		public ItemInstance Find(Predicate<ItemInstance> match)
		{
			foreach (GridItem item in _items)
			{
				if (match(item.Instance)) return item.Instance;
			}
			return null;
		}

		// ---------- Добавление ----------

		// Положить предмет ровно в клетку. Предмет не должен уже лежать в этой сетке (для этого — TryMove)
		public bool TryPlace(ItemInstance instance, Vector2Int position)
		{
			if (instance == null || Contains(instance) || !CanPlace(instance, position)) return false;

			_items.Add(new GridItem(instance, position));
			Changed?.Invoke();
			return true;
		}

		// Положить предмет куда получится: сначала долить в уже лежащие такие же стопки, остаток — в свободное место.
		// Если стопку долили целиком, сам instance в сетку не попадает (его Count станет 0) — он "растворился" в других
		public bool TryAdd(ItemInstance instance)
		{
			if (instance == null || Contains(instance)) return false;
			return TryAddInternal(instance.Data, instance.Count, instance);
		}

		// Добавить amount штук предмета (подбор, "Взять всё"): стопки доливаются, остаток раскладывается новыми
		// стопками не больше maxStack каждая
		public bool TryAdd(ItemData data, int amount)
		{
			if (data == null || amount <= 0) return false;
			return TryAddInternal(data, amount, null);
		}

		// влезет ли amount штук (ничего не меняя)
		public bool CanAdd(ItemData data, int amount)
		{
			if (data == null || amount <= 0) return false;
			return Plan(data, amount, null, out _, out _);
		}

		private bool TryAddInternal(ItemData data, int amount, ItemInstance source)
		{
			if (!Plan(data, amount, source, out List<(int index, int add)> merges, out List<GridItem> pieces)) return false;

			int merged = 0;
			foreach ((int index, int add) merge in merges)
			{
				ItemInstance stack = _items[merge.index].Instance;
				stack.SetCount(stack.Count + merge.add);
				merged += merge.add;
			}
			_items.AddRange(pieces);

			if (source != null)
			{
				// в переносимом экземпляре остаётся ровно то, что легло в его стопку (остальное разлито по другим);
				// весь ушёл в чужие стопки — у него больше нет своей
				bool placed = pieces.Count > 0 && pieces[0].Instance == source;
				source.SetCount(placed ? Mathf.Min(source.MaxStack, amount - merged) : 0);
			}

			Changed?.Invoke();
			return true;
		}

		// Раскладка amount штук без изменения сетки: сколько долить в какие стопки и какие новые стопки куда положить.
		// source — существующий экземпляр (переносимый из другого места): он становится первой новой стопкой, чтобы
		// не потерять его состояние; остальные стопки создаются заново
		private bool Plan(ItemData data, int amount, ItemInstance source, out List<(int index, int add)> merges,
			out List<GridItem> pieces)
		{
			merges = new List<(int index, int add)>();
			pieces = new List<GridItem>();

			ItemInstance prototype = source ?? new ItemInstance(data);
			int remaining = amount;

			if (prototype.IsStackable)
			{
				for (int i = 0; i < _items.Count && remaining > 0; i++)
				{
					ItemInstance stack = _items[i].Instance;
					if (!stack.CanStackWith(prototype) || stack.SpaceLeft <= 0) continue;

					int add = Mathf.Min(stack.SpaceLeft, remaining);
					merges.Add((i, add));
					remaining -= add;
				}
			}

			// новые стопки ставим "временно" прямо в список, чтобы следующая искала место уже с учётом предыдущих,
			// и сразу убираем — до коммита сетка не меняется
			int firstPiece = _items.Count;
			bool fits = true;
			while (remaining > 0)
			{
				int count = Mathf.Min(prototype.MaxStack, remaining);
				ItemInstance piece = pieces.Count == 0 && source != null ? source : new ItemInstance(data, count);

				if (!TryFindFreePosition(piece.GridSize, out Vector2Int position))
				{
					fits = false;
					break;
				}

				GridItem placed = new GridItem(piece, position);
				_items.Add(placed);
				pieces.Add(placed);
				remaining -= count;
			}
			_items.RemoveRange(firstPiece, _items.Count - firstPiece);

			return fits;
		}

		// ---------- Перемещение ----------

		// Переложить лежащий в сетке предмет на новую клетку. Место свободно — просто перенос; его целиком
		// перекрывает ровно один другой предмет, и тот помещается на освобождающееся место — обмен местами;
		// иначе (задето больше одного предмета, выход за край) — отказ без изменений
		public bool TryMove(ItemInstance instance, Vector2Int position)
		{
			int index = IndexOf(instance);
			if (index < 0) return false;

			RectInt target = new RectInt(position, instance.GridSize);
			if (!IsInside(target)) return false;

			List<int> overlapping = FindOverlapping(target, instance, null);
			if (overlapping.Count == 0)
			{
				_items[index] = new GridItem(instance, position);
				Changed?.Invoke();
				return true;
			}

			if (overlapping.Count != 1) return false;

			int otherIndex = overlapping[0];
			GridItem other = _items[otherIndex];
			Vector2Int otherPosition = _items[index].Position;
			RectInt otherTarget = new RectInt(otherPosition, other.Size);

			// сосед должен сам поместиться на освобождающееся место — с учётом того, что оба предмета сейчас "сняты с доски"
			// и что переносимый уже будет лежать на новом месте
			if (!IsInside(otherTarget) || otherTarget.Overlaps(target) ||
				FindOverlapping(otherTarget, instance, other.Instance).Count > 0)
			{
				return false;
			}

			_items[index] = new GridItem(instance, position);
			_items[otherIndex] = new GridItem(other.Instance, otherPosition);
			Changed?.Invoke();
			return true;
		}

		// ---------- Удаление ----------

		public bool Remove(ItemInstance instance)
		{
			int index = IndexOf(instance);
			if (index < 0) return false;

			_items.RemoveAt(index);
			Changed?.Invoke();
			return true;
		}

		// Списать amount штук предмета (патроны на перезарядку, ключ на дверь). Нет столько — ничего не списывается.
		// Берётся с последних положенных стопок; опустевшие стопки убираются
		public bool TryRemove(ItemData data, int amount)
		{
			if (data == null || amount <= 0 || CountOf(data) < amount) return false;

			int remaining = amount;
			for (int i = _items.Count - 1; i >= 0 && remaining > 0; i--)
			{
				ItemInstance stack = _items[i].Instance;
				if (stack.Data != data) continue;

				int take = Mathf.Min(stack.Count, remaining);
				stack.SetCount(stack.Count - take);
				remaining -= take;
				if (stack.Count <= 0) _items.RemoveAt(i);
			}

			Changed?.Invoke();
			return true;
		}

		// ---------- Внутреннее ----------

		private int IndexOf(ItemInstance instance)
		{
			if (instance == null) return -1;

			for (int i = 0; i < _items.Count; i++)
			{
				if (_items[i].Instance == instance) return i;
			}
			return -1;
		}

		private List<int> FindOverlapping(RectInt rect, ItemInstance ignoreA, ItemInstance ignoreB)
		{
			List<int> result = new List<int>();
			for (int i = 0; i < _items.Count; i++)
			{
				ItemInstance instance = _items[i].Instance;
				if (instance == ignoreA || instance == ignoreB) continue;
				if (rect.Overlaps(_items[i].Rect)) result.Add(i);
			}
			return result;
		}
	}
}
