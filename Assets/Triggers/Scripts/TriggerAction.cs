using System;
using UnityEngine;

namespace Triggers
{
	// Какие события BaseTrigger интересуют конкретное действие — можно комбинировать (например, Enter | Stay)
	[Flags]
	public enum TriggerEventType
	{
		Enter = 1 << 0,
		Exit = 1 << 1,
		Stay = 1 << 2
	}

	// База для "что делать, когда сработал BaseTrigger". Сам не детектит и не фильтрует — только
	// подписывается на уже отфильтрованные C#-события конкретного BaseTrigger и реагирует. Один
	// BaseTrigger может кормить сколько угодно независимых TriggerAction, в том числе на разных объектах.
	public abstract class TriggerAction : MonoBehaviour
	{
		[Tooltip("Если не задано — берётся BaseTrigger с этого же объекта")]
		[SerializeField] private BaseTrigger targetTrigger;
		[Tooltip("На какие события детектора реагировать")]
		[SerializeField] private TriggerEventType reactTo = TriggerEventType.Enter;

		protected BaseTrigger Trigger { get; private set; }

		protected virtual void Awake()
		{
			Trigger = targetTrigger != null ? targetTrigger : GetComponent<BaseTrigger>();
			if (Trigger == null)
			{
				Debug.LogError($"{GetType().Name} на {name}: не найден BaseTrigger — назначь Target Trigger вручную", this);
			}
		}

		private void OnEnable()
		{
			if (Trigger == null) return;

			if ((reactTo & TriggerEventType.Enter) != 0) Trigger.Entered += OnEnter;
			if ((reactTo & TriggerEventType.Exit) != 0) Trigger.Exited += OnExit;
			if ((reactTo & TriggerEventType.Stay) != 0) Trigger.Stayed += OnStay;
		}

		private void OnDisable()
		{
			if (Trigger == null) return;

			Trigger.Entered -= OnEnter;
			Trigger.Exited -= OnExit;
			Trigger.Stayed -= OnStay;
		}

		// по умолчанию все три сведены к Execute — большинству действий (подобрать предмет, разово
		// дёрнуть UnityEvent) неважно, какое именно из включённых в reactTo событий произошло.
		// Переопредели OnEnter/OnExit/OnStay напрямую, если поведение должно отличаться по типу события
		// (см. DamageTriggerAction/HealTriggerAction — им нужна разная логика для Enter и для Stay)
		protected virtual void OnEnter(Collider other) => Execute(other);
		protected virtual void OnExit(Collider other) => Execute(other);
		protected virtual void OnStay(Collider other) => Execute(other);

		protected virtual void Execute(Collider other) { }
	}
}
