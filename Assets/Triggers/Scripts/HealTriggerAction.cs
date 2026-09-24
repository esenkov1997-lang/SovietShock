using System.Collections.Generic;
using UnityEngine;
using Weapons;

namespace Triggers
{
	// Чинит IRepairable у вошедшего объекта (например, Repairable — см. Weapons/Scripts/Repairable.cs) —
	// зона ремонта, чек-пойнт активации и т.п. Симметрично DamageTriggerAction:
	// RepairOnEnter — разовый ремонт при заходе, RepairPerSecond — по времени, пока объект внутри
	// (нужен Stay в TriggerAction.ReactTo). Если включаешь Stay — обязательно включи и Exit, иначе
	// кэш цели не очищается при выходе объекта из триггера
	public class HealTriggerAction : TriggerAction
	{
		[Tooltip("Разовый ремонт при входе")]
		[SerializeField] private int repairOnEnter = 20;
		[Tooltip("Прочности в секунду, пока объект внутри — работает, только если в TriggerAction.ReactTo включён Stay")]
		[SerializeField] private float repairPerSecond = 0f;

		private readonly Dictionary<Collider, IRepairable> _targets = new Dictionary<Collider, IRepairable>();
		private readonly Dictionary<Collider, float> _accumulators = new Dictionary<Collider, float>();

		protected override void OnEnter(Collider other)
		{
			IRepairable repairable = other.GetComponentInParent<IRepairable>();
			if (repairable == null) return;

			_targets[other] = repairable;
			_accumulators[other] = 0f;

			if (repairOnEnter > 0) repairable.Repair(repairOnEnter);
		}

		protected override void OnStay(Collider other)
		{
			if (repairPerSecond <= 0f || !_targets.TryGetValue(other, out IRepairable repairable)) return;

			float accumulated = _accumulators[other] + repairPerSecond * Time.deltaTime;
			int whole = Mathf.FloorToInt(accumulated);
			_accumulators[other] = accumulated - whole;

			if (whole > 0) repairable.Repair(whole);
		}

		protected override void OnExit(Collider other)
		{
			_targets.Remove(other);
			_accumulators.Remove(other);
		}
	}
}
