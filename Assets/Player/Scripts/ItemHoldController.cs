using System.Collections;
using Interactables;
using StarterAssets;
using UnityEngine;
using Weapons;

namespace Player
{
	// Удержание физических предметов (Interactables.PickableObject) перед собой — без инвентаря.
	// Подбор идёт через PlayerInteractor: он наводится на PickableObject, по кнопке Interact тот вызывает Pickup.
	// Пока предмет в руках, PlayerInteractor ничего не ищет, а повторное нажатие Interact передаёт сюда (Drop);
	// Fire (ЛКМ) — бросок. Оружие на это время убирается из рук, чтобы ЛКМ не стреляла.
	//
	// Держим предмет kinematic-ребёнком HoldPoint: он жёстко едет за камерой без дрожания. Сам по себе kinematic
	// прошёл бы сквозь стены, поэтому каждый кадр габариты предмета "протягиваются" (BoxCast) от камеры до
	// позиции удержания: упёрся в стену — предмет подтягивается к камере, а если ближе MinHoldDistance —
	// выпадает из рук. При отпускании предмет дополнительно выталкивается из геометрии, если всё-таки в неё залез.
	//
	// На время удержания предмет переводится на слой IgnorePlayer (в матрице коллизий он не сталкивается со
	// слоем Player) — иначе CharacterController упирается в собственный предмет и не может идти вперёд.
	//
	// Вешать на тот же объект, что и PlayerInteractor (или на его родителя).
	// LateUpdate с поздним порядком — после того, как FirstPersonController повернул камеру в этом кадре:
	// иначе проверка стен шла бы по позе камеры прошлого кадра, и на резком повороте предмет влезал бы в стену
	[DefaultExecutionOrder(100)]
	public class ItemHoldController : MonoBehaviour
	{
		[Tooltip("Точка перед камерой, к которой крепится предмет (пустой объект — ребёнок PlayerCameraRoot). " +
			"Смещение и поворот конкретного предмета — в его PickableObject")]
		[SerializeField] private Transform holdPoint;
		[Tooltip("Откуда идут проверки стен и направление броска — точка глаз игрока. Если не задано — родитель Hold Point (PlayerCameraRoot)")]
		[SerializeField] private Transform viewOrigin;
		[Tooltip("Если не задано — берётся StarterAssetsInputs с родительских объектов (обычно с игрока)")]
		[SerializeField] private StarterAssetsInputs input;

		[Header("Hold")]
		[Tooltip("Скорость, с которой подобранный предмет подтягивается в позицию удержания. Больше — резче")]
		[SerializeField] private float moveToHoldSpeed = 15f;
		[Tooltip("Слой, на который переводится предмет, пока он в руках и пока не отлетит от игрока после броска. " +
			"В матрице коллизий (Project Settings → Physics) он не должен сталкиваться со слоем игрока")]
		[SerializeField] private string heldLayerName = "IgnorePlayer";

		[Header("Obstacles")]
		[Tooltip("Слои, которые считаются препятствием для предмета в руках (стены, пол, мебель). " +
			"Слой игрока и слой удержания исключаются автоматически")]
		[SerializeField] private LayerMask obstacleMask = ~0;
		[Tooltip("Зазор между предметом и препятствием, м — чтобы предмет не касался стены вплотную и не мерцал с ней")]
		[SerializeField] private float safeOffset = 0.03f;
		[Tooltip("Если стена не даёт отодвинуть предмет от глаз дальше этого расстояния (м, до центра предмета), " +
			"он автоматически выпадает из рук. Должно быть меньше расстояния до Hold Point")]
		[SerializeField] private float minHoldDistance = 0.5f;

		[Header("Release")]
		[Tooltip("Импульс броска по ЛКМ. Учитывает массу предмета: тяжёлые летят ближе")]
		[SerializeField] private float throwForce = 8f;
		[Tooltip("Отпущенный/брошенный предмет получает скорость игрока — на бегу он не падает \"назад\"")]
		[SerializeField] private bool inheritPlayerVelocity = true;
		[Tooltip("Сколько максимум секунд после отпускания предмет остаётся на слое удержания — пока не выйдет из капсулы игрока")]
		[SerializeField] private float restoreLayerTimeout = 2f;

		// сколько раз подряд выталкивать предмет из геометрии при отпускании (он может сидеть сразу в нескольких стенах)
		private const int DepenetrationIterations = 4;
		private static readonly Collider[] OverlapBuffer = new Collider[16];

		private PickableObject _held;
		private bool _isHolding;
		private Transform _originalParent;
		private RigidbodyInterpolation _originalInterpolation;
		private bool _fireLatch;
		private int _heldLayer = -1;

		private CharacterController _playerController;
		private Collider[] _playerColliders;
		private WeaponController[] _weapons;

		public bool IsHolding => _isHolding;
		public PickableObject Held => _held;

		// препятствия — всё из obstacleMask, кроме игрока и предметов на слое удержания (включая тот, что в руках)
		private int ObstacleLayers
		{
			get
			{
				int mask = obstacleMask.value;
				if (_heldLayer >= 0) mask &= ~(1 << _heldLayer);
				if (_playerController != null) mask &= ~(1 << _playerController.gameObject.layer);
				return mask;
			}
		}

		private void Awake()
		{
			if (viewOrigin == null && holdPoint != null) viewOrigin = holdPoint.parent;
			if (viewOrigin == null && Camera.main != null) viewOrigin = Camera.main.transform;
			if (input == null) input = GetComponentInParent<StarterAssetsInputs>();

			// корень игрока — объект с CharacterController (PlayerCapsule); коллайдеры собираем сейчас,
			// пока в иерархии игрока нет чужих предметов
			_playerController = GetComponentInParent<CharacterController>();
			Transform playerRoot = _playerController != null ? _playerController.transform : transform.root;
			_playerColliders = playerRoot.GetComponentsInChildren<Collider>(true);
			_weapons = playerRoot.GetComponentsInChildren<WeaponController>(true);

			_heldLayer = LayerMask.NameToLayer(heldLayerName);
			if (_heldLayer < 0) Debug.LogError($"{nameof(ItemHoldController)} на {name}: слой \"{heldLayerName}\" не найден в Tags and Layers", this);
			if (holdPoint == null) Debug.LogError($"{nameof(ItemHoldController)} на {name}: не задан Hold Point", this);
		}

		public void Pickup(PickableObject item)
		{
			if (_isHolding || item == null || item.IsHeld || holdPoint == null) return;

			_held = item;
			_isHolding = true;

			// событие — до перевода в kinematic: подписчики вроде BreakableDebris.CancelFreeze сами трогают isKinematic
			item.NotifyPickedUp();

			Rigidbody body = item.Body;
			_originalInterpolation = body.interpolation;
			body.isKinematic = true;
			// интерполяция kinematic-тела перетирала бы позицию, которую задаёт родитель, — предмет бы дрожал
			body.interpolation = RigidbodyInterpolation.None;

			if (_heldLayer >= 0) item.SetHeldLayer(_heldLayer);

			_originalParent = item.transform.parent;
			item.transform.SetParent(holdPoint, true);

			SetWeaponsHolstered(true);
			_fireLatch = input != null && input.fire;
		}

		// отпустить без броска — по повторному нажатию Interact (вызывает PlayerInteractor) или при упоре в стену
		public void Drop()
		{
			Release(false);
		}

		public void Throw()
		{
			Release(true);
		}

		private void LateUpdate()
		{
			if (!_isHolding) return;

			// предмет уничтожили прямо в руках — просто возвращаем игроку оружие
			if (_held == null)
			{
				_isHolding = false;
				SetWeaponsHolstered(false);
				return;
			}

			Transform item = _held.transform;
			float t = 1f - Mathf.Exp(-moveToHoldSpeed * Time.deltaTime);
			Vector3 position = Vector3.Lerp(item.position, holdPoint.TransformPoint(_held.HoldOffsetPosition), t);
			Quaternion rotation = Quaternion.Slerp(item.rotation, holdPoint.rotation * _held.HoldOffsetRotation, t);

			bool blocked = FitBetweenViewAndObstacle(_held, ref position, rotation, out float freeDistance);
			item.SetPositionAndRotation(position, rotation);

			// стена вплотную — не вдавливаем предмет в неё, а роняем
			if (blocked && freeDistance < minHoldDistance)
			{
				Drop();
				return;
			}

			if (input == null) return;

			// бросок — по фронту нажатия, чтобы ЛКМ, зажатая ещё до подбора, не бросила предмет сразу
			bool firePressed = input.fire && !_fireLatch;
			_fireLatch = input.fire;
			if (firePressed) Throw();
		}

		private void Release(bool thrown)
		{
			if (!_isHolding || _held == null) return;

			PickableObject item = _held;
			_held = null;
			_isHolding = false;

			// прежнего родителя могли уничтожить, пока предмет был в руках — тогда в корень сцены
			item.transform.SetParent(_originalParent != null ? _originalParent : null, true);

			// безопасная точка сброса: предмет не должен оказаться за стеной или внутри неё
			Vector3 position = item.transform.position;
			FitBetweenViewAndObstacle(item, ref position, item.transform.rotation, out _);
			item.transform.position = position;
			Depenetrate(item);

			Rigidbody body = item.Body;
			body.isKinematic = false;
			body.interpolation = _originalInterpolation;
			body.linearVelocity = inheritPlayerVelocity && _playerController != null ? _playerController.velocity : Vector3.zero;
			body.angularVelocity = Vector3.zero;

			if (thrown && viewOrigin != null)
			{
				body.AddForce(viewOrigin.forward * throwForce, ForceMode.Impulse);
			}

			item.NotifyReleased();
			StartCoroutine(RestoreLayerWhenClear(item));

			// нажатие ЛКМ, которым бросили, "съедаем" — иначе только что вернувшееся оружие его подхватит
			if (thrown && input != null) input.fire = false;
			SetWeaponsHolstered(false);
		}

		// "Протягивает" габариты предмета item (с поворотом rotation) от глаз до его позиции position. Если по пути
		// препятствие — сдвигает position к глазам так, чтобы предмет остановился перед ним с зазором safeOffset.
		// freeDistance — на каком расстоянии от глаз может оказаться центр предмета. true — препятствие было
		private bool FitBetweenViewAndObstacle(PickableObject item, ref Vector3 position, Quaternion rotation, out float freeDistance)
		{
			freeDistance = float.PositiveInfinity;
			if (viewOrigin == null) return false;

			Bounds local = item.LocalBounds;
			Vector3 scale = Abs(item.transform.lossyScale);
			Vector3 centerOffset = rotation * Vector3.Scale(local.center, scale);
			Vector3 halfExtents = Vector3.Scale(local.extents, scale);

			Vector3 origin = viewOrigin.position;
			Vector3 toCenter = position + centerOffset - origin;
			float distance = toCenter.magnitude;
			if (distance < 0.0001f) return false;
			Vector3 direction = toCenter / distance;

			int mask = ObstacleLayers;
			RaycastHit hit;
			bool blocked;

			// BoxCast не видит того, что коробка задевает уже в начальной точке. Если предмет слишком большой и
			// у самых глаз уже цепляет стену — проверяем тонким лучом и отступаем на габарит предмета вдоль луча
			if (Physics.CheckBox(origin, halfExtents, rotation, mask, QueryTriggerInteraction.Ignore))
			{
				blocked = Physics.Raycast(origin, direction, out hit, distance, mask, QueryTriggerInteraction.Ignore);
				if (blocked) freeDistance = hit.distance - ExtentAlong(direction, rotation, halfExtents) - safeOffset;
			}
			else
			{
				blocked = Physics.BoxCast(origin, halfExtents, direction, out hit, rotation, distance, mask, QueryTriggerInteraction.Ignore);
				if (blocked) freeDistance = hit.distance - safeOffset;
			}

			if (!blocked) return false;

			freeDistance = Mathf.Max(0f, freeDistance);
			position -= direction * (distance - freeDistance);
			return true;
		}

		// Последняя страховка при отпускании: если предмет всё же пересекается с геометрией (например, боком влез
		// в стену сбоку, чего не видно по линии взгляда) — выталкиваем его по кратчайшему пути
		private void Depenetrate(PickableObject item)
		{
			int mask = ObstacleLayers;

			for (int iteration = 0; iteration < DepenetrationIterations; iteration++)
			{
				// у физики выключен Auto Sync Transforms — без этого bounds и позы коллайдеров остались бы старыми
				Physics.SyncTransforms();
				bool moved = false;

				foreach (Collider part in item.Colliders)
				{
					if (part == null || part.isTrigger || !part.enabled) continue;

					Bounds bounds = part.bounds;
					int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, OverlapBuffer, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
					for (int i = 0; i < count; i++)
					{
						Collider other = OverlapBuffer[i];
						if (other.transform.IsChildOf(item.transform)) continue;

						if (Physics.ComputePenetration(part, part.transform.position, part.transform.rotation,
							other, other.transform.position, other.transform.rotation, out Vector3 pushDirection, out float pushDistance))
						{
							item.transform.position += pushDirection * (pushDistance + safeOffset);
							moved = true;
						}
					}
				}

				if (!moved) return;
			}
		}

		// предмет возвращается на свой слой не сразу, а когда выйдет из капсулы игрока — иначе физика
		// выталкивала бы его рывком (или толкала игрока)
		private IEnumerator RestoreLayerWhenClear(PickableObject item)
		{
			float deadline = Time.time + restoreLayerTimeout;
			while (Time.time < deadline && item != null && !item.IsHeld && OverlapsPlayer(item))
			{
				yield return new WaitForFixedUpdate();
			}

			// предмет уже снова в руках — слой вернёт следующий Release
			if (item == null || item.IsHeld) yield break;
			item.RestoreLayers();
		}

		private bool OverlapsPlayer(PickableObject item)
		{
			foreach (Collider itemCollider in item.Colliders)
			{
				if (itemCollider == null) continue;
				foreach (Collider playerCollider in _playerColliders)
				{
					if (playerCollider != null && itemCollider.bounds.Intersects(playerCollider.bounds)) return true;
				}
			}
			return false;
		}

		private void SetWeaponsHolstered(bool holstered)
		{
			foreach (WeaponController weapons in _weapons)
			{
				if (weapons != null) weapons.SetHolstered(holstered);
			}
		}

		// половина размера повёрнутой коробки вдоль направления
		private static float ExtentAlong(Vector3 direction, Quaternion rotation, Vector3 halfExtents)
		{
			return Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.right)) * halfExtents.x
				+ Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.up)) * halfExtents.y
				+ Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.forward)) * halfExtents.z;
		}

		private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

		private void OnDrawGizmosSelected()
		{
			if (holdPoint == null) return;
			Gizmos.color = Color.green;
			Gizmos.DrawWireSphere(holdPoint.position, 0.1f);
			Gizmos.DrawRay(holdPoint.position, holdPoint.forward * 0.3f);

			// сфера MinHoldDistance вокруг глаз: упёрся в стену ближе неё — предмет выпадает
			Transform eye = viewOrigin != null ? viewOrigin : holdPoint.parent;
			if (eye == null) return;
			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(eye.position, minHoldDistance);
		}
	}
}
