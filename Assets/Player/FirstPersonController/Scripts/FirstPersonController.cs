using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;

		[Header("Movement Feel")]
		[Tooltip("How fast horizontal speed ramps up towards its target while grounded, in m/s^2")]
		public float Acceleration = 25.0f;
		[Tooltip("How fast horizontal speed ramps down towards its target (e.g. to a stop) while grounded, in m/s^2")]
		public float Deceleration = 30.0f;
		[Range(0f, 1f)]
		[Tooltip("Fraction of ground Acceleration/Deceleration applied while airborne. Keep this low so jumps carry momentum instead of being steerable like on the ground; 0 = no air control at all")]
		public float AirControl = 0.1f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Crouch")]
		[Tooltip("Move speed of the character while crouched, in m/s")]
		public float CrouchSpeed = 2.0f;
		[Tooltip("Height of the CharacterController while crouched, in m")]
		public float CrouchHeight = 1.0f;
		[Tooltip("How fast the character transitions between standing and crouching height")]
		public float CrouchTransitionSpeed = 8.0f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.4f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;
		[Tooltip("Max gap below the controller that gets snapped shut every frame while grounded (stairs, downhill slopes). Without this, gravity alone only pulls the controller down a tiny amount per frame, so walking off a stair tread leaves it briefly airborne until the next tread catches it — Grounded flickers false/true once per step, which then reads as a real landing to anything watching JustLanded (e.g. weapon-bob impact springs). Should be at least as tall as your steepest stair riser")]
		public float StepDownDistance = 0.3f;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		// persists across frames (rather than being derived fresh from input each frame) so that ground
		// acceleration/deceleration and reduced air control both work: it's the character's actual
		// carried momentum, not just "whatever the input currently says"
		private Vector3 _horizontalVelocity;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

		// crouch
		private bool _isCrouching;
		private bool _crouchButtonLatch;
		private bool _jumpButtonLatch;
		private float _standingHeight;
		private Vector3 _standingCenter;
		private float _standingCameraTargetY;
		private float _standingFeetOffset;

		// read by add-on components (e.g. LedgeMantle, CameraHeadBob) that need to know movement state
		// without owning or duplicating it themselves
		public bool IsCrouching => _isCrouching;
		public float CurrentSpeed => _horizontalVelocity.magnitude;
		// true only when sprint is actually in effect on movement speed — Move()'s targetSpeed ternary
		// already gives crouch priority over sprint, so this must exclude crouch too, or a consumer
		// (e.g. CameraHeadBob) would see "sprinting" while the character is actually moving at CrouchSpeed
		public bool IsSprinting => !_isCrouching && Grounded && _input.sprint && _input.move.y > 0.1f;
		// true for exactly the one Update() where Grounded flips from false to true, so a consumer
		// (e.g. a footstep/landing sound component) can react to touchdown without polling Grounded itself
		public bool JustLanded { get; private set; }
		// downward speed at the moment JustLanded became true, e.g. to scale a landing sound by fall height
		public float LandingSpeed { get; private set; }
		public void ClearVerticalVelocity() => _verticalVelocity = 0f;

		// additive camera offsets, applied on top of the mouse-look pitch and crouch height every frame —
		// lets add-on components (LedgeMantle's climb tilt, hand-animated weapon camera bones, etc.)
		// contribute to the camera without fighting CameraRotation() for ownership of the Transform
		public float ExtraPitchOffset;
		public Vector3 ExtraPositionOffset;
		public Quaternion ExtraRotationOffset = Quaternion.identity;

		// recoil uses these instead of Extra*Offset: a kick/drift is a *permanent* change to where the
		// player is aiming (same as normal mouse look), not a transient visual effect that resets itself —
		// so it reuses the exact same mechanism mouse look already uses (_cinemachineTargetPitch / transform.Rotate)
		public void AddPitchKick(float degrees) => _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch + degrees, BottomClamp, TopClamp);
		public void AddYaw(float degrees) => transform.Rotate(Vector3.up * degrees);

		// used by external scripts that take over the character for a while (e.g. Interactables.PlayerCameraFocus
		// moving the player to a lever) — after handing control back, mouse look must continue from the new
		// pitch, and the momentum carried from before the takeover must not push the character off its new spot
		public float CameraPitch
		{
			get => _cinemachineTargetPitch;
			set => _cinemachineTargetPitch = ClampAngle(value, BottomClamp, TopClamp);
		}
		public void StopMotion()
		{
			_horizontalVelocity = Vector3.zero;
			_verticalVelocity = 0f;
		}

		// base local position (X/Z from the original prefab setup, Y adjusted for crouch) — set at Start
		// and by ApplyCrouchHeight(), actually written to the Transform once, together, in CameraRotation()
		private Vector3 _cameraBaseLocalPosition;

		// optional add-on component — mantle lives entirely in its own script, see LedgeMantle.cs
		private LedgeMantle _ledgeMantle;

		// last wall normal CharacterController.Move hit this frame (floor/ceiling hits excluded) — used to
		// project next frame's move onto the wall plane so the player slides along it instead of sticking
		// when pressing diagonally into it
		private bool _touchingWall;
		private Vector3 _wallNormal;

#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;

			// remember the standing dimensions so crouch can lerp back to them
			_standingHeight = _controller.height;
			_standingCenter = _controller.center;
			_standingFeetOffset = _standingCenter.y - _standingHeight * 0.5f;
			if (CinemachineCameraTarget != null)
			{
				_cameraBaseLocalPosition = CinemachineCameraTarget.transform.localPosition;
				_standingCameraTargetY = _cameraBaseLocalPosition.y;
			}

			_ledgeMantle = GetComponent<LedgeMantle>(); // optional — fine if this component isn't present
		}

		private void Update()
		{
			// the ledge-climb coroutine drives transform.position directly, skip everything else while it runs
			if (_ledgeMantle != null && _ledgeMantle.IsClimbing) return;

			bool freshJumpPress = UpdateCrouchState();

			if (freshJumpPress && !_isCrouching && _ledgeMantle != null && _ledgeMantle.TryMantle())
			{
				_input.jump = false; // consumed by the climb, don't also jump this frame
				return;
			}

			JumpAndGravity();
			GroundedCheck();
			ApplyCrouchHeight();
			Move();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			bool wasGrounded = Grounded;
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

			// _verticalVelocity is still the real impact speed here: JumpAndGravity() (earlier this same
			// Update) only zeroes it once Grounded is already true, i.e. starting next frame — so this is
			// the one place that can capture the speed the character actually landed at
			JustLanded = Grounded && !wasGrounded;
			if (JustLanded) LandingSpeed = Mathf.Max(0f, -_verticalVelocity);
		}

		private void CameraRotation()
		{
			// пропускаем накопление только когда мышь реально не двигалась (ровно ноль), а не по маленькому
			// порогу — иначе медленное движение мышью частично "проглатывается" и камера будто подвисает
			if (_input.look != Vector2.zero)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}

			// applied every frame (not just when the mouse moves), so add-on effects like LedgeMantle's
			// camera tilt or a hand-animated weapon camera bone still show up even if the player isn't
			// currently moving the mouse. Position and rotation are each written exactly once, here,
			// combining every contributor — nothing else should touch CinemachineCameraTarget's transform
			CinemachineCameraTarget.transform.localPosition = _cameraBaseLocalPosition + ExtraPositionOffset;
			CinemachineCameraTarget.transform.localRotation =
				Quaternion.Euler(_cinemachineTargetPitch + ExtraPitchOffset, 0.0f, 0.0f) * ExtraRotationOffset;
		}

		// returns true if jump was freshly pressed this frame and wasn't consumed to stand up from a crouch
		private bool UpdateCrouchState()
		{
			// crouch is a toggle: press to crouch, press Crouch or Jump again to stand back up
			bool rawJumpHeld = _input.jump;
			bool crouchPressed = _input.crouch && !_crouchButtonLatch;
			bool jumpPressed = rawJumpHeld && !_jumpButtonLatch;

			_crouchButtonLatch = _input.crouch;
			_jumpButtonLatch = rawJumpHeld;

			if (crouchPressed)
			{
				if (_isCrouching)
				{
					// only stand up if there's room above, otherwise stay crouched
					if (CanStandUp()) _isCrouching = false;
				}
				else
				{
					_isCrouching = true;
				}
			}
			else if (_isCrouching && jumpPressed && CanStandUp())
			{
				// space while crouched only stands you up, it doesn't also jump this frame
				_isCrouching = false;
				_input.jump = false;
				jumpPressed = false;
			}

			return jumpPressed;
		}

		private bool CanStandUp()
		{
			float radius = Mathf.Max(_controller.radius - _controller.skinWidth, 0.01f);
			float feetY = transform.position.y + _standingFeetOffset;

			// sweep a capsule from the character's current height up to full standing height
			Vector3 currentTop = transform.position;
			currentTop.y = feetY + _controller.height - radius + _controller.skinWidth;

			Vector3 standingTop = transform.position;
			standingTop.y = feetY + _standingHeight - radius;

			// check every layer, but ignore the character's own CharacterController collider
			Collider[] hits = Physics.OverlapCapsule(currentTop, standingTop, radius, ~0, QueryTriggerInteraction.Ignore);
			foreach (Collider hit in hits)
			{
				if (hit != _controller) return false;
			}
			return true;
		}

		// full-body clearance check at an arbitrary feet position, ignoring the character's own collider.
		// public so add-on components (e.g. LedgeMantle checking a landing spot) can reuse it instead of
		// duplicating the same OverlapCapsule + self-exclusion logic
		public bool HasHeadroomAt(Vector3 feetPosition)
		{
			float radius = Mathf.Max(_controller.radius - _controller.skinWidth, 0.01f);

			Vector3 bottom = feetPosition;
			bottom.y += radius;
			Vector3 top = feetPosition;
			top.y += _standingHeight - radius;

			Collider[] hits = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
			foreach (Collider hit in hits)
			{
				if (hit != _controller) return false;
			}
			return true;
		}

		private void ApplyCrouchHeight()
		{
			float targetHeight = _isCrouching ? CrouchHeight : _standingHeight;
			float newHeight = Mathf.MoveTowards(_controller.height, targetHeight, CrouchTransitionSpeed * Time.deltaTime);

			_controller.height = newHeight;
			_controller.center = new Vector3(_standingCenter.x, _standingCenter.y - (_standingHeight - newHeight) * 0.5f, _standingCenter.z);

			// only updates the tracked base position — CameraRotation() is the single place that actually
			// writes to the Transform, combining this with ExtraPositionOffset
			_cameraBaseLocalPosition.y = _standingCameraTargetY - (_standingHeight - newHeight);
		}

		private void Move()
		{
			float targetSpeed = _isCrouching ? CrouchSpeed : (IsSprinting ? SprintSpeed : MoveSpeed);

			if (_input.move == Vector2.zero) targetSpeed = 0.0f;

			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// camera/body-relative wish direction from raw input
			Vector3 inputDirection = _input.move != Vector2.zero
				? (transform.right * _input.move.x + transform.forward * _input.move.y).normalized
				: Vector3.zero;

			Vector3 wishVelocity = inputDirection * (targetSpeed * inputMagnitude);

			// speeding up uses Acceleration, slowing down (or stopping) uses Deceleration; both are scaled
			// down by AirControl while airborne so jumps carry their ground momentum instead of being
			// freely steerable mid-air
			float rate = wishVelocity.sqrMagnitude > _horizontalVelocity.sqrMagnitude ? Acceleration : Deceleration;
			if (!Grounded) rate *= AirControl;

			_horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, wishVelocity, rate * Time.deltaTime);

			// sliding along the wall instead of stalling: only redirect when actually moving into it
			// (dot < 0), otherwise a wall grazed earlier would keep deflecting velocity away from it.
			// Projecting the persisted velocity itself (not just this frame's displacement) keeps next
			// frame's acceleration baseline consistent with the slide, instead of snapping back to full
			// speed into the wall.
			if (_touchingWall && Vector3.Dot(_horizontalVelocity, _wallNormal) < 0f)
			{
				_horizontalVelocity = Vector3.ProjectOnPlane(_horizontalVelocity, _wallNormal);
			}
			_touchingWall = false; // re-set by OnControllerColliderHit below if still against a wall this move

			_controller.Move(_horizontalVelocity * Time.deltaTime + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

			// Grounded here still reflects the check from earlier this Update (before this Move ran) — only
			// snap while we were already on the ground and aren't actively rising from a jump; a real fall
			// off a ledge should stay a real fall, not get glued back down
			if (Grounded && _verticalVelocity <= 0f) SnapToGround();
		}

		// Stair/slope descent assist (industry-standard "step down" snap — see StepDownDistance tooltip):
		// re-runs the same sphere check GroundedCheck() uses, but cast StepDownDistance further down. If it
		// finds ground just past where the sphere already sits, closes that exact gap in one extra Move()
		// call so the controller settles on the next tread the same frame it walked off the previous one,
		// instead of free-falling into it over the next frame or two.
		private void SnapToGround()
		{
			Vector3 origin = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);

			if (!Physics.SphereCast(origin, GroundedRadius, Vector3.down, out RaycastHit hit, StepDownDistance, GroundLayers, QueryTriggerInteraction.Ignore))
			{
				return; // nothing within the allowed step-down range — a genuine drop-off, let gravity take over
			}

			if (hit.distance > 0f) _controller.Move(Vector3.down * hit.distance);
		}

		private void OnControllerColliderHit(ControllerColliderHit hit)
		{
			// ignore near-horizontal surfaces (floor/ceiling) — only walls should affect sliding
			if (Mathf.Abs(hit.normal.y) < 0.1f)
			{
				_touchingWall = true;
				_wallNormal = hit.normal;
			}
		}

		private void JumpAndGravity()
		{
			if (Grounded)
			{
				// reset the fall timeout timer
				_fallTimeoutDelta = FallTimeout;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump (disabled while crouched)
				if (_input.jump && !_isCrouching && _jumpTimeoutDelta <= 0.0f)
				{
					// the square root of H * -2 * G = how much velocity needed to reach desired height
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// reset the jump timeout timer
				_jumpTimeoutDelta = JumpTimeout;

				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// if we are not grounded, do not jump
				_input.jump = false;
			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}