using UnityEngine;
using Weapons;

namespace HUD
{
	// Полоса здоровья игрока. Читает Health на PlayerCapsule (рядом с FirstPersonController) и рисует его
	// через BarGraphic на этом же объекте.
	//
	// "След" урона: при попадании заполнение сразу падает, а убывшая часть ещё Trail Delay секунд видна
	// цветом Trail Color и потом плавно догоняет — видно, сколько именно сняли. Стандартный приём шутеров.
	// Здоровье читается каждый кадр, а не по событию OnDamaged — так полоса покажет и лечение, когда оно появится.
	[RequireComponent(typeof(BarGraphic))]
	public class PlayerHealthBarUI : PlayerHUDBinding
	{
		[Header("Анимация")]
		[Tooltip("Скорость, с которой заполнение догоняет реальное здоровье. 0 — мгновенно")]
		[SerializeField] private float fillSmoothing = 20f;
		[Tooltip("Сколько секунд \"след\" урона стоит на месте, прежде чем начать догонять")]
		[SerializeField] private float trailDelay = 0.5f;
		[Tooltip("Скорость, с которой \"след\" догоняет заполнение (доля полосы в секунду)")]
		[SerializeField] private float trailSpeed = 0.6f;

		[Header("Цвета")]
		[Tooltip("Цвет полосы при нормальном здоровье")]
		[SerializeField] private Color normalColor = new Color(0.85f, 0.85f, 0.85f);
		[Tooltip("Цвет полосы при низком здоровье")]
		[SerializeField] private Color lowHealthColor = new Color(0.9f, 0.2f, 0.15f);
		[Tooltip("Ниже этой доли здоровья полоса красится в Low Health Color")]
		[Range(0f, 1f)]
		[SerializeField] private float lowHealthFraction = 0.3f;

		private BarGraphic _bar;
		private Health _health;
		private float _targetFill = 1f;
		private float _trailHoldUntil;

		private void Awake()
		{
			_bar = GetComponent<BarGraphic>();
			_bar.Fill = 1f;
			_bar.Trail = 1f;
			_bar.FillColor = normalColor;
		}

		protected override void OnBind(WeaponController weapons)
		{
			_health = Movement != null ? Movement.GetComponent<Health>() : null;
			if (_health == null)
			{
				Debug.LogWarning($"{name}: у игрока нет Health на PlayerCapsule — полоса здоровья не работает", this);
				return;
			}

			// новый игрок — сразу его здоровье, без анимации "доезда"
			_targetFill = GetHealthFraction();
			_bar.Fill = _targetFill;
			_bar.Trail = _targetFill;
		}

		protected override void OnUnbind(WeaponController weapons)
		{
			_health = null;
		}

		private void Update()
		{
			if (_health == null) return;

			float fraction = GetHealthFraction();
			if (fraction < _targetFill) _trailHoldUntil = Time.time + trailDelay; // получили урон — след замирает
			_targetFill = fraction;

			_bar.Fill = fillSmoothing > 0f
				? Mathf.Lerp(_bar.Fill, _targetFill, 1f - Mathf.Exp(-fillSmoothing * Time.deltaTime))
				: _targetFill;

			// след только убывает вслед за заполнением; при лечении он просто совпадает с ним
			if (_bar.Trail <= _bar.Fill) _bar.Trail = _bar.Fill;
			else if (Time.time >= _trailHoldUntil) _bar.Trail = Mathf.MoveTowards(_bar.Trail, _bar.Fill, trailSpeed * Time.deltaTime);

			_bar.FillColor = fraction <= lowHealthFraction ? lowHealthColor : normalColor;
		}

		private float GetHealthFraction()
		{
			return _health.MaxHealth > 0 ? (float)_health.CurrentHealth / _health.MaxHealth : 0f;
		}
	}
}
