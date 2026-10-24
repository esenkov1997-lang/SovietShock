using UnityEngine;

namespace StarterAssets
{
	// Procedural camera head-bob while walking/sprinting. Feeds FirstPersonController.ExtraPositionOffset
	// (the same additive slot LedgeMantle's climb tilt uses) and ExtraRollOffset instead of touching the camera
	// transform itself, so this composes with other add-on effects instead of fighting them for ownership.
	// Runs in Update() rather than LateUpdate() so the offset is guaranteed to be set before
	// FirstPersonController's own LateUpdate (CameraRotation) reads it later this same frame.
	//
	// Форма — как движение головы при ходьбе: за один ЦИКЛ (стрейд — два шага, левой и правой) голова один раз
	// качается в сторону (BobX, Roll) и ДВА раза вверх-вниз (BobY — по одному на каждый шаг): выше всего над
	// опорной ногой (в крайней боковой точке), ниже всего при переносе веса (по центру). При таком соотношении
	// 1:2 траектория рисует дугу-"улыбку", а не диагональ, как было бы при одинаковом цикле у X и Y.
	[RequireComponent(typeof(FirstPersonController))]
	public class CameraHeadBob : MonoBehaviour
	{
		[Header("Bob Shape")]
		[Tooltip("Боковое качание за один цикл = стрейд (два шага): 0..1 — от центра в одну сторону, через центр в другую и обратно")]
		public AnimationCurve BobX = new AnimationCurve(
			new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(0.5f, 0f), new Keyframe(0.75f, -1f), new Keyframe(1f, 0f));
		[Tooltip("Вверх-вниз за ОДИН ШАГ (за цикл X читается дважды): 0 и 1 — перенос веса (низ), 0.5 — над опорной ногой (верх). " +
			"Вместе с BobX даёт дугу")]
		public AnimationCurve BobY = new AnimationCurve(
			new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
		[Tooltip("Крен головы за один цикл (как BobX — в сторону опорной ноги). Знак кривой — направление наклона")]
		public AnimationCurve BobRoll = new AnimationCurve(
			new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(0.5f, 0f), new Keyframe(0.75f, -1f), new Keyframe(1f, 0f));

		[Header("Walk")]
		[Tooltip("Циклов (стрейдов — пар шагов) в секунду при ходьбе")]
		public float WalkBobSpeed = 0.9f;
		[Tooltip("Амплитуда при ходьбе, метры")]
		public float WalkBobAmount = 0.04f;
		[Tooltip("Амплитуда крена при ходьбе, градусы. 0 — без крена")]
		public float WalkRollAmount = 0.5f;

		[Header("Sprint")]
		[Tooltip("Циклов (стрейдов — пар шагов) в секунду при спринте")]
		public float SprintBobSpeed = 1.3f;
		[Tooltip("Амплитуда при спринте, метры")]
		public float SprintBobAmount = 0.07f;
		[Tooltip("Амплитуда крена при спринте, градусы")]
		public float SprintRollAmount = 0.9f;

		[Header("Blend")]
		[Tooltip("How fast the bob fades in/out when starting/stopping or leaving the ground")]
		public float BlendSpeed = 8f;
		[Tooltip("Minimum ground speed, in m/s, before the bob starts blending in")]
		public float MinSpeed = 0.2f;
		[Tooltip("Скорость, с которой частота и амплитуда bob плавно перестраиваются при смене ходьба ↔ спринт: не прыгают " +
			"за один кадр, а догоняют новое значение. Больше — быстрее, меньше — мягче. Обычно 4–10")]
		public float StateSmoothing = 6f;

		private FirstPersonController _movement;

		private float _phase;
		private float _blend;

		// "дошагивание" при остановке: фаза доигрывается до ближайшей нейтральной точки (центр, низ шага), а не обрывается
		private bool _settling;
		private float _settleTargetPhase;
		private float _settleRate;
		private float _lastMovingBobSpeed;
		// сглаженные частота и амплитуды — см. Update
		private float _bobSpeed;
		private float _bobAmount;
		private float _rollAmount;

		private void Start()
		{
			_movement = GetComponent<FirstPersonController>();
		}

		private void OnDisable()
		{
			if (_movement == null) return;
			_movement.ExtraPositionOffset = Vector3.zero;
			_movement.ExtraRollOffset = 0f;
		}

		private void Update()
		{
			bool isMoving = _movement.IsGroundedStable && _movement.CurrentSpeed > MinSpeed;
			// IsSprinting (not raw _input.sprint) already excludes crouch — holding sprint while crouched
			// must still bob at crouch pace, since movement itself stays at CrouchSpeed
			bool sprinting = isMoving && _movement.IsSprinting;

			// scaled by how close actual speed is to the relevant target (MoveSpeed/SprintSpeed), so
			// crouch — which moves slower than MoveSpeed but isn't its own bob category — naturally bobs
			// slower and smaller instead of at full walk pace, and the accel/decel ramp is reflected too
			float referenceSpeed = sprinting ? _movement.SprintSpeed : _movement.MoveSpeed;
			float speedRatio = referenceSpeed > 0f ? Mathf.Clamp01(_movement.CurrentSpeed / referenceSpeed) : 0f;

			// Спринт переключается за один кадр (в том числе сам по себе — IsSprinting требует движения вперёд, при стрейфе
			// на бегу он мигает), а частота и амплитуда у ходьбы и спринта разные. Без сглаживания амплитуда прыгала бы
			// скачком посреди шага — камера дёргалась. Сглаживаем экспоненциально (не зависит от FPS)
			float targetSpeed = (sprinting ? SprintBobSpeed : WalkBobSpeed) * speedRatio;
			float targetAmount = (sprinting ? SprintBobAmount : WalkBobAmount) * speedRatio;
			float targetRoll = (sprinting ? SprintRollAmount : WalkRollAmount) * speedRatio;
			float stateT = 1f - Mathf.Exp(-StateSmoothing * Time.deltaTime);
			_bobSpeed = Mathf.Lerp(_bobSpeed, targetSpeed, stateT);
			_bobAmount = Mathf.Lerp(_bobAmount, targetAmount, stateT);
			_rollAmount = Mathf.Lerp(_rollAmount, targetRoll, stateT);

			AdvancePhase(isMoving);
			float cycle = Mathf.Repeat(_phase, 1f);       // стрейд: боковое качание и крен
			float step = Mathf.Repeat(_phase * 2f, 1f);   // шаг: вверх-вниз — дважды за стрейд

			// пока камера дошагивает до нейтрали — покачивание не гасим: иначе из крайнего положения она вернулась бы
			// в центр за время гашения (при большой Blend Speed — за кадр). Гасим, только когда она уже в нейтрали
			_blend = Mathf.MoveTowards(_blend, isMoving || _settling ? 1f : 0f, Time.deltaTime * BlendSpeed);

			if (_blend <= 0f)
			{
				if (!isMoving) _phase = 0f; // start the next bob cleanly from neutral instead of mid-cycle
				_movement.ExtraPositionOffset = Vector3.zero;
				_movement.ExtraRollOffset = 0f;
				return;
			}

			// нарастание/спад по плавной кривой, а не линейно — без рывка при старте и резкой остановки
			float blend = Mathf.SmoothStep(0f, 1f, _blend);
			float x = BobX.Evaluate(cycle) * _bobAmount * blend;
			float y = BobY.Evaluate(step) * _bobAmount * blend;
			_movement.ExtraPositionOffset = new Vector3(x, y, 0f);
			_movement.ExtraRollOffset = BobRoll.Evaluate(cycle) * _rollAmount * blend;
		}

		// Фаза шага. Идём — фаза растёт в темпе шага. Остановились посреди шага — не обрываем, а "дошагиваем":
		// фаза доигрывается до ближайшей нейтральной точки — границы полушага (0 или 0.5 цикла: по центру по горизонтали,
		// внизу шага по вертикали, без крена) — в темпе последнего шага, с плавным замедлением к концу, как будто
		// человек ставит ногу и останавливается. Пошли снова посреди дошагивания — шаг продолжается с того же места
		private void AdvancePhase(bool isMoving)
		{
			if (isMoving)
			{
				_settling = false;
				_phase += Time.deltaTime * _bobSpeed;
				_lastMovingBobSpeed = _bobSpeed;
				return;
			}

			if (!_settling)
			{
				if (_blend <= 0f) return; // уже стоим, покачивания нет

				_settleTargetPhase = Mathf.Ceil(_phase * 2f) * 0.5f;
				float remaining = _settleTargetPhase - _phase;
				if (remaining <= 0.0001f) return; // и так в нейтрали

				// стартовая скорость дошагивания — темп последнего шага (но не "замирание", если игрок едва шёл);
				// дальше экспоненциальное замедление к цели: rate подобран так, что в первый момент скорость равна этому темпу
				float speed = Mathf.Max(_lastMovingBobSpeed, WalkBobSpeed * 0.5f);
				_settleRate = speed / remaining;
				_settling = true;
			}

			_phase = Mathf.Lerp(_phase, _settleTargetPhase, 1f - Mathf.Exp(-_settleRate * Time.deltaTime));

			// дошли до нейтрали — или амплитуда уже затухла вместе с остановкой (хвост экспоненты длинный, а двигать
			// камеру к этому моменту уже некуда)
			if (_settleTargetPhase - _phase < 0.002f || _bobAmount < 0.0005f)
			{
				_phase = _settleTargetPhase;
				_settling = false;
			}
		}
	}
}
