using UnityEngine;

namespace Sound
{
	// Несколько AudioSource для звуков интерфейса, по очереди. AudioSource-шаблон (на том же объекте) задаёт микшер,
	// 2D/3D и затухание; из него на старте делаются копии. Pitch у AudioSource один на все звуки, что в нём играют, —
	// с одним источником быстрые щелчки меняли бы высоту ещё не доигравшего предыдущего.
	public sealed class SoundVoices
	{
		private readonly AudioSource[] _sources;
		private int _next;

		public SoundVoices(AudioSource template, int count)
		{
			template.playOnAwake = false;

			_sources = new AudioSource[Mathf.Max(1, count)];
			_sources[0] = template;
			for (int i = 1; i < _sources.Length; i++)
			{
				AudioSource copy = template.gameObject.AddComponent<AudioSource>();
				CopySettings(template, copy);
				_sources[i] = copy;
			}
		}

		public void Play(RandomSound sound)
		{
			AudioClip clip = sound?.PickClip();
			if (clip == null) return;

			AudioSource source = _sources[_next];
			_next = (_next + 1) % _sources.Length;

			source.pitch = Random.Range(sound.pitchRange.x, sound.pitchRange.y);
			source.PlayOneShot(clip, sound.volume);
		}

		// всё, что влияет на то, как звук слышен: микшер, 2D/3D, затухание по расстоянию
		private static void CopySettings(AudioSource from, AudioSource to)
		{
			to.playOnAwake = false;
			to.outputAudioMixerGroup = from.outputAudioMixerGroup;
			to.volume = from.volume;
			to.priority = from.priority;
			to.spatialBlend = from.spatialBlend;
			to.spread = from.spread;
			to.dopplerLevel = from.dopplerLevel;
			to.rolloffMode = from.rolloffMode;
			to.minDistance = from.minDistance;
			to.maxDistance = from.maxDistance;
			to.reverbZoneMix = from.reverbZoneMix;
			to.bypassEffects = from.bypassEffects;
			to.bypassListenerEffects = from.bypassListenerEffects;
			to.bypassReverbZones = from.bypassReverbZones;
			to.ignoreListenerPause = from.ignoreListenerPause;
			if (from.rolloffMode == AudioRolloffMode.Custom)
			{
				to.SetCustomCurve(AudioSourceCurveType.CustomRolloff, from.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
			}
		}
	}
}
