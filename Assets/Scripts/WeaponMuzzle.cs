using System.Collections;
using UnityEngine;

namespace Weapons
{
	// Повесь на дочерний Transform у дула InHandPrefab, чтобы WeaponController знал, откуда спавнить
	// пулю. Если маркера нет — пуля спавнится из самого WeaponHolder.
	//
	// Заодно управляет визуальными эффектами дула — партиклами (огонь/искры) и/или простым мешем со
	// своим шейдером (например, у ключа-горелки — меш пламени вместо партиклов). Можно использовать
	// и то, и другое одновременно, оба включаются вместе. Отдельно от MuzzleFlash, который отвечает
	// только за свет. WeaponController вызывает Flash()/SetContinuous() у обоих компонентов синхронно.
	public class WeaponMuzzle : MonoBehaviour
	{
		[Tooltip("Опционально — партиклы огня/искр у дула. Если не задано, ищется на этом же объекте или в детях")]
		[SerializeField] private ParticleSystem particles;
		[Tooltip("Опционально — меш со своим шейдером вместо (или вместе с) партиклов, например пламя горелки. Просто переключается через Renderer.enabled")]
		[SerializeField] private Renderer flameMesh;
		[Tooltip("Сколько секунд виден flameMesh при разовой Flash(). На непрерывный режим (SetContinuous) не влияет")]
		[SerializeField] private float flashDuration = 0.05f;

		private Coroutine _meshFlashRoutine;

		private void Awake()
		{
			if (particles == null) particles = GetComponentInChildren<ParticleSystem>();
			if (flameMesh != null) flameMesh.enabled = false;
		}

		// разовый всплеск — для обычного выстрела
		public void Flash()
		{
			if (particles != null) particles.Play();

			if (flameMesh == null) return;

			if (_meshFlashRoutine != null) StopCoroutine(_meshFlashRoutine);
			_meshFlashRoutine = StartCoroutine(MeshFlashRoutine());
		}

		// непрерывный режим для горелки/лечения — держит партиклы/меш включёнными, пока on == true.
		// Вызывать каждый кадр с текущим состоянием
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
			flameMesh.enabled = on;
		}

		private IEnumerator MeshFlashRoutine()
		{
			flameMesh.enabled = true;
			yield return new WaitForSeconds(flashDuration);
			flameMesh.enabled = false;
		}
	}
}
