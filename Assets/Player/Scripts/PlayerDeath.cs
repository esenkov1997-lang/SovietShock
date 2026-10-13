using System;
using Inventory;
using StarterAssets;
using UnityEngine;
using Weapons;

namespace Player
{
	// Смерть игрока от первого лица — без анимаций, физикой. Вешается на PlayerCapsule рядом с Health.
	//
	// Когда Health доходит до нуля: управление выключается, оружие убирается, а на месте камеры появляется
	// невидимая "голова" — шар с Rigidbody, к которому прикрепляется камера. Голова получает толчок и рывок
	// (резко запрокидывается и заваливается вбок — "рывок от удара") и падает на пол сама: каждый раз по-разному,
	// а сквозь стену не пройдёт — её останавливает коллайдер. Через Settle Time голова замирает.
	//
	// Событие Died — для экрана смерти и прочих реакций (звук, виньетка) — следующие этапы.
	[RequireComponent(typeof(Health), typeof(FirstPersonController))]
	public class PlayerDeath : MonoBehaviour
	{
		[Header("Голова")]
		[Tooltip("Радиус физической головы (м). Должен быть больше Near Clip Plane камеры, иначе у стены будет видно сквозь неё")]
		[SerializeField] private float headRadius = 0.15f;
		[Tooltip("Масса головы (кг). Влияет на то, как она отскакивает от препятствий")]
		[SerializeField] private float headMass = 5f;
		[Tooltip("Сопротивление вращению. Больше — голова быстрее перестаёт кувыркаться и не катится, как мяч")]
		[SerializeField] private float angularDrag = 4f;
		[Tooltip("Трение о поверхности. Больше — меньше скользит и катится после падения")]
		[Range(0f, 1f)]
		[SerializeField] private float friction = 0.9f;
		[Tooltip("Через сколько секунд после смерти голова замирает окончательно (без дрожания на полу)")]
		[SerializeField] private float settleTime = 4f;

		[Header("Толчок")]
		[Tooltip("Скорость (м/с), с которой голову отбрасывает назад в момент смерти")]
		[SerializeField] private float backwardImpulse = 1.2f;
		[Tooltip("Случайный толчок вбок (м/с): от 0 до этого значения в случайную сторону — падение каждый раз разное")]
		[SerializeField] private float sideImpulse = 0.8f;
		[Tooltip("Толчок вверх (м/с) — короткое \"подбрасывание\" перед падением")]
		[SerializeField] private float upwardImpulse = 0.3f;
		[Tooltip("Передать голове скорость, с которой игрок двигался в момент смерти (бежал — упадёт по инерции вперёд)")]
		[SerializeField] private bool inheritVelocity = true;

		[Header("Рывок от удара")]
		[Tooltip("Скорость, с которой голову запрокидывает назад (град/с) в первый момент")]
		[SerializeField] private float joltPitch = 220f;
		[Tooltip("Случайный крен вбок (град/с): от половины до полного значения в случайную сторону — заваливание на бок")]
		[SerializeField] private float joltRoll = 160f;
		[Tooltip("Случайный поворот головы влево/вправо (град/с)")]
		[SerializeField] private float joltYaw = 60f;

		private Health _health;
		private FirstPersonController _movement;
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private PlayerSyncedAnimation _syncedAnimation;
		private PlayerInteractor _interactor;
		private WeaponController[] _weapons;
		private Transform _cameraRoot;

		private Rigidbody _head;
		private float _deathTime;

		public bool IsDead { get; private set; }

		// игрок погиб — подписываются экран смерти, звук, эффекты
		public event Action Died;

		private void Awake()
		{
			_health = GetComponent<Health>();
			_movement = GetComponent<FirstPersonController>();
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
			_syncedAnimation = GetComponent<PlayerSyncedAnimation>();
			_interactor = GetComponentInChildren<PlayerInteractor>(true);
			_weapons = GetComponentsInChildren<WeaponController>(true);
			if (_movement.CinemachineCameraTarget != null) _cameraRoot = _movement.CinemachineCameraTarget.transform;
		}

		private void OnEnable()
		{
			_health.OnDeath.AddListener(Die);
		}

		private void OnDisable()
		{
			_health.OnDeath.RemoveListener(Die);
		}

		private void FixedUpdate()
		{
			if (_head == null || _head.isKinematic) return;

			// окончательно замираем — иначе физика может долго "дышать" на полу
			if (Time.time - _deathTime >= settleTime)
			{
				_head.linearVelocity = Vector3.zero;
				_head.angularVelocity = Vector3.zero;
				_head.isKinematic = true;
			}
		}

		private void Die()
		{
			if (IsDead) return;
			IsDead = true;
			_deathTime = Time.time;

			// захват врагом прерываем ДО того, как заберём камеру — иначе он вернул бы её в голову капсулы
			if (_syncedAnimation != null) _syncedAnimation.Abort();

			Vector3 velocity = inheritVelocity && _controller != null ? _controller.velocity : Vector3.zero;
			LockPlayer();

			if (_cameraRoot == null)
			{
				Debug.LogWarning($"{name}: PlayerDeath не нашёл камеру игрока (CinemachineCameraTarget)", this);
				Died?.Invoke();
				return;
			}

			SpawnHead(velocity);
			Died?.Invoke();
		}

		private void SpawnHead(Vector3 inheritedVelocity)
		{
			GameObject head = new GameObject($"{name}_DeathHead");
			// Ignore Raycast: голова не загораживает обзор врагам и не ловит выстрелы, но сталкивается с миром
			head.layer = 2;
			head.transform.SetPositionAndRotation(_cameraRoot.position, _cameraRoot.rotation);

			SphereCollider collider = head.AddComponent<SphereCollider>();
			collider.radius = headRadius;
			collider.material = new PhysicsMaterial("DeathHead")
			{
				dynamicFriction = friction,
				staticFriction = friction,
				bounciness = 0.05f,
				frictionCombine = PhysicsMaterialCombine.Maximum,
				bounceCombine = PhysicsMaterialCombine.Minimum
			};
			if (_controller != null) Physics.IgnoreCollision(collider, _controller);

			_head = head.AddComponent<Rigidbody>();
			_head.mass = headMass;
			_head.angularDamping = angularDrag;
			_head.interpolation = RigidbodyInterpolation.Interpolate;
			_head.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

			// толчок: назад от взгляда, случайно вбок, чуть вверх
			Transform view = _cameraRoot;
			Vector3 back = -Flat(view.forward);
			Vector3 side = Flat(view.right) * (UnityEngine.Random.Range(-1f, 1f) * sideImpulse);
			_head.linearVelocity = inheritedVelocity + back * backwardImpulse + side + Vector3.up * upwardImpulse;

			// рывок: запрокидывание назад + заваливание на случайный бок + немного поворота головы
			float rollSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
			Vector3 localJolt = new Vector3(
				-joltPitch,
				UnityEngine.Random.Range(-joltYaw, joltYaw),
				rollSign * UnityEngine.Random.Range(joltRoll * 0.5f, joltRoll));
			_head.angularVelocity = view.TransformDirection(localJolt * Mathf.Deg2Rad);

			// камера (точнее, то, за чем следует Cinemachine) теперь едет вместе с головой
			_cameraRoot.SetParent(head.transform, true);
			_cameraRoot.localPosition = Vector3.zero;
			_cameraRoot.localRotation = Quaternion.identity;
		}

		// то же, что у PlayerCameraFocus / PlayerSyncedAnimation: выключаем контроллер, ввод и оружие
		private void LockPlayer()
		{
			_movement.StopMotion();
			_movement.enabled = false;
			if (_interactor != null) _interactor.enabled = false;
			foreach (WeaponController weapons in _weapons)
				if (weapons != null) weapons.SetHolstered(true);

			InventoryUI inventory = FindFirstObjectByType<InventoryUI>();
			if (inventory != null && inventory.IsOpen) inventory.Close();

			if (_input == null) return;
			_input.move = Vector2.zero;
			_input.look = Vector2.zero;
			_input.jump = false;
			_input.sprint = false;
			_input.cursorInputForLook = false;
			_input.GameplayInputEnabled = false;
		}

		private static Vector3 Flat(Vector3 direction)
		{
			direction.y = 0f;
			return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
		}
	}
}
