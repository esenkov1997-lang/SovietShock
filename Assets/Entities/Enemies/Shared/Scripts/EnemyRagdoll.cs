using UnityEngine;

namespace Enemies
{
	// Ragdoll врага. Кости модели с Rigidbody + Collider + CharacterJoint (их создаёт Ragdoll Wizard)
	// при жизни работают как ХИТБОКСЫ: тела kinematic — их двигает анимация, физика на них не действует,
	// но пули и удары попадают именно по ним (а значит, по форме тела, а не по грубой капсуле).
	// При смерти Activate(): Animator выключается, тела становятся физическими — тело падает само,
	// сохраняя инерцию бега, а добивающий выстрел (WeaponController.ApplyHitForce) его отбрасывает.
	public class EnemyRagdoll : MonoBehaviour
	{
		[Tooltip("Animator модели — выключается при включении ragdoll. Если пусто — ищется среди дочерних объектов")]
		[SerializeField] private Animator animator;
		[Tooltip("Передать телу скорость, с которой враг двигался в момент смерти — бегущая собака падает по инерции, а не замирает")]
		[SerializeField] private bool inheritVelocity = true;

		private Rigidbody[] _bodies;

		public bool IsActive { get; private set; }

		private void Awake()
		{
			if (animator == null) animator = GetComponentInChildren<Animator>();
			_bodies = GetComponentsInChildren<Rigidbody>();

			if (_bodies.Length == 0)
				Debug.LogWarning($"{name}: EnemyRagdoll не нашёл Rigidbody на костях — создай ragdoll (Ragdoll Wizard)", this);

			SetPhysical(false);
		}

		public void Activate(Vector3 velocity)
		{
			if (IsActive) return;
			IsActive = true;

			if (animator != null) animator.enabled = false;
			SetPhysical(true);

			if (!inheritVelocity) return;
			foreach (Rigidbody body in _bodies)
				body.linearVelocity = velocity;
		}

		private void SetPhysical(bool physical)
		{
			foreach (Rigidbody body in _bodies)
			{
				body.isKinematic = !physical;
				// сглаживание и непрерывная коллизия нужны только падающему телу: при жизни кости двигает анимация
				body.interpolation = physical ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
				body.collisionDetectionMode = physical ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
			}
		}
	}
}
