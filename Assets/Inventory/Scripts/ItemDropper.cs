using UnityEngine;

namespace Inventory
{
	// Выбрасывает предметы из инвентаря в мир: спавнит worldPrefab предмета перед игроком с небольшим толчком.
	// Вешается на корень игрока рядом с InventoryHolder. Точка выброса — например, DropPoint у WeaponHolder.
	// Выброшенный предмет несёт тот же экземпляр (ItemInstance): подобрав его снова, игрок получит то же оружие
	// с тем же магазином, ту же стопку.
	public class ItemDropper : MonoBehaviour
	{
		[Tooltip("Откуда и в каком направлении (вперёд — синяя ось) летит предмет. Пусто — от этого объекта")]
		[SerializeField] private Transform dropPoint;
		[Tooltip("Насколько впереди точки появляется предмет, м")]
		[Min(0f)]
		[SerializeField] private float forwardOffset = 0.5f;
		[Tooltip("Сила толчка вперёд и чуть вверх (если у предмета есть Rigidbody)")]
		[Min(0f)]
		[SerializeField] private float force = 3f;
		[Tooltip("Стопка выбрасывается поштучно (см. Drop Stack As One Object у предмета): разброс точек появления, м — " +
			"чтобы предметы не появились друг в друге")]
		[Min(0f)]
		[SerializeField] private float spread = 0.15f;

		// false — у предмета не задан World Prefab (или на нём нет WorldItem), выбрасывать нечего
		public static bool CanDrop(ItemInstance item)
		{
			return item != null && item.Data != null && item.Data.worldPrefab != null;
		}

		public bool Drop(ItemInstance item)
		{
			if (!CanDrop(item))
			{
				if (item?.Data != null)
				{
					Debug.LogWarning($"{item.Data.name}: не задан World Prefab — предмет нельзя выбросить в мир", item.Data);
				}
				return false;
			}

			// одним объектом — сама стопка; поштучно — сам экземпляр становится первой штукой, остальные — новые
			if (item.Data.dropStackAsOneObject || item.Count <= 1)
			{
				Spawn(item, Vector3.zero);
				return true;
			}

			int count = item.Count;
			item.SetCount(1);
			Spawn(item, Vector3.zero);
			for (int i = 1; i < count; i++)
			{
				Vector2 offset = Random.insideUnitCircle * spread;
				Spawn(new ItemInstance(item.Data), new Vector3(offset.x, 0f, offset.y));
			}
			return true;
		}

		// localOffset — смещение в осях точки выброса (x — вбок, z — вперёд)
		private void Spawn(ItemInstance item, Vector3 localOffset)
		{
			Transform origin = dropPoint != null ? dropPoint : transform;
			Vector3 position = origin.position + origin.forward * forwardOffset + origin.TransformVector(localOffset);
			GameObject dropped = Instantiate(item.Data.worldPrefab, position, origin.rotation);

			if (dropped.TryGetComponent(out WorldItem worldItem)) worldItem.SetInstance(item);
			else Debug.LogWarning($"{item.Data.name}: на World Prefab нет компонента WorldItem — выброшенный предмет нельзя будет подобрать", dropped);

			if (dropped.TryGetComponent(out Rigidbody body))
			{
				body.AddForce((origin.forward + Vector3.up * 0.5f) * force, ForceMode.Impulse);
			}
		}
	}
}
