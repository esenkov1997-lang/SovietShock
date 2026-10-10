using UnityEngine;
using Weapons;

namespace Enemies
{
	// Реакция врага на попадание: анимация вздрагивания (триггер Hit) и короткое "оглушение" (IsStaggered),
	// пока она играет. Мозг на время оглушения стоит на месте и прерывает атаку.
	// Оглушение кончается по Animation Event "HitEnd" в клипе вздрагивания.
	//
	// Reaction Cooldown — защита от "станлока": иначе автоматная очередь держала бы собаку в вечном вздрагивании.
	// Урон в период кулдауна проходит, но без анимации и без прерывания.
	[RequireComponent(typeof(Health))]
	public class EnemyHitReaction : MonoBehaviour
	{
		[Tooltip("Имя trigger-параметра вздрагивания в Animator Controller")]
		[SerializeField] private string hitTrigger = "Hit";
		[Tooltip("После вздрагивания следующие попадания в течение этого времени (сек) не вызывают новую реакцию")]
		[SerializeField] private float reactionCooldown = 1.5f;
		[Tooltip("Подстраховка: если событие HitEnd не пришло за это время (сек) — оглушение снимается с предупреждением в консоли")]
		[SerializeField] private float maxStaggerDuration = 2f;

		private Health _health;
		private Animator _animator;
		private EnemyAnimationEvents _events;
		private int _hitTriggerHash;
		private bool _hasHitTrigger;
		private float _staggerStartTime;
		private float _nextReactionTime;

		public bool IsStaggered { get; private set; }

		private void Awake()
		{
			_health = GetComponent<Health>();
			_animator = GetComponentInChildren<Animator>();
			_events = GetComponentInChildren<EnemyAnimationEvents>();

			_hitTriggerHash = Animator.StringToHash(hitTrigger);
			if (_animator != null)
				foreach (AnimatorControllerParameter parameter in _animator.parameters)
					if (parameter.nameHash == _hitTriggerHash && parameter.type == AnimatorControllerParameterType.Trigger)
						_hasHitTrigger = true;

			if (!_hasHitTrigger)
				Debug.LogWarning($"{name}: в Animator Controller нет trigger-параметра \"{hitTrigger}\" — реакции на попадание не будет", this);
		}

		private void OnEnable()
		{
			_health.OnDamaged.AddListener(HandleDamaged);
			if (_events != null) _events.HitEndReceived += HandleHitEnd;
		}

		private void OnDisable()
		{
			_health.OnDamaged.RemoveListener(HandleDamaged);
			if (_events != null) _events.HitEndReceived -= HandleHitEnd;
			IsStaggered = false;
		}

		private void Update()
		{
			if (IsStaggered && Time.time - _staggerStartTime > maxStaggerDuration)
			{
				Debug.LogWarning($"{name}: вздрагивание не получило событие HitEnd за {maxStaggerDuration} сек — " +
					"проверь Animation Events на клипе Hit", this);
				HandleHitEnd();
			}
		}

		private void HandleDamaged(int currentHealth, int maxHealth)
		{
			if (!_hasHitTrigger || currentHealth <= 0) return; // смертельный урон — дальше ragdoll, не вздрагивание
			if (IsStaggered || Time.time < _nextReactionTime) return;

			IsStaggered = true;
			_staggerStartTime = Time.time;
			_animator.SetTrigger(_hitTriggerHash);
		}

		private void HandleHitEnd()
		{
			if (!IsStaggered) return;
			IsStaggered = false;
			_nextReactionTime = Time.time + reactionCooldown;
		}
	}
}
