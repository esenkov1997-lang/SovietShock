using UnityEngine;
using UnityEngine.Events;

namespace Weapons
{
	// Простейшая реализация IDamageable — повесь на любой объект, который должен получать урон от оружия.
	public class Health : MonoBehaviour, IDamageable
	{
		[Tooltip("Максимальное и стартовое здоровье")]
		public int MaxHealth = 100;
		[Tooltip("Уничтожить объект при смерти — для быстрого теста. Для кастомного поведения (ragdoll, счёт очков и т.д.) используй OnDeath ниже")]
		public bool DestroyOnDeath = true;

		[Tooltip("Вызывается при получении урона: (текущее здоровье, максимум)")]
		public UnityEvent<int, int> OnDamaged;
		[Tooltip("Вызывается один раз, когда здоровье падает до нуля")]
		public UnityEvent OnDeath;

		private int _currentHealth;
		private bool _isDead;

		public int CurrentHealth => _currentHealth;
		public bool IsDead => _isDead;

		private void Awake()
		{
			_currentHealth = MaxHealth;
		}

		public void TakeDamage(int amount)
		{
			if (_isDead || amount <= 0) return;

			_currentHealth = Mathf.Max(_currentHealth - amount, 0);
			Debug.Log($"{name}: получено {amount} урона, здоровье {_currentHealth}/{MaxHealth}");
			OnDamaged?.Invoke(_currentHealth, MaxHealth);

			if (_currentHealth <= 0) Die();
		}

		private void Die()
		{
			_isDead = true;
			Debug.Log($"{name}: погиб");
			OnDeath?.Invoke();

			if (DestroyOnDeath) Destroy(gameObject);
		}
	}
}
