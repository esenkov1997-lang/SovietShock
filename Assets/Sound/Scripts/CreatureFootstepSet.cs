using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sound
{
	// Шаги конкретного существа (лапы собаки, копыта, когти...) по разным поверхностям.
	// Поверхность по-прежнему помечается своим SurfaceSoundSet через SoundSurface — как для шагов игрока,
	// ничего перенастраивать на уровне не нужно. А здесь для каждого такого материала указывается,
	// как по нему звучит именно это существо. Материал без своей строки звучит клипами Default.
	// Создаётся через Assets/Create/Audio/Creature Footstep Set, один ассет на тип существа.
	[CreateAssetMenu(fileName = "CreatureFootstepSet", menuName = "Audio/Creature Footstep Set")]
	public class CreatureFootstepSet : ScriptableObject
	{
		[Serializable]
		public class SurfaceEntry
		{
			[Tooltip("Материал поверхности — тот же SurfaceSoundSet, что висит на полу через SoundSurface")]
			public SurfaceSoundSet Surface;
			[Tooltip("Звуки шагов существа по этому материалу")]
			public AudioClip[] Clips;
			[Tooltip("Множитель громкости для этого материала (металл громче ковра)")]
			[Range(0f, 2f)]
			public float VolumeMultiplier = 1f;
		}

		[Tooltip("Звуки шагов по поверхностям без своей строки ниже (и без SoundSurface вообще)")]
		public AudioClip[] DefaultClips;
		[Tooltip("Отдельные звуки для конкретных материалов")]
		public SurfaceEntry[] Surfaces;
		[Tooltip("Случайный питч на каждый шаг")]
		public Vector2 PitchRange = new Vector2(0.9f, 1.1f);
		[Range(0f, 1f)]
		public float Volume = 0.6f;

		// exclude — клип с прошлого раза, чтобы не повторять его дважды подряд
		public AudioClip GetClip(SurfaceSoundSet surface, AudioClip exclude, out float volume)
		{
			volume = Volume;
			if (surface != null && Surfaces != null)
			{
				foreach (SurfaceEntry entry in Surfaces)
				{
					if (entry.Surface != surface || entry.Clips == null || entry.Clips.Length == 0) continue;
					volume *= entry.VolumeMultiplier;
					return PickRandom(entry.Clips, exclude);
				}
			}
			return PickRandom(DefaultClips, exclude);
		}

		// так же, как в SurfaceSoundSet: исключённый индекс пропускается арифметикой, без цикла пересэмплирования
		private static AudioClip PickRandom(AudioClip[] clips, AudioClip exclude)
		{
			if (clips == null || clips.Length == 0) return null;
			if (clips.Length == 1) return clips[0];

			int excludeIndex = exclude != null ? Array.IndexOf(clips, exclude) : -1;
			if (excludeIndex < 0) return clips[Random.Range(0, clips.Length)];

			int index = Random.Range(0, clips.Length - 1);
			if (index >= excludeIndex) index++;
			return clips[index];
		}
	}
}
