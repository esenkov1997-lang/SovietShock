using System.Collections;
using Lighting;
using UnityEngine;

namespace Weapons
{
	// Повесь на дочерний Transform у дула InHandPrefab, чтобы WeaponController знал, откуда спавнить
	// пулю. Если маркера нет — пуля спавнится из самого WeaponHolder.
	//
	// Заодно отвечает за ВСЮ визуальную часть дула, кроме света: партиклы (огонь/искры) и/или меш со
	// своим шейдером (например, у ключа-горелки — меш пламени вместо партиклов). Свет — отдельно,
	// MuzzleFlash (+ опционально Lighting.LightEffect на том же Light). WeaponController вызывает
	// Flash()/SetContinuous() у обоих компонентов синхронно.
	//
	// Меш пламени может плавно появляться/гаснуть через Float-параметр своего шейдера (flameFadeProperty,
	// например _Fade в Shader Graph: 0 — не видно, 1 — полностью) и мерцать синхронно со светом (flickerSource).
	// Меняется копия материала именно этого меша, общий материал-ассет не трогается.
	public class WeaponMuzzle : MonoBehaviour
	{
		[Tooltip("Опционально — партиклы огня/искр у дула. Если не задано, ищется на этом же объекте или в детях")]
		[SerializeField] private ParticleSystem particles;
		[Tooltip("Опционально — меш со своим шейдером вместо (или вместе с) партиклов, например пламя горелки")]
		[SerializeField] private Renderer flameMesh;
		[Tooltip("Сколько секунд виден flameMesh при разовой Flash(). На непрерывный режим (SetContinuous) не влияет")]
		[SerializeField] private float flashDuration = 0.05f;

		[Header("Flame Fade")]
		[Tooltip("Reference-имя Float-параметра в шейдере пламени (0 — не видно, 1 — полностью). Пусто или у шейдера нет такого параметра — меш просто включается/выключается без плавности")]
		[SerializeField] private string flameFadeProperty = "_Fade";
		[Tooltip("За сколько секунд пламя плавно появляется/гаснет. 0 — мгновенно")]
		[SerializeField] private float flameFadeDuration = 0.1f;
		[Tooltip("Опционально — LightEffect света дула: пламя мерцает синхронно с его фликером/пульсом")]
		[SerializeField] private LightEffect flickerSource;

		private Material _flameMaterial;
		private int _flameFadeId;
		private bool _hasFadeProperty;
		private bool _flameOn;
		private float _flameFade; // 0..1
		private Coroutine _meshFlashRoutine;

		private void Awake()
		{
			if (particles == null) particles = GetComponentInChildren<ParticleSystem>();

			if (flameMesh != null)
			{
				if (!string.IsNullOrEmpty(flameFadeProperty))
				{
					// .material — копия только для этого меша (не MaterialPropertyBlock: с SRP Batcher в URP
					// это рекомендованный способ, MPB выбивает рендерер из батчера и с Shader Graph ненадёжен)
					_flameMaterial = flameMesh.material;
					_flameFadeId = Shader.PropertyToID(flameFadeProperty);
					_hasFadeProperty = _flameMaterial.HasProperty(_flameFadeId);
					if (!_hasFadeProperty)
					{
						Debug.LogWarning($"WeaponMuzzle на {name}: у шейдера пламени нет Float-параметра {flameFadeProperty} — пламя будет включаться без плавности", this);
					}
				}

				ApplyFlame(0f);
			}
		}

		private void OnDestroy()
		{
			// копия из Renderer.material не удаляется сама вместе с объектом
			if (_flameMaterial != null) Destroy(_flameMaterial);
		}

		private void Update()
		{
			if (flameMesh == null) return;

			float target = _flameOn ? 1f : 0f;
			_flameFade = flameFadeDuration > 0f ? Mathf.MoveTowards(_flameFade, target, Time.deltaTime / flameFadeDuration) : target;

			float flicker = flickerSource != null ? flickerSource.EffectsMultiplier : 1f;
			ApplyFlame(_flameFade * flicker);
		}

		// разовый всплеск — для обычного выстрела
		public void Flash()
		{
			if (particles != null) particles.Play();

			if (flameMesh == null) return;

			if (_meshFlashRoutine != null) StopCoroutine(_meshFlashRoutine);
			_meshFlashRoutine = StartCoroutine(MeshFlashRoutine());
		}

		// непрерывный режим для горелки/лечения — держит партиклы/пламя включёнными, пока on == true
		public void SetContinuous(bool on)
		{
			if (particles != null)
			{
				if (on)
				{
					if (!particles.isPlaying) particles.Play();
				}
				// StopEmitting — новые частицы переставали рождаться, но уже вылетевшие ещё доживают/затухают
				// сами, а не исчезают мгновенно при отпускании кнопки
				else if (particles.isPlaying)
				{
					particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
				}
			}

			if (flameMesh == null) return;

			if (_meshFlashRoutine != null)
			{
				StopCoroutine(_meshFlashRoutine);
				_meshFlashRoutine = null;
			}
			_flameOn = on;
		}

		// вспышка выстрела — без плавного появления, иначе за 0.05 сек пламя просто не успело бы разгореться
		private IEnumerator MeshFlashRoutine()
		{
			_flameOn = true;
			_flameFade = 1f;
			yield return new WaitForSeconds(flashDuration);
			_flameOn = false;
			_meshFlashRoutine = null;
		}

		private void ApplyFlame(float level)
		{
			// полностью погасшее пламя не рисуем вовсе
			flameMesh.enabled = level > 0.001f;
			if (_hasFadeProperty) _flameMaterial.SetFloat(_flameFadeId, level);
		}
	}
}
