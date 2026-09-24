using System;
using UnityEngine;
using UnityEngine.Events;

namespace Triggers
{
	// Чистый детектор: знает только "кто вошёл/вышел/стоит внутри, прошёл ли фильтр" — и ничего не знает
	// о том, что должно произойти в ответ. Логика действий (урон, ремонт, подбор и т.п.) сознательно
	// вынесена в TriggerAction — держим детектор переиспользуемым для любой задачи без правки этого файла.
	//
	// Держи на объекте с Collider (isTrigger = true).
	[RequireComponent(typeof(Collider))]
	public class BaseTrigger : MonoBehaviour
	{
		[Header("Filter")]
		[Tooltip("Слои, которые триггер вообще замечает")]
		[SerializeField] protected LayerMask targetLayers = ~0;
		[Tooltip("Если задано — коллайдер должен иметь именно этот тег. Оставь пустым, чтобы не проверять тег")]
		[SerializeField] protected string requiredTag = "";

		[Header("Mode")]
		[SerializeField] private TriggerMode mode = TriggerMode.Repeatable;
		[Tooltip("Минимальный интервал между срабатываниями Enter для Repeatable (0 — без ограничения). Не используется для OneShot")]
		[SerializeField] private float cooldown = 0f;

		[Header("Unity Events")]
		[Tooltip("Лёгкая настройка в инспекторе без кода — тот же момент, что и C#-событие Entered. Для логики, завязанной на игровые системы (урон/ремонт/подбор), используй TriggerAction")]
		[SerializeField] private ColliderUnityEvent onEntered;
		[SerializeField] private ColliderUnityEvent onExited;
		[SerializeField] private ColliderUnityEvent onStayed;

		// то же самое, но для кода — TriggerAction подписывается именно сюда, без надобности лезть в инспектор
		public event Action<Collider> Entered;
		public event Action<Collider> Exited;
		public event Action<Collider> Stayed;

		private Collider _collider;
		private float _nextAllowedEnterTime;

		// true после первого срабатывания OneShot — Collider уже выключен, повторный Enter физически невозможен
		public bool HasFired { get; private set; }

		protected virtual void Awake()
		{
			_collider = GetComponent<Collider>();
			_collider.isTrigger = true; // страховка на случай, если забыли поставить галочку в инспекторе
		}

		protected virtual void OnTriggerEnter(Collider other)
		{
			if (!PassesFilter(other)) return;
			if (mode == TriggerMode.Repeatable && Time.time < _nextAllowedEnterTime) return;

			if (mode == TriggerMode.Repeatable && cooldown > 0f) _nextAllowedEnterTime = Time.time + cooldown;

			Entered?.Invoke(other);
			onEntered?.Invoke(other);

			if (mode == TriggerMode.OneShot)
			{
				HasFired = true;
				_collider.enabled = false; // деактивируется — дальше никаких Enter/Exit/Stay от этого триггера
			}
		}

		protected virtual void OnTriggerExit(Collider other)
		{
			if (!PassesFilter(other)) return;

			Exited?.Invoke(other);
			onExited?.Invoke(other);
		}

		protected virtual void OnTriggerStay(Collider other)
		{
			if (!PassesFilter(other)) return;

			Stayed?.Invoke(other);
			onStayed?.Invoke(other);
		}

		// Точка расширения для подклассов (см. ComponentTrigger<T>) — базовая реализация проверяет
		// только LayerMask и (опционально) тег, без единого GetComponent
		protected virtual bool PassesFilter(Collider other)
		{
			if ((targetLayers.value & (1 << other.gameObject.layer)) == 0) return false;
			if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return false;
			return true;
		}
	}

	// Обычный UnityEvent<T> Unity не умеет сериализовать в инспекторе — нужен конкретный класс-наследник
	[Serializable]
	public class ColliderUnityEvent : UnityEvent<Collider> { }
}
