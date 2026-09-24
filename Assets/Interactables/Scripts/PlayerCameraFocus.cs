using System.Collections;
using Player;
using StarterAssets;
using UnityEngine;
using Weapons;

namespace Interactables
{
	// Вешается на корень игрока (PlayerCapsule — тот же объект, где FirstPersonController и CharacterController).
	// Забирает у игрока управление на время "фокусировки" на объекте (см. InteractableFocusObject) и
	// возвращает его обратно. Два режима (см. InteractionType):
	//
	//  StaticLook   — капсула стоит на месте, камера (CinemachineCameraTarget) плавно перелетает в
	//                 cameraTarget объекта. Курсор остаётся зажатым и скрытым — UI терминала управляется с
	//                 клавиатуры. Выход — exitKey (Escape) или ExitInteraction(): камера летит обратно
	//                 в голову, управление возвращается. С включённым
	//                 InteractableFocusObject.RepositionOnStaticLook капсула на входе незаметно переставляется
	//                 в playerRepositionPoint — и после выхода игрок стоит уже там.
	//  PhysicalMove — вся капсула (вместе с камерой) плавно едет в playerRepositionPoint с выключенным
	//                 CharacterController и ОСТАЁТСЯ там. Углы обзора FirstPersonController (yaw — поворот
	//                 капсулы, pitch — CameraPitch) подстраиваются под новую позу, управление возвращается сразу.
	//
	// Управление отключается выключением самого FirstPersonController: его Update (движение) и LateUpdate
	// (единственное место, которое пишет в Transform камеры) перестают выполняться, так что двигать камеру
	// здесь можно напрямую, ни с кем не конкурируя. Cinemachine просто следует за CinemachineCameraTarget.
	//
	// В обоих режимах на всё время фокуса оружие убирается из рук (WeaponController.SetHolstered — скрыто,
	// стрельба/прицел/перезарядка/смена/выброс заблокированы), а инвентарь закрывается и не открывается
	// (Inventory.InventoryUI слушает FocusStarted и проверяет IsBusy).
	[RequireComponent(typeof(FirstPersonController))]
	[RequireComponent(typeof(CharacterController))]
	public class PlayerCameraFocus : MonoBehaviour
	{
		[Header("Transition")]
		[Tooltip("Длительность перелёта камеры / перемещения капсулы, в секундах")]
		[SerializeField] private float transitionDuration = 0.6f;
		[Tooltip("Кривая сглаживания перелёта (ось X — время 0..1, ось Y — доля пути 0..1)")]
		[SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		[Header("StaticLook")]
		[Tooltip("Кнопка выхода из режима StaticLook")]
		[SerializeField] private KeyCode exitKey = KeyCode.Escape;

		[Header("References (необязательно — ищутся автоматически)")]
		[Tooltip("Объект, за которым следует Cinemachine-камера. Если не задано — FirstPersonController.CinemachineCameraTarget")]
		[SerializeField] private Transform cameraRoot;
		[Tooltip("Если не задано — ищется среди дочерних объектов игрока. Выключается на время фокуса, " +
			"чтобы E не срабатывала повторно и подсказка не висела поверх терминала")]
		[SerializeField] private PlayerInteractor playerInteractor;

		private enum State { Idle, EnteringStatic, InStatic, ExitingStatic, Moving }

		private FirstPersonController _movement;
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private LedgeMantle _ledgeMantle;
		private WeaponController[] _weapons;

		private State _state = State.Idle;
		private InteractableFocusObject _current;
		private Coroutine _routine;

		// поза камеры в голове (локально относительно капсулы) — куда возвращаться из StaticLook
		private Vector3 _headLocalPosition;
		private Quaternion _headLocalRotation;

		// true, пока идёт любое взаимодействие — в том числе перелёт туда/обратно
		public bool IsBusy => _state != State.Idle;
		public InteractableFocusObject CurrentFocus => _current;

		// для систем вне игрока, которым нужно отреагировать на фокус (например, InventoryUI закрывает панель).
		// FocusStarted вызывается ДО блокировки управления — так всё, что подписчик сделает с вводом (закрытие
		// инвентаря снова включает обзор и боевой ввод), будет тут же перекрыто блокировкой фокуса, а не наоборот
		public event System.Action FocusStarted;
		public event System.Action FocusEnded;

		private void Awake()
		{
			_movement = GetComponent<FirstPersonController>();
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
			_ledgeMantle = GetComponent<LedgeMantle>(); // необязателен

			if (cameraRoot == null && _movement.CinemachineCameraTarget != null)
				cameraRoot = _movement.CinemachineCameraTarget.transform;
			if (playerInteractor == null) playerInteractor = GetComponentInChildren<PlayerInteractor>(true);
			_weapons = GetComponentsInChildren<WeaponController>(true);
		}

		private void Update()
		{
			// Escape прерывает и уже установившийся фокус, и ещё идущий перелёт к объекту
			// ...если только объект не обрабатывает Escape сам (см. InteractableFocusObject.HandlesExitKeyExternally)
			bool objectHandlesExit = _current != null && _current.HandlesExitKeyExternally;
			if ((_state == State.InStatic || _state == State.EnteringStatic) && !objectHandlesExit && Input.GetKeyDown(exitKey))
			{
				ExitInteraction();
			}
		}

		private void OnDisable()
		{
			// игрока выключили/уничтожили посреди взаимодействия — не оставляем его без управления
			if (_state == State.Idle) return;

			if (_routine != null) StopCoroutine(_routine);
			_routine = null;

			if (_state != State.Moving && cameraRoot != null)
			{
				cameraRoot.localPosition = _headLocalPosition;
				cameraRoot.localRotation = _headLocalRotation;
			}
			_controller.enabled = true;
			Finish();
		}

		// Точка входа для InteractableFocusObject. false — если начать нельзя (уже заняты, идёт залезание
		// на уступ, у объекта не назначена нужная точка).
		public bool BeginInteraction(InteractableFocusObject target)
		{
			if (target == null || IsBusy || cameraRoot == null) return false;
			if (_ledgeMantle != null && _ledgeMantle.IsClimbing) return false;

			// сначала проверяем настройки объекта — до того, как кого-либо оповещать
			if (target.Type == InteractionType.StaticLook && target.CameraTarget == null)
			{
				Debug.LogError($"{nameof(InteractableFocusObject)} на {target.name}: не назначен cameraTarget", target);
				return false;
			}
			if (target.PlayerRepositionPoint == null &&
				(target.Type == InteractionType.PhysicalMove || target.RepositionOnStaticLook))
			{
				Debug.LogError($"{nameof(InteractableFocusObject)} на {target.name}: не назначен playerRepositionPoint", target);
				return false;
			}

			_current = target;
			FocusStarted?.Invoke(); // до LockPlayer — см. комментарий у события

			// корутина синхронно выполняется до первого yield, так что LockPlayer отрабатывает прямо здесь
			_routine = target.Type == InteractionType.StaticLook
				? StartCoroutine(StaticLookEnterRoutine(target.CameraTarget,
					target.RepositionOnStaticLook ? target.PlayerRepositionPoint : null))
				: StartCoroutine(PhysicalMoveRoutine(target.PlayerRepositionPoint));

			target.RaiseInteractionStart();
			return true;
		}

		// Выход из StaticLook: вызывается по exitKey или извне (например, кнопкой "Закрыть" в UI терминала
		// через InteractableFocusObject.ExitInteraction). В PhysicalMove выходить не из чего — игнорируется.
		public void ExitInteraction()
		{
			if (_state != State.InStatic && _state != State.EnteringStatic) return;

			if (_routine != null) StopCoroutine(_routine);
			_routine = StartCoroutine(StaticLookExitRoutine());

			LockCursor();
		}

		// В редакторе Escape (наш exitKey по умолчанию) сам по себе отпускает курсор в Game view — и он висит
		// видимым, пока не кликнешь в окно. Поэтому зажимаем явно: сразу при выходе и ещё раз в UnlockPlayer —
		// на случай, если редактор обработал Escape позже нашего Update в этом же кадре. В билде это no-op
		private static void LockCursor()
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
		}

		// ---------- StaticLook ----------

		// repositionPoint — необязательно: если задан, капсула сразу, незаметно для игрока (камера в этот момент
		// улетает к объекту), переставляется в эту точку, и по выходу камера возвращается уже в голову на новом месте
		private IEnumerator StaticLookEnterRoutine(Transform target, Transform repositionPoint)
		{
			_state = State.EnteringStatic;

			// запоминаем позу ДО отключения контроллера — это ровно то, что он сам туда записал в последнем LateUpdate
			_headLocalPosition = cameraRoot.localPosition;
			_headLocalRotation = cameraRoot.localRotation;

			LockPlayer();

			if (repositionPoint != null) TeleportKeepingCamera(repositionPoint);

			// летим за целью в мировых координатах; цель читается каждый кадр — на случай, если объект движется
			yield return Transition(
				() => cameraRoot.position, () => cameraRoot.rotation,
				() => target.position, () => target.rotation,
				(p, r) => cameraRoot.SetPositionAndRotation(p, r));

			_state = State.InStatic;
			_routine = null;
		}

		private IEnumerator StaticLookExitRoutine()
		{
			_state = State.ExitingStatic;

			// цель — "голова" в ТЕКУЩЕЙ позе капсулы: если на входе её переставили (TeleportKeepingCamera),
			// камера вернётся уже на новое место
			Transform parent = cameraRoot.parent;
			yield return Transition(
				() => cameraRoot.position, () => cameraRoot.rotation,
				() => parent != null ? parent.TransformPoint(_headLocalPosition) : _headLocalPosition,
				() => parent != null ? parent.rotation * _headLocalRotation : _headLocalRotation,
				(p, r) => cameraRoot.SetPositionAndRotation(p, r));

			// ставим локальную позу точно, без накопленной погрешности лерпа — FirstPersonController
			// в следующем LateUpdate запишет сюда то же самое (его pitch либо не менялся, либо уже
			// синхронизирован с _headLocalRotation в TeleportKeepingCamera)
			cameraRoot.localPosition = _headLocalPosition;
			cameraRoot.localRotation = _headLocalRotation;

			_routine = null;
			Finish();
		}

		// Мгновенно ставит капсулу в точку (yaw из точки, наклон точки по X — будущий pitch камеры),
		// сохраняя при этом МИРОВУЮ позу камеры: cameraRoot — ребёнок капсулы, и без этого камеру дёрнуло бы
		// вместе с ней. Игрок видит только непрерывный перелёт камеры к объекту
		private void TeleportKeepingCamera(Transform point)
		{
			cameraRoot.GetPositionAndRotation(out Vector3 cameraPosition, out Quaternion cameraRotation);

			// CharacterController перехватывает прямую запись в transform.position
			_controller.enabled = false;
			transform.SetPositionAndRotation(point.position, Quaternion.Euler(0f, point.eulerAngles.y, 0f));
			Physics.SyncTransforms();
			_controller.enabled = true;

			cameraRoot.SetPositionAndRotation(cameraPosition, cameraRotation);

			// "голова", в которую камера вернётся на выходе, смотрит под наклоном точки — и контроллер
			// сразу получает тот же pitch, чтобы после выхода мышь продолжила ровно с этого угла.
			// FirstPersonController выключен, так что в камеру он это не запишет до конца фокуса
			float pitch = Mathf.DeltaAngle(0f, point.eulerAngles.x);
			_movement.CameraPitch = pitch;
			_headLocalRotation = Quaternion.Euler(_movement.CameraPitch, 0f, 0f); // уже с учётом клампа
			_movement.StopMotion(); // инерция шага "к терминалу" не должна сдвинуть игрока после выхода
		}

		// ---------- PhysicalMove ----------

		private IEnumerator PhysicalMoveRoutine(Transform point)
		{
			_state = State.Moving;

			LockPlayer();
			// CharacterController перехватывает прямую запись в transform.position — на время
			// перемещения он выключен
			_controller.enabled = false;

			// капсула поворачивается только по yaw (иначе она завалится), а наклон точки (её ось X)
			// превращается в pitch камеры — так точка задаёт полный взгляд "на рычаг"
			float startPitch = _movement.CameraPitch;
			float targetPitch = Mathf.DeltaAngle(0f, point.eulerAngles.x);

			yield return Transition(
				() => transform.position, () => transform.rotation,
				() => point.position, () => Quaternion.Euler(0f, point.eulerAngles.y, 0f),
				(p, r) => transform.SetPositionAndRotation(p, r),
				t =>
				{
					// FirstPersonController выключен и не пишет в камеру — плавно ведём pitch сами
					// (Extra*Offset-эффекты он сам вернёт поверх в первом же LateUpdate после включения)
					float pitch = Mathf.LerpAngle(startPitch, targetPitch, t);
					cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
				});

			// игрок остаётся в новой точке: камеру НЕ возвращаем, а синхронизируем с ней внутренние углы
			// контроллера — yaw уже в transform.rotation, pitch передаём явно
			_movement.CameraPitch = targetPitch;
			_movement.StopMotion(); // иначе инерция шага "до рычага" столкнёт игрока с точки

			Physics.SyncTransforms(); // чтобы коллайдер капсулы оказался в новой позиции до первого Move
			_controller.enabled = true;

			_routine = null;
			Finish();
		}

		// ---------- Общее ----------

		// Плавный переход от стартовой позы к целевой по transitionCurve. Старт фиксируется в начале,
		// цель читается каждый кадр (Vector3.Lerp / Quaternion.Slerp). onProgress — доп. действие с t.
		private IEnumerator Transition(
			System.Func<Vector3> getFromPos, System.Func<Quaternion> getFromRot,
			System.Func<Vector3> getToPos, System.Func<Quaternion> getToRot,
			System.Action<Vector3, Quaternion> apply,
			System.Action<float> onProgress = null)
		{
			Vector3 fromPos = getFromPos();
			Quaternion fromRot = getFromRot();

			float elapsed = 0f;
			while (elapsed < transitionDuration)
			{
				elapsed += Time.deltaTime;
				float t = transitionCurve.Evaluate(Mathf.Clamp01(elapsed / transitionDuration));

				apply(Vector3.Lerp(fromPos, getToPos(), t), Quaternion.Slerp(fromRot, getToRot(), t));
				onProgress?.Invoke(t);
				yield return null;
			}

			apply(getToPos(), getToRot());
			onProgress?.Invoke(1f);
		}

		// Отключает движение/обзор/боевой ввод — тем же способом, что и Inventory.InventoryUI
		// Курсор мыши намеренно не трогаем: UI терминалов управляется с клавиатуры, мышь остаётся зажатой и
		// скрытой, как в игре (cursorInputForLook = false — движение мыши просто не крутит камеру)
		private void LockPlayer()
		{
			_movement.enabled = false;
			if (playerInteractor != null) playerInteractor.enabled = false;
			foreach (WeaponController weapons in _weapons)
			{
				if (weapons != null) weapons.SetHolstered(true);
			}

			if (_input != null)
			{
				_input.move = Vector2.zero;
				_input.look = Vector2.zero;
				_input.jump = false;
				_input.sprint = false;
				_input.cursorInputForLook = false;
				_input.GameplayInputEnabled = false; // заодно сбрасывает зажатые fire/aim
			}
		}

		private void UnlockPlayer()
		{
			if (_input != null)
			{
				_input.look = Vector2.zero;
				// нажатия за время фокуса (клавиатурная навигация по UI терминала тоже идёт через них) иначе
				// "дотекут" до игрока: E сразу откроет объект снова, пробел — прыжок сразу после выхода
				_input.interact = false;
				_input.jump = false;
				_input.cursorInputForLook = true;
				_input.GameplayInputEnabled = true;
			}

			LockCursor(); // см. комментарий у LockCursor — редактор мог отпустить курсор по Escape

			_movement.enabled = true;
			if (playerInteractor != null) playerInteractor.enabled = true;
			// SetHolstered(false) заново достаёт текущее оружие — с Draw-анимацией
			foreach (WeaponController weapons in _weapons)
			{
				if (weapons != null) weapons.SetHolstered(false);
			}
		}

		private void Finish()
		{
			InteractableFocusObject finished = _current;
			_current = null;
			_state = State.Idle;

			UnlockPlayer();
			FocusEnded?.Invoke();

			// событие — последним, когда управление уже возвращено: подписчики могут сразу начать новое взаимодействие
			if (finished != null) finished.RaiseInteractionEnd();
		}
	}
}
