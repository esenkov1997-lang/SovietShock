using UnityEngine;

namespace Sound
{
	// Зацикленный звук, который звучит, пока эмитит система частиц на этом же объекте (например, искры горелки —
	// см. RepairToolData.SparksPrefab). Сам следит за ParticleSystem.isEmitting, поэтому тому, кто включает
	// и гасит частицы (WeaponController), про звук знать не нужно. Включение и выключение — с коротким фейдом,
	// чтобы звук не щёлкал, когда луч горелки то касается поверхности, то уходит в воздух.
	//
	// Настройки самого источника (3D: Spatial Blend = 1, дальность, микшер) — в AudioSource на этом же объекте.
	[RequireComponent(typeof(AudioSource))]
	public class ParticleEmissionAudio : MonoBehaviour
	{
		[Tooltip("Система частиц, за эмиссией которой следит звук. Пусто — берётся с этого объекта или его детей")]
		[SerializeField] private ParticleSystem particles;

		[Tooltip("Варианты зацикленного клипа — при каждом включении выбирается случайный. Loop в импорте клипа ставить не нужно")]
		[SerializeField] private AudioClip[] loopClips = new AudioClip[0];
		[Range(0f, 1f)]
		[SerializeField] private float volume = 1f;
		[Tooltip("Случайный питч в диапазоне X..Y при каждом включении. (1, 1) — без разброса")]
		[SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);
		[Tooltip("Начинать клип со случайного места — повторные включения не звучат одинаково")]
		[SerializeField] private bool randomStartTime = true;

		[Header("Fade")]
		[Tooltip("Время нарастания громкости при появлении искр, сек")]
		[SerializeField] private float fadeInTime = 0.05f;
		[Tooltip("Время затухания после того, как искры перестали появляться, сек")]
		[SerializeField] private float fadeOutTime = 0.15f;

		private AudioSource _source;

		private void Awake()
		{
			_source = GetComponent<AudioSource>();
			_source.loop = true;
			_source.playOnAwake = false;
			_source.volume = 0f;

			if (particles == null) particles = GetComponentInChildren<ParticleSystem>();
		}

		private void Update()
		{
			bool emitting = particles != null && particles.isEmitting;

			if (emitting && !_source.isPlaying) StartLoop();
			if (!_source.isPlaying) return;

			float target = emitting ? volume : 0f;
			float fadeTime = emitting ? fadeInTime : fadeOutTime;
			float step = fadeTime > 0f ? volume / fadeTime * Time.deltaTime : float.PositiveInfinity;
			_source.volume = Mathf.MoveTowards(_source.volume, target, step);

			if (!emitting && _source.volume <= 0f) _source.Stop();
		}

		private void StartLoop()
		{
			if (loopClips == null || loopClips.Length == 0) return;

			AudioClip clip = loopClips[Random.Range(0, loopClips.Length)];
			if (clip == null) return;

			_source.clip = clip;
			_source.pitch = Random.Range(pitchRange.x, pitchRange.y);
			_source.volume = 0f;
			_source.time = randomStartTime ? Random.Range(0f, clip.length) : 0f;
			_source.Play();
		}
	}
}
