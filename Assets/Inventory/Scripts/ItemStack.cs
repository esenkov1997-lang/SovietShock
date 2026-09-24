using System;

namespace Inventory
{
	// Рантайм-запись в InventoryHolder: конкретный предмет, сколько его в стеке, и где в сетке он лежит.
	// (x, y) — верхняя левая ячейка занимаемого прямоугольника (см. ItemData.GridSize).
	[Serializable]
	public struct ItemStack
	{
		public ItemData item;
		public int count;
		public int x;
		public int y;

		public ItemStack(ItemData item, int count, int x, int y)
		{
			this.item = item;
			this.count = count;
			this.x = x;
			this.y = y;
		}
	}
}
