using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StarterAssets
{
	// Ledge climbing (mantle) as a standalone add-on to FirstPersonController.
	// FirstPersonController stays the single authority on *when* to attempt a mantle — it already owns
	// jump/crouch input arbitration, so it calls TryMantle() at the exact moment it decides "this was a
	// fresh jump press, not consumed by standing up from a crouch". This component owns *how* the
	// detection and the actual climb work, so the two don't get tangled into one big movement script.
	[RequireComponent(typeof(CharacterController))]
	public class LedgeMantle : MonoBehaviour
	{
		[Header("Ledge Climb")]
		[Tooltip("Maximum height, from the character's feet, of a ledge that can be climbed")]
		public float MaxClimbHeight = 1.2f;
		[Tooltip("How far in front of the character to look for a climbable ledge")]
		public float ClimbCheckDistance = 0.6f;
		[Tooltip("How fast the character moves up onto the ledge, in m/s")]
		public float ClimbSpeed = 4.0f;
		[Tooltip("Radius of the SphereCast probes used to detect a ledge. Thicker is more forgiving of angle/edge cases than a thin raycast")]
		public float ClimbProbeRadius = 0.2f;

		[Header("Camera Tilt")]
		[Tooltip("Наклон камеры (pitch) во время подъёма, по нормализованному времени climb'а 0..1")]
		public AnimationCurve CameraTiltCurve = new AnimationCurve(
			new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));
		[Tooltip("Амплитуда наклона, градусы")]
		public float CameraTiltAmount = 8f;

		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private FirstPersonController _movement;

		// captured at Start, before any crouch shrinking has happened, so this reflects the standing pose
		private float _feetOffset;

		private bool _isClimbing;
		public bool IsClimbing => _isClimbing;

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
				Debug.Log("Mantle: нет стены впереди (шаг 1 — wall probe)");
				return false;
			}

			// 2) is the wall short enough to climb (nothing blocking at max climb height)?
			Vector3 topProbeOrigin = transform.position;
			topProbeOrigin.y = feetY + MaxClimbHeight;
			if (SphereCast(topProbeOrigin, ClimbProbeRadius, moveDirection, ClimbCheckDistance, out _))
			{
				Debug.Log("Mantle: стена выше MaxClimbHeight (шаг 2 — top probe)");
				return false;
			}

			// 3) find the actual surface height of the ledge just past the wall face
			Vector3 downProbeOrigin = wallHit.point + moveDirection * (ClimbProbeRadius + _controller.skinWidth);
			downProbeOrigin.y = feetY + MaxClimbHeight;
			if (!SphereCast(downProbeOrigin, ClimbProbeRadius, Vector3.down, MaxClimbHeight - lowProbeHeight + 0.1f, out RaycastHit ledgeHit))
			{
				Debug.Log("Mantle: нет поверхности уступа сверху (шаг 3 — down probe)");
				return false;
			}

			float ledgeHeight = ledgeHit.point.y - feetY;
			if (ledgeHeight < _controller.stepOffset || ledgeHeight > MaxClimbHeight)
			{
				Debug.Log($"Mantle: высота уступа {ledgeHeight:F2}м вне диапазона [{_controller.stepOffset:F2}, {MaxClimbHeight:F2}]");
				return false;
			}

			// 4) make sure the character actually fits standing on top of the ledge
			Vector3 landingFeet = downProbeOrigin + moveDirection * radius;
			landingFeet.y = ledgeHit.point.y;
			if (_movement == null || !_movement.HasHeadroomAt(landingFeet))
			{
				Debug.Log("Mantle: не хватает места стоять на уступе (шаг 4 — headroom)");
				return false;
			}

			Debug.Log($"Mantle: залезть! высота уступа {ledgeHeight:F2}м, точка приземления {landingFeet}");

			StartCoroutine(ClimbLedge(landingFeet - new Vector3(0f, _feetOffset, 0f)));
			return true;
		}

		// sweeps a sphere instead of a thin ray — much more forgiving of approach angle, thin colliders
		// and edge cases than a single-point raycast. Casts from the character's own centre and uses
		// SphereCastAll to skip past the self-hit, rather than offsetting the origin forward first —
		// an earlier version offset the origin by (own radius + probe radius), which could overshoot
		// past thin walls entirely and never register a hit no matter the distance
		private bool SphereCast(Vector3 origin, float probeRadius, Vector3 direction, float maxDistance, out RaycastHit hit)
		{
			RaycastHit[] hits = Physics.SphereCastAll(origin, probeRadius, direction, maxDistance, ~0, QueryTriggerInteraction.Ignore);

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

		private IEnumerator ClimbLedge(Vector3 targetPosition)
		{
			_isClimbing = true;
			_controller.enabled = false;

			// go up first, then over, then settle down onto the ledge, instead of cutting the corner
			// diagonally through the wall
			float clearance = Mathf.Max(_controller.radius, 0.15f);
			Vector3 startPosition = transform.position;
			Vector3 raisedAtStart = new Vector3(startPosition.x, targetPosition.y + clearance, startPosition.z);
			Vector3 raisedAtTarget = new Vector3(targetPosition.x, targetPosition.y + clearance, targetPosition.z);

			float totalDistance = Vector3.Distance(startPosition, raisedAtStart)
				+ Vector3.Distance(raisedAtStart, raisedAtTarget)
				+ Vector3.Distance(raisedAtTarget, targetPosition);
			float totalDuration = Mathf.Max(totalDistance / ClimbSpeed, 0.01f);
			Coroutine tiltRoutine = StartCoroutine(TrackCameraTilt(totalDuration));

			yield return MoveTo(startPosition, raisedAtStart);   // straight up, clear of the wall
			yield return MoveTo(raisedAtStart, raisedAtTarget);  // straight over, above the ledge edge
			yield return MoveTo(raisedAtTarget, targetPosition); // settle down onto the ledge

			StopCoroutine(tiltRoutine);
			if (_movement != null) _movement.ExtraPitchOffset = 0f; // don't leave the tilt stuck on

			transform.position = targetPosition;
			if (_movement != null) _movement.ClearVerticalVelocity();
			_controller.enabled = true;
			_isClimbing = false;
		}

		// drives FirstPersonController.ExtraPitchOffset over the whole climb, independent of which
		// of the three MoveTo() phases is currently running
		private IEnumerator TrackCameraTilt(float totalDuration)
		{
			if (_movement == null) yield break;

			float elapsed = 0f;
			while (elapsed < totalDuration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / totalDuration);
				_movement.ExtraPitchOffset = CameraTiltCurve.Evaluate(t) * CameraTiltAmount;
				yield return null;
			}
		}

		private IEnumerator MoveTo(Vector3 from, Vector3 to)
		{
			float duration = Mathf.Max(Vector3.Distance(from, to) / ClimbSpeed, 0.01f);
			float elapsed = 0f;

			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				transform.position = Vector3.Lerp(from, to, elapsed / duration);
				yield return null;
			}

			transform.position = to;
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
