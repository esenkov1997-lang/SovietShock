using UnityEngine;
using Weapons;

namespace Enemies
{
	// "Мозг" собаки — конечный автомат (FSM): в каждый момент собака находится ровно в одном состоянии,
	// у каждого состояния своё поведение в Update и свои условия перехода в другие.
	// Мозг ничего не видит и не слышит сам — он спрашивает EnemyPerception и командует EnemyMovement.
	//
	// Спокойные состояния (во что собака возвращается, когда всё тихо):
	//   Idle        — охраняет пост: стоит там, где её поставили на уровне, и смотрит туда же. Ушла — возвращается.
	//   Patrol      — если назначен Patrol Route: ходит по точкам маршрута.
	// Тревожные:
	//   Investigate — услышала шум или мельком видела игрока: идёт к месту, осматривается, успокаивается.
	//   Chase       — заметила игрока: бежит к нему. Потеряла из виду — бежит к последней известной позиции, оттуда Search.
	//   Search      — обыскивает местность вокруг места потери (первая точка — в сторону, куда бежал игрок),
	//                 на каждой точке осматривается. Время вышло — возвращается к спокойному состоянию.
	//   Attack      — игрок в зоне укуса: стоит, кусает (урон по Animation Event), после укуса — снова Chase.
	//   Dead        — Health дошло до нуля: всё выключается, тело становится ragdoll.
	// Поверх любого состояния — оглушение от попадания (EnemyHitReaction): пока играет вздрагивание, собака стоит,
	// атака прерывается. Любое попадание сразу поднимает тревогу — собака знает, где стрелок.
	[RequireComponent(typeof(EnemyMovement), typeof(EnemyPerception), typeof(EnemyAttack))]
	[RequireComponent(typeof(Health), typeof(EnemyRagdoll))]
	public class DogAI : MonoBehaviour
	{
		public enum State
		{
			Idle,
			Patrol,
			Investigate,
			Search,
			Chase,
			Attack,
			Dead
		}

		[Header("Спокойное поведение")]
		[Tooltip("Маршрут патруля из сцены. Пусто — собака охраняет пост (место и направление, где она стоит на уровне)")]
		[SerializeField] private PatrolRoute patrolRoute;
		[Tooltip("Походка при патруле и возвращении на пост")]
		[SerializeField] private EnemyGait patrolGait = EnemyGait.Walk;
		[Tooltip("Насколько далеко (м) от поста собака считает, что она уже на месте")]
		[SerializeField] private float postTolerance = 0.5f;

		[Header("Подозрение")]
		[Tooltip("Если собака видела игрока мельком (Awareness не дошла до 1) и он скрылся — при Awareness выше этого порога " +
			"она пойдёт проверить место, где его видела. Ниже — решит, что показалось")]
		[Range(0f, 1f)]
		[SerializeField] private float investigateGlimpseThreshold = 0.4f;

		[Header("Проверка шума (Investigate)")]
		[Tooltip("Походка, когда собака спокойно идёт проверить шум")]
		[SerializeField] private EnemyGait investigateGait = EnemyGait.Walk;
		[Tooltip("Сколько секунд собака осматривается на месте шума, прежде чем успокоиться")]
		[SerializeField] private float investigateLookTime = 3f;

		[Header("Погоня (Chase)")]
		[Tooltip("Запас (м) к Attack Range (из EnemyAttack), после которого собака снова срывается с места — убирает дёрганье на границе")]
		[SerializeField] private float resumeChaseBuffer = 0.5f;
		[Tooltip("Походка в погоне")]
		[SerializeField] private EnemyGait chaseGait = EnemyGait.Run;

		[Header("Поиск (Search)")]
		[Tooltip("Сколько секунд собака ищет игрока, прежде чем сдаться")]
		[SerializeField] private float searchDuration = 15f;
		[Tooltip("Радиус (м) вокруг места потери, в котором выбираются точки поиска")]
		[SerializeField] private float searchRadius = 8f;
		[Tooltip("Походка, когда собака спешит к месту, где потеряла игрока или услышала шум во время погони/поиска. " +
			"Бег выглядит естественнее: она ещё \"горячая\" после погони")]
		[SerializeField] private EnemyGait searchApproachGait = EnemyGait.Run;
		[Tooltip("Походка между точками поиска — собака уже на месте и обнюхивает окрестности")]
		[SerializeField] private EnemyGait searchGait = EnemyGait.Walk;
		[Tooltip("Сколько секунд осматриваться на каждой точке поиска: случайно между X и Y")]
		[SerializeField] private Vector2 searchPointLookTime = new Vector2(1.5f, 3f);

		[Header("Осмотр на месте")]
		[Tooltip("Поворачиваться по сторонам, пока осматривается. Выключи, если анимация осмотра сама крутит собаку")]
		[SerializeField] private bool turnWhileLooking = true;
		[Tooltip("Насколько далеко (градусы) влево/вправо от исходного направления собака оглядывается")]
		[SerializeField] private float lookAroundAngle = 100f;
		[Tooltip("Пауза (сек) между поворотами при осмотре: случайно между X и Y")]
		[SerializeField] private Vector2 lookAroundPause = new Vector2(0.5f, 1.2f);

		[Header("Отладка")]
		[Tooltip("Текущее состояние — только для просмотра в Play Mode")]
		[SerializeField] private State currentState = State.Idle;

		private EnemyMovement _movement;
		private EnemyPerception _perception;
		private EnemyAnimator _animator;
		private EnemyAttack _attack;
		private Health _health;
		private EnemyHitReaction _hitReaction;
		private EnemyRagdoll _ragdoll;

		private Vector3 _postPosition;
		private Vector3 _postForward;
		private bool _wasSeeingPlayer;
		private bool _isLookingAround;        // выставляется состоянием каждый кадр, в конце Update уходит в аниматор

		// Patrol
		private int _patrolIndex;
		private int _patrolDirection = 1;
		private float _patrolWaitTimer;       // < 0 — идём к точке, >= 0 — стоим на ней

		// Investigate
		private Vector3 _investigatePoint;
		private EnemyGait _currentInvestigateGait;
		private float _investigateTimer;      // < 0 — идём, >= 0 — осматриваемся на месте

		// Search
		private Vector3 _searchCenter;
		private Vector3 _searchPoint;
		private Vector3 _searchFirstDirection;
		private float _searchEndTime;
		private float _searchLookTimer;       // < 0 — идём к точке, >= 0 — осматриваемся
		private float _searchLookDuration;
		private EnemyGait _searchMoveGait;    // к месту потери/шума — бегом, между точками поиска — шагом

		// Chase
		private bool _holdingPosition;

		// Осмотр
		private Vector3 _lookBaseForward;
		private Vector3 _lookHeading;
		private float _lookPauseTimer;

		public State CurrentState => currentState;

		private void Awake()
		{
			_movement = GetComponent<EnemyMovement>();
			_perception = GetComponent<EnemyPerception>();
			_animator = GetComponent<EnemyAnimator>();
			_attack = GetComponent<EnemyAttack>();
			_health = GetComponent<Health>();
			_hitReaction = GetComponent<EnemyHitReaction>(); // необязательный — без него собака не вздрагивает
			_ragdoll = GetComponent<EnemyRagdoll>();

			// тело остаётся лежать — уничтожение объекта сломало бы ragdoll
			if (_health.DestroyOnDeath)
			{
				Debug.LogWarning($"{name}: у Health включён Destroy On Death — для ragdoll выключаю", this);
				_health.DestroyOnDeath = false;
			}
		}

		private void OnEnable()
		{
			_perception.NoiseHeard += HandleNoiseHeard;
			_health.OnDamaged.AddListener(HandleDamaged);
			_health.OnDeath.AddListener(Die);
		}

		private void OnDisable()
		{
			_perception.NoiseHeard -= HandleNoiseHeard;
			_health.OnDamaged.RemoveListener(HandleDamaged);
			_health.OnDeath.RemoveListener(Die);
		}

		private void Start()
		{
			// пост — там, куда собаку поставили на уровне
			_postPosition = transform.position;
			_postForward = transform.forward;
			EnterCalm();
		}

		private void Update()
		{
			_isLookingAround = false;

			// оглушение перекрывает любое состояние: стоим, атаку прерываем. Состояние при этом не меняется —
			// после вздрагивания собака продолжит то, что делала (обычно погоню: попадание поднимает тревогу)
			if (_hitReaction != null && _hitReaction.IsStaggered)
			{
				_attack.Cancel();
				_movement.Stop();
				if (_animator != null) _animator.SetSearching(false);
				return;
			}

			switch (currentState)
			{
				case State.Idle: UpdateIdle(); break;
				case State.Patrol: UpdatePatrol(); break;
				case State.Investigate: UpdateInvestigate(); break;
				case State.Search: UpdateSearch(); break;
				case State.Chase: UpdateChase(); break;
				case State.Attack: UpdateAttack(); break;
			}

			if (_animator != null) _animator.SetSearching(_isLookingAround);
		}

		// Общая реакция на зрение для всех не-боевых состояний. true — зрение "забрало управление" в этом кадре.
		private bool HandleSight()
		{
			if (_perception.IsAlerted)
			{
				EnterChase();
				return true;
			}

			// видит игрока, но ещё не уверена — останавливается и всматривается, пока Awareness растёт
			if (_perception.CanSeePlayer)
			{
				_wasSeeingPlayer = true;
				_movement.Stop();
				_movement.FaceTowards(_perception.Player.position);
				return true;
			}

			// игрок скрылся, не успев быть замеченным: если подозрение сильное — идём посмотреть
			if (_wasSeeingPlayer)
			{
				_wasSeeingPlayer = false;
				if (_perception.Awareness >= investigateGlimpseThreshold && _perception.HasLastKnownPosition)
				{
					EnterInvestigate(_perception.LastKnownPlayerPosition, investigateGait);
					return true;
				}

				// показалось — продолжаем то, что делали (путь заново построит MoveTo)
			}

			return false;
		}

		// ---------- Спокойные: Idle / Patrol ----------

		private void EnterCalm()
		{
			if (patrolRoute != null && patrolRoute.Count > 0) EnterPatrol();
			else EnterIdle();
		}

		private void EnterIdle()
		{
			currentState = State.Idle;
			_movement.Stop();
		}

		private void UpdateIdle()
		{
			if (HandleSight()) return;

			Vector3 toPost = _postPosition - transform.position;
			toPost.y = 0f;
			if (toPost.magnitude > postTolerance)
			{
				_movement.MoveTo(_postPosition, patrolGait);
				return;
			}

			_movement.Stop();
			_movement.FaceDirection(_postForward);
		}

		private void EnterPatrol()
		{
			currentState = State.Patrol;
			// продолжаем с ближайшей точки — после погони собака могла убежать далеко от маршрута
			_patrolIndex = patrolRoute.GetNearestIndex(transform.position);
			_patrolWaitTimer = -1f;
		}

		private void UpdatePatrol()
		{
			if (HandleSight()) return;

			Transform point = patrolRoute.GetPoint(_patrolIndex);

			if (_patrolWaitTimer < 0f)
			{
				_movement.MoveTo(point.position, patrolGait);
				if (_movement.HasArrived)
				{
					_movement.Stop();
					_patrolWaitTimer = patrolRoute.GetWaitTime(_patrolIndex);
				}
				return;
			}

			PatrolPoint settings = patrolRoute.GetSettings(_patrolIndex);
			if (settings != null)
			{
				if (settings.FaceDirection) _movement.FaceDirection(point.forward);
				if (settings.PlaySearchAnimation) _isLookingAround = true;
			}

			_patrolWaitTimer -= Time.deltaTime;
			if (_patrolWaitTimer <= 0f)
			{
				_patrolIndex = patrolRoute.GetNextIndex(_patrolIndex, ref _patrolDirection);
				_patrolWaitTimer = -1f;
			}
		}

		// ---------- Investigate ----------

		private void EnterInvestigate(Vector3 point, EnemyGait gait)
		{
			currentState = State.Investigate;
			_investigatePoint = point;
			_currentInvestigateGait = gait;
			_investigateTimer = -1f;
		}

		private void UpdateInvestigate()
		{
			if (HandleSight()) return;

			if (_investigateTimer < 0f)
			{
				_movement.MoveTo(_investigatePoint, _currentInvestigateGait);
				if (_movement.HasArrived)
				{
					_movement.Stop();
					_investigateTimer = 0f;
					BeginLookAround();
				}
				return;
			}

			UpdateLookAround();
			_investigateTimer += Time.deltaTime;
			if (_investigateTimer >= investigateLookTime)
			{
				_perception.ForgetPlayer();
				EnterCalm();
			}
		}

		// ---------- Search ----------

		// goToCenterFirst: true — сначала дойти до center (например, до свежего шума), false — уже на месте, сразу осматриваемся
		private void EnterSearch(Vector3 center, Vector3 firstDirection, bool goToCenterFirst)
		{
			currentState = State.Search;
			_searchCenter = center;
			_searchFirstDirection = firstDirection;
			_searchEndTime = Time.time + searchDuration;

			if (goToCenterFirst)
			{
				_searchPoint = center;
				_searchMoveGait = searchApproachGait;
				_searchLookTimer = -1f;
			}
			else
			{
				_movement.Stop();
				StartLookingAtSearchPoint();
			}
		}

		private void UpdateSearch()
		{
			if (HandleSight()) return;

			if (Time.time >= _searchEndTime)
			{
				_perception.ForgetPlayer();
				EnterCalm();
				return;
			}

			if (_searchLookTimer < 0f)
			{
				_movement.MoveTo(_searchPoint, _searchMoveGait);
				if (_movement.HasArrived)
				{
					_movement.Stop();
					StartLookingAtSearchPoint();
				}
				return;
			}

			UpdateLookAround();
			_searchLookTimer += Time.deltaTime;
			if (_searchLookTimer < _searchLookDuration) return;

			if (TryPickNextSearchPoint(out Vector3 next))
			{
				_searchPoint = next;
				_searchMoveGait = searchGait;
				_searchLookTimer = -1f;
			}
			else
			{
				StartLookingAtSearchPoint(); // точку не нашли (тупик, узкое место) — осматриваемся ещё раз
			}
		}

		private void StartLookingAtSearchPoint()
		{
			_searchLookTimer = 0f;
			_searchLookDuration = Random.Range(searchPointLookTime.x, searchPointLookTime.y);
			BeginLookAround();
		}

		private bool TryPickNextSearchPoint(out Vector3 point)
		{
			// первая точка — в ту сторону, куда бежал игрок: скорее всего, он там
			Vector3 direction = _searchFirstDirection;
			direction.y = 0f;
			_searchFirstDirection = Vector3.zero;

			if (direction.sqrMagnitude > 0.25f)
			{
				Vector3 ahead = _searchCenter + direction.normalized * searchRadius * 0.6f;
				if (_movement.TryFindReachablePoint(ahead, searchRadius * 0.3f, out point)) return true;
			}

			return _movement.TryFindReachablePoint(_searchCenter, searchRadius, out point);
		}

		// ---------- Chase ----------

		private void EnterChase()
		{
			currentState = State.Chase;
			_holdingPosition = false;
			_wasSeeingPlayer = false;
		}

		private void UpdateChase()
		{
			if (_perception.CanSeePlayer)
			{
				ChaseVisiblePlayer(_perception.Player.position);
				return;
			}

			// не видим — бежим туда, где видели последний раз (EnemyPerception ещё секунду "ведёт" игрока за углом)
			_holdingPosition = false;
			if (!_perception.HasLastKnownPosition)
			{
				EnterCalm();
				return;
			}

			_movement.MoveTo(_perception.LastKnownPlayerPosition, chaseGait);
			if (_movement.HasArrived)
				EnterSearch(_perception.LastKnownPlayerPosition, _perception.LastKnownPlayerVelocity, goToCenterFirst: false);
		}

		private void ChaseVisiblePlayer(Vector3 playerPosition)
		{
			float distance = HorizontalDistanceTo(playerPosition);
			float stopDistance = _attack.AttackRange;

			// подбежали — стоим, доворачиваемся и кусаем, как только атака готова (кулдаун прошёл, игрок перед мордой);
			// срываемся снова, только когда игрок отошёл с запасом
			if (_holdingPosition ? distance <= stopDistance + resumeChaseBuffer : distance <= stopDistance)
			{
				if (!_holdingPosition) _movement.Stop();
				_holdingPosition = true;
				_movement.FaceTowards(playerPosition);

				if (_attack.CanAttack(playerPosition)) EnterAttack();
			}
			else
			{
				_holdingPosition = false;
				_movement.MoveTo(playerPosition, chaseGait);
			}
		}

		// ---------- Attack ----------

		private void EnterAttack()
		{
			currentState = State.Attack;
			_movement.Stop();
			_attack.Begin();
		}

		private void UpdateAttack()
		{
			if (_attack.IsAttacking)
			{
				// до момента укуса доворачиваемся за игроком, после — нет: от укуса можно увернуться
				if (_attack.ShouldTrackTarget && _perception.Player != null)
					_movement.FaceTowards(_perception.Player.position);
				return;
			}

			// атака закончилась (событие AttackEnd) — снова погоня; если игрок рядом, она же решит кусать ещё раз
			EnterChase();
		}

		// ---------- Осмотр на месте ----------

		private void BeginLookAround()
		{
			_lookBaseForward = transform.forward;
			_lookHeading = _lookBaseForward;
			_lookPauseTimer = Random.Range(lookAroundPause.x, lookAroundPause.y);
		}

		// крутит головой-телом по сторонам вокруг исходного направления и включает анимацию осмотра
		private void UpdateLookAround()
		{
			_isLookingAround = true;
			if (!turnWhileLooking) return;

			if (!_movement.FaceDirection(_lookHeading)) return;

			_lookPauseTimer -= Time.deltaTime;
			if (_lookPauseTimer > 0f) return;

			_lookHeading = Quaternion.Euler(0f, Random.Range(-lookAroundAngle, lookAroundAngle), 0f) * _lookBaseForward;
			_lookPauseTimer = Random.Range(lookAroundPause.x, lookAroundPause.y);
		}

		// ---------- Урон и смерть ----------

		private void HandleDamaged(int currentHealth, int maxHealth)
		{
			if (currentHealth <= 0) return; // смерть обработает Die

			// попали — значит, игрок рядом и опасен: тревога сразу, даже если собака спала или смотрела в другую сторону
			_perception.AlertToPlayer();
		}

		private void Die()
		{
			currentState = State.Dead;

			// скорость берём до выключения агента — ragdoll продолжит движение по инерции
			Vector3 velocity = _movement.Agent.velocity;

			_attack.Cancel();
			_attack.enabled = false;
			_movement.DisableAgent();
			_perception.enabled = false;
			if (_hitReaction != null) _hitReaction.enabled = false;
			if (_animator != null) _animator.enabled = false;

			_ragdoll.Activate(velocity);
			enabled = false;
		}

		// ---------- Слух ----------

		private void HandleNoiseHeard(Vector3 position)
		{
			switch (currentState)
			{
				case State.Idle:
				case State.Patrol:
					EnterInvestigate(position, investigateGait);
					break;
				case State.Investigate:
					// новый шум важнее старого — идём к нему той же походкой
					EnterInvestigate(position, _currentInvestigateGait);
					break;
				case State.Search:
					// во время поиска шум — подсказка: переносим поиск туда, время поиска начинается заново
					EnterSearch(position, Vector3.zero, goToCenterFirst: true);
					break;
				case State.Chase:
					// пока видим игрока, шум не важен; потеряли — свежий шум точнее старой позиции
					if (!_perception.CanSeePlayer) EnterSearch(position, Vector3.zero, goToCenterFirst: true);
					break;
			}
		}

		// расстояние без учёта высоты: игрок на ступеньке выше не должен считаться "далеко"
		private float HorizontalDistanceTo(Vector3 point)
		{
			Vector3 offset = point - transform.position;
			offset.y = 0f;
			return offset.magnitude;
		}

		private void OnValidate()
		{
			searchPointLookTime.y = Mathf.Max(searchPointLookTime.x, searchPointLookTime.y);
			lookAroundPause.y = Mathf.Max(lookAroundPause.x, lookAroundPause.y);
		}

		// При выделенной собаке: зелёная стрелка — пост, жёлтая линия — куда идёт проверять,
		// оранжевый круг — зона поиска. Зону атаки рисует EnemyAttack.
		private void OnDrawGizmosSelected()
		{
			if (!Application.isPlaying) return;

			if (patrolRoute == null)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawWireSphere(_postPosition, postTolerance);
				Gizmos.DrawRay(_postPosition, _postForward);
			}

			if (currentState == State.Investigate)
			{
				Gizmos.color = Color.yellow;
				Gizmos.DrawLine(transform.position, _investigatePoint);
				Gizmos.DrawWireSphere(_investigatePoint, 0.3f);
			}
			else if (currentState == State.Search)
			{
				Gizmos.color = new Color(1f, 0.55f, 0f);
				Gizmos.DrawWireSphere(_searchCenter, searchRadius);
				Gizmos.DrawLine(transform.position, _searchPoint);
				Gizmos.DrawWireSphere(_searchPoint, 0.3f);
			}
		}
	}
}
