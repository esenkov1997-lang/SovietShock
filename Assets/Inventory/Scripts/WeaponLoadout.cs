using System;
using UnityEngine;
using Weapons;

namespace Inventory
{
	// Колесо избранного оружия: N слотов (по умолчанию 4, по часовой стрелке = клавиши 1–4). Оружие ПЕРЕНОСИТСЯ сюда
	// из сетки (и обратно) — в колесе оно не занимает места в инвентаре. Это единственный источник правды о том,
	// какое оружие доступно в руках: WeaponController (IWeaponSlots) показывает выбранный слот и сам реагирует на
	// изменения — убрали оружие, которое в руках, из колеса — берётся следующее.
	//
	// Вешается на корень игрока рядом с InventoryHolder. WeaponController ищется среди дочерних объектов
	// (он на WeaponHolder в руках). Логика переноса между сеткой, колесом и ящиком (обмены, "нет места") —
	// не здесь, а в том, кто переносит (UI инвентаря): колесо умеет только класть, забирать и менять местами.
	[RequireComponent(typeof(InventoryHolder))]
	public class WeaponLoadout : MonoBehaviour, IWeaponSlots
	{
		[Tooltip("Если не задано — ищется среди дочерних объектов игрока")]
		[SerializeField] private WeaponController weaponController;
		[Tooltip("Сколько слотов в колесе (клавиши Weapon1..WeaponN в Input Actions — по одной на слот)")]
		[Min(1)]
		[SerializeField] private int slotCount = 4;
		[Tooltip("Оружие в слотах при старте, по порядку слотов (элемент 0 — слот 1). В руки берётся первое из них")]
		[SerializeField] private WeaponItemData[] startingWeapons = new WeaponItemData[0];

		private ItemInstance[] _slots;

		public int SlotCount => Slots.Length;
		public event Action SlotsChanged;

		// создаётся при первом обращении: InventoryHolder и UI могут обратиться раньше, чем прошёл Awake
		private ItemInstance[] Slots
		{
			get
			{
				if (_slots == null) CreateSlots();
				return _slots;
			}
		}

		private void Awake()
		{
			if (weaponController == null) weaponController = GetComponentInChildren<WeaponController>();
			if (_slots == null) CreateSlots();
		}

		// WeaponController подключается в Start — к этому моменту его Awake гарантированно прошёл
		private void Start()
		{
			if (weaponController == null)
			{
				Debug.LogWarning($"{nameof(WeaponLoadout)} на {name}: не найден WeaponController — оружие из колеса " +
					"не будет появляться в руках", this);
				return;
			}

			weaponController.BindSlots(this);

			// стартовое оружие — сразу в руках, без анимации доставания
			int first = FirstOccupiedSlot();
			if (first >= 0) weaponController.SelectSlot(first, instant: true);
		}

		private void OnDestroy()
		{
			if (weaponController != null) weaponController.BindSlots(null);
		}

		private void CreateSlots()
		{
			_slots = new ItemInstance[Mathf.Max(1, slotCount)];

			for (int i = 0; i < startingWeapons.Length; i++)
			{
				if (startingWeapons[i] == null) continue;
				if (i >= _slots.Length)
				{
					Debug.LogWarning($"{nameof(WeaponLoadout)} на {name}: стартового оружия больше, чем слотов ({_slots.Length}) — " +
						$"{startingWeapons[i].name} пропущено", this);
					continue;
				}

				ItemInstance instance = new ItemInstance(startingWeapons[i]);
				if (CanHold(instance)) _slots[i] = instance;
				else Debug.LogWarning($"{startingWeapons[i].name}: не задан Weapon Data — в колесо не кладётся", startingWeapons[i]);
			}
		}

		// ---------- Запросы ----------

		// в колесо кладётся только оружие с боевыми данными
		public static bool CanHold(ItemInstance item) => item != null && item.Weapon != null;

		// null — слот пуст
		public ItemInstance GetItem(int index) => IsValid(index) ? Slots[index] : null;

		WeaponState IWeaponSlots.GetSlot(int index) => GetItem(index)?.Weapon;

		public int IndexOf(ItemInstance item)
		{
			if (item == null) return -1;
			return Array.IndexOf(Slots, item);
		}

		// первый пустой слот по часовой стрелке; -1 — колесо полное
		public int FirstEmptySlot() => Array.IndexOf(Slots, null);

		public int FirstOccupiedSlot()
		{
			for (int i = 0; i < Slots.Length; i++)
			{
				if (Slots[i] != null) return i;
			}
			return -1;
		}

		// какой слот выбран (в руках или в процессе смены); -1 — руки пустые
		public int SelectedSlot => weaponController != null ? weaponController.SelectedSlotIndex : -1;

		private bool IsValid(int index) => index >= 0 && index < Slots.Length;

		// ---------- Изменение ----------

		// Положить оружие в слот, вернув то, что там лежало (null — слот был пуст). Оружие должно прийти "снаружи"
		// (из сетки, ящика, с пола) — уже лежащее в колесе переставляется через Swap. Кто кладёт, сам решает, куда
		// деть вернувшееся оружие (см. правило обмена в UI). Не получилось — false, ничего не меняется
		public bool TryPut(int index, ItemInstance item, out ItemInstance previous)
		{
			previous = null;
			if (!IsValid(index) || !CanHold(item) || IndexOf(item) >= 0) return false;

			previous = Slots[index];
			Slots[index] = item;
			SlotsChanged?.Invoke();
			return true;
		}

		// Забрать оружие из слота (в сетку, ящик, на пол). null — слот был пуст
		public ItemInstance Take(int index)
		{
			if (!IsValid(index) || Slots[index] == null) return null;

			ItemInstance item = Slots[index];
			Slots[index] = null;
			SlotsChanged?.Invoke();
			return item;
		}

		// Поменять два слота местами (перетаскивание внутри колеса; один из них может быть пустым)
		public bool Swap(int a, int b)
		{
			if (!IsValid(a) || !IsValid(b) || a == b) return false;

			(Slots[a], Slots[b]) = (Slots[b], Slots[a]);
			SlotsChanged?.Invoke();
			return true;
		}

		// Выбрать слот: оружие из него — в руки, пустой — руки пустые (как клавиши 1–4)
		public void Select(int index)
		{
			if (weaponController != null) weaponController.SelectSlot(index);
		}

		// Подбор с пола: в первый свободный слот и сразу в руки. false — колесо полное (или это не оружие)
		internal bool TryAddFromPickup(ItemInstance item)
		{
			int index = FirstEmptySlot();
			if (index < 0 || !TryPut(index, item, out _)) return false;

			Select(index);
			return true;
		}
	}
}
