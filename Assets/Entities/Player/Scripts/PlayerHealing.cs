using Inventory;
using StarterAssets;
using UnityEngine;
using Weapons;

namespace Player
{
	// Лечение аптечкой по кнопке Heal (H). Вешается на PlayerCapsule рядом с Health и InventoryHolder.
	//
	// Нажатие: если в инвентаре есть лечащий предмет (Inventory.HealingItemData) — даже при полном здоровье, тогда он
	// тратится впустую, — WeaponController убирает оружие и берёт предмет в руки (BeginItemAction). Дальше всё по событиям клипа предмета:
	//   ItemApplied     — игла вошла: предмет списывается из инвентаря, начинается лечение;
	//   ItemUseFinished — лечение набрано полностью (healAmount равномерно между этими двумя событиями);
	//   WeaponHidden    — предмет убран, оружие возвращается (это уже делает WeaponController).
	// Прерывание (руки убраны — терминал, захват, смерть): лечение останавливается, набранное остаётся, аптечка
	// после ItemApplied уже потрачена. Пока предмет в руках, бегать нельзя (ходить можно)
	[RequireComponent(typeof(Health), typeof(InventoryHolder))]
	public class PlayerHealing : MonoBehaviour, IItemActionHandler
	{
		[Tooltip("Если не задано — берётся StarterAssetsInputs с этого объекта или родителей")]
		[SerializeField] private StarterAssetsInputs input;
		[Tooltip("Если не задано — ищется среди дочерних (WeaponHolder)")]
		[SerializeField] private WeaponController weapons;

		private Health _health;
		private InventoryHolder _inventory;
		private FirstPersonController _movement;
		private LedgeMantle _mantle;

		// предмет, который сейчас в руках; null — лечения нет
		private HealingItemData _item;
		// окно лечения (между ItemApplied и ItemUseFinished)
		private bool _healing;
		private float _healRate;
		private float _healAccumulator;
		private int _healed;

		public bool IsHealing => _item != null;

		private void Awake()
		{
			_health = GetComponent<Health>();
			_inventory = GetComponent<InventoryHolder>();
			_movement = GetComponent<FirstPersonController>();
			_mantle = GetComponent<LedgeMantle>();
			if (input == null) input = GetComponentInParent<StarterAssetsInputs>();
			if (weapons == null) weapons = GetComponentInChildren<WeaponController>();
		}

		private void OnDisable()
		{
			// компонент выключили посреди лечения — руки не должны остаться с аптечкой
			if (IsHealing && weapons != null) weapons.CancelItemAction();
		}

		private void Update()
		{
			if (input != null && input.heal)
			{
				input.heal = false; // одноразовое срабатывание, как reload
				TryStartHealing();
			}

			if (_healing) TickHealing();
		}

		private void TryStartHealing()
		{
			// полное здоровье не мешает: аптечку можно вколоть и так — она потратится впустую, решает игрок
			if (IsHealing || weapons == null || _health.IsDead) return;
			if (_mantle != null && _mantle.IsClimbing) return;

			HealingItemData item = FindHealingItem();
			if (item == null) return;

			if (!weapons.BeginItemAction(item, item.useTrigger, this)) return;

			_item = item;
			if (_movement != null) _movement.SprintBlocked = true;
		}

		// первая аптечка в инвентаре. Разные виды лечения (малая/большая) — пока без выбора, берётся первая попавшаяся
		private HealingItemData FindHealingItem()
		{
			foreach (ItemStack stack in _inventory.Items)
			{
				if (stack.item is HealingItemData healing && stack.count > 0) return healing;
			}
			return null;
		}

		private void TickHealing()
		{
			int remaining = _item.healAmount - _healed;
			_healAccumulator += _healRate * Time.deltaTime;
			int whole = Mathf.Min(Mathf.FloorToInt(_healAccumulator), remaining);
			if (whole <= 0) return;

			_healAccumulator -= whole;
			ApplyHeal(whole);
		}

		private void ApplyHeal(int amount)
		{
			_healed += amount;
			_health.Heal(amount);
		}

		void IItemActionHandler.OnItemApplied(float activeDuration)
		{
			if (_item == null) return;

			// аптечку успели выложить из инвентаря, пока руки доставали её — лечить нечем
			if (!_inventory.RemoveItem(_item, 1)) return;

			_healed = 0;
			_healAccumulator = 0f;
			if (activeDuration > 0f)
			{
				_healRate = _item.healAmount / activeDuration;
				_healing = true;
			}
			else
			{
				ApplyHeal(_item.healAmount); // окна нет (нет ItemUseFinished) — сразу целиком
			}
		}

		void IItemActionHandler.OnItemFinished()
		{
			// окно закрылось — добираем остаток, потерянный на дробных кадрах, чтобы вышло ровно healAmount
			if (_healing) ApplyHeal(_item.healAmount - _healed);
			_healing = false;
			Release();
		}

		void IItemActionHandler.OnItemCancelled()
		{
			_healing = false;
			Release();
		}

		// руки ещё могут доигрывать клип (убирают шприц) — но лечение кончилось, бег снова можно
		private void Release()
		{
			_item = null;
			if (_movement != null) _movement.SprintBlocked = false;
		}
	}
}
