using System.Collections;
using UnityEngine;

namespace Weapons
{
	// Вешается на Light рядом с дулом (обычно там же, где WeaponMuzzle). WeaponController вызывает
	// публичный Flash() при каждом выстреле — не завязано на конкретную анимацию/клип.
	// Партиклы дула (огонь/искры) — отдельная ответственность WeaponMuzzle, не этого компонента.
	[RequireComponent(typeof(Light))]
	public class MuzzleFlash : MonoBehaviour
	{
		[Tooltip("Сколько секунд горит вспышка")]
		public float FlashDuration = 0.05f;
		[Tooltip("Интенсивность света во время вспышки")]
		public float FlashIntensity = 8f;

		private Light _light;
		private Coroutine _flashRoutine;

		private void Awake()
		{
			_light = GetComponent<Light>();
			_light.enabled = false;
		}

		public void Flash()
		{
			if (_flashRoutine != null) StopCoroutine(_flashRoutine);
			_flashRoutine = StartCoroutine(FlashRoutine());
		}

		// непрерывный режим для горелки/лечения — держит свет включённым, пока on == true, без
		// автовыключения по таймеру (в отличие от Flash()). Вызывать каждый кадр с текущим состоянием
		public void SetContinuous(bool on)
		{
			if (_flashRoutine != null)
			{
				StopCoroutine(_flashRoutine);
				_flashRoutine = null;
			}

			_light.intensity = FlashIntensity;
			_light.enabled = on;
		}

		private IEnumerator FlashRoutine()
		{
			_light.intensity = FlashIntensity;
			_light.enabled = true;
			yield return new WaitForSeconds(FlashDuration);
			_light.enabled = false;
		}
	}
}
