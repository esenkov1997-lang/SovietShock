using System.Collections;
using UnityEngine;

namespace Interactables
{
	// Простая дверь на повороте вокруг своей оси (петля = pivot этого объекта в редакторе).
	// Open()/Close() — публичные методы без параметров: их дёргает BaseTrigger через UnityEvent
	// (см. инструкцию по триггер-системе) или TriggerAction. Дверь ничего не знает про триггеры —
	// её можно с тем же успехом открыть кнопкой в UI или Animation Event.
	public class Door : MonoBehaviour
	{
		[Tooltip("На сколько градусов открывается дверь относительно стартового поворота")]
		[SerializeField] private float openAngle = 90f;
		[Tooltip("Скорость поворота, градусов в секунду")]
		[SerializeField] private float openSpeed = 120f;

		private Quaternion _closedRotation;
		private Quaternion _openRotation;
		private Coroutine _routine;

		private void Awake()
		{
			_closedRotation = transform.localRotation;
			_openRotation = _closedRotation * Quaternion.Euler(0f, openAngle, 0f);
		}

		public void Open() => StartRotating(_openRotation);

		public void Close() => StartRotating(_closedRotation);

		private void StartRotating(Quaternion target)
		{
			if (_routine != null) StopCoroutine(_routine);
			_routine = StartCoroutine(RotateTo(target));
		}

		private IEnumerator RotateTo(Quaternion target)
		{
			while (Quaternion.Angle(transform.localRotation, target) > 0.5f)
			{
				transform.localRotation = Quaternion.RotateTowards(transform.localRotation, target, openSpeed * Time.deltaTime);
				yield return null;
			}
			transform.localRotation = target;
		}
	}
}
