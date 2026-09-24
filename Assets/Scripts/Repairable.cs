using UnityEngine;
using UnityEngine.Events;

namespace Weapons
{
	// Реализует и IRepairable, и IDamageable на одной прочности — повесь на объект, который можно
	// и чинить, и ломать (например, кнопка-триггер): почини оружием с WeaponData.CanRepair, чтобы
	// активировать, или нанеси урон обычным оружием, чтобы сломать. Логику самих триггеров сюда не
	// кладём — только состояние "прочность/активен", остальное подпишется на OnRepaired/OnFullyRepaired/OnDamaged/OnBroken.
	public class Repairable : MonoBehaviour, IRepairable, IDamageable
	{
		[Tooltip("Прочность, при достижении которой объект считается починенным и активируется")]
		public int MaxHealth = 100;
		[Tooltip("Стартовая прочность — обычно 0, объект начинает сломанным")]
		public int StartingHealth;

		[Tooltip("Вызывается при каждом попадании ремонтом: (текущая прочность, максимум)")]
		public UnityEvent<int, int> OnRepaired;
		[Tooltip("Вызывается один раз, когда прочность достигает максимума")]
		public UnityEvent OnFullyRepaired;
		[Tooltip("Вызывается при каждом попадании уроном: (текущая прочность, максимум)")]
		public UnityEvent<int, int> OnDamaged;
		[Tooltip("Вызывается один раз, когда прочность падает до нуля и объект ломается")]
		public UnityEvent OnBroken;

		private int _currentHealth;
		private bool _isActive;

		public int CurrentHealth => _currentHealth;
		public bool IsActive => _isActive;

		private void Awake()
		{
			_currentHealth = Mathf.Clamp(StartingHealth, 0, MaxHealth);
			_isActive = _currentHealth >= MaxHealth;
		}

		// прочность меняется независимо от текущего состояния (даже наполовину починенный объект можно
		// чинить дальше) — на границах (0 и MaxHealth) просто клампится, доп. попадания туда же не эффекта
		public void Repair(int amount)
		{
			if (amount <= 0) return;

			int previousHealth = _currentHealth;
			_currentHealth = Mathf.Min(_currentHealth + amount, MaxHealth);
			if (_currentHealth == previousHealth) return; // уже полная прочность, эффекта нет

			Debug.Log($"{name}: чинится, +{amount} прочности, {_currentHealth}/{MaxHealth}");
			OnRepaired?.Invoke(_currentHealth, MaxHealth);

			if (_currentHealth >= MaxHealth) Activate();
		}

		public void TakeDamage(int amount)
		{
			if (amount <= 0) return;

			int previousHealth = _currentHealth;
			_currentHealth = Mathf.Max(_currentHealth - amount, 0);
			if (_currentHealth == previousHealth) return; // уже сломан, эффекта нет

			Debug.Log($"{name}: получено {amount} урона, прочность {_currentHealth}/{MaxHealth}");
			OnDamaged?.Invoke(_currentHealth, MaxHealth);

			if (_currentHealth <= 0) Break();
		}

		private void Activate()
		{
			_isActive = true;
			Debug.Log($"{name}: починен и активирован");
			OnFullyRepaired?.Invoke();
		}

		private void Break()
		{
			_isActive = false;
			Debug.Log($"{name}: сломан");
			OnBroken?.Invoke();
		}
	}
}
