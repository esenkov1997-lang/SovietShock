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
		[Tooltip("Бессмертие: урон игнорируется полностью (здоровье не меняется, OnDamaged не вызывается). " +
			"Для игрока включается из меню паузы → Отладка")]
		public bool Invulnerable;

		[Tooltip("Вызывается при получении урона: (текущее здоровье, максимум)")]
		public UnityEvent<int, int> OnDamaged;
		[Tooltip("Вызывается один раз, когда здоровье падает до нуля")]
		public UnityEvent OnDeath;
		[Tooltip("Вызывается, когда здоровье реально прибавилось (аптечка и т.п.): (текущее здоровье, максимум)")]
		public UnityEvent<int, int> OnHealed;

		private int _currentHealth;
		private bool _isDead;

		public int CurrentHealth => _currentHealth;
		public bool IsDead => _isDead;
		public bool IsFull => _currentHealth >= MaxHealth;

		private void Awake()
		{
			_currentHealth = MaxHealth;
		}

		public void TakeDamage(int amount)
		{
			if (_isDead || amount <= 0 || Invulnerable) return;

			_currentHealth = Mathf.Max(_currentHealth - amount, 0);
			Debug.Log($"{name}: получено {amount} урона, здоровье {_currentHealth}/{MaxHealth}");
			OnDamaged?.Invoke(_currentHealth, MaxHealth);

			if (_currentHealth <= 0) Die();
		}

		// прибавляет здоровье, не выше MaxHealth. Мёртвого не лечит — воскрешение, если понадобится, отдельный метод
		public void Heal(int amount)
		{
			if (_isDead || amount <= 0 || IsFull) return;

			_currentHealth = Mathf.Min(_currentHealth + amount, MaxHealth);
			OnHealed?.Invoke(_currentHealth, MaxHealth);
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
