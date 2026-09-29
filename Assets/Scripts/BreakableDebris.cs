using System.Collections;
using UnityEngine;

namespace Weapons
{
	// Обломок разрушаемого объекта. Держи выключенным в префабе и включай через Health.OnDeath → GameObject.SetActive(true).
	// При включении получает толчок в заданном направлении, а через время (если включено) замораживает Rigidbody, чтобы не нагружать физику.
	[RequireComponent(typeof(Rigidbody))]
	public class BreakableDebris : MonoBehaviour
	{
		[Header("Launch")]
		[Tooltip("Launch direction. Shown as a cyan arrow in the Scene view while the object is selected")]
		public Vector3 LaunchDirection = Vector3.forward;
		[Tooltip("On — direction is in the object's local axes (rotates with the prefab). Off — world axes")]
		public bool LocalSpace = true;
		[Tooltip("Launch impulse. Depends on Rigidbody mass: with Mass = 1, values of 2–5 give a noticeable launch")]
		public float LaunchForce = 3f;
		[Tooltip("Random deviation from the launch direction, in degrees. 0 — always exactly along the arrow")]
		[Range(0f, 90f)] public float SpreadAngle = 15f;
		[Tooltip("Random spin strength. 0 — no spin")]
		public float RandomTorque = 1f;

		[Header("Freeze")]
		[Tooltip("Make the Rigidbody kinematic after a delay (debris stops moving but keeps its collision). Turn off for debris that can be picked up and thrown")]
		public bool FreezeEnabled = true;
		[Tooltip("Seconds after launch before the Rigidbody is frozen")]
		[Min(0f)] public float FreezeDelay = 3f;

		private Rigidbody _rigidbody;
		private Coroutine _freezeRoutine;
		private bool _launched;

		private void Awake()
		{
			_rigidbody = GetComponent<Rigidbody>();
		}

		private void OnEnable()
		{
			if (_launched) return;
			_launched = true;

			_rigidbody.isKinematic = false;

			Vector3 direction = GetWorldDirection();
			if (SpreadAngle > 0f)
			{
				// Случайный поворот направления внутри конуса SpreadAngle
				Quaternion spread = Quaternion.AngleAxis(Random.Range(0f, SpreadAngle), Random.onUnitSphere);
				direction = spread * direction;
			}

			_rigidbody.AddForce(direction * LaunchForce, ForceMode.Impulse);
			if (RandomTorque > 0f) _rigidbody.AddTorque(Random.onUnitSphere * RandomTorque, ForceMode.Impulse);

			if (FreezeEnabled) _freezeRoutine = StartCoroutine(FreezeAfterDelay());
		}

		// Отменяет отложенную заморозку и возвращает обычную физику — пригодится системе подбора предметов
		public void CancelFreeze()
		{
			if (_freezeRoutine != null)
			{
				StopCoroutine(_freezeRoutine);
				_freezeRoutine = null;
			}
			_rigidbody.isKinematic = false;
		}

		private IEnumerator FreezeAfterDelay()
		{
			yield return new WaitForSeconds(FreezeDelay);
			_rigidbody.isKinematic = true;
			_freezeRoutine = null;
		}

		private Vector3 GetWorldDirection()
		{
			Vector3 direction = LocalSpace ? transform.TransformDirection(LaunchDirection) : LaunchDirection;
			return direction.sqrMagnitude > 0f ? direction.normalized : Vector3.zero;
		}

		private void OnDrawGizmosSelected()
		{
			Vector3 direction = GetWorldDirection();
			if (direction == Vector3.zero) return;

			Vector3 start = transform.position;
			Vector3 end = start + direction * Mathf.Max(0.5f, LaunchForce * 0.25f);
			Gizmos.color = Color.cyan;
			Gizmos.DrawLine(start, end);
			Gizmos.DrawWireSphere(end, 0.05f);
		}
	}
}
