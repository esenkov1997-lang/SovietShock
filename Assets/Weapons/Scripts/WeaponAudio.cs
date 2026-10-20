using UnityEngine;

namespace Weapons
{
	// Один звук оружия: несколько вариантов клипа (выбирается случайный, чтобы серия не звучала механически),
	// своя громкость и разброс питча
	[System.Serializable]
	public class WeaponSound
	{
		[Tooltip("Варианты клипа — каждый раз играется случайный. Пусто — звука нет")]
		public AudioClip[] Clips = new AudioClip[0];
		[Range(0f, 1f)]
		public float Volume = 1f;
		[Tooltip("Случайный питч в диапазоне X..Y при каждом проигрывании. (1, 1) — без разброса")]
		public Vector2 PitchRange = Vector2.one;

		public bool IsEmpty => Clips == null || Clips.Length == 0;

		public AudioClip PickClip() => IsEmpty ? null : Clips[Random.Range(0, Clips.Length)];
		public float PickPitch() => Random.Range(PitchRange.x, PitchRange.y);
	}

	// Вешается на корень InHandPrefab (модель оружия в руках) — все звуки конкретного оружия настраиваются
	// прямо на префабе, вместе с его AudioSource (микшер, громкость, 2D/3D). WeaponController находит компонент
	// при экипировке и только говорит, ЧТО сыграть, а не КАК. Нет компонента — оружие беззвучное.
	// Звук попадания по поверхности — не здесь, он зависит от цели (SurfaceSoundSet).
	public class WeaponAudio : MonoBehaviour
	{
		[Header("Sources")]
		[Tooltip("Источник разовых звуков (выстрел, удар, перезарядка...). Пусто — создаётся автоматически (2D). Свой — если нужен микшер/3D/другие настройки")]
		[SerializeField] private AudioSource oneShotSource;
		[Tooltip("Источник зацикленного звука (горение горелки). Пусто — создаётся автоматически с настройками One Shot Source")]
		[SerializeField] private AudioSource loopSource;

		[Header("Attack")]
		[Tooltip("Выстрел или взмах (у холодного играется в начале удара, независимо от попадания)")]
		public WeaponSound Attack = new WeaponSound();
		[Tooltip("Сухой щелчок — попытка выстрела с пустым магазином")]
		public WeaponSound DryFire = new WeaponSound();

		[Header("Handling")]
		[Tooltip("Доставание оружия")]
		public WeaponSound Draw = new WeaponSound();
		[Tooltip("Начало перезарядки")]
		public WeaponSound Reload = new WeaponSound();

		[Header("Repair Tool")]
		[Tooltip("Зажигание горелки — один раз в момент, когда появляются пламя и свет (Animation Event RepairStart)")]
		public WeaponSound RepairStart = new WeaponSound();
		[Tooltip("Горение — зацикленный, начинается ровно после RepairStart и играет, пока горит горелка. Loop в импорте клипа ставить не нужно")]
		public WeaponSound RepairLoop = new WeaponSound();
		[Tooltip("Затухание — один раз, когда горелка гаснет (ПКМ отпущена, кончился газ, оружие убрано)")]
		public WeaponSound RepairEnd = new WeaponSound();

		private bool _repairLoopOn;

		private void Awake()
		{
			if (oneShotSource == null)
			{
				oneShotSource = gameObject.AddComponent<AudioSource>();
				oneShotSource.playOnAwake = false;
			}

			if (loopSource == null) loopSource = CreateLoopSource(oneShotSource);
			loopSource.loop = true;
			loopSource.playOnAwake = false;
		}

		public void PlayAttack() => PlayOneShot(Attack);
		public void PlayDryFire() => PlayOneShot(DryFire);
		public void PlayDraw() => PlayOneShot(Draw);
		public void PlayReload() => PlayOneShot(Reload);

		// PlayOneShot, а не Play — звуки не обрывают друг друга при быстрой стрельбе/очереди
		public void PlayOneShot(WeaponSound sound)
		{
			AudioClip clip = sound.PickClip();
			if (clip == null) return;

			oneShotSource.pitch = sound.PickPitch();
			oneShotSource.PlayOneShot(clip, sound.Volume);
		}

		// горелка: зажигание — one-shot, горение — луп, запланированный ровно на конец звука зажигания
		// (PlayScheduled), чтобы стык был без щелчка и паузы. Выключение — стоп лупа (заодно отменяет
		// ещё не начавшийся запланированный) и one-shot затухания
		public void SetRepairLoop(bool on)
		{
			if (_repairLoopOn == on) return;
			_repairLoopOn = on;

			if (!on)
			{
				loopSource.Stop();
				PlayOneShot(RepairEnd);
				return;
			}

			double loopStart = AudioSettings.dspTime;
			AudioClip startClip = RepairStart.PickClip();
			if (startClip != null)
			{
				float pitch = RepairStart.PickPitch();
				oneShotSource.pitch = pitch;
				oneShotSource.PlayOneShot(startClip, RepairStart.Volume);
				loopStart += startClip.length / Mathf.Max(0.01f, pitch);
			}

			AudioClip loopClip = RepairLoop.PickClip();
			if (loopClip == null) return;

			loopSource.clip = loopClip;
			loopSource.volume = RepairLoop.Volume;
			loopSource.pitch = RepairLoop.PickPitch();
			loopSource.PlayScheduled(loopStart);
		}

		private AudioSource CreateLoopSource(AudioSource template)
		{
			AudioSource source = gameObject.AddComponent<AudioSource>();
			source.outputAudioMixerGroup = template.outputAudioMixerGroup;
			source.spatialBlend = template.spatialBlend;
			source.priority = template.priority;
			source.rolloffMode = template.rolloffMode;
			source.minDistance = template.minDistance;
			source.maxDistance = template.maxDistance;
			return source;
		}
	}
}
