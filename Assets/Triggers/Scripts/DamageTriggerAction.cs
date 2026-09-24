using System.Collections.Generic;
using UnityEngine;
using Weapons;

namespace Triggers
{
	// Наносит урон IDamageable у вошедшего объекта — зона лавы, шипы, ловушка и т.п.
	// DamageOnEnter — разовый урон при заходе (нужен Enter в TriggerAction.ReactTo).
	// DamagePerSecond — урон по времени, пока объект внутри (нужен Stay в TriggerAction.ReactTo).
	// Можно использовать оба сразу. Если включаешь Stay — обязательно включи и Exit, иначе кэш цели
	// не очищается при выходе объекта из триггера
	public class DamageTriggerAction : TriggerAction
	{
		[Tooltip("Разовый урон при входе")]
		[SerializeField] private int damageOnEnter = 10;
		[Tooltip("Урон в секунду, пока объект внутри — работает, только если в TriggerAction.ReactTo включён Stay")]
		[SerializeField] private float damagePerSecond = 0f;

		// IDamageable конкретного коллайдера ищем один раз на Enter и держим тут — Stay не делает
		// GetComponentInParent каждый кадр
		private readonly Dictionary<Collider, IDamageable> _targets = new Dictionary<Collider, IDamageable>();
		// дробный остаток damagePerSecond * deltaTime между кадрами — иначе на высоком FPS каждый Stay
		// округлялся бы вверх минимум до 1 урона и ломал бы задуманный DamagePerSecond
		private readonly Dictionary<Collider, float> _accumulators = new Dictionary<Collider, float>();

		protected override void OnEnter(Collider other)
		{
			IDamageable damageable = other.GetComponentInParent<IDamageable>();
			if (damageable == null) return;

			_targets[other] = damageable;
			_accumulators[other] = 0f;

			if (damageOnEnter > 0) damageable.TakeDamage(damageOnEnter);
		}

		protected override void OnStay(Collider other)
		{
			if (damagePerSecond <= 0f || !_targets.TryGetValue(other, out IDamageable damageable)) return;

			float accumulated = _accumulators[other] + damagePerSecond * Time.deltaTime;
			int whole = Mathf.FloorToInt(accumulated);
			_accumulators[other] = accumulated - whole;

			if (whole > 0) damageable.TakeDamage(whole);
		}

		protected override void OnExit(Collider other)
		{
			_targets.Remove(other);
			_accumulators.Remove(other);
		}
	}
}
