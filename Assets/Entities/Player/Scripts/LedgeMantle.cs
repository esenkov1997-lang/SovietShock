using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

namespace StarterAssets
{
	// Ledge climbing (mantle) as a standalone add-on to FirstPersonController.
	// FirstPersonController stays the single authority on *when* to attempt a mantle — it already owns
	// jump/crouch input arbitration, so it calls TryMantle() at the exact moment it decides "this was a
	// fresh jump press, not consumed by standing up from a crouch". This component owns *how* the
	// detection and the actual climb work, so the two don't get tangled into one big movement script.
	//
	// Два варианта по одному и тому же прыжку:
	//  Mantle — залезть НА уступ (ящик, высокий бортик, платформа).
	//  Vault  — перелезть ЧЕРЕЗ невысокое тонкое препятствие (забор, перила, узкий ящик) и приземлиться
	//           за ним. Выбирается сам, если препятствие не выше VaultMaxHeight, не глубже VaultMaxDepth,
	//           за ним есть пол и место для игрока; иначе — обычный mantle наверх.
	[RequireComponent(typeof(CharacterController))]
	public class LedgeMantle : MonoBehaviour
	{
		[Header("Ledge Climb")]
		[Tooltip("Минимальная высота уступа от ног игрока, на который залезаем. Ниже — обычный прыжок: " +
			"на невысокий ящик можно просто запрыгнуть, mantle там выглядит излишне")]
		public float MinClimbHeight = 0.9f;
		[Tooltip("Maximum height, from the character's feet, of a ledge that can be climbed")]
		public float MaxClimbHeight = 1.2f;
		[Tooltip("How far in front of the character to look for a climbable ledge")]
		public float ClimbCheckDistance = 0.6f;
		[Tooltip("How fast the character moves up onto the ledge, in m/s")]
		public float ClimbSpeed = 4.0f;
		[Tooltip("Radius of the SphereCast probes used to detect a ledge. Thicker is more forgiving of angle/edge cases than a thin raycast")]
		public float ClimbProbeRadius = 0.2f;

		[Header("Vault (перелезание)")]
		[Tooltip("Перелезать через препятствия, а не только залезать на них")]
		public bool EnableVault = true;
		[Tooltip("Минимальная высота препятствия, через которое перелезаем, м. Ниже — обычный прыжок " +
			"(через бордюр или низкую трубу проще перепрыгнуть)")]
		public float VaultMinHeight = 0.5f;
		[Tooltip("Максимальная высота препятствия от ног игрока, через которое можно перелезть, м. " +
			"Сравнивается с точной измеренной высотой верха — выше этого значения будет обычный mantle")]
		public float VaultMaxHeight = 1.0f;
		[Tooltip("Максимальная толщина препятствия (от передней грани до задней), м. Толще — это уже платформа, на неё залезаем")]
		public float VaultMaxDepth = 0.8f;
		[Tooltip("На сколько метров пол за препятствием может быть ниже ног игрока")]
		public float VaultMaxDrop = 1.5f;
		[Tooltip("Отступ точки приземления от задней грани препятствия (сверх радиуса капсулы), м")]
		public float VaultLandingOffset = 0.1f;
		[Tooltip("Насколько выше верха препятствия проходят ноги игрока при перелезании, м")]
		public float VaultClearance = 0.15f;
		[Tooltip("Скорость перелезания, м/с — обычно быстрее, чем залезание")]
		public float VaultSpeed = 5.0f;
		[Tooltip("Перелезать пригнувшись, высотой капсулы как в присяде (FirstPersonController.CrouchHeight) — " +
			"так можно пролезть в окно. Проём должен быть не ниже CrouchHeight + Vault Clearance")]
		public bool VaultCrouched = true;

		[Header("Camera Tilt")]
		[Tooltip("Наклон камеры (pitch) во время подъёма, по нормализованному времени climb'а 0..1")]
		public AnimationCurve CameraTiltCurve = new AnimationCurve(
			new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));
		[Tooltip("Амплитуда наклона, градусы")]
		public float CameraTiltAmount = 8f;
		[Tooltip("Амплитуда наклона при перелезании, градусы (кривая та же)")]
		public float VaultTiltAmount = 5f;

		[Header("Prompt")]
		[Tooltip("Подсказка, пока перед игроком есть уступ, на который можно залезть. Клавиша прыжка " +
			"подставляется перед текстом сама — \"[Space] Взобраться\"")]
		public LocalizedString MantlePrompt = new LocalizedString("UI", "prompt.mantle");
		[Tooltip("Подсказка, пока перед игроком есть препятствие, через которое можно перелезть — \"[Space] Перелезть\"")]
		public LocalizedString VaultPrompt = new LocalizedString("UI", "prompt.vault");

		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private FirstPersonController _movement;

		// captured at Start, before any crouch shrinking has happened, so this reflects the standing pose
		private float _feetOffset;

		private bool _isClimbing;
		public bool IsClimbing => _isClimbing;

		// результат проверки: куда встанет transform, на какой высоте (transform.y) идёт горизонтальная
		// часть пути и что это — залезание или перелезание
		private struct LedgeMove
		{
			public Vector3 Target;
			public float PassHeight;
			public bool IsVault;
		}

		// last climb attempt — only for gizmo visualization in the editor
		private struct ClimbProbe
		{
			public Vector3 Start;
			public Vector3 End;
			public float Radius;
			public bool Hit;
		}
		private readonly List<ClimbProbe> _debugClimbProbes = new List<ClimbProbe>();

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
			_movement = GetComponent<FirstPersonController>();

			_feetOffset = _controller.center.y - _controller.height * 0.5f;
		}

		// called by FirstPersonController on a fresh jump press. Returns true (and starts climbing)
		// if there's a climbable ledge in front of the character; otherwise leaves everything untouched
		// so the caller can fall through to a normal jump.
		public bool TryMantle()
		{
			if (!FindLedge(true, out LedgeMove move)) return false;

			if (move.IsVault)
			{
				// пригнуться — камера опускается на ту же величину, что и в присяде
				float crouchDrop = VaultCrouched && _movement != null
					? Mathf.Max(0f, _movement.StandingHeight - _movement.CrouchHeight)
					: 0f;
				StartCoroutine(ClimbLedge(move.Target, move.PassHeight, VaultSpeed, VaultTiltAmount, crouchDrop));
			}
			else
			{
				StartCoroutine(ClimbLedge(move.Target, move.PassHeight, ClimbSpeed, CameraTiltAmount, 0f));
			}
			return true;
		}

		// Подсказка для того, что сделает прыжок прямо сейчас: VaultPrompt, MantlePrompt или null, если
		// лезть некуда. Те же проверки, что в TryMantle, но без подъёма и без логов (вызывается каждый кадр,
		// см. Player.PlayerInteractor)
		public LocalizedString AvailablePrompt
		{
			get
			{
				if (_controller == null || !FindLedge(false, out LedgeMove move)) return null;
				return move.IsVault ? VaultPrompt : MantlePrompt;
			}
		}

		// Все проверки mantle/vault. log — писать в консоль, почему не получилось (только для настоящей попытки по прыжку)
		private bool FindLedge(bool log, out LedgeMove move)
		{
			move = default;
			_debugClimbProbes.Clear();

			if (_isClimbing || (_movement != null && _movement.IsCrouching)) return false;

			Vector3 moveDirection = transform.forward; // TEMP DEBUG: look direction instead of move input

			float radius = Mathf.Max(_controller.radius - _controller.skinWidth, 0.01f);
			float feetY = transform.position.y + _feetOffset;
			float lowProbeHeight = Mathf.Max(_controller.stepOffset + 0.05f, 0.1f);

			// 1) is there actually a wall blocking the way (above what Step Offset already climbs)?
			Vector3 wallProbeOrigin = transform.position;
			wallProbeOrigin.y = feetY + lowProbeHeight;
			if (!SphereCast(wallProbeOrigin, ClimbProbeRadius, moveDirection, ClimbCheckDistance, out RaycastHit wallHit))
			{
				if (log) Debug.Log("Mantle: нет стены впереди (шаг 1 — wall probe)");
				return false;
			}

			// 2) ищем САМЫЙ НИЖНИЙ свободный просвет над стеной: пробы вперёд снизу вверх с шагом в радиус пробы.
			// Не одной пробой на MaxClimbHeight — у окна над проёмом снова стена: проба на максимальной высоте
			// прошла бы над ней, и вместо подоконника нашёлся бы верх стены
			float gapHeight = -1f;
			float probeStep = Mathf.Max(ClimbProbeRadius, 0.05f);
			for (float h = lowProbeHeight + probeStep; h <= MaxClimbHeight + 0.001f; h += probeStep)
			{
				Vector3 gapProbeOrigin = transform.position;
				gapProbeOrigin.y = feetY + h;
				if (!SphereCast(gapProbeOrigin, ClimbProbeRadius, moveDirection, ClimbCheckDistance, out _))
				{
					gapHeight = h;
					break;
				}
			}
			if (gapHeight < 0f)
			{
				if (log) Debug.Log("Mantle: стена выше MaxClimbHeight, просвета нет (шаг 2 — gap probe)");
				return false;
			}

			// 3) find the actual surface height of the ledge just past the wall face — от найденного просвета вниз.
			// Сфера на этой высоте только что прошла вперёд без столкновений, поэтому старт пробы точно свободен
			Vector3 downProbeOrigin = wallHit.point + moveDirection * (ClimbProbeRadius + _controller.skinWidth);
			downProbeOrigin.y = feetY + gapHeight;
			if (!SphereCast(downProbeOrigin, ClimbProbeRadius, Vector3.down, gapHeight - lowProbeHeight + 0.1f, out RaycastHit ledgeHit))
			{
				if (log) Debug.Log("Mantle: нет поверхности уступа сверху (шаг 3 — down probe)");
				return false;
			}

			float ledgeHeight = ledgeHit.point.y - feetY;
			if (ledgeHeight < _controller.stepOffset || ledgeHeight > MaxClimbHeight)
			{
				if (log) Debug.Log($"Mantle: высота уступа {ledgeHeight:F2}м вне диапазона [{_controller.stepOffset:F2}, {MaxClimbHeight:F2}]");
				return false;
			}

			// 3b) низкое препятствие — пробуем перелезть через него. Не вышло — падаем в обычный mantle наверх
			if (EnableVault && ledgeHeight >= VaultMinHeight && ledgeHeight <= VaultMaxHeight &&
				FindVaultLanding(wallHit.point, moveDirection, feetY, ledgeHit.point.y, radius, log, out Vector3 vaultFeet))
			{
				if (log) Debug.Log($"Vault: перелезть! высота {ledgeHeight:F2}м, приземление {vaultFeet}");
				move = new LedgeMove
				{
					Target = vaultFeet - new Vector3(0f, _feetOffset, 0f),
					PassHeight = ledgeHit.point.y + VaultClearance - _feetOffset,
					IsVault = true,
				};
				return true;
			}

			// 3c) mantle только на высокие уступы — на низкие игрок запрыгнет обычным прыжком
			if (ledgeHeight < MinClimbHeight)
			{
				if (log) Debug.Log($"Mantle: уступ {ledgeHeight:F2}м ниже MinClimbHeight {MinClimbHeight:F2}м — обычный прыжок");
				return false;
			}

			// 4) make sure the character actually fits standing on top of the ledge
			Vector3 landingFeet = downProbeOrigin + moveDirection * radius;
			landingFeet.y = ledgeHit.point.y;
			if (_movement == null || !_movement.HasHeadroomAt(landingFeet))
			{
				if (log) Debug.Log("Mantle: не хватает места стоять на уступе (шаг 4 — headroom)");
				return false;
			}

			if (log) Debug.Log($"Mantle: залезть! высота уступа {ledgeHeight:F2}м, точка приземления {landingFeet}");

			Vector3 target = landingFeet - new Vector3(0f, _feetOffset, 0f);
			move = new LedgeMove
			{
				Target = target,
				PassHeight = target.y + Mathf.Max(_controller.radius, 0.15f),
				IsVault = false,
			};
			return true;
		}

		// Перелезание: препятствие тонкое, за ним пол, игрок там помещается и над верхом есть где пролезть.
		// wallPoint — точка передней грани, topY — мировая высота верха препятствия
		private bool FindVaultLanding(Vector3 wallPoint, Vector3 direction, float feetY, float topY, float radius,
			bool log, out Vector3 landingFeet)
		{
			landingFeet = default;
			int mask = _movement != null ? _movement.CollisionMask : ~0;

			// V1) толщина: за VaultMaxDepth от передней грани, ниже верха, должно быть пусто. Высота пробы —
			// середина между ногами и верхом: там препятствие точно есть (его нашла wall probe)
			float probeY = Mathf.Lerp(feetY + Mathf.Max(_controller.stepOffset + 0.05f, 0.1f), topY, 0.5f);
			Vector3 farOrigin = wallPoint + direction * (VaultMaxDepth + ClimbProbeRadius);
			farOrigin.y = probeY;
			if (Physics.CheckSphere(farOrigin, ClimbProbeRadius, mask, QueryTriggerInteraction.Ignore))
			{
				if (log) Debug.Log("Vault: препятствие толще VaultMaxDepth (или сразу за ним другое) — mantle");
				return false;
			}

			// V2) задняя грань: от пустой точки — обратно к игроку
			if (!SphereCast(farOrigin, ClimbProbeRadius, -direction, VaultMaxDepth + ClimbProbeRadius, out RaycastHit backHit))
			{
				if (log) Debug.Log("Vault: не нашли заднюю грань препятствия — mantle");
				return false;
			}
			// SphereCast находит точку на грани; горизонтально отходим от неё на радиус капсулы + отступ
			Vector3 landing = backHit.point + direction * (radius + _controller.skinWidth + VaultLandingOffset);

			// V3) пол за препятствием: заметно ниже верха (иначе это широкая платформа) и не глубже VaultMaxDrop
			Vector3 groundOrigin = landing;
			groundOrigin.y = topY + ClimbProbeRadius + 0.05f;
			float groundDistance = topY - feetY + VaultMaxDrop + ClimbProbeRadius + 0.05f;
			if (!SphereCast(groundOrigin, ClimbProbeRadius, Vector3.down, groundDistance, out RaycastHit groundHit))
			{
				if (log) Debug.Log("Vault: за препятствием нет пола в пределах VaultMaxDrop — mantle");
				return false;
			}
			if (groundHit.point.y > topY - _controller.stepOffset)
			{
				if (log) Debug.Log("Vault: за препятствием пол почти на уровне верха — это платформа, mantle");
				return false;
			}
			landing.y = groundHit.point.y;

			// V4) игрок помещается стоя в точке приземления
			if (_movement == null || !_movement.HasHeadroomAt(landing))
			{
				if (log) Debug.Log("Vault: не хватает места в точке приземления — mantle");
				return false;
			}

			// V5) путь над верхом свободен: капсула на высоте прохода от игрока до точки над приземлением.
			// Пригнувшись — высотой приседа, поэтому пролезаем в окно, куда стоя не пройти
			float passFeetY = topY + VaultClearance;
			float passHeight = VaultCrouched && _movement != null ? _movement.CrouchHeight : _controller.height;
			Vector3 passStart = new Vector3(transform.position.x, passFeetY, transform.position.z);
			Vector3 passEnd = new Vector3(landing.x, passFeetY, landing.z);
			if (!IsCapsulePathClear(passStart, passEnd, radius, passHeight, mask))
			{
				if (log) Debug.Log($"Vault: над препятствием не пролезть даже высотой {passHeight:F2}м (низкий проём / преграда) — mantle");
				return false;
			}

			landingFeet = landing;
			return true;
		}

		// свободен ли путь капсулы высотой height (по ногам) из from в to, не считая собственного коллайдера
		private bool IsCapsulePathClear(Vector3 fromFeet, Vector3 toFeet, float radius, float height, int mask)
		{
			height = Mathf.Max(height, radius * 2f);
			Vector3 bottom = fromFeet + Vector3.up * radius;
			Vector3 top = fromFeet + Vector3.up * (height - radius);
			Vector3 delta = toFeet - fromFeet;
			float distance = delta.magnitude;

			// уже в стартовой точке что-то мешает (например, потолок прямо над игроком)
			foreach (Collider overlap in Physics.OverlapCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore))
			{
				if (overlap != (Collider)_controller) return false;
			}

			if (distance < 0.001f) return true;
			foreach (RaycastHit hit in Physics.CapsuleCastAll(bottom, top, radius, delta / distance, distance, mask, QueryTriggerInteraction.Ignore))
			{
				if (hit.collider != (Collider)_controller) return false;
			}
			return true;
		}

		// sweeps a sphere instead of a thin ray — much more forgiving of approach angle, thin colliders
		// and edge cases than a single-point raycast. Casts from the character's own centre and uses
		// SphereCastAll to skip past the self-hit, rather than offsetting the origin forward first —
		// an earlier version offset the origin by (own radius + probe radius), which could overshoot
		// past thin walls entirely and never register a hit no matter the distance.
		// Only layers the character collides with (Physics layer matrix): an item carried on the IgnorePlayer
		// layer (see Player.ItemHoldController) must not be mistaken for a wall or a ledge to climb onto
		private bool SphereCast(Vector3 origin, float probeRadius, Vector3 direction, float maxDistance, out RaycastHit hit)
		{
			RaycastHit[] hits = Physics.SphereCastAll(origin, probeRadius, direction, maxDistance, _movement != null ? _movement.CollisionMask : ~0, QueryTriggerInteraction.Ignore);

			bool found = false;
			hit = default;
			float closestDistance = float.MaxValue;

			foreach (RaycastHit candidate in hits)
			{
				if (candidate.collider == (Collider)_controller) continue; // ignore self
				if (candidate.distance < closestDistance)
				{
					closestDistance = candidate.distance;
					hit = candidate;
					found = true;
				}
			}

			float debugDistance = found ? hit.distance : maxDistance;
			_debugClimbProbes.Add(new ClimbProbe
			{
				Start = origin,
				End = origin + direction.normalized * debugDistance,
				Radius = probeRadius,
				Hit = found
			});

			return found;
		}

		// passHeight — высота transform.y, на которой идёт горизонтальная часть пути: при mantle чуть выше
		// уступа, при vault чуть выше верха препятствия (а цель — ниже, за ним)
		// crouchDrop — на сколько метров опустить камеру, пока игрок проходит над препятствием (0 — не пригибаться).
		// Сам CharacterController на время подъёма выключен, поэтому физически капсулу уменьшать не нужно —
		// "пролезает ли" уже проверено в FindVaultLanding, а здесь опускаем только камеру, чтобы она не прошла
		// сквозь стену над окном
		private IEnumerator ClimbLedge(Vector3 targetPosition, float passHeight, float speed, float tiltAmount, float crouchDrop)
		{
			_isClimbing = true;
			_controller.enabled = false;

			// go up first, then over, then settle down onto the ledge (or the floor behind the obstacle),
			// instead of cutting the corner diagonally through the wall
			Vector3 startPosition = transform.position;
			float raisedY = Mathf.Max(passHeight, startPosition.y);
			Vector3 raisedAtStart = new Vector3(startPosition.x, raisedY, startPosition.z);
			Vector3 raisedAtTarget = new Vector3(targetPosition.x, raisedY, targetPosition.z);

			float totalDistance = Vector3.Distance(startPosition, raisedAtStart)
				+ Vector3.Distance(raisedAtStart, raisedAtTarget)
				+ Vector3.Distance(raisedAtTarget, targetPosition);
			float totalDuration = Mathf.Max(totalDistance / speed, 0.01f);
			Coroutine tiltRoutine = StartCoroutine(TrackCameraTilt(totalDuration, tiltAmount));

			// пригибание привязано к фазам, а не к общему времени: к началу прохода над препятствием камера
			// гарантированно уже внизу, как бы ни соотносились длины подъёма и прохода
			yield return MoveTo(startPosition, raisedAtStart, speed,    // straight up, clear of the wall — пригибаемся
				p => SetCrouchDrop(crouchDrop * Mathf.SmoothStep(0f, 1f, p)));
			yield return MoveTo(raisedAtStart, raisedAtTarget, speed,   // straight over, above the ledge edge — пригнувшись
				_ => SetCrouchDrop(crouchDrop));
			yield return MoveTo(raisedAtTarget, targetPosition, speed,  // settle down — выпрямляемся
				p => SetCrouchDrop(crouchDrop * (1f - Mathf.SmoothStep(0f, 1f, p))));

			StopCoroutine(tiltRoutine);
			if (_movement != null)
			{
				// don't leave the tilt / crouch drop stuck on
				_movement.ExtraPitchOffset = 0f;
				_movement.ExtraCameraDrop = 0f;
			}

			transform.position = targetPosition;
			if (_movement != null) _movement.ClearVerticalVelocity();
			_controller.enabled = true;
			_isClimbing = false;
		}

		// drives FirstPersonController.ExtraPitchOffset over the whole climb, independent of which
		// of the three MoveTo() phases is currently running
		private IEnumerator TrackCameraTilt(float totalDuration, float amount)
		{
			if (_movement == null) yield break;

			float elapsed = 0f;
			while (elapsed < totalDuration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / totalDuration);
				_movement.ExtraPitchOffset = CameraTiltCurve.Evaluate(t) * amount;
				yield return null;
			}
		}

		// onProgress — вызывается каждый кадр с долей пройденного отрезка 0..1 (для пригибания камеры)
		private IEnumerator MoveTo(Vector3 from, Vector3 to, float speed, System.Action<float> onProgress = null)
		{
			float duration = Mathf.Max(Vector3.Distance(from, to) / speed, 0.01f);
			float elapsed = 0f;

			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				transform.position = Vector3.Lerp(from, to, t);
				onProgress?.Invoke(t);
				yield return null;
			}

			transform.position = to;
			onProgress?.Invoke(1f);
		}

		private void SetCrouchDrop(float drop)
		{
			if (_movement != null) _movement.ExtraCameraDrop = drop;
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			// last ledge-climb attempt: green = hit something, red = swept all the way through (no hit)
			foreach (ClimbProbe probe in _debugClimbProbes)
			{
				Gizmos.color = probe.Hit ? transparentGreen : transparentRed;
				Gizmos.DrawWireSphere(probe.Start, probe.Radius);
				Gizmos.DrawWireSphere(probe.End, probe.Radius);
				Gizmos.DrawLine(probe.Start, probe.End);
			}
		}
	}
}
