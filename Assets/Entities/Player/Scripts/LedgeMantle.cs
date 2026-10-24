using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

namespace StarterAssets
{
	// Движение тела из клипа подъёма (высокий подъём): длительность клипа, время захвата (MantleGrab) и где корень
	// (ноги игрока) относительно старта в кадре захвата и в последнем кадре — в осях игрока (X — вбок, Y — вверх, Z — вперёд)
	public struct MantleClipMotion
	{
		public float Duration;
		public float GrabTime;
		public Vector3 RootAtGrab;
		public Vector3 RootAtEnd;
		// где в клипе край уступа по оси "вперёд" (от старта) — по ладоням в кадре захвата. NaN — неизвестно
		// (тогда движение вперёд привязывается к точке приземления)
		public float EdgeForwardAtGrab;
	}

	// Необязательная анимация рук при подъёме (см. Player.MantleArmsAnimation) — LedgeMantle не знает ни про оружие,
	// ни про риг рук.
	//  • обычный подъём и перелезание (TryBegin): движение идёт по ClimbSpeed/VaultSpeed, клип играет поверх на своей скорости;
	//  • высокий подъём (TryBeginClimbMotion): наоборот, движение тела берётся из корня клипа (SampleClimbRoot),
	//    LedgeMantle только подгоняет его под реальную высоту уступа
	public interface IMantleAnimation
	{
		// начать анимацию; false — анимировать нечем
		bool TryBegin(bool isVault);
		// высокий подъём с движением тела из клипа; false — для этой высоты такого клипа нет (тогда обычный подъём)
		bool TryBeginClimbMotion(float ledgeHeight, out MantleClipMotion motion);
		// где корень клипа высокого подъёма в момент time относительно старта — в осях игрока
		Vector3 SampleClimbRoot(float time);
		// анимация сама ведёт камеру — тогда наклон по CameraTiltCurve не добавляется, чтобы наклоны не сложились
		bool DrivesCamera { get; }
		// подъём закончен или прерван — убрать анимацию
		void End();
	}

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
		[Tooltip("Высокий подъём по клипу: за сколько секунд в начале игрок плавно разворачивается лицом к стене, а камера — " +
			"в \"прямо\" (клип строился с таким взглядом, руки привязаны к камере). Пока идёт подъём, мышь камеру не крутит")]
		public float ClimbAlignDuration = 0.15f;

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
		[Tooltip("Скорость перелезания, м/с — обычно быстрее, чем залезание. С Vault Keep Momentum — минимальная: на бегу быстрее")]
		public float VaultSpeed = 5.0f;
		[Tooltip("Насколько дуга перелезания поднимается над минимально нужной высотой (верх препятствия + Vault Clearance), м. " +
			"0 — впритирку, больше — заметнее \"прыжок\" через препятствие")]
		public float VaultArcHeight = 0.1f;
		[Tooltip("Сохранять разгон: перелезание идёт со скоростью бега, если она выше Vault Speed, а после приземления " +
			"игрок продолжает движение с той же скоростью — бег не прерывается. Выключено — после приземления игрок стоит")]
		public bool VaultKeepMomentum = true;
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
		private IMantleAnimation _animation;
		// анимация рук идёт прямо сейчас — чтобы убрать её, если подъём прервётся (объект выключили посреди корутины)
		private bool _animating;

		private void OnDisable()
		{
			// подъём по клипу прервали посреди — мышь снова управляет камерой
			if (_movement != null) _movement.LookInputBlocked = false;

			if (!_animating) return;
			_animating = false;
			_animation.End();
		}

		// captured at Start, before any crouch shrinking has happened, so this reflects the standing pose
		private float _feetOffset;

		// на сколько точка опоры (pivot) игрока ниже поверхности, на которой он стоит: низ капсулы выше pivot на _feetOffset,
		// но CharacterController стоит на Skin Width над поверхностью. Перевод "уровень пола → положение игрока" для точек
		// приземления — без учёта Skin Width игрок "приземлялся" на эти сантиметры в уступ (и высокий подъём шёл ниже края)
		private float StandingPivotOffset => _feetOffset - _controller.skinWidth;

		private bool _isClimbing;
		public bool IsClimbing => _isClimbing;

		// результат проверки: куда встанет transform, на какой высоте (transform.y) идёт горизонтальная
		// часть пути и что это — залезание или перелезание
		private struct LedgeMove
		{
			public Vector3 Target;
			public float PassHeight;
			public bool IsVault;
			// только для перелезания: точки на передней и задней грани препятствия и направление движения — по ним строится дуга
			public Vector3 FrontEdge;
			public Vector3 BackEdge;
			public Vector3 Direction;
			// нормаль передней грани (подъём) — куда развернуть игрока лицом к стене перед подъёмом по клипу
			public Vector3 WallNormal;
			// точка на передней грани (подъём) — по ней ладони из клипа совмещаются с реальным краем
			public Vector3 EdgePoint;
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
			_animation = GetComponent<IMantleAnimation>();

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
				StartCoroutine(VaultOver(move, crouchDrop));
			}
			else
			{
				// высокий уступ и есть клип с движением тела под эту высоту — подъём по клипу, иначе обычный
				Quaternion originalRotation = transform.rotation;
				if (TryFaceWall(ref move) && _animation != null &&
					_animation.TryBeginClimbMotion(move.Target.y - transform.position.y, out MantleClipMotion motion))
				{
					Quaternion facingWall = transform.rotation;
					transform.rotation = originalRotation; // разворот к стене — плавно, в ClimbWithClip
					StartCoroutine(ClimbWithClip(move.Target, move.EdgePoint, motion, facingWall));
				}
				else
				{
					transform.rotation = originalRotation;
					StartCoroutine(ClimbLedge(move.Target, move.PassHeight, false, ClimbSpeed, CameraTiltAmount, 0f));
				}
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

			float probeStep = Mathf.Max(ClimbProbeRadius, 0.05f);

			// 1) передняя грань препятствия (выше того, что и так перешагивает Step Offset): пробы вперёд снизу вверх
			// до MaxClimbHeight. Не одной пробой у ног — у балкона, навеса, стола под краем пусто, стена есть только
			// на высоте самой плиты: проба у ног прошла бы под ней, и залезть получалось бы только с прыжка
			RaycastHit wallHit = default;
			float wallHeight = -1f;
			for (float h = lowProbeHeight; h <= MaxClimbHeight + 0.001f; h += probeStep)
			{
				Vector3 wallProbeOrigin = transform.position;
				wallProbeOrigin.y = feetY + h;
				if (SphereCast(wallProbeOrigin, ClimbProbeRadius, moveDirection, ClimbCheckDistance, out wallHit))
				{
					wallHeight = h;
					break;
				}
			}
			if (wallHeight < 0f)
			{
				if (log) Debug.Log("Mantle: нет стены или края уступа впереди (шаг 1 — wall probe)");
				return false;
			}

			// 2) ищем САМЫЙ НИЖНИЙ свободный просвет над гранью: пробы вперёд снизу вверх с шагом в радиус пробы.
			// Не одной пробой на MaxClimbHeight — у окна над проёмом снова стена: проба на максимальной высоте
			// прошла бы над ней, и вместо подоконника нашёлся бы верх стены
			// на одну пробу выше MaxClimbHeight: край у самого верха диапазона (тонкая плита балкона) иначе не получил
			// бы ни одной пробы над собой. Высоту самого уступа это не расширяет — её ограничивает проверка в шаге 3
			float gapHeight = -1f;
			for (float h = wallHeight + probeStep; h <= MaxClimbHeight + probeStep + 0.001f; h += probeStep)
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
			if (!SphereCast(downProbeOrigin, ClimbProbeRadius, Vector3.down, gapHeight - wallHeight + 0.1f, out RaycastHit ledgeHit))
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
				FindVaultLanding(wallHit.point, moveDirection, feetY, ledgeHit.point.y, radius, log, out Vector3 vaultFeet, out Vector3 backEdge))
			{
				if (log) Debug.Log($"Vault: перелезть! высота {ledgeHeight:F2}м, приземление {vaultFeet}");
				move = new LedgeMove
				{
					Target = vaultFeet - new Vector3(0f, StandingPivotOffset, 0f),
					PassHeight = ledgeHit.point.y + VaultClearance - _feetOffset,
					IsVault = true,
					FrontEdge = wallHit.point,
					BackEdge = backEdge,
					Direction = moveDirection,
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

			Vector3 target = landingFeet - new Vector3(0f, StandingPivotOffset, 0f);
			move = new LedgeMove
			{
				Target = target,
				PassHeight = target.y + Mathf.Max(_controller.radius, 0.15f),
				IsVault = false,
				WallNormal = wallHit.normal,
				EdgePoint = wallHit.point,
			};
			return true;
		}

		// Перелезание: препятствие тонкое, за ним пол, игрок там помещается и над верхом есть где пролезть.
		// wallPoint — точка передней грани, topY — мировая высота верха препятствия
		private bool FindVaultLanding(Vector3 wallPoint, Vector3 direction, float feetY, float topY, float radius,
			bool log, out Vector3 landingFeet, out Vector3 backEdge)
		{
			landingFeet = default;
			backEdge = default;
			int mask = _movement != null ? _movement.CollisionMask : ~0;

			// V1) толщина: за VaultMaxDepth от передней грани, ниже верха, должно быть пусто. Высота пробы —
			// середина между точкой, где wall probe нашла грань, и верхом: там препятствие точно есть (у навеса
			// или балкона под гранью пусто, поэтому не от ног)
			float probeY = Mathf.Lerp(wallPoint.y, topY, 0.5f);
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
			// с учётом подъёма дуги (VaultOver): над препятствием ноги идут на VaultArcHeight выше минимума
			float passFeetY = topY + VaultClearance + Mathf.Max(0f, VaultArcHeight);
			float passHeight = VaultCrouched && _movement != null ? _movement.CrouchHeight : _controller.height;
			Vector3 passStart = new Vector3(transform.position.x, passFeetY, transform.position.z);
			Vector3 passEnd = new Vector3(landing.x, passFeetY, landing.z);
			if (!IsCapsulePathClear(passStart, passEnd, radius, passHeight, mask))
			{
				if (log) Debug.Log($"Vault: над препятствием не пролезть даже высотой {passHeight:F2}м (низкий проём / преграда) — mantle");
				return false;
			}

			landingFeet = landing;
			backEdge = backHit.point;
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
		private IEnumerator ClimbLedge(Vector3 targetPosition, float passHeight, bool isVault, float speed, float tiltAmount, float crouchDrop)
		{
			_isClimbing = true;
			_controller.enabled = false;

			// go up first, then over, then settle down onto the ledge (or the floor behind the obstacle),
			// instead of cutting the corner diagonally through the wall
			Vector3 startPosition = transform.position;
			float raisedY = Mathf.Max(passHeight, startPosition.y);
			Vector3 raisedAtStart = new Vector3(startPosition.x, raisedY, startPosition.z);
			Vector3 raisedAtTarget = new Vector3(targetPosition.x, raisedY, targetPosition.z);

			float upDuration = Vector3.Distance(startPosition, raisedAtStart) / speed;
			float overDuration = Vector3.Distance(raisedAtStart, raisedAtTarget) / speed;
			float settleDuration = Vector3.Distance(raisedAtTarget, targetPosition) / speed;

			// анимация рук (если есть) на движение не влияет — просто играет поверх
			bool animated = _animation != null && _animation.TryBegin(isVault);
			_animating = animated;
			if (animated && _animation.DrivesCamera) tiltAmount = 0f;

			float totalDuration = Mathf.Max(upDuration + overDuration + settleDuration, 0.01f);
			Coroutine tiltRoutine = StartCoroutine(TrackCameraTilt(totalDuration, tiltAmount));

			// пригибание привязано к фазам, а не к общему времени: к началу прохода над препятствием камера
			// гарантированно уже внизу, как бы ни соотносились длины подъёма и прохода
			yield return MoveTo(startPosition, raisedAtStart, upDuration,      // straight up, clear of the wall — пригибаемся
				p => SetCrouchDrop(crouchDrop * Mathf.SmoothStep(0f, 1f, p)));
			yield return MoveTo(raisedAtStart, raisedAtTarget, overDuration,   // straight over, above the ledge edge — пригнувшись
				_ => SetCrouchDrop(crouchDrop));
			yield return MoveTo(raisedAtTarget, targetPosition, settleDuration, // settle down — выпрямляемся
				p => SetCrouchDrop(crouchDrop * (1f - Mathf.SmoothStep(0f, 1f, p))));

			if (animated) _animation.End();
			_animating = false;
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

		// Перелезание — одна плавная дуга, а не "вверх → вперёд → вниз" как у подъёма на уступ:
		//   A  — старт;
		//   B  — над передней гранью (за радиус капсулы до неё, чтобы не задеть угол), на высоте прохода + VaultArcHeight;
		//   B′ — над задней гранью (за радиус капсулы после неё), на той же высоте;
		//   C  — приземление.
		// Через точки идёт гладкая кривая (Эрмит) — направление движения меняется без изломов. В B и B′ касательная
		// горизонтальная: подъём A→B плавно выходит в горизонталь, над препятствием игрок идёт РОВНО на высоте прохода
		// (не выше — иначе при перелезании в окно голова задела бы верх проёма, который проверен на этой высоте
		// в FindVaultLanding), спуск B′→C плавно уходит вниз. До B игрок дальше от грани, чем радиус капсулы, —
		// угол не задевает. Скорость постоянная вдоль пути (время отрезков — пропорционально их длине)
		private IEnumerator VaultOver(LedgeMove move, float crouchDrop)
		{
			_isClimbing = true;
			_controller.enabled = false;

			Vector3 direction = move.Direction;
			float entrySpeed = _movement != null ? Mathf.Max(0f, Vector3.Dot(_movement.HorizontalVelocity, direction)) : 0f;
			float speed = Mathf.Max(VaultKeepMomentum ? Mathf.Max(VaultSpeed, entrySpeed) : VaultSpeed, 0.01f);

			float clearance = _controller.radius + _controller.skinWidth;
			float arcY = move.PassHeight + VaultArcHeight;
			Vector3 a = transform.position;
			Vector3 b = new Vector3(move.FrontEdge.x, arcY, move.FrontEdge.z) - direction * clearance;
			Vector3 b2 = new Vector3(move.BackEdge.x, arcY, move.BackEdge.z) + direction * clearance;
			Vector3 c = move.Target;

			// игрок уже вплотную к препятствию — B не должна оказаться позади него: тогда просто поднимаемся на месте
			if (Vector3.Dot(b - a, direction) < 0f) b = new Vector3(a.x, arcY, a.z);
			// препятствие тоньше, чем две "мёртвые зоны" капсулы, — B′ не должна быть перед B
			if (Vector3.Dot(b2 - b, direction) < 0f) b2 = b;

			// направления касательных: в крайних точках — вдоль первого/последнего отрезка, над препятствием — горизонтально
			Vector3 tangentA = SafeDirection(b - a, Vector3.up);
			Vector3 tangentB = direction;
			Vector3 tangentB2 = direction;
			Vector3 tangentC = SafeDirection(c - b2, Vector3.down);

			float lengthAB = Vector3.Distance(a, b);
			float lengthBB = Vector3.Distance(b, b2);
			float lengthBC = Vector3.Distance(b2, c);
			float totalLength = lengthAB + lengthBB + lengthBC;
			float totalDuration = Mathf.Max(totalLength / speed, 0.01f);

			bool animated = _animation != null && _animation.TryBegin(true);
			_animating = animated;
			float tiltAmount = animated && _animation.DrivesCamera ? 0f : VaultTiltAmount;
			Coroutine tiltRoutine = StartCoroutine(TrackCameraTilt(totalDuration, tiltAmount));

			// пригибание (окно): опускаемся на подъёме к B, держим над препятствием, выпрямляемся на спуске
			yield return FollowSegment(a, b, tangentA, tangentB, lengthAB, speed,
				p => SetCrouchDrop(crouchDrop * Mathf.SmoothStep(0f, 1f, p)));
			yield return FollowSegment(b, b2, tangentB, tangentB2, lengthBB, speed,
				_ => SetCrouchDrop(crouchDrop));
			yield return FollowSegment(b2, c, tangentB2, tangentC, lengthBC, speed,
				p => SetCrouchDrop(crouchDrop * (1f - Mathf.SmoothStep(0f, 1f, p))));

			if (animated) _animation.End();
			_animating = false;
			StopCoroutine(tiltRoutine);
			if (_movement != null)
			{
				_movement.ExtraPitchOffset = 0f;
				_movement.ExtraCameraDrop = 0f;
			}

			transform.position = c;
			if (_movement != null)
			{
				_movement.ClearVerticalVelocity();
				// бег продолжается с той же скоростью, что была до перелезания; без сохранения — с места
				_movement.SetHorizontalVelocity(VaultKeepMomentum ? direction * entrySpeed : Vector3.zero);
			}
			_controller.enabled = true;
			_isClimbing = false;
		}

		// Высокий подъём: тело идёт ровно по корню из клипа (прыжок → вис → подтягивание, как в Blender), а не по скорости.
		// Подгонка под реальный уступ (motion warping) — отдельно по высоте и по движению вперёд:
		//  • до MantleGrab (прыжок) движение корня масштабируется так, чтобы в кадре захвата смещение до реального уступа
		//    было таким же, как в клипе, — ладони ложатся на край при любой высоте в диапазоне клипа;
		//  • после захвата (вис и подтягивание) — как в клипе, просто сдвинуто: относительно края всё то же.
		// Привязка: по высоте — верх уступа (корень в последнем кадре клипа стоит на уступе), вперёд — КРАЙ: ладони в кадре
		// захвата (в клипе они лежат на крае заготовки) совмещаются с реальной передней гранью. Поэтому и приземление —
		// как в Blender относительно края; место для игрока там проверяется заранее. Если ладони найти не удалось или
		// места нет — вперёд привязываемся к точке приземления из проверок FindLedge
		//
		// Взгляд — как в синхронных анимациях: клип строился с камерой, смотрящей прямо на стену, а руки привязаны к камере.
		// Поэтому в начале игрок плавно (ClimbAlignDuration) разворачивается лицом к стене и наклон камеры уходит в "прямо",
		// а на всё время подъёма мышь камеру не крутит — поворот камеры целиком из клипа (MantleArmsAnimation)
		private IEnumerator ClimbWithClip(Vector3 targetPosition, Vector3 edgePoint, MantleClipMotion motion, Quaternion facingWall)
		{
			_isClimbing = true;
			_animating = true;
			_controller.enabled = false;

			Quaternion startRotation = transform.rotation;
			float startPitch = _movement != null ? _movement.CameraPitch : 0f;
			if (_movement != null) _movement.LookInputBlocked = true;

			// движение — сразу в направлении "лицом к стене", а не по тому, куда игрок смотрел
			Vector3 start = transform.position;
			Quaternion yaw = Quaternion.Euler(0f, facingWall.eulerAngles.y, 0f);
			Vector3 targetLocal = Quaternion.Inverse(yaw) * (targetPosition - start);

			// на сколько реальный уступ выше/дальше, чем в клипе
			float heightShift = targetLocal.y - motion.RootAtEnd.y;
			float forwardShift = targetLocal.z - motion.RootAtEnd.z;

			// вперёд — по краю: ладони из клипа на реальную переднюю грань, если в получившейся точке приземления есть место
			if (!float.IsNaN(motion.EdgeForwardAtGrab))
			{
				float edgeForward = Vector3.Dot(edgePoint - start, yaw * Vector3.forward);
				float edgeShift = edgeForward - motion.EdgeForwardAtGrab;
				Vector3 edgeAlignedEnd = start + yaw * new Vector3(0f, motion.RootAtEnd.y + heightShift, motion.RootAtEnd.z + edgeShift);
				Vector3 endFeet = edgeAlignedEnd + new Vector3(0f, StandingPivotOffset, 0f);
				// там и место для игрока, и опора под ногами (узкий уступ мог закончиться раньше, чем в клипе)
				bool hasGround = Physics.Raycast(endFeet + Vector3.up * 0.3f, Vector3.down, 0.5f,
					_movement != null ? _movement.CollisionMask : ~0, QueryTriggerInteraction.Ignore);
				if (_movement != null && hasGround && _movement.HasHeadroomAt(endFeet))
				{
					forwardShift = edgeShift;
					targetPosition = edgeAlignedEnd;
				}
			}

			float elapsed = 0f;
			while (elapsed < motion.Duration)
			{
				elapsed = Mathf.Min(elapsed + Time.deltaTime, motion.Duration);
				Vector3 root = _animation.SampleClimbRoot(elapsed);

				float y = WarpToLedge(root.y, motion.RootAtGrab.y, heightShift, elapsed, motion.GrabTime);
				float z = WarpToLedge(root.z, motion.RootAtGrab.z, forwardShift, elapsed, motion.GrabTime);
				transform.position = start + yaw * new Vector3(0f, y, z); // вбок не двигаем — уступ строго впереди

				// плавный разворот лицом к стене и наклон камеры в "прямо"
				float align = ClimbAlignDuration > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / ClimbAlignDuration)) : 1f;
				transform.rotation = Quaternion.Slerp(startRotation, facingWall, align);
				if (_movement != null) _movement.CameraPitch = Mathf.Lerp(startPitch, 0f, align);
				yield return null;
			}

			transform.rotation = facingWall;
			if (_movement != null) _movement.LookInputBlocked = false;
			_animation.End();
			_animating = false;

			transform.position = targetPosition;
			if (_movement != null) _movement.ClearVerticalVelocity();
			_controller.enabled = true;
			_isClimbing = false;
		}

		// Разворачивает игрока лицом к стене (по горизонтальной нормали передней грани) и заново проверяет уступ уже
		// в этом направлении — точка приземления и высота считаются перпендикулярно стене. false — стена не
		// вертикальная или в новом направлении уступ не проходит (тогда вызывающий возвращает поворот и лезет как обычно)
		private bool TryFaceWall(ref LedgeMove move)
		{
			Vector3 facing = -new Vector3(move.WallNormal.x, 0f, move.WallNormal.z);
			if (facing.sqrMagnitude < 0.01f) return false;

			transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
			if (!FindLedge(false, out LedgeMove aligned) || aligned.IsVault) return false;

			move = aligned;
			return true;
		}

		// одна ось подгонки: до захвата — масштаб так, чтобы в кадре захвата значение сдвинулось ровно на shift
		// (если в клипе по этой оси до захвата почти нет движения — сдвиг набирается равномерно по времени),
		// после захвата — значение из клипа + shift
		private static float WarpToLedge(float clipValue, float clipValueAtGrab, float shift, float time, float grabTime)
		{
			if (time >= grabTime || grabTime <= 0f) return clipValue + shift;

			if (Mathf.Abs(clipValueAtGrab) > 0.1f)
			{
				float scale = Mathf.Max(0f, (clipValueAtGrab + shift) / clipValueAtGrab);
				return clipValue * scale;
			}
			return clipValue + shift * (time / grabTime);
		}

		// отрезок дуги p0→p1: кубический Эрмит с касательными по направлениям dir0/dir1, масштабированными на длину
		// отрезка — без петель и выбросов при сильно разных длинах соседних отрезков
		private IEnumerator FollowSegment(Vector3 p0, Vector3 p1, Vector3 dir0, Vector3 dir1, float length, float speed,
			System.Action<float> onProgress)
		{
			float duration = length / speed;
			Vector3 m0 = dir0 * length;
			Vector3 m1 = dir1 * length;

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				transform.position = Hermite(p0, p1, m0, m1, t);
				onProgress?.Invoke(t);
				yield return null;
			}

			transform.position = p1;
			onProgress?.Invoke(1f);
		}

		private static Vector3 Hermite(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t)
		{
			float t2 = t * t;
			float t3 = t2 * t;
			return (2f * t3 - 3f * t2 + 1f) * p0
				+ (t3 - 2f * t2 + t) * m0
				+ (-2f * t3 + 3f * t2) * p1
				+ (t3 - t2) * m1;
		}

		private static Vector3 SafeDirection(Vector3 v, Vector3 fallback)
		{
			return v.sqrMagnitude > 0.000001f ? v.normalized : fallback;
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
		private IEnumerator MoveTo(Vector3 from, Vector3 to, float duration, System.Action<float> onProgress = null)
		{
			duration = Mathf.Max(duration, 0.01f);
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
