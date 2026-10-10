using System.Collections.Generic;
using UnityEngine;
using Weapons;

namespace Enemies
{
	// Ближняя атака врага (укус, удар лапой). Мозг решает, КОГДА атаковать (CanAttack → Begin),
	// а этот компонент — КАК: запускает анимацию и наносит урон в нужный кадр по Animation Event.
	//
	// Цикл атаки:
	//   Begin()          → триггер Attack в аниматоре, враг доворачивается к цели (Track Target Until Hit)
	//   event AttackHit  → проверка сферой в Hit Point: всё с IDamageable внутри получает урон.
	//                      Урон считается в момент укуса, а не в начале — игрок может увернуться
	//   event AttackEnd  → атака закончена, мозг возвращается к погоне; дальше ждём Cooldown
	public class EnemyAttack : MonoBehaviour
	{
		private const int OverlapBufferSize = 16;

		[Header("Когда атаковать")]
		[Tooltip("С какого расстояния (м, от центра врага до игрока) начинается атака. На этом же расстоянии враг останавливается в погоне")]
		[SerializeField] private float attackRange = 1.4f;
		[Tooltip("Атака начинается, только если цель в пределах этого угла перед мордой (градусы, полный угол)")]
		[SerializeField] private float attackAngle = 40f;
		[Tooltip("Пауза (сек) между концом одной атаки и началом следующей")]
		[SerializeField] private float cooldown = 0.8f;

		[Header("Урон")]
		[Tooltip("Урон за один укус")]
		[SerializeField] private int damage = 15;
		[Tooltip("Точка, где проверяется попадание: пустой объект у пасти внутри кости головы/челюсти. Если пусто — перед врагом на высоте 0.5 м")]
		[SerializeField] private Transform hitPoint;
		[Tooltip("Радиус сферы попадания (м) вокруг Hit Point. Больше — проще попасть, сложнее увернуться")]
		[SerializeField] private float hitRadius = 0.6f;
		[Tooltip("Слои, по которым наносится урон")]
		[SerializeField] private LayerMask hitLayers = ~(1 << 2);

		[Header("Поведение во время атаки")]
		[Tooltip("До момента укуса враг доворачивается за целью. После — направление фиксировано, и от укуса можно отскочить в сторону")]
		[SerializeField] private bool trackTargetUntilHit = true;

		[Header("Анимация")]
		[Tooltip("Имя trigger-параметра атаки в Animator Controller")]
		[SerializeField] private string attackTrigger = "Attack";
		[Tooltip("Подстраховка: если событие AttackEnd не пришло за это время (сек) — атака завершается принудительно с предупреждением в консоли")]
		[SerializeField] private float maxAttackDuration = 3f;

		private readonly Collider[] _overlapBuffer = new Collider[OverlapBufferSize];
		private readonly HashSet<IDamageable> _damagedThisHit = new HashSet<IDamageable>();

		private Animator _animator;
		private EnemyAnimationEvents _events;
		private int _attackTriggerHash;
		private bool _hasAttackTrigger;

		private bool _hitDone;
		private float _attackStartTime;
		private float _nextAttackTime;

		public bool IsAttacking { get; private set; }
		public float AttackRange => attackRange;
		// до укуса враг ещё может доворачиваться за целью
		public bool ShouldTrackTarget => IsAttacking && trackTargetUntilHit && !_hitDone;

		private Vector3 HitPosition => hitPoint != null
			? hitPoint.position
			: transform.position + transform.forward * attackRange * 0.7f + Vector3.up * 0.5f;

		private void Awake()
		{
			_animator = GetComponentInChildren<Animator>();
			_events = GetComponentInChildren<EnemyAnimationEvents>();

			if (_events == null)
				Debug.LogWarning($"{name}: нет EnemyAnimationEvents на модели — укусы не будут наносить урон. " +
					"Добавь компонент на объект с Animator", this);

			_attackTriggerHash = Animator.StringToHash(attackTrigger);
			if (_animator != null)
				foreach (AnimatorControllerParameter parameter in _animator.parameters)
					if (parameter.nameHash == _attackTriggerHash && parameter.type == AnimatorControllerParameterType.Trigger)
						_hasAttackTrigger = true;

			if (!_hasAttackTrigger)
				Debug.LogWarning($"{name}: в Animator Controller нет trigger-параметра \"{attackTrigger}\" — анимация атаки не запустится", this);
		}

		private void OnEnable()
		{
			if (_events == null) return;
			_events.AttackHitReceived += HandleAttackHit;
			_events.AttackEndReceived += HandleAttackEnd;
		}

		private void OnDisable()
		{
			if (_events == null) return;
			_events.AttackHitReceived -= HandleAttackHit;
			_events.AttackEndReceived -= HandleAttackEnd;
		}

		private void Update()
		{
			if (IsAttacking && Time.time - _attackStartTime > maxAttackDuration)
			{
				Debug.LogWarning($"{name}: атака не получила событие AttackEnd за {maxAttackDuration} сек — " +
					"проверь Animation Events на клипе атаки", this);
				Finish();
			}
		}

		// цель в зоне, перед мордой и атака перезарядилась
		public bool CanAttack(Vector3 targetPosition)
		{
			if (IsAttacking || Time.time < _nextAttackTime) return false;

			Vector3 toTarget = targetPosition - transform.position;
			toTarget.y = 0f;
			if (toTarget.magnitude > attackRange) return false;

			Vector3 forward = transform.forward;
			forward.y = 0f;
			return Vector3.Angle(forward, toTarget) <= attackAngle * 0.5f;
		}

		public void Begin()
		{
			IsAttacking = true;
			_hitDone = false;
			_attackStartTime = Time.time;
			if (_hasAttackTrigger) _animator.SetTrigger(_attackTriggerHash);
		}

		// прервать атаку снаружи (например, враг получил урон или умер)
		public void Cancel()
		{
			if (!IsAttacking) return;
			if (_hasAttackTrigger) _animator.ResetTrigger(_attackTriggerHash);
			Finish();
		}

		private void HandleAttackHit()
		{
			if (!IsAttacking || _hitDone) return;
			_hitDone = true;

			// у одной цели может быть несколько коллайдеров — урон каждой цели только один раз за укус
			_damagedThisHit.Clear();
			int count = Physics.OverlapSphereNonAlloc(HitPosition, hitRadius, _overlapBuffer, hitLayers, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				Collider hit = _overlapBuffer[i];
				if (hit.transform.IsChildOf(transform)) continue; // себя не кусаем

				IDamageable damageable = hit.GetComponentInParent<IDamageable>();
				if (damageable == null || !_damagedThisHit.Add(damageable)) continue;

				damageable.TakeDamage(damage);
			}
		}

		private void HandleAttackEnd()
		{
			if (IsAttacking) Finish();
		}

		private void Finish()
		{
			IsAttacking = false;
			_nextAttackTime = Time.time + cooldown;
		}

		// при выделенном враге: оранжевая сфера — зона укуса, красная дуга — откуда начинается атака
		private void OnDrawGizmosSelected()
		{
			Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
			Gizmos.DrawWireSphere(HitPosition, hitRadius);

#if UNITY_EDITOR
			Vector3 forward = transform.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude < 0.0001f) return;
			UnityEditor.Handles.color = Color.red;
			Vector3 from = Quaternion.Euler(0f, -attackAngle * 0.5f, 0f) * forward.normalized;
			UnityEditor.Handles.DrawWireArc(transform.position, Vector3.up, from, attackAngle, attackRange);
			UnityEditor.Handles.DrawLine(transform.position, transform.position + from * attackRange);
			UnityEditor.Handles.DrawLine(transform.position,
				transform.position + Quaternion.Euler(0f, attackAngle * 0.5f, 0f) * forward.normalized * attackRange);
#endif
		}
	}
}
