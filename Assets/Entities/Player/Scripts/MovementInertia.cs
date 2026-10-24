using UnityEngine;

namespace StarterAssets
{
	// Настройки инерции движения — один набор для оружия (Weapons.WeaponAnimationProfileSO) и камеры (CameraInertia)
	[System.Serializable]
	public class MovementInertiaSettings
	{
		[Tooltip("Наклон (pitch, вокруг X) на каждый м/с² ускорения вперёд-назад, градусы. Знак — направление")]
		public float PitchPerAcceleration = 0.15f;
		[Tooltip("Крен (roll, вокруг Z) на каждый м/с² бокового ускорения (стрейф), градусы")]
		public float RollPerAcceleration = 0.12f;
		[Tooltip("Смещение против ускорения вперёд-назад и вбок на каждый м/с², метры")]
		public float PositionPerAcceleration = 0.001f;
		[Tooltip("Вертикаль (прыжок, приземление, приседание/вставание): наклон на каждый м/с², градусы")]
		public float VerticalPitchPerAcceleration = 0.1f;
		[Tooltip("Вертикаль: смещение против вертикального ускорения на каждый м/с², метры")]
		public float VerticalPositionPerAcceleration = 0.001f;
		[Tooltip("Ускорение, выше которого эффект больше не растёт, м/с² — отдельно по горизонтали и по вертикали")]
		public float MaxAcceleration = 30f;
		[Tooltip("Насколько плавно эффект реагирует на старт/остановку/приземление: меньше — мягче и тягучее, больше — резче. Обычно 4–12")]
		public float InputSmoothing = 8f;
		[Tooltip("Жёсткость пружины смещения: больше — быстрее и мельче колебания")]
		public float Stiffness = 90f;
		[Tooltip("Затухание пружины смещения (без колебаний — примерно 2 × √Stiffness)")]
		public float Damping = 9f;
		[Tooltip("Жёсткость пружины поворота — мягче смещения, поворот откликается позже и \"догоняет\" его (дуга)")]
		public float RotationStiffness = 40f;
		[Tooltip("Затухание пружины поворота (без колебаний — примерно 2 × √Rotation Stiffness)")]
		public float RotationDamping = 6f;
		[Tooltip("Насколько затухающий наклон закручивается в дугу (часть кивка с запаздыванием уходит в поворот вбок). 0 — только кивок")]
		public float ArcCoupling = 0.3f;
	}

	// Инерция движения: объект (оружие, камера) как груз на пружине, привязанной к телу игрока. Цель пружины —
	// отклонение против ускорения игрока: на старте объект отстаёт (anticipation), при остановке проскакивает и
	// затухающе качается (overlap); то же при прыжке, приземлении и приседании.
	//
	// Ускорение берётся не из реальной позиции, а из скоростей FirstPersonController: горизонтальная скорость,
	// вертикальная скорость физики (на земле она постоянна — лестница не даёт рывков) и скорость смены высоты камеры
	// при приседании. Считается в мировых осях и только потом переводится в оси игрока — поворот мышью на бегу
	// ложной инерции не создаёт.
	public class MovementInertia
	{
		private Vector3 _lastVelocity;
		private float _lastCameraHeight;
		private bool _hasLast;
		private Vector3 _smoothedAcceleration; // в осях игрока: X — вбок, Y — вверх, Z — вперёд

		private Vector3 _angles;               // pitch / yaw / roll, градусы
		private Vector3 _angleVelocity;
		private Vector3 _position;
		private Vector3 _positionVelocity;

		// один шаг: читает движение игрока, ведёт пружины. multiplier — общий множитель силы (например, гашение в прицеле)
		public void Update(FirstPersonController movement, MovementInertiaSettings settings, float multiplier, float dt,
			out Vector3 position, out Quaternion rotation)
		{
			position = Vector3.zero;
			rotation = Quaternion.identity;
			if (movement == null || settings == null || dt <= 0f) return;

			float cameraHeight = movement.CameraBaseHeight;
			float crouchVelocity = _hasLast ? (cameraHeight - _lastCameraHeight) / dt : 0f;
			_lastCameraHeight = cameraHeight;

			Vector3 velocity = movement.HorizontalVelocity + Vector3.up * (movement.VerticalVelocity + crouchVelocity);
			Vector3 worldAcceleration = _hasLast ? (velocity - _lastVelocity) / dt : Vector3.zero;
			_lastVelocity = velocity;
			_hasLast = true;

			Vector3 acceleration = Quaternion.Inverse(movement.transform.rotation) * worldAcceleration;
			// отдельно по горизонтали и по вертикали: приземление (скорость меняется за кадр) не должно "забивать"
			// реакцию на старт/остановку и наоборот
			Vector3 horizontal = Vector3.ClampMagnitude(new Vector3(acceleration.x, 0f, acceleration.z), settings.MaxAcceleration);
			acceleration = new Vector3(horizontal.x, Mathf.Clamp(acceleration.y, -settings.MaxAcceleration, settings.MaxAcceleration), horizontal.z);

			// ускорение игрока ступенчатое — сглаживаем, чтобы вход и выход были плавными (не зависит от FPS)
			_smoothedAcceleration = Vector3.Lerp(_smoothedAcceleration, acceleration, 1f - Mathf.Exp(-settings.InputSmoothing * dt));

			Vector3 a = _smoothedAcceleration * multiplier;
			float pitch = a.z * settings.PitchPerAcceleration + a.y * settings.VerticalPitchPerAcceleration;
			Vector3 targetAngles = new Vector3(pitch, 0f, -a.x * settings.RollPerAcceleration);
			Vector3 targetPosition = new Vector3(
				-a.x * settings.PositionPerAcceleration,
				-a.y * settings.VerticalPositionPerAcceleration,
				-a.z * settings.PositionPerAcceleration);

			// у смещения и поворота разные пружины: поворот мягче и откликается позже — из разницы фаз складывается дуга
			DriveSpring(ref _position, ref _positionVelocity, targetPosition, settings.Stiffness, settings.Damping, dt);
			DriveSpring(ref _angles, ref _angleVelocity, targetAngles, settings.RotationStiffness, settings.RotationDamping, dt);

			// дуга: скорость кивка подталкивает поворот вбок (сдвиг по фазе на четверть колебания — путь по кругу)
			_angleVelocity.y += _angleVelocity.x * settings.ArcCoupling * Mathf.Sqrt(Mathf.Max(settings.RotationStiffness, 0f)) * dt;

			position = _position;
			rotation = Quaternion.Euler(_angles);
		}

		// пружина, которая тянет value к target — полуявный Эйлер
		private static void DriveSpring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float stiffness, float damping, float dt)
		{
			Vector3 acceleration = stiffness * (target - value) - damping * velocity;
			velocity += acceleration * dt;
			value += velocity * dt;
		}
	}
}
