using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Enemies
{
	public enum EnemyVoiceEvent
	{
		Alert,     // заметил игрока (лай)
		Grab,      // держит игрока в синхронной атаке (рычание)
		Retreat,   // отступает раненый (скулёж)
		Death,     // погиб (скулёж)
		Hurt,      // получил попадание (визг)
		Suspicious,// что-то видит, но ещё не уверен (тихое рычание)
		ChaseBark  // бежит к игроку — лает время от времени
	}

	// Голос врага: на каждое событие — свой набор клипов (лай, рычание, скулёж). Мозг (DogAI) говорит,
	// КОГДА звучать, этот компонент — ЧЕМ: случайный клип без повтора подряд, случайный питч, пауза между репликами.
	//
	// Реплики не обрывают друг друга: у голоса два источника звука, новая реплика звучит на свободном, а текущая
	// доигрывает до конца (визг от предпоследней пули + предсмертный скулёж от последней). Обрывается только
	// самая незначительная реплика — если оба источника заняты, а новая важнее. Пока звучит что-то важное
	// (визг, скулёж), менее важное (лай, подозрение) просто пропускается.
	//
	// AudioSource лучше повесить на кость головы — тогда после смерти звук идёт от падающей головы ragdoll.
	// Второй источник создаётся рядом с ним сам, с теми же настройками.
	//
	// Лай (при обнаружении и в погоне) может быть ШУМОМ для других врагов (Alert Noise Radius): соседние собаки
	// услышат и придут проверить — стая поднимается вместе.
	public class EnemyVoice : MonoBehaviour
	{
		[Serializable]
		public class VoiceLine
		{
			[Tooltip("Варианты звука — каждый раз случайный, без повтора одного и того же подряд")]
			public AudioClip[] Clips;
			[Range(0f, 1f)]
			public float Volume = 1f;
			[Tooltip("Случайный питч на каждую реплику — одни и те же клипы звучат разнообразнее")]
			public Vector2 PitchRange = new Vector2(0.92f, 1.08f);
			[Tooltip("Минимальная пауза (сек) между репликами этого события — чтобы собака не лаяла пулемётной очередью")]
			public float MinInterval = 3f;
			[Tooltip("Если больше Min Interval — пауза случайная между ними (естественнее для периодического лая). 0 — ровно Min Interval")]
			public float MaxInterval;

			[NonSerialized] public AudioClip LastClip;
			[NonSerialized] public float NextAllowedTime;
		}

		// один канал голоса: источник + что на нём сейчас звучит
		private class Channel
		{
			public AudioSource Source;
			public int Priority;
		}

		[Tooltip("Источник звука голоса. Лучше на кости головы (3D, Spatial Blend = 1). Если пусто — создаётся на этом объекте")]
		[SerializeField] private AudioSource audioSource;

		[Header("Реплики")]
		[Tooltip("Заметил игрока — лай")]
		[SerializeField] private VoiceLine alert = new VoiceLine();
		[Tooltip("Бежит к игроку и видит его — лает время от времени. Если клипов нет — берутся клипы Alert")]
		[SerializeField] private VoiceLine chase = new VoiceLine { MinInterval = 2f, MaxInterval = 4.5f };
		[Tooltip("Держит игрока (синхронная атака) — рычание. Звучит непрерывно: клипы сменяют друг друга, пока захват идёт")]
		[SerializeField] private VoiceLine grab = new VoiceLine { MinInterval = 0f };
		[Tooltip("Отступает раненый — скулёж")]
		[SerializeField] private VoiceLine retreat = new VoiceLine();
		[Tooltip("Погиб — скулёж")]
		[SerializeField] private VoiceLine death = new VoiceLine { MinInterval = 0f };
		[Tooltip("Получил попадание — визг. Пауза защищает от визга на каждую пулю автоматной очереди")]
		[SerializeField] private VoiceLine hurt = new VoiceLine { MinInterval = 0.6f };
		[Tooltip("Видит игрока, но ещё не уверен (шар осведомлённости жёлтый) — тихое рычание. Громкость лучше пониже")]
		[SerializeField] private VoiceLine suspicious = new VoiceLine { Volume = 0.5f, MinInterval = 4f };

		[Header("Лай как сигнал стае")]
		[Tooltip("Лай (при обнаружении и в погоне) — шум для других врагов этого радиуса (м): они придут проверить. 0 — выключено")]
		[SerializeField] private float alertNoiseRadius = 15f;

		private Channel[] _channels;
		private VoiceLine _looping;
		private int _loopingPriority;
		private Channel _loopChannel;

		private void Awake()
		{
			if (audioSource == null)
			{
				audioSource = gameObject.AddComponent<AudioSource>();
				audioSource.playOnAwake = false;
				audioSource.spatialBlend = 1f;
				audioSource.minDistance = 1.5f;
				audioSource.maxDistance = 30f;
				audioSource.rolloffMode = AudioRolloffMode.Linear;
			}

			_channels = new[]
			{
				new Channel { Source = audioSource },
				new Channel { Source = CloneSource(audioSource) }
			};
		}

		// одна реплика; false — не прозвучала (нет клипов, ещё не прошла пауза или звучит что-то важнее)
		public bool Play(EnemyVoiceEvent voiceEvent)
		{
			VoiceLine line = GetLine(voiceEvent);
			int priority = GetPriority(voiceEvent);
			if (voiceEvent == EnemyVoiceEvent.Death) StopLoop(); // мёртвая собака больше не рычит

			// пока звучит что-то важнее — менее важная реплика пропускается (лай не лезет поверх визга)
			if (HighestPlayingPriority() > priority) return false;
			if (!PlayLine(line, priority, ignoreInterval: voiceEvent == EnemyVoiceEvent.Death)) return false;

			if ((voiceEvent == EnemyVoiceEvent.Alert || voiceEvent == EnemyVoiceEvent.ChaseBark) && alertNoiseRadius > 0f)
				NoiseSystem.Emit(audioSource.transform.position, alertNoiseRadius, gameObject);
			return true;
		}

		// реплики этого события сменяют друг друга без пауз, пока не вызван StopLoop (рычание во время захвата)
		public void PlayLoop(EnemyVoiceEvent voiceEvent)
		{
			_looping = GetLine(voiceEvent);
			_loopingPriority = GetPriority(voiceEvent);
			_loopChannel = null;
			PlayLoopClip();
		}

		// больше не повторять — но текущий клип доигрывает до конца, а не обрывается
		public void StopLoop()
		{
			_looping = null;
			_loopChannel = null;
		}

		private void Update()
		{
			// клип петли доиграл — следующий из того же набора
			if (_looping != null && (_loopChannel == null || !_loopChannel.Source.isPlaying)) PlayLoopClip();
		}

		private void PlayLoopClip()
		{
			_loopChannel = PlayLine(_looping, _loopingPriority, ignoreInterval: true) ? _lastChannel : null;
		}

		private Channel _lastChannel;

		private bool PlayLine(VoiceLine line, int priority, bool ignoreInterval)
		{
			if (line.Clips == null || line.Clips.Length == 0) return false;
			if (!ignoreInterval && Time.time < line.NextAllowedTime) return false;

			Channel channel = PickChannel(priority);
			if (channel == null) return false;

			AudioClip clip = PickClip(line);
			if (clip == null) return false;

			line.LastClip = clip;
			float interval = line.MaxInterval > line.MinInterval ? Random.Range(line.MinInterval, line.MaxInterval) : line.MinInterval;
			line.NextAllowedTime = Time.time + interval;

			channel.Priority = priority;
			channel.Source.clip = clip;
			channel.Source.volume = line.Volume;
			channel.Source.pitch = Random.Range(line.PitchRange.x, line.PitchRange.y);
			channel.Source.Play();
			_lastChannel = channel;
			return true;
		}

		// свободный канал; если оба заняты — тот, где звучит менее важное, и только если новая реплика не менее важна
		private Channel PickChannel(int priority)
		{
			Channel weakest = null;
			foreach (Channel channel in _channels)
			{
				if (!channel.Source.isPlaying) return channel;
				if (weakest == null || channel.Priority < weakest.Priority) weakest = channel;
			}
			return weakest != null && weakest.Priority <= priority ? weakest : null;
		}

		private int HighestPlayingPriority()
		{
			int highest = int.MinValue;
			foreach (Channel channel in _channels)
				if (channel.Source.isPlaying && channel.Priority > highest) highest = channel.Priority;
			return highest;
		}

		private static AudioClip PickClip(VoiceLine line)
		{
			AudioClip[] clips = line.Clips;
			if (clips.Length == 1) return clips[0];

			int lastIndex = line.LastClip != null ? Array.IndexOf(clips, line.LastClip) : -1;
			if (lastIndex < 0) return clips[Random.Range(0, clips.Length)];

			// индекс прошлого клипа пропускается арифметикой — без цикла "крутить, пока не выпадет другой"
			int index = Random.Range(0, clips.Length - 1);
			if (index >= lastIndex) index++;
			return clips[index];
		}

		// второй источник рядом с первым, с теми же настройками звучания
		private static AudioSource CloneSource(AudioSource original)
		{
			AudioSource clone = original.gameObject.AddComponent<AudioSource>();
			clone.playOnAwake = false;
			clone.outputAudioMixerGroup = original.outputAudioMixerGroup;
			clone.spatialBlend = original.spatialBlend;
			clone.minDistance = original.minDistance;
			clone.maxDistance = original.maxDistance;
			clone.rolloffMode = original.rolloffMode;
			if (original.rolloffMode == AudioRolloffMode.Custom)
				clone.SetCustomCurve(AudioSourceCurveType.CustomRolloff, original.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
			clone.dopplerLevel = original.dopplerLevel;
			clone.spread = original.spread;
			clone.priority = original.priority;
			clone.reverbZoneMix = original.reverbZoneMix;
			return clone;
		}

		// важность реплики: смерть > боль/отступление > захват/лай > подозрение
		private static int GetPriority(EnemyVoiceEvent voiceEvent)
		{
			switch (voiceEvent)
			{
				case EnemyVoiceEvent.Death: return 3;
				case EnemyVoiceEvent.Hurt:
				case EnemyVoiceEvent.Retreat: return 2;
				case EnemyVoiceEvent.Grab:
				case EnemyVoiceEvent.Alert:
				case EnemyVoiceEvent.ChaseBark: return 1;
				default: return 0;
			}
		}

		private VoiceLine GetLine(EnemyVoiceEvent voiceEvent)
		{
			switch (voiceEvent)
			{
				case EnemyVoiceEvent.Alert: return alert;
				case EnemyVoiceEvent.ChaseBark: return chase.Clips != null && chase.Clips.Length > 0 ? chase : alert;
				case EnemyVoiceEvent.Grab: return grab;
				case EnemyVoiceEvent.Retreat: return retreat;
				case EnemyVoiceEvent.Hurt: return hurt;
				case EnemyVoiceEvent.Suspicious: return suspicious;
				default: return death;
			}
		}
	}
}
