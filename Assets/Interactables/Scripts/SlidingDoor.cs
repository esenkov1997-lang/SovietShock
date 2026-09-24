using System.Collections;
using UnityEngine;

namespace Interactables
{
	// Слайд-дверь: двигается в сторону вдоль локальной оси на фиксированное расстояние вместо поворота
	// (см. Door.cs — дверь на петле). Open()/Close() — тот же публичный контракт без параметров: их дёргает
	// BaseTrigger через UnityEvent, TriggerAction или, как в этом случае, UnityEvent Repairable.OnFullyRepaired
	// (см. инструкцию по триггер-системе). Дверь ничего не знает про триггеры или кнопку с HP.
	public class SlidingDoor : MonoBehaviour
	{
		[Tooltip("На сколько метров дверь сдвигается по каждой оси (в локальных координатах) при полном открытии")]
		[SerializeField] private Vector3 slideOffset = new Vector3(2f, 0f, 0f);
		[Tooltip("Время полного хода из закрытого состояния в открытое (и обратно), секунды")]
		[SerializeField] private float moveDuration = 1f;
		[Tooltip("Кривая хода двери по нормализованному времени 0..1 — 0 соответствует закрытому положению, 1 — открытому. Используется одинаково и для открытия, и для закрытия (закрытие проигрывает её в обратную сторону)")]
		[SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		private Vector3 _closedPosition;
		private Vector3 _openPosition;

		// линейный прогресс 0..1 (0 = закрыто, 1 = открыто), от него берётся moveCurve.Evaluate.
		// Хранится отдельно от самой позиции, чтобы Open()/Close() могли развернуть движение в любой
		// момент без рывка — аналог Quaternion.RotateTowards в Door.cs, но с кривой вместо постоянной скорости
		private float _progress;
		private Coroutine _routine;

		// целевое состояние (куда дверь сейчас едет или уже приехала), а не факт завершения анимации —
		// кнопка-переключатель (см. DoorButton) должна знать, открывать или закрывать, даже пока дверь
		// ещё в движении
		public bool IsOpen { get; private set; }

		private void Awake()
		{
			_closedPosition = transform.localPosition;
			_openPosition = _closedPosition + slideOffset;
		}

		public void Open()
		{
			IsOpen = true;
			StartMoving(1f);
		}

		public void Close()
		{
			IsOpen = false;
			StartMoving(0f);
		}

		private void StartMoving(float target)
		{
			if (_routine != null) StopCoroutine(_routine);
			_routine = StartCoroutine(MoveTo(target));
		}

		private IEnumerator MoveTo(float target)
		{
			float speed = moveDuration > 0f ? 1f / moveDuration : float.MaxValue;

			while (!Mathf.Approximately(_progress, target))
			{
				_progress = Mathf.MoveTowards(_progress, target, speed * Time.deltaTime);
				transform.localPosition = Vector3.LerpUnclamped(_closedPosition, _openPosition, moveCurve.Evaluate(_progress));
				yield return null;
			}
		}
	}
}
