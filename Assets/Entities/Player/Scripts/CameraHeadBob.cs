using UnityEngine;

namespace StarterAssets
{
	// Procedural camera head-bob while walking/sprinting. Feeds FirstPersonController.ExtraPositionOffset
	// (the same additive slot LedgeMantle's climb tilt uses) instead of touching the camera transform
	// itself, so this composes with other add-on effects instead of fighting them for ownership.
	// Runs in Update() rather than LateUpdate() so the offset is guaranteed to be set before
	// FirstPersonController's own LateUpdate (CameraRotation) reads it later this same frame.
	[RequireComponent(typeof(FirstPersonController))]
	public class CameraHeadBob : MonoBehaviour
	{
		[Header("Bob Shape")]
		[Tooltip("Sideways sway over one bob cycle (0..1 = one full step-step cycle)")]
		public AnimationCurve BobX = new AnimationCurve(
			new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(0.5f, 0f), new Keyframe(0.75f, -1f), new Keyframe(1f, 0f));
		[Tooltip("Vertical bob over one bob cycle")]
		public AnimationCurve BobY = new AnimationCurve(
			new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

		[Header("Walk")]
		[Tooltip("Bob cycles per second while walking")]
		public float WalkBobSpeed = 1.8f;
		[Tooltip("Bob amplitude while walking, in meters")]
		public float WalkBobAmount = 0.04f;

		[Header("Sprint")]
		[Tooltip("Bob cycles per second while sprinting")]
		public float SprintBobSpeed = 2.6f;
		[Tooltip("Bob amplitude while sprinting, in meters")]
		public float SprintBobAmount = 0.07f;

		[Header("Blend")]
		[Tooltip("How fast the bob fades in/out when starting/stopping or leaving the ground")]
		public float BlendSpeed = 8f;
		[Tooltip("Minimum ground speed, in m/s, before the bob starts blending in")]
		public float MinSpeed = 0.2f;

		private FirstPersonController _movement;

		private float _phase;
		private float _blend;

		private void Start()
		{
			_movement = GetComponent<FirstPersonController>();
		}

		private void Update()
		{
			bool isMoving = _movement.Grounded && _movement.CurrentSpeed > MinSpeed;
			// IsSprinting (not raw _input.sprint) already excludes crouch — holding sprint while crouched
			// must still bob at crouch pace, since movement itself stays at CrouchSpeed
			bool sprinting = isMoving && _movement.IsSprinting;

			// scaled by how close actual speed is to the relevant target (MoveSpeed/SprintSpeed), so
			// crouch — which moves slower than MoveSpeed but isn't its own bob category — naturally bobs
			// slower and smaller instead of at full walk pace, and the accel/decel ramp is reflected too
			float referenceSpeed = sprinting ? _movement.SprintSpeed : _movement.MoveSpeed;
			float speedRatio = referenceSpeed > 0f ? Mathf.Clamp01(_movement.CurrentSpeed / referenceSpeed) : 0f;

			float bobSpeed = (sprinting ? SprintBobSpeed : WalkBobSpeed) * speedRatio;
			_phase += Time.deltaTime * bobSpeed;
			float cycle = Mathf.Repeat(_phase, 1f);

			_blend = Mathf.MoveTowards(_blend, isMoving ? 1f : 0f, Time.deltaTime * BlendSpeed);

			if (_blend <= 0f)
			{
				if (!isMoving) _phase = 0f; // start the next bob cleanly from neutral instead of mid-cycle
				_movement.ExtraPositionOffset = Vector3.zero;
				return;
			}

			float bobAmount = (sprinting ? SprintBobAmount : WalkBobAmount) * speedRatio;
			float x = BobX.Evaluate(cycle) * bobAmount * _blend;
			float y = BobY.Evaluate(cycle) * bobAmount * _blend;
			_movement.ExtraPositionOffset = new Vector3(x, y, 0f);
		}
	}
}
