using System;
using UnityEngine;

namespace Enemies
{
	// Процедурный поворот головы (Look At / head tracking): враг смотрит на точку, не прерывая анимацию.
	// Это не "настоящий" IK с решателем — для головы он не нужен. Стандартный приём: после того как Animator
	// выставил позу, повернуть несколько костей шеи и голову так, чтобы морда смотрела на цель.
	// Поворот делится между костями по весам (шея чуть-чуть, голова больше) — так изгиб выглядит живым,
	// а не как голова на шарнире.
	//
	// Порядок за кадр: Animator (поза из клипа) → Spine Animator (LateUpdate, порядок -11) → этот компонент
	// (LateUpdate, порядок 100). То есть поворот ложится ПОВЕРХ анимации и Spine Animator, ничего не ломая.
	// В Update кости возвращаются в исходную позу — если какой-то клип не анимирует кость шеи, поворот
	// не накапливается кадр за кадром.
	//
	// Компонент ничего не решает сам — куда смотреть, говорит мозг (DogAI) через SetLookTarget / ClearLookTarget.
	// Ставится на модель (объект с Animator и Spine Animator).
	[DefaultExecutionOrder(100)]
	public class EnemyHeadLook : MonoBehaviour
	{
		[Serializable]
		private class LookBone
		{
			[Tooltip("Кость шеи или головы")]
			public Transform bone;
			[Tooltip("Доля поворота на эту кость относительно остальных. Например шея 1, шея 1, голова 2")]
			[Min(0f)] public float weight = 1f;
		}

		[Header("Кости")]
		[Tooltip("Кости от шеи к голове, ПО ПОРЯДКУ (сначала ближняя к туловищу, последней — голова). " +
			"Последняя кость доворачивается точно на цель, остальные берут свою долю изгиба")]
		[SerializeField] private LookBone[] bones = Array.Empty<LookBone>();
		[Tooltip("Глаза — те же Eyes, что у EnemyPerception, дочерний объект кости головы. Его синяя стрелка Z (в режиме Local) " +
			"должна смотреть вдоль морды: именно её компонент наводит на цель. Если пусто — направление морды берётся " +
			"из стартовой позы (считается, что там морда смотрит вперёд по телу)")]
		[SerializeField] private Transform eyes;
		[Tooltip("Относительно чего считаются пределы поворота — корень врага (направление тела). Если пусто — этот объект")]
		[SerializeField] private Transform body;

		[Header("Пределы")]
		[Tooltip("Насколько (градусы) голова поворачивается влево/вправо от направления тела")]
		[Range(0f, 120f)]
		[SerializeField] private float maxYaw = 70f;
		[Tooltip("Насколько (градусы) голова поднимается вверх")]
		[Range(0f, 90f)]
		[SerializeField] private float maxPitchUp = 35f;
		[Tooltip("Насколько (градусы) голова опускается вниз")]
		[Range(0f, 90f)]
		[SerializeField] private float maxPitchDown = 40f;
		[Tooltip("Если цель дальше этого угла (градусы) от направления тела — например, прямо за спиной — " +
			"голова не тянется до упора, а плавно возвращается в анимацию. Разворачивать тело — забота мозга")]
		[Range(0f, 180f)]
		[SerializeField] private float releaseYaw = 120f;

		[Header("Плавность")]
		[Tooltip("За сколько секунд голова догоняет цель. Меньше — резче, больше — ленивее")]
		[SerializeField] private float followSmoothTime = 0.15f;
		[Tooltip("Ограничение скорости поворота головы (градусы/сек)")]
		[SerializeField] private float maxTurnSpeed = 360f;
		[Tooltip("За сколько секунд эффект плавно включается, когда цель появилась")]
		[SerializeField] private float blendInTime = 0.3f;
		[Tooltip("За сколько секунд голова плавно возвращается в анимацию, когда цель пропала")]
		[SerializeField] private float blendOutTime = 0.5f;
		[Tooltip("Общая сила эффекта. 1 — голова смотрит точно на цель, 0.7 — \"косится\" в её сторону")]
		[Range(0f, 1f)]
		[SerializeField] private float maxWeight = 1f;

		[Header("Отладка")]
		[Tooltip("Только для проверки: если назначено — враг всё время смотрит на этот объект (например, на камеру игрока). " +
			"Мозг управляет головой, только когда поле пустое")]
		[SerializeField] private Transform debugTarget;

		private Quaternion[] _restLocalRotations;
		// направление "куда смотрит морда" в пространстве последней кости — берётся из позы на старте
		private Vector3 _headForwardLocal;
		private bool _hasTarget;
		private Vector3 _targetPoint;
		private float _requestWeight = 1f;
		private float _weight;
		private float _yaw;
		private float _pitch;
		private float _yawVelocity;
		private float _pitchVelocity;
		private bool _anglesInitialized;

		public bool HasTarget => _hasTarget || debugTarget != null;
		// текущая сила эффекта 0..1 (с учётом плавного включения/выключения)
		public float Weight => _weight;

		private Transform Head => bones.Length > 0 ? bones[bones.Length - 1].bone : null;
		private Transform Body => body != null ? body : transform;

		// weight 0..1 — насколько сильно смотреть именно на эту цель (умножается на Max Weight). Например, на бегу
		// мозг просит меньше, чтобы голова не "застывала" и сохраняла покачивание из клипа бега
		public void SetLookTarget(Vector3 point, float weight = 1f)
		{
			_hasTarget = true;
			_targetPoint = point;
			_requestWeight = Mathf.Clamp01(weight);
		}

		public void ClearLookTarget()
		{
			_hasTarget = false;
		}

		private void Awake()
		{
			_restLocalRotations = new Quaternion[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				if (bones[i].bone == null)
				{
					Debug.LogWarning($"{name}: в EnemyHeadLook не назначена кость №{i}", this);
					enabled = false;
					return;
				}
				_restLocalRotations[i] = bones[i].bone.localRotation;
			}

			if (Head == null)
			{
				Debug.LogWarning($"{name}: EnemyHeadLook без костей — назначь шею и голову", this);
				enabled = false;
				return;
			}

			if (eyes != null && !eyes.IsChildOf(Head))
				Debug.LogWarning($"{name}: Eyes в EnemyHeadLook должны быть внутри кости головы ({Head.name}), иначе не повернутся вместе с ней", this);

			// направление морды в осях кости головы: из Eyes, а без них — из стартовой позы, где морда смотрит
			// туда же, куда тело. Так не нужно знать, какая ось у кости "вперёд" (в Blender и Unity они часто разные)
			Vector3 forward = eyes != null ? eyes.forward : Body.forward;
			_headForwardLocal = Quaternion.Inverse(Head.rotation) * forward;
		}

		private void OnDisable()
		{
			// выключили (смерть → ragdoll) — следующее включение начнёт с нуля, без рывка
			_weight = 0f;
			_anglesInitialized = false;
		}

		private void Update()
		{
			// вернуть кости в исходную позу ДО Animator: анимированные он всё равно перезапишет,
			// а неанимированные иначе крутились бы дальше каждый кадр
			if (_weight <= 0f) return;
			for (int i = 0; i < bones.Length; i++)
				bones[i].bone.localRotation = _restLocalRotations[i];
		}

		private void LateUpdate()
		{
			float dt = Time.deltaTime;
			if (dt <= 0f) return;

			bool hasTarget = HasTarget;
			Vector3 targetPoint = debugTarget != null ? debugTarget.position : _targetPoint;

			// направление на цель в осях тела → углы поворота (yaw — влево/вправо, pitch — вверх/вниз)
			bool inRange = false;
			float targetYaw = 0f;
			float targetPitch = 0f;
			if (hasTarget)
			{
				Vector3 toTarget = targetPoint - EyePosition;
				if (toTarget.sqrMagnitude > 0.0001f)
				{
					DirectionToAngles(toTarget, out targetYaw, out targetPitch);
					inRange = Mathf.Abs(targetYaw) <= releaseYaw;
				}
			}

			bool active = hasTarget && inRange;
			float blendTime = active ? blendInTime : blendOutTime;
			float requestWeight = debugTarget != null ? 1f : _requestWeight;
			float targetWeight = active ? maxWeight * requestWeight : 0f;
			_weight = blendTime > 0f ? Mathf.MoveTowards(_weight, targetWeight, dt / blendTime) : targetWeight;
			if (_weight <= 0f)
			{
				_anglesInitialized = false;
				return;
			}

			if (!_anglesInitialized)
			{
				// голова начинает поворот оттуда, куда она смотрит в анимации, а не из центра
				DirectionToAngles(HeadForward, out _yaw, out _pitch);
				_yawVelocity = _pitchVelocity = 0f;
				_anglesInitialized = true;
			}

			if (active)
			{
				targetYaw = Mathf.Clamp(targetYaw, -maxYaw, maxYaw);
				targetPitch = Mathf.Clamp(targetPitch, -maxPitchDown, maxPitchUp);
				_yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, followSmoothTime, maxTurnSpeed, dt);
				_pitch = Mathf.SmoothDampAngle(_pitch, targetPitch, ref _pitchVelocity, followSmoothTime, maxTurnSpeed, dt);
			}
			// при затухании углы замирают, а вес плавно уводит голову обратно в анимацию

			ApplyLook(AnglesToDirection(_yaw, _pitch), _weight);
		}

		// Поворачивает кости по очереди от шеи к голове. Каждая берёт свою долю оставшегося поворота,
		// последняя (голова) — весь остаток, поэтому при весе 1 морда смотрит точно в нужную сторону
		private void ApplyLook(Vector3 desiredDirection, float weight)
		{
			float remaining = 0f;
			for (int i = 0; i < bones.Length; i++) remaining += bones[i].weight;
			if (remaining <= 0f) return;

			for (int i = 0; i < bones.Length; i++)
			{
				float share = bones[i].weight / remaining;
				remaining -= bones[i].weight;
				if (share <= 0f) continue;

				Quaternion delta = Quaternion.FromToRotation(HeadForward, desiredDirection);
				Transform bone = bones[i].bone;
				bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, share * weight) * bone.rotation;
				if (remaining <= 0f) break;
			}
		}

		private Vector3 EyePosition => eyes != null ? eyes.position : Head.position;
		private Vector3 HeadForward => Head.rotation * _headForwardLocal;

		private void DirectionToAngles(Vector3 worldDirection, out float yaw, out float pitch)
		{
			Vector3 local = Body.InverseTransformDirection(worldDirection).normalized;
			yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
			pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
		}

		private Vector3 AnglesToDirection(float yaw, float pitch)
		{
			return Body.TransformDirection(Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward);
		}

		private void OnDrawGizmosSelected()
		{
			if (Head == null) return;
			Vector3 origin = eyes != null ? eyes.position : Head.position;
			Transform bodyTransform = Body;

			// пределы поворота: жёлтые — влево/вправо, голубые — вверх/вниз
			const float length = 0.6f;
			Gizmos.color = Color.yellow;
			Gizmos.DrawRay(origin, bodyTransform.TransformDirection(Quaternion.Euler(0f, -maxYaw, 0f) * Vector3.forward) * length);
			Gizmos.DrawRay(origin, bodyTransform.TransformDirection(Quaternion.Euler(0f, maxYaw, 0f) * Vector3.forward) * length);
			Gizmos.color = Color.cyan;
			Gizmos.DrawRay(origin, bodyTransform.TransformDirection(Quaternion.Euler(-maxPitchUp, 0f, 0f) * Vector3.forward) * length);
			Gizmos.DrawRay(origin, bodyTransform.TransformDirection(Quaternion.Euler(maxPitchDown, 0f, 0f) * Vector3.forward) * length);

			if (!Application.isPlaying) return;
			// красная — куда сейчас смотрит морда, зелёная — на цель
			Gizmos.color = Color.red;
			Gizmos.DrawRay(origin, HeadForward * length);
			if (HasTarget)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawLine(origin, debugTarget != null ? debugTarget.position : _targetPoint);
			}
		}
	}
}
