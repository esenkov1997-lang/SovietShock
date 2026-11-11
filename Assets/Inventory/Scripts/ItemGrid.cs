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

		// влезет ли amount штук (ничего не меняя); для уже существующего экземпляра — CanAdd(ItemInstance)
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

		public enum MoveKind { Invalid, Move, Swap, Merge }

		// Что произойдёт, если положить лежащий в сетке предмет верхним левым углом в клетку position (ничего не
		// меняя) — для подсветки места при перетаскивании. other — второй участник (Swap/Merge), иначе null:
		//   Move  — место свободно, просто перенос;
		//   Merge — предмет бросили на такую же стопку, в которой есть место: доливается в неё;
		//   Swap  — место целиком занимает ровно один другой предмет, и он помещается на освобождающееся место;
		//   Invalid — задето больше одного предмета, выход за край или обмен не помещается
		public MoveKind EvaluateMove(ItemInstance instance, Vector2Int position, out ItemInstance other)
		{
			other = null;
			int index = IndexOf(instance);
			if (index < 0) return MoveKind.Invalid;

			RectInt target = new RectInt(position, instance.GridSize);
			List<int> overlapping = FindOverlapping(target, instance, null);

			// стопку бросили на такую же стопку — доливаем (даже если предмет торчит за край: важна цель, а не форма)
			if (overlapping.Count == 1)
			{
				ItemInstance stack = _items[overlapping[0]].Instance;
				if (stack.CanStackWith(instance) && stack.SpaceLeft > 0)
				{
					other = stack;
					return MoveKind.Merge;
				}
			}

			if (!IsInside(target)) return MoveKind.Invalid;
			if (overlapping.Count == 0) return MoveKind.Move;
			if (overlapping.Count != 1) return MoveKind.Invalid;

			GridItem neighbour = _items[overlapping[0]];
			RectInt neighbourTarget = new RectInt(_items[index].Position, neighbour.Size);

			// сосед должен сам поместиться на освобождающееся место — с учётом того, что оба предмета сейчас
			// "сняты с доски" и что переносимый уже будет лежать на новом месте
			if (!IsInside(neighbourTarget) || neighbourTarget.Overlaps(target) ||
				FindOverlapping(neighbourTarget, instance, neighbour.Instance).Count > 0)
			{
				return MoveKind.Invalid;
			}

			other = neighbour.Instance;
			return MoveKind.Swap;
		}

		// Переложить лежащий в сетке предмет (см. EvaluateMove — перенос, обмен местами или долив в стопку).
		// Invalid — отказ без изменений
		public bool TryMove(ItemInstance instance, Vector2Int position)
		{
			MoveKind kind = EvaluateMove(instance, position, out ItemInstance other);
			int index = IndexOf(instance);

			switch (kind)
			{
				case MoveKind.Move:
					_items[index] = new GridItem(instance, position);
					break;

				case MoveKind.Swap:
					int otherIndex = IndexOf(other);
					Vector2Int freed = _items[index].Position;
					_items[index] = new GridItem(instance, position);
					_items[otherIndex] = new GridItem(other, freed);
					break;

				case MoveKind.Merge:
					// доливаем сколько влезет, остаток остаётся лежать на старом месте
					int moved = Mathf.Min(other.SpaceLeft, instance.Count);
					other.SetCount(other.Count + moved);
					instance.SetCount(instance.Count - moved);
					if (instance.Count <= 0) _items.RemoveAt(index);
					break;

				default:
					return false;
			}

			Changed?.Invoke();
			return true;
		}

		// ---------- Перенос из другой сетки (ящик ↔ инвентарь) ----------

		// Что будет, если положить предмет из сетки source верхним левым углом в клетку position ЭТОЙ сетки
		// (ничего не меняя) — для подсветки при перетаскивании:
		//   Move  — место свободно: предмет переходит целиком;
		//   Merge — бросили на такую же стопку, в которой есть место: доливается сколько влезет, остаток остаётся в source;
		//   Invalid — занято другим предметом или выходит за край. Обмена местами между разными сетками нет
		public MoveKind EvaluateTransfer(ItemGrid source, ItemInstance instance, Vector2Int position, out ItemInstance other)
		{
			other = null;
			if (source == null || source == this || instance == null || !source.Contains(instance)) return MoveKind.Invalid;

			RectInt target = new RectInt(position, instance.GridSize);
			List<int> overlapping = FindOverlapping(target, null, null);

			if (overlapping.Count == 1)
			{
				ItemInstance stack = _items[overlapping[0]].Instance;
				if (stack.CanStackWith(instance) && stack.SpaceLeft > 0)
				{
					other = stack;
					return MoveKind.Merge;
				}
			}

			return IsInside(target) && overlapping.Count == 0 ? MoveKind.Move : MoveKind.Invalid;
		}

		// Перенести предмет из source в клетку этой сетки по правилам EvaluateTransfer
		public bool TryTransfer(ItemGrid source, ItemInstance instance, Vector2Int position)
		{
			switch (EvaluateTransfer(source, instance, position, out ItemInstance other))
			{
				case MoveKind.Move:
					source.Remove(instance);
					return TryPlace(instance, position);

				case MoveKind.Merge:
					int moved = Mathf.Min(other.SpaceLeft, instance.Count);
					other.SetCount(other.Count + moved);
					Changed?.Invoke();
					if (moved >= instance.Count) source.Remove(instance);
					else source.ChangeCount(instance, instance.Count - moved);
					return true;

				default:
					return false;
			}
		}

		// Влезет ли предмет из другого места целиком (стопки доливаются, остаток — в свободное место) — двойной клик,
		// "Взять все". Предмет не должен уже лежать в этой сетке
		public bool CanAdd(ItemInstance instance)
		{
			if (instance == null || instance.Data == null || Contains(instance)) return false;
			return Plan(instance.Data, instance.Count, instance, out _, out _);
		}

		// Перенести предмет из source целиком куда получится (см. CanAdd). Не влезает целиком — ничего не меняется
		public bool TryTransferAll(ItemGrid source, ItemInstance instance)
		{
			if (source == null || source == this || !source.Contains(instance) || !CanAdd(instance)) return false;

			source.Remove(instance);
			return TryAdd(instance);
		}

		// ---------- Удаление ----------

		// часть стопки ушла в другую сетку — меняем количество и сообщаем об изменении
		private void ChangeCount(ItemInstance instance, int count)
		{
			instance.SetCount(count);
			Changed?.Invoke();
		}

		public bool Remove(ItemInstance instance)
		{
			int index = IndexOf(instance);
			if (index < 0) return false;

			_items.RemoveAt(index);
			Changed?.Invoke();
			return true;
		}

		// Забрать из конкретной стопки amount штук (выбросить одну, перенести в ящик). Берётся вся стопка — уходит
		// сам экземпляр (со своим состоянием); часть — стопка уменьшается, а taken — новый экземпляр с этой частью
		public bool TryTake(ItemInstance instance, int amount, out ItemInstance taken)
		{
			taken = null;
			int index = IndexOf(instance);
			if (index < 0 || amount <= 0) return false;

			if (amount >= instance.Count)
			{
				_items.RemoveAt(index);
				taken = instance;
			}
			else
			{
				instance.SetCount(instance.Count - amount);
				taken = new ItemInstance(instance.Data, amount);
			}

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
