using System;

namespace Inventory
{
	// Стартовое содержимое сетки, задаётся в инспекторе (InventoryHolder, позже — контейнеры): какой предмет,
	// сколько штук и в какую клетку положить. (x, y) — верхняя левая клетка занимаемого прямоугольника.
	// В игре предметы живут уже как ItemInstance внутри ItemGrid — эта запись только для настройки.
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
