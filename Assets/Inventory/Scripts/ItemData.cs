using UnityEngine;
using UnityEngine.Localization;

namespace Inventory
{
	// Data-driven описание предмета. Сам ассет не хранит рантайм-состояние (сколько штук у игрока) —
	// это делает InventoryHolder через ItemStack, ровно та же идея, что WeaponData/WeaponSlot в системе оружия.
	[CreateAssetMenu(fileName = "NewItemData", menuName = "Inventory/Item Data")]
	public class ItemData : ScriptableObject
	{
		[Header("Identity")]
		public string id;
		[Tooltip("Название предмета — ключ таблицы локализации (например, таблица Items, ключ item.medkit). " +
			"Используется и в подсказке подбора, и в ячейке инвентаря")]
		public LocalizedString itemName = new LocalizedString();
		public Sprite icon;
		public ItemType type;

		[Header("World")]
		[Tooltip("Префаб предмета в мире (должен содержать компонент WorldItem) — спавнится, например, при выбрасывании из инвентаря")]
		public GameObject worldPrefab;

		[Header("Stacking")]
		[Tooltip("Максимум предметов в одном стеке. 1 — предмет не стакается (оружие, квестовые предметы)")]
		public int maxStack = 1;

		[Header("Grid")]
		[Tooltip("Сколько ячеек занимает предмет в сетке инвентаря по ширине/высоте — например, 1x1 (канистра) или 1x2 (оружие)")]
		[SerializeField] private Vector2Int gridSize = Vector2Int.one;

		// клампим здесь, а не в OnValidate — у WeaponItemData/AmmoItemData уже есть свой OnValidate,
		// и заводить ещё один в базовом классе означало бы полагаться на то, что подкласс не забудет
		// вызвать base.OnValidate() (Unity это не проверяет и не гарантирует)
		public Vector2Int GridSize => new Vector2Int(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
	}
}
