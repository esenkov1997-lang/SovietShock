using System;
using UnityEngine;

namespace Interactables
{
	// Звуки интерфейса терминала. Вешается на КОРЕНЬ префаба терминала (рядом с TerminalInstance) вместе с
	// AudioSource — звук идёт из самого терминала в мире. TerminalMainController и модули (TerminalApp)
	// находят этот компонент сами (GetComponentInParent) и вызывают нужный звук — вручную связывать не нужно.
	//
	// AudioSource на объекте служит ШАБЛОНОМ: на старте из него делается несколько копий (Voices), и звуки
	// играют по очереди из разных копий. Pitch у AudioSource один на все звуки, что в нём играют, — с одним
	// источником быстрое листание меняло бы высоту ещё не доигравшего предыдущего щелчка.
	[DisallowMultipleComponent]
	[RequireComponent(typeof(AudioSource))]
	public class TerminalAudio : MonoBehaviour
	{
		[Serializable]
		public class Sound
		{
			[Tooltip("Варианты звука — каждый раз выбирается случайный, но не тот же, что в прошлый раз")]
			public AudioClip[] clips = new AudioClip[0];
			[Range(0f, 1f)] public float volume = 1f;
			[Tooltip("Случайный pitch в этом диапазоне при каждом проигрывании (1 — без изменений)")]
			public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

			[NonSerialized] public AudioClip lastClip;

			public AudioClip PickClip()
			{
				if (clips == null || clips.Length == 0) return null;
				if (clips.Length == 1) return clips[0];

				// случайный из всех, кроме прошлого: берём индекс из (n-1) и "перепрыгиваем" прошлый
				int lastIndex = Array.IndexOf(clips, lastClip);
				int index = UnityEngine.Random.Range(0, lastIndex >= 0 ? clips.Length - 1 : clips.Length);
				if (lastIndex >= 0 && index >= lastIndex) index++;
				return clips[index];
			}
		}

		[Header("Навигация")]
		[Tooltip("Перемещение выделения стрелками: окна меню, список писем, переключение колонок")]
		[SerializeField] private Sound navigate = new Sound { pitchRange = new Vector2(0.93f, 1.07f) };
		[Tooltip("Enter — открыть модуль (окно меню)")]
		[SerializeField] private Sound confirm = new Sound();
		[Tooltip("Escape — шаг назад внутри терминала (из модуля в меню, из переписки в список)")]
		[SerializeField] private Sound back = new Sound();

		[Header("Вход / выход")]
		[Tooltip("Игрок начал работать с терминалом")]
		[SerializeField] private Sound enter = new Sound { pitchRange = new Vector2(0.98f, 1.02f) };
		[Tooltip("Игрок вышел из терминала")]
		[SerializeField] private Sound exit = new Sound { pitchRange = new Vector2(0.98f, 1.02f) };

		[Header("Пароль")]
		[Tooltip("Нажатие клавиши при вводе пароля (ввод и стирание символа)")]
		[SerializeField] private Sound typing = new Sound { pitchRange = new Vector2(0.9f, 1.1f) };
		[Tooltip("Верный пароль")]
		[SerializeField] private Sound accessGranted = new Sound { pitchRange = Vector2.one };
		[Tooltip("Неверный пароль")]
		[SerializeField] private Sound accessDenied = new Sound { pitchRange = new Vector2(0.97f, 1.03f) };

		[Header("Voices")]
		[Tooltip("Сколько звуков могут звучать одновременно, не мешая pitch друг другу")]
		[Range(1, 8)]
		[SerializeField] private int voices = 4;

		private AudioSource[] _sources;
		private int _nextSource;

		public void PlayNavigate() => Play(navigate);
		public void PlayConfirm() => Play(confirm);
		public void PlayBack() => Play(back);
		public void PlayEnter() => Play(enter);
		public void PlayExit() => Play(exit);
		public void PlayTyping() => Play(typing);
		public void PlayAccessGranted() => Play(accessGranted);
		public void PlayAccessDenied() => Play(accessDenied);

		private void Awake()
		{
			AudioSource template = GetComponent<AudioSource>();
			template.playOnAwake = false;

			_sources = new AudioSource[Mathf.Max(1, voices)];
			_sources[0] = template;
			for (int i = 1; i < _sources.Length; i++)
			{
				AudioSource copy = gameObject.AddComponent<AudioSource>();
				CopySettings(template, copy);
				_sources[i] = copy;
			}
		}

		private void Play(Sound sound)
		{
			if (sound == null || _sources == null) return;

			AudioClip clip = sound.PickClip();
			if (clip == null) return;
			sound.lastClip = clip;

			AudioSource source = _sources[_nextSource];
			_nextSource = (_nextSource + 1) % _sources.Length;

			source.pitch = UnityEngine.Random.Range(sound.pitchRange.x, sound.pitchRange.y);
			source.PlayOneShot(clip, sound.volume);
		}

		// всё, что влияет на то, как звук слышен в мире: микшер, 3D, затухание по расстоянию
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
			if (from.rolloffMode == AudioRolloffMode.Custom)
			{
				to.SetCustomCurve(AudioSourceCurveType.CustomRolloff, from.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
			}
		}
	}
}
