using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;

namespace Interactables
{
	public enum InteractionType
	{
		// Терминалы, сейфы, кодовые замки: игрок стоит, камера подлетает к объекту, курсор свободен
		StaticLook,
		// Рычаги, двери: игрок сам перемещается в точку перед объектом и остаётся там
		PhysicalMove
	}

	// Интерактивный объект с фокусировкой камеры/игрока. Работает через обычную систему интеракции
	// (IInteractable + Player.PlayerInteractor): по нажатию E передаёт управление в PlayerCameraFocus
	// игрока, а сам только хранит настройки и события для инспектора.
	//
	// OnInteractionStart — сразу при начале (открыть UI терминала, запустить анимацию рычага).
	// OnInteractionEnd   — когда управление вернулось игроку: в StaticLook — после возврата камеры
	//                      в голову, в PhysicalMove — сразу по прибытии в playerRepositionPoint.
	public class InteractableFocusObject : MonoBehaviour, IInteractable
	{
		[Tooltip("Подсказка при наведении. По умолчанию — общая \"Использовать\" из таблицы UI; " +
			"для конкретного объекта можно выбрать свой ключ (\"Открыть терминал\", \"Потянуть рычаг\")")]
		[SerializeField] private LocalizedString interactionPrompt = new LocalizedString("UI", "prompt.use");
		[SerializeField] private InteractionType interactionType = InteractionType.StaticLook;

		[Tooltip("StaticLook: точка и направление, куда перелетает камера (синяя ось Z — смотрит на объект)")]
		[SerializeField] private Transform cameraTarget;
		[Tooltip("PhysicalMove (и StaticLook с repositionOnStaticLook): куда встаёт капсула игрока (позиция ног). " +
			"Поворот по Y — куда повёрнут игрок, наклон по X — наклон камеры вверх/вниз после перемещения")]
		[SerializeField] private Transform playerRepositionPoint;
		[Tooltip("StaticLook: при входе незаметно переставить игрока в playerRepositionPoint — после выхода " +
			"он окажется там, а не там, откуда подошёл. Выключено — игрок остаётся на месте")]
		[SerializeField] private bool repositionOnStaticLook;

		[Header("Approach")]
		[Tooltip("Взаимодействовать можно только спереди, в пределах Approach Angle. Сзади и сбоку объекта " +
			"подсказка не появляется и нажатие не срабатывает")]
		[SerializeField] private bool restrictApproachAngle;
		[Tooltip("Ширина сектора перед объектом в градусах (120 — по 60° в каждую сторону от \"лица\"). " +
			"Сектор виден в сцене зелёным, когда объект выделен")]
		[Range(10f, 360f)]
		[SerializeField] private float approachAngle = 120f;
		[Tooltip("Необязательно: объект, синяя ось Z которого смотрит НАРУЖУ из лица объекта (туда, где стоит игрок). " +
			"Не задано — \"лицом\" считается сторона, откуда на объект смотрит Camera Target")]
		[SerializeField] private Transform frontReference;

		[Header("Events")]
		public UnityEvent OnInteractionStart;
		public UnityEvent OnInteractionEnd;

		// кто сейчас с нами взаимодействует — чтобы ExitInteraction() из UI знал, кого отпускать
		private PlayerCameraFocus _activeFocus;

		public LocalizedString InteractionPrompt => interactionPrompt;
		public InteractionType Type => interactionType;
		public Transform CameraTarget => cameraTarget;
		public Transform PlayerRepositionPoint => playerRepositionPoint;
		public bool RepositionOnStaticLook => interactionType == InteractionType.StaticLook && repositionOnStaticLook;
		public bool IsInUse => _activeFocus != null;

		// true — выход по exitKey (Escape) в StaticLook обрабатывает сам объект, а не PlayerCameraFocus.
		// Нужно, когда у объекта своя навигация по Escape (например, TerminalMainController: в полноэкранном
		// окне Escape — "назад в меню", а не выход). Объект тогда сам вызывает ExitInteraction(), когда нужно.
		// Выставляется из кода таким компонентом (не в инспекторе) — так его нельзя забыть включить
		public bool HandlesExitKeyExternally { get; set; }

		public void Interact(GameObject interactor)
		{
			if (IsInUse) return;

			// PlayerInteractor передаёт не обязательно корень игрока, а свой объект — ищем вверх по иерархии
			PlayerCameraFocus focus = interactor.GetComponentInParent<PlayerCameraFocus>();
			if (focus == null)
			{
				Debug.LogError($"{nameof(InteractableFocusObject)} на {name}: у {interactor.name} " +
					$"(и его родителей) нет компонента {nameof(PlayerCameraFocus)}", this);
				return;
			}

			if (focus.BeginInteraction(this)) _activeFocus = focus;
		}

		// Игрок должен быть перед "лицом" объекта. Сравниваются только горизонтальные направления — рост игрока
		// и высота экрана на результат не влияют
		public bool CanInteractFrom(Vector3 viewerPosition)
		{
			if (!restrictApproachAngle || approachAngle >= 360f) return true;

			GetFront(out Vector3 pivot, out Vector3 front);
			Vector3 toViewer = Vector3.ProjectOnPlane(viewerPosition - pivot, Vector3.up);
			front = Vector3.ProjectOnPlane(front, Vector3.up);
			if (toViewer.sqrMagnitude < 0.0001f || front.sqrMagnitude < 0.0001f) return true;

			return Vector3.Angle(front, toViewer) <= approachAngle * 0.5f;
		}

		// pivot — откуда отсчитывается угол, front — направление "наружу" из лица объекта
		private void GetFront(out Vector3 pivot, out Vector3 front)
		{
			if (frontReference != null)
			{
				pivot = frontReference.position;
				front = frontReference.forward;
				return;
			}

			pivot = transform.position;
			// Camera Target смотрит НА объект — значит, лицо объекта обращено в обратную сторону
			front = cameraTarget != null ? -cameraTarget.forward : transform.forward;
		}

		// Для кнопки "Закрыть"/"Выход" в UI терминала — привязывается в инспекторе как обычный метод
		public void ExitInteraction()
		{
			if (_activeFocus != null) _activeFocus.ExitInteraction();
		}

		// Вызываются только из PlayerCameraFocus
		internal void RaiseInteractionStart()
		{
			OnInteractionStart?.Invoke();
		}

		internal void RaiseInteractionEnd()
		{
			_activeFocus = null;
			OnInteractionEnd?.Invoke();
		}

		private void OnDrawGizmosSelected()
		{
			// камера: сфера + луч взгляда; игрок: капсула-"ноги" + направление
			if (cameraTarget != null)
			{
				Gizmos.color = Color.cyan;
				Gizmos.DrawWireSphere(cameraTarget.position, 0.05f);
				Gizmos.DrawRay(cameraTarget.position, cameraTarget.forward * 0.5f);
			}
			if (playerRepositionPoint != null)
			{
				Gizmos.color = Color.yellow;
				Gizmos.DrawWireSphere(playerRepositionPoint.position + Vector3.up * 0.1f, 0.3f);
				Gizmos.DrawRay(playerRepositionPoint.position + Vector3.up * 1.5f,
					Quaternion.Euler(0f, playerRepositionPoint.eulerAngles.y, 0f) * Vector3.forward * 0.6f);
			}

			// сектор, из которого можно взаимодействовать: две границы + дуга
			if (restrictApproachAngle && approachAngle < 360f)
			{
				GetFront(out Vector3 pivot, out Vector3 front);
				front = Vector3.ProjectOnPlane(front, Vector3.up);
				if (front.sqrMagnitude < 0.0001f) return;
				front.Normalize();

				const float radius = 1.5f;
				const int segments = 24;
				float half = approachAngle * 0.5f;

				Gizmos.color = Color.green;
				Vector3 previous = pivot + Quaternion.Euler(0f, -half, 0f) * front * radius;
				Gizmos.DrawLine(pivot, previous);
				for (int i = 1; i <= segments; i++)
				{
					float angle = Mathf.Lerp(-half, half, i / (float)segments);
					Vector3 point = pivot + Quaternion.Euler(0f, angle, 0f) * front * radius;
					Gizmos.DrawLine(previous, point);
					previous = point;
				}
				Gizmos.DrawLine(pivot, previous);
			}
		}
	}
}
