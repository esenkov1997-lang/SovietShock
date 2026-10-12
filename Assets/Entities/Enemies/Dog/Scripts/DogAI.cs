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
	//   Retreat     — здоровье упало ниже порога: убегает в укрытие (CoverFinder) или просто подальше, выжидает и снова
	//                 нападает. Укрытие раскрыто — меняет его или нападает.
	//   Grab        — синхронная атака (EnemySyncedAttack): вцепилась в руку игрока, обе анимации играют вместе.
	//                 Начинается вместо обычного укуса с шансом из ассета; по окончании — снова Chase.
	//   Unreachable — до игрока не дойти (забрался высоко): кружит под ним и смотрит вверх; попадание — прячется
	//                 в укрытие и потом снова выходит; игрок спустился — в атаку; долго недосягаем — теряет интерес.
	//                 Загнали в угол (игрок подошёл вплотную) — огрызается и дерётся.
	//   Dead       — Health дошло до нуля: всё выключается, тело становится ragdoll.
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
			Retreat,
			Unreachable,
			Grab,
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

		[Header("Отступление (Retreat)")]
		[Tooltip("Когда здоровье опускается до этой доли (0.3 = 30%), собака отбегает. 0 — никогда не отступает")]
		[Range(0f, 1f)]
		[SerializeField] private float retreatHealthFraction = 0.3f;
		[Tooltip("Сколько раз за жизнь собака может отступить. После этого дерётся до конца")]
		[SerializeField] private int maxRetreats = 1;
		[Tooltip("На какое расстояние (м) от игрока собака старается убежать, если укрытие не найдено (или нет CoverFinder)")]
		[SerializeField] private float retreatDistance = 10f;
		[Tooltip("Сколько секунд собака выжидает на месте отступления (следя за игроком), прежде чем снова напасть")]
		[SerializeField] private float retreatRecoverTime = 4f;
		[Tooltip("Если игрок подошёл к отступающей собаке ближе этого (м) — она загнана в угол и сразу нападает")]
		[SerializeField] private float corneredDistance = 2.5f;
		[Tooltip("Подстраховка: если за это время (сек) собака так и не добежала до точки (застряла) — считаем, что добежала")]
		[SerializeField] private float maxRetreatRunTime = 6f;
		[Tooltip("С CoverFinder: как часто (сек) проверять, не видит ли игрок собаку в укрытии")]
		[SerializeField] private float coverCheckInterval = 0.3f;
		[Tooltip("С CoverFinder: сколько раз собака может сменить раскрытое укрытие, прежде чем перестать прятаться и напасть")]
		[SerializeField] private int maxCoverChanges = 2;

		[Header("Недосягаемый игрок (Unreachable)")]
		[Tooltip("Как часто (сек) проверять, можно ли дойти до игрока (один расчёт пути)")]
		[SerializeField] private float reachCheckInterval = 0.3f;
		[Tooltip("Игрок достижим, если NavMesh есть не дальше этого (м) от него по горизонтали...")]
		[SerializeField] private float reachHorizontalTolerance = 1.5f;
		[Tooltip("...и не дальше этого (м) по высоте. Игрок на ящике выше — недосягаем")]
		[SerializeField] private float reachVerticalTolerance = 1f;
		[Tooltip("Сколько секунд игрок должен оставаться недосягаемым, прежде чем собака это \"поймёт\" — прыжок на ящик и обратно не считается")]
		[SerializeField] private float unreachableConfirmTime = 1f;
		[Tooltip("Сколько секунд игрок должен быть снова достижим, чтобы собака бросилась в атаку")]
		[SerializeField] private float reachableConfirmTime = 0.3f;
		[Tooltip("Радиус (м), по которому собака кружит под недосягаемым игроком")]
		[SerializeField] private float paceRadius = 3.5f;
		[Tooltip("Насколько далеко (градусы) в сторону собака смещается по кругу за один переход")]
		[SerializeField] private float paceArc = 70f;
		[Tooltip("Сколько секунд стоять и смотреть на игрока между переходами: случайно между X и Y")]
		[SerializeField] private Vector2 paceWaitTime = new Vector2(1f, 2.5f);
		[Tooltip("Получив попадание от недосягаемого игрока, собака прячется (нужен CoverFinder) на столько секунд, потом снова выходит кружить")]
		[SerializeField] private float hideTime = 5f;
		[Tooltip("Через столько секунд недосягаемости собака теряет интерес и уходит на пост/патруль. Пока игрок недосягаем, " +
			"она будет только смотреть на него. Попадание или спуск игрока — снова в бой. 0 — никогда не сдаётся")]
		[SerializeField] private float loseInterestTime = 30f;

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
		[Tooltip("Скорость спокойных поворотов на месте (град/с): осмотр, разворот на посту, точки патруля. " +
			"Большие углы при наличии EnemyTurnInPlace поворачиваются клипом, этой скоростью доводится остаток. " +
			"Повороты в бою (к игроку, перед укусом) берут быструю Turn In Place Speed из EnemyMovement")]
		[SerializeField] private float lookAroundTurnSpeed = 90f;

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
		private CoverFinder _coverFinder;
		private EnemySyncedAttack _syncedAttack;

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

		// Retreat
		private Vector3 _retreatPoint;
		private float _retreatStartTime;
		private float _retreatRecoverTimer;   // < 0 — бежим к точке, >= 0 — выжидаем на месте
		private int _retreatsUsed;
		private bool _retreatInCover;         // бежим в укрытие (а не просто подальше) — тогда следим, не раскрыто ли оно
		private int _coverChanges;
		private float _nextCoverCheckTime;

		// Unreachable
		private bool _playerUnreachable;     // подтверждённо: дойти до игрока нельзя (с выдержкой Unreachable Confirm Time)
		private bool _rawReachable = true;    // результат последней проверки, без выдержки
		private float _rawReachableSince;
		private float _nextReachCheckTime;
		private bool _gaveUpOnUnreachable;    // потеряла интерес: пока игрок недосягаем, только смотрит на него, не гонится
		private float _unreachableStartTime;
		private bool _unreachableHiding;      // false — кружит под игроком, true — прячется в укрытии
		private Vector3 _pacePoint;
		private float _paceWaitTimer;         // < 0 — идём к точке, >= 0 — стоим и смотрим на игрока
		private Vector3 _hidePoint;
		private float _hideTimer;             // < 0 — бежим в укрытие, >= 0 — выжидаем в нём
		private float _hideRunStartTime;
		private bool _hideInCover;            // true — настоящее укрытие (следим, не раскрыто ли), false — просто подальше

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
			_coverFinder = GetComponent<CoverFinder>(); // необязательный — без него отступает просто подальше от игрока
			_syncedAttack = GetComponent<EnemySyncedAttack>(); // необязательный — без него только обычные укусы

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
			UpdateReachability();

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
				case State.Retreat: UpdateRetreat(); break;
				case State.Unreachable: UpdateUnreachable(); break;
				case State.Grab: UpdateGrab(); break;
			}

			if (_animator != null) _animator.SetSearching(_isLookingAround);
		}

		// Общая реакция на зрение для всех не-боевых состояний. true — зрение "забрало управление" в этом кадре.
		private bool HandleSight()
		{
			// сдалась на недосягаемом игроке — пока он там, не гонится, а только смотрит (ветка CanSeePlayer ниже)
			if (_perception.IsAlerted && !(_gaveUpOnUnreachable && _playerUnreachable))
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
			_movement.FaceDirection(_postForward, 5f, lookAroundTurnSpeed, animated: true);
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
				if (settings.FaceDirection) _movement.FaceDirection(point.forward, 5f, lookAroundTurnSpeed, animated: true);
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
			if (_playerUnreachable)
			{
				EnterUnreachable(hide: false);
				return;
			}

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

				// в момент укуса — с шансом из SyncedAttackData вместо него захват (вцепиться в руку)
				if (_attack.CanAttack(playerPosition))
				{
					if (_syncedAttack != null && _syncedAttack.TryStart(_perception.Player)) EnterGrab();
					else EnterAttack();
				}
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

		// ---------- Grab ----------

		// всё — стыковку, синхронный старт, урон, конец — ведёт EnemySyncedAttack; мозг просто ждёт
		private void EnterGrab()
		{
			currentState = State.Grab;
			_holdingPosition = false;
		}

		private void UpdateGrab()
		{
			if (_syncedAttack.IsActive) return;

			// клип доигран (отпустила, отскочила) — снова в бой: собака "знает", где игрок
			_perception.AlertToPlayer();
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

			if (!_movement.FaceDirection(_lookHeading, 5f, lookAroundTurnSpeed, animated: true)) return;

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

			_gaveUpOnUnreachable = false; // стреляют — значит, интерес вернулся

			// во время захвата урон не меняет поведение — синхронная анимация доигрывается
			if (currentState == State.Grab) return;

			if (ShouldRetreat(currentHealth, maxHealth))
			{
				EnterRetreat();
				if (currentState == State.Retreat) return;
			}

			// по собаке стреляют сверху, а достать стрелка нельзя — не стоять под огнём, а прятаться
			if (currentState != State.Retreat && IsPlayerUnreachableNow()) EnterUnreachable(hide: true);
		}

		// Попадание — сильный сигнал, ждать выдержку Unreachable Confirm Time тут незачем: если флаг ещё не
		// подтверждён (собака искала игрока, стояла на посту или погоня только началась), проверяем сразу.
		private bool IsPlayerUnreachableNow()
		{
			if (_playerUnreachable) return true;

			Transform player = _perception.Player;
			if (player == null) return false;
			if (_movement.CanReach(player.position, reachHorizontalTolerance, reachVerticalTolerance)) return false;

			// записываем как уже подтверждённое, чтобы UpdateReachability не начал выдержку заново
			_playerUnreachable = true;
			_rawReachable = false;
			_rawReachableSince = Time.time - unreachableConfirmTime;
			return true;
		}

		// ---------- Unreachable ----------

		// Раз в Reach Check Interval проверяем, можно ли дойти до игрока, и подтверждаем результат выдержкой
		// по времени. Проверка нужна только в бою (и пока собака "сдалась" — чтобы заметить, что игрок спустился).
		private void UpdateReachability()
		{
			bool needed = currentState == State.Chase || currentState == State.Attack ||
				currentState == State.Unreachable || _gaveUpOnUnreachable;
			if (!needed)
			{
				_playerUnreachable = false;
				_rawReachable = true;
				_rawReachableSince = Time.time;
				return;
			}

			if (Time.time < _nextReachCheckTime) return;
			_nextReachCheckTime = Time.time + reachCheckInterval;

			Transform player = _perception.Player;
			if (player == null) return;

			// видим — проверяем, где игрок сейчас; не видим — где видели последний раз
			Vector3 target = !_perception.CanSeePlayer && _perception.HasLastKnownPosition
				? _perception.LastKnownPlayerPosition
				: player.position;

			bool reachable = _movement.CanReach(target, reachHorizontalTolerance, reachVerticalTolerance);
			if (reachable != _rawReachable)
			{
				_rawReachable = reachable;
				_rawReachableSince = Time.time;
			}

			float heldFor = Time.time - _rawReachableSince;
			if (!_rawReachable && heldFor >= unreachableConfirmTime) _playerUnreachable = true;
			if (_rawReachable && heldFor >= reachableConfirmTime)
			{
				_playerUnreachable = false;
				_gaveUpOnUnreachable = false;
			}
		}

		private void EnterUnreachable(bool hide)
		{
			if (currentState != State.Unreachable) _unreachableStartTime = Time.time;
			currentState = State.Unreachable;
			_holdingPosition = false;

			if (hide && TryStartHiding()) return;
			StartPacing();
		}

		private void UpdateUnreachable()
		{
			Transform player = _perception.Player;
			if (player == null || _perception.IgnoresPlayer)
			{
				EnterCalm();
				return;
			}

			// спустился — в атаку
			if (!_playerUnreachable)
			{
				EnterChase();
				return;
			}

			if (loseInterestTime > 0f && Time.time - _unreachableStartTime >= loseInterestTime)
			{
				_gaveUpOnUnreachable = true;
				_perception.ForgetPlayer();
				EnterCalm();
				return;
			}

			if (_unreachableHiding) UpdateHiding(player);
			else UpdatePacing(player);
		}

		// Кружит под игроком: переходит между точками на кольце вокруг него и на каждой стоит, глядя вверх на игрока
		private void StartPacing()
		{
			_unreachableHiding = false;
			PickPacePoint();
			_paceWaitTimer = -1f;
		}

		private void UpdatePacing(Transform player)
		{
			if (_paceWaitTimer < 0f)
			{
				_movement.MoveTo(_pacePoint, EnemyGait.Run); // бой — всегда бегом, шаг только в спокойных состояниях
				if (_movement.HasArrived)
				{
					_movement.Stop();
					_paceWaitTimer = Random.Range(paceWaitTime.x, paceWaitTime.y);
				}
				return;
			}

			_movement.FaceTowards(GetKnownPlayerPosition(player));
			_paceWaitTimer -= Time.deltaTime;
			if (_paceWaitTimer > 0f) return;

			PickPacePoint();
			_paceWaitTimer = -1f;
		}

		private void PickPacePoint()
		{
			Transform player = _perception.Player;
			if (player == null)
			{
				_pacePoint = transform.position;
				return;
			}
			Vector3 playerPosition = GetKnownPlayerPosition(player);

			// центр кружения — ближайший к игроку достижимый пол (под ящиком, у подножия лестницы без пути наверх)
			if (!_movement.TryGetReachablePointNear(playerPosition, paceRadius + 2f, out Vector3 center))
				center = transform.position;

			// смещаемся по кругу от своего текущего места — собака обходит игрока, а не прыгает через центр
			Vector3 fromCenter = transform.position - center;
			fromCenter.y = 0f;
			fromCenter = fromCenter.sqrMagnitude > 0.01f ? fromCenter.normalized : -transform.forward;

			const int attempts = 6;
			for (int i = 0; i < attempts; i++)
			{
				Vector3 direction = Quaternion.Euler(0f, Random.Range(-paceArc, paceArc), 0f) * fromCenter;
				if (_movement.TryGetReachablePointNear(center + direction * paceRadius, 1.5f, out _pacePoint)) return;
			}
			_pacePoint = center;
		}

		// Прячется от выстрелов сверху: в укрытие, а если его нет (с высоты игрок видит всю площадку) или нет
		// CoverFinder — хотя бы подальше из-под огня. false — бежать вообще некуда
		private bool TryStartHiding()
		{
			Transform player = _perception.Player;
			if (player == null) return false;

			_hideInCover = _coverFinder != null && _coverFinder.TryFindCover(player, _perception.PlayerEyePosition, out _hidePoint);
			if (!_hideInCover && !_movement.TryFindPointAwayFrom(player.position, retreatDistance, out _hidePoint)) return false;

			_unreachableHiding = true;
			_hideTimer = -1f;
			_hideRunStartTime = Time.time;
			return true;
		}

		private void UpdateHiding(Transform player)
		{
			if (_hideTimer < 0f)
			{
				_movement.MoveTo(_hidePoint, EnemyGait.Run);
				if (_movement.HasArrived || Time.time - _hideRunStartTime > maxRetreatRunTime)
				{
					_movement.Stop();
					_hideTimer = 0f;
				}
				return;
			}

			// игрок наверху сменил позицию и видит собаку — новое укрытие (или хотя бы подальше), а если некуда — снова кружить.
			// Если укрытия не было с самого начала (просто убежали подальше), проверять нечего
			if (_hideInCover && Time.time >= _nextCoverCheckTime)
			{
				_nextCoverCheckTime = Time.time + coverCheckInterval;
				if (!_coverFinder.IsHiddenFrom(transform.position, player, _perception.PlayerEyePosition))
				{
					if (!TryStartHiding()) StartPacing();
					return;
				}
			}

			_movement.FaceTowards(GetKnownPlayerPosition(player));
			_hideTimer += Time.deltaTime;
			if (_hideTimer >= hideTime) StartPacing();
		}

		private Vector3 GetKnownPlayerPosition(Transform player)
		{
			if (_perception.CanSeePlayer || !_perception.HasLastKnownPosition) return player.position;
			return _perception.LastKnownPlayerPosition;
		}

		// ---------- Retreat ----------

		private bool ShouldRetreat(int currentHealth, int maxHealth)
		{
			if (currentState == State.Retreat || _retreatsUsed >= maxRetreats) return false;
			if (retreatHealthFraction <= 0f || maxHealth <= 0) return false;
			return (float)currentHealth / maxHealth <= retreatHealthFraction;
		}

		private void EnterRetreat()
		{
			Transform player = _perception.Player;
			if (player == null) return;

			// бежать некуда (тупик, узкий коридор) — остаёмся драться
			if (!TryPickRetreatPoint(player, out _retreatPoint, out _retreatInCover)) return;

			_attack.Cancel();
			_retreatsUsed++;
			_coverChanges = 0;
			StartRunningToRetreatPoint();
			currentState = State.Retreat;
		}

		// Сначала ищем укрытие (точку, которую игрок не видит). Нет укрытия или нет CoverFinder —
		// просто точка подальше от игрока, как раньше
		private bool TryPickRetreatPoint(Transform player, out Vector3 point, out bool inCover)
		{
			point = transform.position;
			inCover = _coverFinder != null && _coverFinder.TryFindCover(player, _perception.PlayerEyePosition, out point);
			if (inCover) return true;
			return _movement.TryFindPointAwayFrom(player.position, retreatDistance, out point);
		}

		private void StartRunningToRetreatPoint()
		{
			_retreatStartTime = Time.time;
			_retreatRecoverTimer = -1f;
		}

		private void UpdateRetreat()
		{
			Transform player = _perception.Player;
			if (player == null || _perception.IgnoresPlayer)
			{
				EnterCalm();
				return;
			}

			if (_retreatRecoverTimer < 0f)
			{
				_movement.MoveTo(_retreatPoint, EnemyGait.Run);
				if (_movement.HasArrived || Time.time - _retreatStartTime > maxRetreatRunTime)
				{
					_movement.Stop();
					_retreatRecoverTimer = 0f;
				}
				return;
			}

			// Загнали в угол — огрызаемся: погоня сама перейдёт в атаку, раз игрок вплотную.
			// Проверяем только на месте отступления: в момент бегства игрок почти всегда рядом (собака только что кусала)
			if (HorizontalDistanceTo(player.position) <= corneredDistance)
			{
				EnterChase();
				return;
			}

			// Укрытие раскрыто — игрок перешёл туда, откуда видит собаку. Ищем новое; сменили уже слишком
			// много раз или прятаться больше негде — хватит бегать, нападаем
			if (_retreatInCover && Time.time >= _nextCoverCheckTime)
			{
				_nextCoverCheckTime = Time.time + coverCheckInterval;
				if (!_coverFinder.IsHiddenFrom(transform.position, player, _perception.PlayerEyePosition))
				{
					if (_coverChanges < maxCoverChanges && TryPickRetreatPoint(player, out _retreatPoint, out _retreatInCover))
					{
						_coverChanges++;
						StartRunningToRetreatPoint();
					}
					else
					{
						_perception.AlertToPlayer();
						EnterChase();
					}
					return;
				}
			}

			// выжидаем, глядя туда, где игрок (видим его — на него, нет — на последнюю известную позицию)
			if (_perception.CanSeePlayer) _movement.FaceTowards(player.position);
			else if (_perception.HasLastKnownPosition) _movement.FaceTowards(_perception.LastKnownPlayerPosition);

			_retreatRecoverTimer += Time.deltaTime;
			if (_retreatRecoverTimer < retreatRecoverTime) return;

			// снова в бой: собака "знает", где игрок, — даже если тот спрятался, пока она отдыхала
			_perception.AlertToPlayer();
			EnterChase();
		}

		private void Die()
		{
			// погибла посреди захвата — игрок сразу получает управление
			if (_syncedAttack != null) _syncedAttack.Abort();

			currentState = State.Dead;

			// скорость берём до выключения агента — ragdoll продолжит движение по инерции
			Vector3 velocity = _movement.Velocity;

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
			else if (currentState == State.Retreat)
			{
				Gizmos.color = Color.cyan;
				Gizmos.DrawLine(transform.position, _retreatPoint);
				Gizmos.DrawWireSphere(_retreatPoint, 0.4f);
				Gizmos.DrawWireSphere(transform.position, corneredDistance);
			}
			else if (currentState == State.Unreachable)
			{
				// фиолетовый — точка кружения под игроком, голубой — укрытие от выстрелов сверху
				Gizmos.color = _unreachableHiding ? Color.cyan : new Color(0.7f, 0.3f, 1f);
				Vector3 target = _unreachableHiding ? _hidePoint : _pacePoint;
				Gizmos.DrawLine(transform.position, target);
				Gizmos.DrawWireSphere(target, 0.4f);
			}
		}
	}
}
