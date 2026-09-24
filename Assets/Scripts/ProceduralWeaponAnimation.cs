using StarterAssets;
using UnityEngine;

namespace Weapons
{
	// Единый компонент процедурной анимации оружия в руках — заменяет собой старые WeaponSway и
	// WeaponProceduralAnimation. Все числовые параметры берутся не из WeaponData напрямую, а из
	// WeaponData.AnimationProfile (WeaponAnimationProfileSO) — так один и тот же профиль ощущений
	// переиспользуется на разных пушках, а WeaponData не разрастается портянкой полей.
	//
	// Двигает и вращает WeaponController.VisualRoot — pivot, который WeaponController каждый кадр
	// (в Update, раньше LateUpdate) ставит в HandPosition/AimPosition текущего оружия. К моменту, когда
	// этот компонент читает VisualRoot.localPosition/localRotation, они уже "чистая база" этого кадра —
	// поэтому все три эффекта ниже складываются аддитивно, без взаимных перезаписей и накопления ошибки.
	[RequireComponent(typeof(WeaponController))]
	public class ProceduralWeaponAnimation : MonoBehaviour
	{
		[Header("Ссылки")]
		[Tooltip("Если не задано — берётся StarterAssetsInputs с родительских объектов (обычно с игрока)")]
		[SerializeField] private StarterAssetsInputs input;
		[Tooltip("Если не задано — берётся FirstPersonController с родительских объектов")]
		[SerializeField] private FirstPersonController firstPersonController;

		private WeaponController _weaponController;
		private Transform _visualRoot;
		private WeaponAnimationProfileSO _currentProfile;

		// ---- Look Sway (позиция + поворот от движения мыши) ----
		private Vector3 _swayPosition;
		private Quaternion _swayRotation = Quaternion.identity;

		// ---- Bobbing (фигура Лиссажу по фазе шага) ----
		private float _bobPhase;
		private float _bobBlend;

		// ---- Impact Spring (затухающая пружина для толчков — прыжок/приземление/что угодно ещё) ----
		private Vector3 _springPosition;
		private Vector3 _springPositionVelocity;
		private Vector3 _springRotationEuler;
		private Vector3 _springRotationVelocity;
		private bool _wasGrounded = true;

		private void Awake()
		{
			_weaponController = GetComponent<WeaponController>();
		}

		private void Start()
		{
			if (input == null) input = GetComponentInParent<StarterAssetsInputs>();
			if (firstPersonController == null) firstPersonController = GetComponentInParent<FirstPersonController>();
			_visualRoot = _weaponController.VisualRoot;

			if (firstPersonController != null) _wasGrounded = firstPersonController.Grounded;
		}

		private void LateUpdate()
		{
			WeaponData data = _weaponController.CurrentWeaponData;
			_currentProfile = data != null ? data.AnimationProfile : null;
			if (_currentProfile == null || _visualRoot == null) return;

			DetectJumpAndLanding();

			Vector3 swayOffset = UpdateSway(out Quaternion swayRotation);
			Vector3 bobOffset = UpdateBob(out Quaternion bobRotation);
			Vector3 springOffset = UpdateSpring(out Quaternion springRotation);

			// Композиция: базовая поза (уже в VisualRoot) + sway + bobbing + пружина импульсов —
			// позиции складываются, повороты перемножаются (кватернионный аналог суммы)
			_visualRoot.localPosition += swayOffset + bobOffset + springOffset;
			_visualRoot.localRotation = _visualRoot.localRotation * swayRotation * bobRotation * springRotation;
		}

		// ------------------------------------------------------------------
		// Look Sway: оружие смещается по X/Y и довора́чивается (Roll/Yaw/Pitch) против направления
		// движения мыши, с плавным сглаживанием — Lerp для позиции, Slerp для поворота
		// ------------------------------------------------------------------
		private Vector3 UpdateSway(out Quaternion rotation)
		{
			Vector2 look = input != null ? input.look : Vector2.zero;

			Vector3 targetPosition = new Vector3(
				Mathf.Clamp(-look.x * _currentProfile.SwayPositionAmount, -_currentProfile.MaxSwayPositionOffset, _currentProfile.MaxSwayPositionOffset),
				Mathf.Clamp(-look.y * _currentProfile.SwayPositionAmount, -_currentProfile.MaxSwayPositionOffset, _currentProfile.MaxSwayPositionOffset),
				0f);

			// pitch — от вертикального движения мыши; yaw и roll — от горизонтального, в одну сторону
			// (см. также WeaponSway, откуда взята эта идея с общим Z для yaw/roll)
			Vector3 targetRotationEuler = new Vector3(
				Mathf.Clamp(look.y * _currentProfile.SwayRotationAmount, -_currentProfile.MaxSwayRotationAngle, _currentProfile.MaxSwayRotationAngle),
				Mathf.Clamp(-look.x * _currentProfile.SwayRotationAmount, -_currentProfile.MaxSwayRotationAngle, _currentProfile.MaxSwayRotationAngle),
				Mathf.Clamp(-look.x * _currentProfile.SwayRotationAmount, -_currentProfile.MaxSwayRotationAngle, _currentProfile.MaxSwayRotationAngle));

			// экспоненциальный коэффициент сглаживания — не зависит от FPS, в отличие от плоского Lerp(a, b, speed * dt)
			float smoothT = 1f - Mathf.Exp(-_currentProfile.SwaySmoothing * Time.deltaTime);
			_swayPosition = Vector3.Lerp(_swayPosition, targetPosition, smoothT);
			_swayRotation = Quaternion.Slerp(_swayRotation, Quaternion.Euler(targetRotationEuler), smoothT);

			rotation = _swayRotation;
			return _swayPosition;
		}

		// ------------------------------------------------------------------
		// Bobbing: фигура Лиссажу вместо синусоиды — частота по X вдвое меньше частоты по Y, поэтому
		// траектория рисует "восьмёрку", а не простой эллипс. Плюс лёгкий наклон по Z в такт шагам.
		// Частота и амплитуда домножаются на коэффициенты состояния игрока (спринт/присед/прицел).
		// ------------------------------------------------------------------
		private Vector3 UpdateBob(out Quaternion rotation)
		{
			float speed = firstPersonController != null ? firstPersonController.CurrentSpeed : 0f;
			bool isMoving = firstPersonController != null && firstPersonController.Grounded && speed > _currentProfile.MinMoveSpeedForBob;

			float stateMultiplier = 1f;
			if (firstPersonController != null)
			{
				// присед и спринт взаимоисключающие способы движения — приоритет тот же, что и в
				// FirstPersonController.IsSprinting (спринт в приседе физически невозможен)
				if (firstPersonController.IsCrouching) stateMultiplier *= _currentProfile.CrouchMultiplier;
				else if (firstPersonController.IsSprinting) stateMultiplier *= _currentProfile.SprintMultiplier;
			}

			// прицел гасит bob независимо от способа передвижения — линейно вместе с самим AimBlend,
			// а не резким переключением, чтобы вход/выход из прицела не дёргал покачивание
			stateMultiplier = Mathf.Lerp(stateMultiplier, stateMultiplier * _currentProfile.AimMultiplier, _weaponController.AimBlend);

			float targetBlend = isMoving ? 1f : 0f;
			_bobBlend = Mathf.MoveTowards(_bobBlend, targetBlend, Time.deltaTime * _currentProfile.BobBlendSpeed);

			if (_bobBlend <= 0f)
			{
				if (!isMoving) _bobPhase = 0f; // следующий цикл начнётся чисто с нуля, а не с середины фигуры
				rotation = Quaternion.identity;
				return Vector3.zero;
			}

			_bobPhase += Time.deltaTime * _currentProfile.BobFrequency * stateMultiplier;
			float phaseRadians = _bobPhase * Mathf.PI * 2f;

			// сама фигура Лиссажу: X на половинной частоте относительно Y. Pitch (кивок) идёт на той же
			// частоте, что и Y — по одному кивку на каждый шаг, ровно как реальный вертикальный удар ноги
			// о землю; Roll — на половинной частоте, как и боковое X-смещение (один крен на пару шагов)
			float x = Mathf.Sin(phaseRadians * 0.5f) * _currentProfile.BobAmplitude.x;
			float y = Mathf.Sin(phaseRadians) * _currentProfile.BobAmplitude.y;
			float roll = Mathf.Sin(phaseRadians * 0.5f) * _currentProfile.BobRollAmount;
			float pitch = Mathf.Sin(phaseRadians) * _currentProfile.BobPitchAmount;

			float amount = _bobBlend * stateMultiplier;
			rotation = Quaternion.Euler(pitch * amount, 0f, roll * amount);
			return new Vector3(x, y, 0f) * amount;
		}

		// ------------------------------------------------------------------
		// Impact Spring: затухающий гармонический осциллятор (Damped Spring) отдельно для позиции и
		// поворота. AddImpactImpulse — единственная точка входа для внешних толчков (прыжок, приземление,
		// в будущем — например, отдача или взрыв рядом), сама пружина всегда сама возвращается к нулю.
		// ------------------------------------------------------------------
		private Vector3 UpdateSpring(out Quaternion rotation)
		{
			// у позиции (метры) и поворота (градусы) разный масштаб единиц, поэтому и разные
			// stiffness/damping — общая жёсткая пружина, откалиброванная под метры, гасила бы
			// поворот почти до нуля даже при заметном позиционном толчке
			IntegrateSpring(ref _springPosition, ref _springPositionVelocity, _currentProfile.PositionSpringStiffness, _currentProfile.PositionSpringDamping);
			IntegrateSpring(ref _springRotationEuler, ref _springRotationVelocity, _currentProfile.RotationSpringStiffness, _currentProfile.RotationSpringDamping);

			rotation = Quaternion.Euler(_springRotationEuler);
			return _springPosition;
		}

		// один шаг полуявного (semi-implicit) метода Эйлера для F = -stiffness*x - damping*v —
		// классическая затухающая пружина: жёсткость тянет значение к нулю, затухание гасит колебания.
		// stiffness/damping передаются параметрами, а не читаются из профиля напрямую — так один и тот же
		// метод обслуживает и позицию (метры), и поворот (градусы) со своими константами каждый
		private void IntegrateSpring(ref Vector3 value, ref Vector3 velocity, float stiffness, float damping)
		{
			Vector3 acceleration = -stiffness * value - damping * velocity;
			velocity += acceleration * Time.deltaTime;
			value += velocity * Time.deltaTime;
		}

		// публичный вход для физических толчков по позиции (в локальных координатах VisualRoot) —
		// часть импульса дополнительно передаётся в поворот (крен от толчка), см. ImpulseToRotationCoupling
		public void AddImpactImpulse(Vector3 impulse)
		{
			if (_currentProfile == null) return;

			_springPositionVelocity += impulse;
			_springRotationVelocity += new Vector3(impulse.y, 0f, impulse.x) * _currentProfile.ImpulseToRotationCoupling;
		}

		// ------------------------------------------------------------------
		// Прыжок/приземление — используют уже готовые события FirstPersonController (JustLanded/LandingSpeed
		// для приземления; переход Grounded true→false здесь же — для момента отрыва при прыжке)
		// ------------------------------------------------------------------
		private void DetectJumpAndLanding()
		{
			if (firstPersonController == null) return;

			if (firstPersonController.JustLanded)
			{
				// чем сильнее скорость падения, тем тяжелее оружие "проседает" при приземлении
				float strength = Mathf.Min(firstPersonController.LandingSpeed * _currentProfile.LandingImpulseMultiplier, _currentProfile.MaxImpulseStrength);
				AddImpactImpulse(Vector3.down * strength);
			}

			bool grounded = firstPersonController.Grounded;
			if (_wasGrounded && !grounded)
			{
				// оторвались от земли — фиксированный импульс вниз, аналог проседания веса оружия при взлёте,
				// без привязки к скорости (в отличие от приземления, тут её негде взять заранее)
				AddImpactImpulse(Vector3.down * Mathf.Min(_currentProfile.JumpImpulseStrength, _currentProfile.MaxImpulseStrength));
			}
			_wasGrounded = grounded;
		}
	}
}
