namespace Inventory
{
	// Предмет под курсором — общий для всего инвентаря (сетки, улики, колесо), как ItemDrag для перетаскивания.
	// Виды сообщают, когда курсор зашёл на предмет и ушёл с него; подсказку (ItemTooltipView) показывает экран
	// инвентаря по Item. owner — кто сообщил: уход с одного предмета не стирает уже наведённый другой
	public static class ItemHover
	{
		private static object s_owner;

		public static ItemInstance Item { get; private set; }

		public static void Enter(object owner, ItemInstance item)
		{
			if (owner == null || item == null)
			{
				Exit(owner);
				return;
			}

			s_owner = owner;
			Item = item;
		}

		public static void Exit(object owner)
		{
			if (owner == null || owner != s_owner) return;

			s_owner = null;
			Item = null;
		}
	}
}
