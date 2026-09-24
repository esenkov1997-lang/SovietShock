using System;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Sound
{
	// Все звуки одного материала поверхности (бетон, дерево, металл...) в одном ассете: шаги, приземление
	// и удары предметов. Создаётся через Assets/Create/Audio/Surface Sound Set и вешается на объекты
	// компонентом SoundSurface. Шаги игрока (FootstepPlayer) и столкновения физических предметов
	// (CollisionSoundEmitter) читают из одного и того же ассета, поэтому материал настраивается в одном месте.
	[CreateAssetMenu(fileName = "SurfaceSoundSet", menuName = "Audio/Surface Sound Set")]
	public class SurfaceSoundSet : ScriptableObject
	{
		[Header("Footsteps")]
		[FormerlySerializedAs("Clips")]
		[Tooltip("Клипы шагов игрока по этой поверхности — при каждом шаге выбирается случайный")]
		public AudioClip[] FootstepClips;
		[FormerlySerializedAs("PitchRange")]
		[Tooltip("Случайный питч на каждый шаг, чтобы шаги не звучали одинаково")]
		public Vector2 FootstepPitchRange = new Vector2(0.95f, 1.05f);
		[FormerlySerializedAs("Volume")]
		[Range(0f, 1f)]
		public float FootstepVolume = 1f;

		[Header("Landing")]
		[Tooltip("Клипы приземления игрока после прыжка/падения на эту поверхность. Пусто — звук не проигрывается")]
		public AudioClip[] LandingClips;
		[Tooltip("Случайный питч приземления")]
		public Vector2 LandingPitchRange = new Vector2(0.95f, 1.05f);
		[Range(0f, 1f)]
		public float LandingVolume = 1f;

		[Header("Impact (удары предметов)")]
		[Tooltip("Слабые/медленные удары. Если уровень пуст, берётся ближайший непустой — можно заполнить только один")]
		public AudioClip[] LightImpactClips;
		[Tooltip("Средние удары")]
		public AudioClip[] MediumImpactClips;
		[Tooltip("Сильные удары — падение с высоты, бросок на полной скорости")]
		public AudioClip[] HeavyImpactClips;
		[Tooltip("Скорость удара (м/с вдоль нормали контакта), ниже которой звук не проигрывается — отсекает дребезг покоящихся предметов")]
		public float ImpactMinSpeed = 0.8f;
		[Tooltip("Скорость удара, с которой вместо Light играют клипы Medium")]
		public float ImpactMediumSpeed = 3f;
		[Tooltip("Скорость удара, с которой играют клипы Heavy; на ней же громкость достигает максимума")]
		public float ImpactHeavySpeed = 7f;
		[Tooltip("Случайный питч удара")]
		public Vector2 ImpactPitchRange = new Vector2(0.9f, 1.1f);
		[Range(0f, 1f)]
		public float ImpactVolume = 1f;

		[Header("Weapon Hit (попадания оружия)")]
		[Tooltip("Попадание пули/выстрела по этой поверхности")]
		public AudioClip[] BulletHitClips;
		[Tooltip("Попадание оружия ближнего боя (удар ключом и т.п.) по этой поверхности")]
		public AudioClip[] MeleeHitClips;
		[Tooltip("Случайный питч попадания")]
		public Vector2 HitPitchRange = new Vector2(0.9f, 1.1f);
		[Range(0f, 1f)]
		public float HitVolume = 1f;

		private void OnValidate()
		{
			ImpactMediumSpeed = Mathf.Max(ImpactMediumSpeed, ImpactMinSpeed);
			ImpactHeavySpeed = Mathf.Max(ImpactHeavySpeed, ImpactMediumSpeed);
		}

		// exclude — клип с прошлого раза, чтобы не повторять его дважды подряд
		public AudioClip GetFootstepClip(AudioClip exclude = null) => PickRandom(FootstepClips, exclude);

		public AudioClip GetLandingClip() => PickRandom(LandingClips, null);

		public AudioClip GetHitClip(HitType type, AudioClip exclude = null) =>
			PickRandom(type == HitType.Melee ? MeleeHitClips : BulletHitClips, exclude);

		// Уровень выбирается по силе удара: >= Heavy — Heavy, >= Medium — Medium, иначе Light. Если в нужном
		// уровне нет клипов, берётся ближайший непустой, так что набор с одним заполненным уровнем тоже работает
		public AudioClip GetImpactClip(float speed, AudioClip exclude = null)
		{
			AudioClip[] first, second, third;
			if (speed >= ImpactHeavySpeed) { first = HeavyImpactClips; second = MediumImpactClips; third = LightImpactClips; }
			else if (speed >= ImpactMediumSpeed) { first = MediumImpactClips; second = LightImpactClips; third = HeavyImpactClips; }
			else { first = LightImpactClips; second = MediumImpactClips; third = HeavyImpactClips; }

			AudioClip clip = PickRandom(first, exclude);
			if (clip == null) clip = PickRandom(second, exclude);
			if (clip == null) clip = PickRandom(third, exclude);
			return clip;
		}

		// Индекс исключённого клипа пропускается арифметикой, а не пересэмплированием в цикле — цикл
		// "крутить, пока не выпадет другой" зависает на массиве из одинаковых/пустых элементов
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
