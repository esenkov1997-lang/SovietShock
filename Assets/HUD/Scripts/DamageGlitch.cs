using UnityEngine;
using Weapons;

namespace HUD
{
	// Помехи сигнала при уроне игрока: чем сильнее удар, тем сильнее сбой. По лору — камера шлема "сбоит",
	// когда носителя бьют. Вешается в UIInterface рядом с остальным HUD (игрока находит сама, как полоса здоровья).
	//
	// Каждое попадание даёт всплеск силой по кривой Intensity By Damage, который затухает за Fade Duration.
	// Удары подряд не складываются бесконечно: новый всплеск заменяет текущий, только если он сильнее.
	// По желанию — постоянная лёгкая рябь при низком здоровье.
	public class DamageGlitch : PlayerHUDBinding
	{
		[Header("Всплеск при уроне")]
		[Tooltip("Сила помех от размера удара. По X — урон в процентах от максимального здоровья (0..100), " +
			"по Y — сила всплеска 0..1. По умолчанию: укус на 10% — заметная рябь, на 30% и больше — сильный сбой")]
		[SerializeField] private AnimationCurve intensityByDamage = new AnimationCurve(
			new Keyframe(0f, 0.15f), new Keyframe(10f, 0.35f), new Keyframe(30f, 0.8f), new Keyframe(60f, 1f));
		[Tooltip("За сколько секунд всплеск затухает")]
		[SerializeField] private float fadeDuration = 0.5f;
		[Tooltip("Форма затухания: по X — доля времени 0..1, по Y — доля силы 1..0. " +
			"По умолчанию держится мгновение и быстро спадает")]
		[SerializeField] private AnimationCurve fadeCurve = new AnimationCurve(
			new Keyframe(0f, 1f), new Keyframe(0.15f, 0.9f), new Keyframe(1f, 0f));

		[Header("Низкое здоровье")]
		[Tooltip("Постоянная лёгкая рябь, пока здоровье ниже Low Health Fraction. 0 — выключено")]
		[Range(0f, 1f)]
		[SerializeField] private float lowHealthIntensity;
		[Tooltip("Ниже этой доли здоровья включается постоянная рябь")]
		[Range(0f, 1f)]
		[SerializeField] private float lowHealthFraction = 0.25f;

		private Health _health;
		private int _lastHealth;
		private float _peak;
		private float _hitTime = float.NegativeInfinity;

		protected override void OnDisable()
		{
			base.OnDisable();
			SignalGlitch.Clear(this);
		}

		protected override void OnBind(WeaponController weapons)
		{
			_health = Movement != null ? Movement.GetComponent<Health>() : null;
			if (_health == null)
			{
				Debug.LogWarning($"{name}: у игрока нет Health — помехи от урона не работают", this);
				return;
			}
			_lastHealth = _health.CurrentHealth;
			_health.OnDamaged.AddListener(HandleDamaged);
		}

		protected override void OnUnbind(WeaponController weapons)
		{
			if (_health != null) _health.OnDamaged.RemoveListener(HandleDamaged);
			_health = null;
			SignalGlitch.Clear(this);
		}

		private void HandleDamaged(int currentHealth, int maxHealth)
		{
			int damage = Mathf.Max(0, _lastHealth - currentHealth);
			_lastHealth = currentHealth;
			if (damage <= 0 || maxHealth <= 0) return;

			float damagePercent = 100f * damage / maxHealth;
			float peak = intensityByDamage.Evaluate(damagePercent);

			// новый удар заменяет текущий всплеск, только если он сильнее того, что осталось от прошлого
			if (peak >= CurrentHitIntensity())
			{
				_peak = peak;
				_hitTime = Time.time;
			}
		}

		private void Update()
		{
			if (_health == null) return;

			// после смерти помехами управляет экран смерти
			if (_health.IsDead)
			{
				SignalGlitch.Clear(this);
				return;
			}

			// здоровье могло вырасти (лечение) — следующий урон считаем от актуального значения
			_lastHealth = _health.CurrentHealth;

			float lowHealth = _health.MaxHealth > 0 && (float)_health.CurrentHealth / _health.MaxHealth <= lowHealthFraction
				? lowHealthIntensity
				: 0f;

			SignalGlitch.SetIntensity(this, Mathf.Max(CurrentHitIntensity(), lowHealth));
		}

		private float CurrentHitIntensity()
		{
			if (fadeDuration <= 0f) return 0f;
			float t = (Time.time - _hitTime) / fadeDuration;
			return t >= 1f ? 0f : _peak * fadeCurve.Evaluate(t);
		}
	}
}
