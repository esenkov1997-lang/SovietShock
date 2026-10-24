using StarterAssets;
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
		[Tooltip("Скорость, с которой bob плавно перестраивается при смене состояния (ходьба ↔ спринт ↔ присед): частота и амплитуда " +
			"не прыгают за один кадр, а догоняют новое значение. Больше — быстрее перестройка, меньше — мягче. Обычно 4–10")]
		[SerializeField] private float bobStateSmoothing = 6f;

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

		[Header("Movement Inertia — старт/остановка (anticipation и overlap)")]
		[Tooltip("Наклон оружия (pitch) на каждый м/с² ускорения игрока вперёд-назад, градусы. При старте оружие отстаёт и наклоняется, " +
			"при остановке проскакивает вперёд и качается с затуханием. Знак — направление наклона")]
		[SerializeField] private float inertiaPitchPerAcceleration = 0.15f;
		[Tooltip("Крен оружия (roll) на каждый м/с² бокового ускорения (стрейф), градусы")]
		[SerializeField] private float inertiaRollPerAcceleration = 0.12f;
		[Tooltip("Смещение оружия против ускорения на каждый м/с², метры — отставание при старте и \"проскок\" при остановке")]
		[SerializeField] private float inertiaPositionPerAcceleration = 0.001f;
		[Tooltip("Ускорение игрока, выше которого эффект больше не растёт, м/с² — защита от рывков при резкой смене направления " +
			"и при приземлении (там скорость меняется за один кадр). Действует отдельно по горизонтали и по вертикали")]
		[SerializeField] private float inertiaMaxAcceleration = 30f;
		[Tooltip("Вертикаль (прыжок, приземление, приседание/вставание): наклон оружия на каждый м/с² вертикального ускорения, " +
			"градусы. При приземлении и приседании оружие проседает и кивает, при прыжке и вставании — наоборот. Знак — направление. " +
			"Если включаете — уменьшите или обнулите Landing Impulse Multiplier и Jump Impulse Strength, иначе эффект удвоится")]
		[SerializeField] private float inertiaVerticalPitchPerAcceleration = 0.1f;
		[Tooltip("Вертикаль: смещение оружия против вертикального ускорения на каждый м/с², метры")]
		[SerializeField] private float inertiaVerticalPositionPerAcceleration = 0.001f;
		[Tooltip("Насколько плавно эффект реагирует на старт и остановку: ускорение игрока ступенчатое (при нажатии сразу полное, " +
			"при наборе скорости сразу ноль), и без сглаживания вход и выход из наклона получаются резкими. Меньше — мягче и тягучее, " +
			"больше — резче. Обычно 4–12")]
		[SerializeField] private float inertiaInputSmoothing = 8f;
		[Tooltip("Жёсткость пружины СМЕЩЕНИЯ: больше — быстрее и мельче колебания")]
		[SerializeField] private float inertiaStiffness = 90f;
		[Tooltip("Затухание пружины СМЕЩЕНИЯ: меньше — больше покачиваний после остановки, больше — гасится без колебаний. " +
			"Без колебаний (критическое затухание) примерно при 2 × √Stiffness")]
		[SerializeField] private float inertiaDamping = 9f;
		[Tooltip("Жёсткость пружины ПОВОРОТА. Мягче, чем у смещения, — поворот откликается позже и \"догоняет\" смещение: из этой " +
			"разницы фаз и получается дуга. Чем ниже относительно смещения — тем сильнее отставание")]
		[SerializeField] private float inertiaRotationStiffness = 40f;
		[Tooltip("Затухание пружины ПОВОРОТА (критическое — примерно 2 × √Rotation Stiffness)")]
		[SerializeField] private float inertiaRotationDamping = 6f;
		[Tooltip("Насколько затухающий наклон закручивается в дугу: часть колебания по наклону с запаздыванием переходит в поворот " +
			"в сторону — ствол описывает затухающую дугу/круг, а не просто кивает. 0 — только кивок")]
		[SerializeField] private float inertiaArcCoupling = 0.3f;
		[Tooltip("Множитель инерции в прицеле (AimBlend = 1) — обычно сильно гасит эффект")]
		[SerializeField] private float inertiaAimMultiplier = 0.3f;

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
		public float BobStateSmoothing => bobStateSmoothing;

		public float PositionSpringStiffness => positionSpringStiffness;
		public float PositionSpringDamping => positionSpringDamping;
		public float RotationSpringStiffness => rotationSpringStiffness;
		public float RotationSpringDamping => rotationSpringDamping;
		public float ImpulseToRotationCoupling => impulseToRotationCoupling;

		public float LandingImpulseMultiplier => landingImpulseMultiplier;
		public float JumpImpulseStrength => jumpImpulseStrength;
		public float MaxImpulseStrength => maxImpulseStrength;

		public float InertiaPitchPerAcceleration => inertiaPitchPerAcceleration;
		public float InertiaRollPerAcceleration => inertiaRollPerAcceleration;
		public float InertiaPositionPerAcceleration => inertiaPositionPerAcceleration;
		public float InertiaMaxAcceleration => inertiaMaxAcceleration;
		public float InertiaVerticalPitchPerAcceleration => inertiaVerticalPitchPerAcceleration;
		public float InertiaVerticalPositionPerAcceleration => inertiaVerticalPositionPerAcceleration;
		public float InertiaInputSmoothing => inertiaInputSmoothing;
		public float InertiaStiffness => inertiaStiffness;
		public float InertiaDamping => inertiaDamping;
		public float InertiaRotationStiffness => inertiaRotationStiffness;
		public float InertiaRotationDamping => inertiaRotationDamping;
		public float InertiaArcCoupling => inertiaArcCoupling;
		public float InertiaAimMultiplier => inertiaAimMultiplier;

		// те же значения одним набором — для общего расчёта инерции (StarterAssets.MovementInertia, им же пользуется камера).
		// Заполняется при каждом обращении — правки профиля в Play Mode применяются сразу
		private MovementInertiaSettings _inertiaSettings;
		public MovementInertiaSettings InertiaSettings
		{
			get
			{
				if (_inertiaSettings == null) _inertiaSettings = new MovementInertiaSettings();
				_inertiaSettings.PitchPerAcceleration = inertiaPitchPerAcceleration;
				_inertiaSettings.RollPerAcceleration = inertiaRollPerAcceleration;
				_inertiaSettings.PositionPerAcceleration = inertiaPositionPerAcceleration;
				_inertiaSettings.VerticalPitchPerAcceleration = inertiaVerticalPitchPerAcceleration;
				_inertiaSettings.VerticalPositionPerAcceleration = inertiaVerticalPositionPerAcceleration;
				_inertiaSettings.MaxAcceleration = inertiaMaxAcceleration;
				_inertiaSettings.InputSmoothing = inertiaInputSmoothing;
				_inertiaSettings.Stiffness = inertiaStiffness;
				_inertiaSettings.Damping = inertiaDamping;
				_inertiaSettings.RotationStiffness = inertiaRotationStiffness;
				_inertiaSettings.RotationDamping = inertiaRotationDamping;
				_inertiaSettings.ArcCoupling = inertiaArcCoupling;
				return _inertiaSettings;
			}
		}
	}
}
