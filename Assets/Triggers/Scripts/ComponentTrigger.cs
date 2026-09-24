using System.Collections.Generic;
using UnityEngine;

namespace Triggers
{
	// Специализация BaseTrigger: дополнительно требует, чтобы у вошедшего коллайдера (или его родителя)
	// был компонент T — например, ComponentTrigger<FirstPersonController> сработает только на игрока
	// (см. готовый PlayerTrigger). Найденный компонент кэшируется на весь Enter..Exit, поэтому Stay
	// не делает GetComponentInParent каждый физический кадр.
	public abstract class ComponentTrigger<T> : BaseTrigger where T : Component
	{
		private readonly Dictionary<Collider, T> _cache = new Dictionary<Collider, T>();

		protected override bool PassesFilter(Collider other)
		{
			if (!base.PassesFilter(other)) return false;

			if (_cache.TryGetValue(other, out T cached)) return cached != null;

			T found = other.GetComponentInParent<T>();
			_cache[other] = found; // кэшируем и отрицательный результат — посторонний коллайдер не проверяется заново на каждом Stay
			return found != null;
		}

		protected override void OnTriggerExit(Collider other)
		{
			base.OnTriggerExit(other); // сначала событие — PassesFilter внутри ещё читает кэш
			_cache.Remove(other);
		}
	}
}
