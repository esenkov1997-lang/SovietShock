using UnityEngine;

namespace Weapons
{
	// Один зацикленный эффект горелки (искры или дым) в точке касания луча с поверхностью — см. WeaponController.
	// Экземпляр создаётся из префаба один раз и дальше только переставляется каждый кадр. Живёт в мире, а не
	// в модели оружия — иначе попал бы на слой оружия и рисовался бы WeaponCamera поверх стен.
	// Звук эффекта (если нужен) — Sound.ParticleEmissionAudio на самом префабе: он следит за эмиссией сам.
	public class RepairSurfaceEffect
	{
		// отступ от поверхности по нормали — чтобы частицы не рождались внутри коллайдера
		private const float SurfaceOffset = 0.01f;

		private ParticleSystem _instance;
		// из какого префаба создан экземпляр — у другого инструмента может быть свой
		private GameObject _source;

		// ставит эффект в точку касания (ось Z — по нормали поверхности) и включает эмиссию, если она ещё не идёт.
		// prefab не задан — эффект просто гаснет
		public void Play(GameObject prefab, RaycastHit hit)
		{
			if (prefab == null)
			{
				Stop();
				return;
			}

			if (_instance == null || _source != prefab)
			{
				Dispose();

				GameObject instance = Object.Instantiate(prefab);
				_instance = instance.GetComponentInChildren<ParticleSystem>();
				_source = prefab;
				if (_instance == null)
				{
					Debug.LogWarning($"{prefab.name}: в префабе эффекта горелки нет ParticleSystem", prefab);
					Object.Destroy(instance);
					return;
				}
				// Play On Awake в префабе не должен выстрелить эффектом в точке спавна — эмиссию включаем сами ниже
				_instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			}

			_instance.transform.SetPositionAndRotation(hit.point + hit.normal * SurfaceOffset, Quaternion.LookRotation(hit.normal));
			if (!_instance.isEmitting) _instance.Play(true);
		}

		// гасит эмиссию, но уже вылетевшие частицы догорают сами, а не пропадают мгновенно
		public void Stop()
		{
			if (_instance != null && _instance.isEmitting)
			{
				_instance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
			}
		}

		public void Dispose()
		{
			if (_instance != null) Object.Destroy(_instance.transform.root.gameObject);
			_instance = null;
			_source = null;
		}
	}
}
