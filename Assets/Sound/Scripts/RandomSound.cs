using System;
using UnityEngine;

namespace Sound
{
	// Один звук интерфейса с вариантами: при каждом проигрывании — случайный клип (не тот же, что в прошлый раз),
	// своя громкость и случайный pitch. Играется через SoundVoices. Общий для терминала, инвентаря и т.п.
	[Serializable]
	public class RandomSound
	{
		[Tooltip("Варианты звука — каждый раз выбирается случайный, но не тот же, что в прошлый раз")]
		public AudioClip[] clips = new AudioClip[0];
		[Range(0f, 1f)] public float volume = 1f;
		[Tooltip("Случайный pitch в этом диапазоне при каждом проигрывании (1 — без изменений)")]
		public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

		[NonSerialized] private AudioClip _lastClip;

		public bool IsEmpty => clips == null || clips.Length == 0;

		// следующий клип; null — клипов нет
		public AudioClip PickClip()
		{
			if (IsEmpty) return null;
			if (clips.Length == 1) return _lastClip = clips[0];

			// случайный из всех, кроме прошлого: берём индекс из (n-1) и "перепрыгиваем" прошлый
			int lastIndex = Array.IndexOf(clips, _lastClip);
			int index = UnityEngine.Random.Range(0, lastIndex >= 0 ? clips.Length - 1 : clips.Length);
			if (lastIndex >= 0 && index >= lastIndex) index++;
			return _lastClip = clips[index];
		}
	}
}
