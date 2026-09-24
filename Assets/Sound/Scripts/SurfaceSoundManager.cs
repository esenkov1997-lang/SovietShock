using System.Collections.Generic;
using UnityEngine;

namespace Sound
{
	// Пул 3D-источников звука для ударов предметов и попаданий оружия по поверхностям. Звук играет не на самом предмете, а в точке
	// контакта: предмет могут уничтожить/подобрать посреди звука, и звук не должен оборваться, а число
	// одновременных голосов ограничено — при переполнении вытесняется самый старый, а не создаётся новый.
	// Создаётся сам при первом ударе; чтобы поменять настройки пула, поставь его на сцену вручную.
	public class SurfaceSoundManager : MonoBehaviour
	{
		[Tooltip("Сколько ударов может звучать одновременно")]
		[Min(1)]
		[SerializeField] private int _voiceCount = 16;
		[Tooltip("Дальше этой дистанции (м) удар не слышно")]
		[SerializeField] private float _maxDistance = 30f;

		private static SurfaceSoundManager _instance;

		public static SurfaceSoundManager Instance
		{
			get
			{
				// Awake создаваемого компонента сам выставит _instance и построит пул
				if (_instance == null) new GameObject("SurfaceSoundManager").AddComponent<SurfaceSoundManager>();
				return _instance;
			}
		}

		private AudioSource[] _sources;
		private float[] _startTimes;
		// по набору, а не по предмету: подряд идущие удары о дерево не повторяют клип, даже от разных предметов
		private readonly Dictionary<SurfaceSoundSet, AudioClip> _lastImpactClip = new Dictionary<SurfaceSoundSet, AudioClip>();
		private readonly Dictionary<SurfaceSoundSet, AudioClip> _lastHitClip = new Dictionary<SurfaceSoundSet, AudioClip>();

		private void Awake()
		{
			if (_instance != null && _instance != this)
			{
				Destroy(gameObject);
				return;
			}
			_instance = this;

			_sources = new AudioSource[_voiceCount];
			_startTimes = new float[_voiceCount];
			for (int i = 0; i < _voiceCount; i++)
			{
				GameObject voice = new GameObject("ImpactVoice " + i);
				voice.transform.SetParent(transform, false);
				AudioSource source = voice.AddComponent<AudioSource>();
				source.playOnAwake = false;
				source.spatialBlend = 1f;
				source.maxDistance = _maxDistance;
				_sources[i] = source;
			}
		}

		// impactSpeed — сила удара в м/с (уже с учётом множителя предмета); volumeScale — доп. множитель
		// громкости, например для второго слоя. Возвращает true, если звук реально сыграл
		public bool PlayImpact(SurfaceSoundSet surface, Vector3 position, float impactSpeed, float volumeScale = 1f)
		{
			if (surface == null || impactSpeed < surface.ImpactMinSpeed) return false;

			_lastImpactClip.TryGetValue(surface, out AudioClip lastClip);
			AudioClip clip = surface.GetImpactClip(impactSpeed, lastClip);
			if (clip == null) return false;
			_lastImpactClip[surface] = clip;

			// громкость растёт от 0.25 (а не от 0) — на пороге ImpactMinSpeed звук уже должен быть слышен,
			// а не затухать в ноль ровно там, где порог решил его проиграть
			float t = Mathf.InverseLerp(surface.ImpactMinSpeed, surface.ImpactHeavySpeed, impactSpeed);
			float volume = Mathf.Lerp(0.25f, 1f, t) * surface.ImpactVolume * volumeScale;

			Play(clip, position, volume, surface.ImpactPitchRange);
			return true;
		}

		// попадание оружия: сила не считается, громкость фиксированная (SurfaceSoundSet.HitVolume) —
		// пуля или удар и так всегда "полные", в отличие от падающего предмета
		public bool PlayHit(SurfaceSoundSet surface, HitType type, Vector3 position)
		{
			if (surface == null) return false;

			_lastHitClip.TryGetValue(surface, out AudioClip lastClip);
			AudioClip clip = surface.GetHitClip(type, lastClip);
			if (clip == null) return false;
			_lastHitClip[surface] = clip;

			Play(clip, position, surface.HitVolume, surface.HitPitchRange);
			return true;
		}

		private void Play(AudioClip clip, Vector3 position, float volume, Vector2 pitchRange)
		{
			int index = GetVoiceIndex();
			AudioSource source = _sources[index];
			source.transform.position = position;
			source.clip = clip;
			source.volume = Mathf.Clamp01(volume);
			source.pitch = Random.Range(pitchRange.x, pitchRange.y);
			source.Play();
			_startTimes[index] = Time.time;
		}

		private int GetVoiceIndex()
		{
			int oldest = 0;
			for (int i = 0; i < _sources.Length; i++)
			{
				if (!_sources[i].isPlaying) return i;
				if (_startTimes[i] < _startTimes[oldest]) oldest = i;
			}
			return oldest;
		}
	}
}
