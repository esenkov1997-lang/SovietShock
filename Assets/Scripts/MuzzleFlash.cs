using System.Collections;
using Lighting;
using UnityEngine;

namespace Weapons
{
	// Вешается на Light рядом с дулом (обычно там же, где WeaponMuzzle). WeaponController вызывает
	// публичный Flash() при каждом выстреле — не завязано на конкретную анимацию/клип.
	// Партиклы дула (огонь/искры) — отдельная ответственность WeaponMuzzle, не этого компонента.
	//
	// Если на том же объекте есть Lighting.LightEffect (например, дрожащий свет горелки), этот компонент
	// только включает/выключает его через SetOn, а яркость, фликер и плавность целиком задаются в LightEffect —
	// FlashIntensity тогда не используется. Start On у такого LightEffect стоит выключить
	[RequireComponent(typeof(Light))]
	public class MuzzleFlash : MonoBehaviour
	{
		[Tooltip("Сколько секунд горит вспышка")]
		public float FlashDuration = 0.05f;
		[Tooltip("Интенсивность света во время вспышки. Не используется, если на объекте есть LightEffect")]
		public float FlashIntensity = 8f;

		private Light _light;
		private LightEffect _effect;
		private Coroutine _flashRoutine;

		private void Awake()
		{
			_light = GetComponent<Light>();
			_effect = GetComponent<LightEffect>();
			_light.enabled = false;
		}

		// LightEffect в своём Awake выставляет состояние из Start On — гасим его уже после всех Awake,
		// мгновенно (без fade), чтобы свет горелки не горел до первого выстрела/ремонта
		private void Start()
		{
			if (_effect != null) _effect.SetOn(false, instant: true);
		}

		public void Flash()
		{
			if (_flashRoutine != null) StopCoroutine(_flashRoutine);
			_flashRoutine = StartCoroutine(FlashRoutine());
		}

		// непрерывный режим для горелки/лечения — держит свет включённым, пока on == true, без
		// автовыключения по таймеру (в отличие от Flash())
		public void SetContinuous(bool on)
		{
			if (_flashRoutine != null)
			{
				StopCoroutine(_flashRoutine);
				_flashRoutine = null;
			}

			SetLit(on);
		}

		private IEnumerator FlashRoutine()
		{
			SetLit(true);
			yield return new WaitForSeconds(FlashDuration);
			SetLit(false);
		}

		private void SetLit(bool on)
		{
			if (_effect != null)
			{
				_effect.SetOn(on);
				return;
			}

			_light.intensity = FlashIntensity;
			_light.enabled = on;
		}
	}
}
