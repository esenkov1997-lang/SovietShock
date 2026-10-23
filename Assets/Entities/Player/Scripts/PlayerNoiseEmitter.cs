using Enemies;
using StarterAssets;
using UnityEngine;
using Weapons;

namespace Player
{
	// Шум, который игрок производит для ИИ: шаги (зависят от походки), приземления и выстрелы.
	// Это не звук в динамиках — только событие для врагов через NoiseSystem. Висит на PlayerCapsule
	// рядом с FirstPersonController; сами скрипты движения и оружия про врагов ничего не знают.
	[RequireComponent(typeof(FirstPersonController))]
	public class PlayerNoiseEmitter : MonoBehaviour
	{
		[Header("Движение — радиус слышимости (м)")]
		[Tooltip("Обычная ходьба")]
		[SerializeField] private float walkRadius = 5f;
		[Tooltip("Спринт")]
		[SerializeField] private float sprintRadius = 12f;
		[Tooltip("Ходьба в приседе — почти бесшумно")]
		[SerializeField] private float crouchRadius = 1f;
		[Tooltip("Как часто (сек) враги \"слышат\" шаги при движении")]
		[SerializeField] private float movementNoiseInterval = 0.3f;
		[Tooltip("Ниже этой скорости (м/с) игрок не шумит")]
		[SerializeField] private float minSpeed = 0.2f;

		[Header("Приземление")]
		[Tooltip("Радиус при приземлении с высоты, на скорости падения Landing Speed For Full Radius и выше")]
		[SerializeField] private float landingRadius = 10f;
		[Tooltip("Скорость падения (м/с), ниже которой приземление не шумит")]
		[SerializeField] private float landingMinSpeed = 1.5f;
		[Tooltip("Скорость падения (м/с), при которой шум приземления максимальный")]
		[SerializeField] private float landingSpeedForFullRadius = 8f;

		[Header("Оружие")]
		[Tooltip("Выстрел из огнестрела")]
		[SerializeField] private float firearmRadius = 35f;
		[Tooltip("Взмах холодным оружием")]
		[SerializeField] private float meleeRadius = 3f;

		[Header("Отладка")]
		[Tooltip("Рисовать круги шума в Scene view (Gizmos должны быть включены)")]
		[SerializeField] private bool drawDebug;

		private FirstPersonController _movement;
		private WeaponController _weapons;
		private float _movementTimer;

		// шум идёт из середины капсулы, а не от ступней — иначе луч проверки стен до врага цепляет пол
		private Vector3 NoiseOrigin => transform.position + Vector3.up * 1f;

		private void Awake()
		{
			_movement = GetComponent<FirstPersonController>();
			// WeaponController висит глубоко внутри Player.prefab (на WeaponHolder)
			_weapons = GetComponentInChildren<WeaponController>(true);
		}

		private void OnEnable()
		{
			if (_weapons != null) _weapons.Fired += HandleFired;
		}

		private void OnDisable()
		{
			if (_weapons != null) _weapons.Fired -= HandleFired;
		}

		private void Update()
		{
			if (_movement.JustLanded && _movement.LandingSpeed >= landingMinSpeed)
			{
				float t = Mathf.InverseLerp(landingMinSpeed, landingSpeedForFullRadius, _movement.LandingSpeed);
				Emit(Mathf.Lerp(walkRadius, landingRadius, t));
			}

			bool isMoving = _movement.IsGroundedStable && _movement.CurrentSpeed >= minSpeed;
			if (!isMoving)
			{
				_movementTimer = 0f; // первый шаг после остановки слышен сразу
				return;
			}

			_movementTimer -= Time.deltaTime;
			if (_movementTimer > 0f) return;

			_movementTimer = movementNoiseInterval;
			float radius = _movement.IsCrouching ? crouchRadius : _movement.IsSprinting ? sprintRadius : walkRadius;
			Emit(radius);
		}

		private void HandleFired(WeaponData data)
		{
			Emit(data is MeleeWeaponData ? meleeRadius : firearmRadius);
		}

		private void Emit(float radius)
		{
			NoiseSystem.Emit(NoiseOrigin, radius, gameObject);
			if (drawDebug) NoiseSystem.DrawDebug(new NoiseEvent(NoiseOrigin, radius, gameObject), Color.cyan);
		}
	}
}
