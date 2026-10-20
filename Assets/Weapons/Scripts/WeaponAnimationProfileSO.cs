using UnityEngine;

namespace Weapons
{
	// Профиль процедурной анимации оружия (Sway/Bobbing/Impact Spring), вынесенный из WeaponData в
	// отдельный переиспользуемый ScriptableObject — WeaponData стал "God Object" из-за портянки полей
	// анимации, а сами эти настройки почти не зависят от статов конкретной пушки (урон, магазин и т.п.).
	// Один и тот же профиль можно повесить на несколько похожих по весу/повадке оружий сразу
	// (например, "Light Weapon Profile" на пистолет и ПП, "Heavy Weapon Profile" на пулемёт).
	[CreateAssetMenu(fileName = "NewWeaponAnimationProfile", menuName = "Weapons/Weapon Animation Profile")]
	public class WeaponAnimationProfileSO : ScriptableObject
	{
		[Header("Look Sway — позиция")]
		[Tooltip("Смещение оружия по X/Y от поворота камеры мышью, метры на единицу input.look")]
		[SerializeField] private float swayPositionAmount = 0.02f;
		[Tooltip("Ограничение максимального смещения по X/Y, метры")]
		[SerializeField] private float maxSwayPositionOffset = 0.06f;

		[Header("Look Sway — поворот (Roll/Yaw/Pitch)")]
		[Tooltip("Насколько сильно оружие довора́чивается против направления поворота камеры, градусы на единицу input.look")]
		[SerializeField] private float swayRotationAmount = 1.2f;
		[Tooltip("Ограничение максимального угла отклонения по каждой оси, градусы")]
		[SerializeField] private float maxSwayRotationAngle = 6f;

		[Header("Look Sway — сглаживание")]
		[Tooltip("Скорость, с которой sway догоняет целевое значение (экспоненциальный Lerp/Slerp) — больше значение = резче отклик, меньше = более вязкое, инертное отставание")]
		[SerializeField] private float swaySmoothing = 8f;

		[Header("Bobbing — фигура Лиссажу")]
		[Tooltip("Базовая частота покачивания по Y, циклов в секунду при обычной ходьбе. По X автоматически берётся вдвое меньшая частота — получается фигура-восьмёрка (Lissajous), а не простой эллипс")]
		[SerializeField] private float bobFrequency = 1.8f;
		[Tooltip("Амплитуда покачивания по X (в сторону) и Y (вверх-вниз), метры")]
		[SerializeField] private Vector2 bobAmplitude = new Vector2(0.015f, 0.03f);
		[Tooltip("Амплитуда лёгкого наклона по Z (Roll) в такт шагам, градусы")]
		[SerializeField] private float bobRollAmount = 1.5f;
		[Tooltip("Амплитуда лёгкого кивка по X (Pitch) в такт шагам, градусы — на той же частоте, что и вертикальное покачивание по Y (одно 'кивание' на каждый шаг)")]
		[SerializeField] private float bobPitchAmount = 1.2f;
		[Tooltip("Скорость нарастания/спада bob при старте/остановке движения")]
		[SerializeField] private float bobBlendSpeed = 6f;
		[Tooltip("Минимальная горизонтальная скорость игрока, при которой bob уже считается активным")]
		[SerializeField] private float minMoveSpeedForBob = 0.2f;

		[Header("Bobbing — множители состояния игрока")]
		[Tooltip("Множитель частоты И амплитуды bob во время спринта (FirstPersonController.IsSprinting)")]
		[SerializeField] private float sprintMultiplier = 1.6f;
		[Tooltip("Множитель частоты И амплитуды bob в приседе (FirstPersonController.IsCrouching)")]
		[SerializeField] private float crouchMultiplier = 0.5f;
		[Tooltip("Множитель частоты И амплитуды bob при полном прицеливании (WeaponController.AimBlend = 1) — обычно сильно гасит покачивание. Между 0 и 1 линейно интерполируется вместе с самим прицеливанием")]
		[SerializeField] private float aimMultiplier = 0.25f;

		[Header("Impact Spring — жёсткость/затухание (позиция, метры)")]
		[Tooltip("Жёсткость пружины (stiffness) — чем больше, тем быстрее толчок стягивается обратно к нулю")]
		[SerializeField] private float positionSpringStiffness = 200f;
		[Tooltip("Затухание пружины (damping) — чем больше, тем меньше 'звона'/переколебаний после толчка. При слишком малом значении относительно жёсткости пружина будет заметно раскачиваться")]
		[SerializeField] private float positionSpringDamping = 18f;

		[Header("Impact Spring — жёсткость/затухание (поворот, градусы)")]
		[Tooltip("У поворота своя пара stiffness/damping, а не общая с позицией: градусы и метры — разные по масштабу единицы, одна и та же жёсткая пружина, рассчитанная под метры, гасила бы поворот почти до нуля")]
		[SerializeField] private float rotationSpringStiffness = 40f;
		[Tooltip("Затухание пружины поворота")]
		[SerializeField] private float rotationSpringDamping = 9f;
		[Tooltip("Во сколько раз импульс усиливается при переводе из позиции (метры) в поворот (градусы) — чтобы толчок был реально заметен по X/Z, а не терялся в масштабе")]
		[SerializeField] private float impulseToRotationCoupling = 80f;

		[Header("Impact Spring — приземление и прыжок")]
		[Tooltip("Множитель: скорость падения в момент приземления (FirstPersonController.LandingSpeed) переводится в силу импульса вниз")]
		[SerializeField] private float landingImpulseMultiplier = 0.05f;
		[Tooltip("Фиксированная сила импульса вниз в момент отрыва от земли (прыжок) — 'аналог проседания', без привязки к скорости, в отличие от приземления")]
		[SerializeField] private float jumpImpulseStrength = 0.18f;
		[Tooltip("Ограничение силы одного импульса — защита от неадекватно сильного 'подброса' оружия при падении с большой высоты")]
		[SerializeField] private float maxImpulseStrength = 0.6f;

		public float SwayPositionAmount => swayPositionAmount;
		public float MaxSwayPositionOffset => maxSwayPositionOffset;
		public float SwayRotationAmount => swayRotationAmount;
		public float MaxSwayRotationAngle => maxSwayRotationAngle;
		public float SwaySmoothing => swaySmoothing;

		public float BobFrequency => bobFrequency;
		public Vector2 BobAmplitude => bobAmplitude;
		public float BobRollAmount => bobRollAmount;
		public float BobPitchAmount => bobPitchAmount;
		public float BobBlendSpeed => bobBlendSpeed;
		public float MinMoveSpeedForBob => minMoveSpeedForBob;

		public float SprintMultiplier => sprintMultiplier;
		public float CrouchMultiplier => crouchMultiplier;
		public float AimMultiplier => aimMultiplier;

		public float PositionSpringStiffness => positionSpringStiffness;
		public float PositionSpringDamping => positionSpringDamping;
		public float RotationSpringStiffness => rotationSpringStiffness;
		public float RotationSpringDamping => rotationSpringDamping;
		public float ImpulseToRotationCoupling => impulseToRotationCoupling;

		public float LandingImpulseMultiplier => landingImpulseMultiplier;
		public float JumpImpulseStrength => jumpImpulseStrength;
		public float MaxImpulseStrength => maxImpulseStrength;
	}
}
