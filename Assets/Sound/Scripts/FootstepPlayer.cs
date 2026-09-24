using StarterAssets;
using UnityEngine;

namespace Sound
{
	// Шаги игрока по пройденной дистанции, а не по таймеру: на каждые WalkStepDistance/
	// SprintStepDistance метров проигрывается один шаг — при крауче скорость падает, и шаги
	// сами звучат реже без отдельной настройки. Ног/анимации у FPS-рига нет, поэтому Animation
	// Event (как для WeaponAnimationEvents) не подходит — это стандартный способ для
	// контроллера без видимых ног.
	//
	// Материал поверхности определяется рейкастом вниз и компонентом SoundSurface на
	// том, во что попал рейкаст (тот же принцип, что ImpactSurface у декалей попаданий,
	// см. WeaponController.SpawnImpactDecal). Если компонента нет — берётся DefaultSoundSet.
	[RequireComponent(typeof(AudioSource))]
	public class FootstepPlayer : MonoBehaviour
	{
		[SerializeField] private FirstPersonController _movement;
		[Tooltip("Звуки для поверхностей без SoundSurface (бетон/пол по умолчанию)")]
		[SerializeField] private SurfaceSoundSet _defaultSoundSet;

		[Tooltip("Дистанция в метрах между шагами при ходьбе")]
		[SerializeField] private float _walkStepDistance = 2.2f;
		[Tooltip("Дистанция в метрах между шагами при спринте — отдельно от ходьбы, чтобы темп шагов можно было подогнать на слух независимо")]
		[SerializeField] private float _sprintStepDistance = 2.6f;
		[Tooltip("Минимальная скорость движения, при которой начинают звучать шаги")]
		[SerializeField] private float _minSpeed = 0.2f;
		[Tooltip("На сколько метров ниже точки опоры игрока искать поверхность")]
		[SerializeField] private float _rayDistance = 1f;

		[Header("Landing")]
		[Tooltip("Минимальная скорость падения (м/с) в момент приземления, при которой проигрывается звук — отсекает мелкие подпрыгивания")]
		[SerializeField] private float _landingMinSpeed = 1.5f;
		[Tooltip("Скорость падения (м/с), при которой звук приземления играет на полной громкости — при падении с большей высоты громкость не растёт дальше")]
		[SerializeField] private float _landingSpeedForFullVolume = 8f;

		private AudioSource _audioSource;
		private float _distanceAccumulator;
		private bool _wasMoving;
		private AudioClip _lastClip; // не даём тому же клипу сыграть два шага подряд, см. SurfaceSoundSet.GetFootstepClip

		private void Awake()
		{
			_audioSource = GetComponent<AudioSource>();
			if (_movement == null) _movement = GetComponent<FirstPersonController>();
		}

		private void Update()
		{
			if (_movement.JustLanded)
			{
				PlayLanding();
				// landing already gave audio feedback this frame — if the player is still holding forward,
				// don't also fire the "just started moving" footstep from the isMoving check below and get
				// two overlapping sounds on the same touchdown; normal step cadence resumes next frame
				_wasMoving = _movement.Grounded && _movement.CurrentSpeed >= _minSpeed;
				_distanceAccumulator = 0f;
				return;
			}

			bool isMoving = _movement.Grounded && _movement.CurrentSpeed >= _minSpeed;

			if (!isMoving)
			{
				_distanceAccumulator = 0f; // следующий шаг звучит сразу, а не с середины дистанции
				_wasMoving = false;
				return;
			}

			// первый шаг после остановки — сразу, не дожидаясь полного StepDistance, иначе короткий
			// тап "вперёд-отпустил" вообще не успевает набрать дистанцию и остаётся беззвучным
			if (!_wasMoving)
			{
				_wasMoving = true;
				_distanceAccumulator = 0f;
				PlayFootstep();
				return;
			}

			float stepDistance = _movement.IsSprinting ? _sprintStepDistance : _walkStepDistance;

			_distanceAccumulator += _movement.CurrentSpeed * Time.deltaTime;
			if (_distanceAccumulator < stepDistance) return;

			_distanceAccumulator = 0f;
			PlayFootstep();
		}

		private void PlayFootstep()
		{
			SurfaceSoundSet soundSet = GetSoundSetAtFeet();
			if (soundSet == null) return;

			AudioClip clip = soundSet.GetFootstepClip(_lastClip);
			if (clip == null) return;

			_lastClip = clip;
			_audioSource.pitch = Random.Range(soundSet.FootstepPitchRange.x, soundSet.FootstepPitchRange.y);
			_audioSource.PlayOneShot(clip, soundSet.FootstepVolume);
		}

		private void PlayLanding()
		{
			if (_movement.LandingSpeed < _landingMinSpeed) return;

			SurfaceSoundSet soundSet = GetSoundSetAtFeet();
			if (soundSet == null) return;

			AudioClip clip = soundSet.GetLandingClip();
			if (clip == null) return;

			// remapped to start at 0.3 rather than 0 — right at _landingMinSpeed the clip should still be
			// clearly audible, not fade out to silence exactly where the cutoff already decided it plays
			float t = Mathf.InverseLerp(_landingMinSpeed, _landingSpeedForFullVolume, _movement.LandingSpeed);
			float volumeScale = Mathf.Lerp(0.3f, 1f, t);
			_audioSource.pitch = Random.Range(soundSet.LandingPitchRange.x, soundSet.LandingPitchRange.y);
			_audioSource.PlayOneShot(clip, soundSet.LandingVolume * volumeScale);
		}

		private SurfaceSoundSet GetSoundSetAtFeet()
		{
			SurfaceSoundSet soundSet = _defaultSoundSet;

			if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down,
				out RaycastHit hit, _rayDistance, _movement.GroundLayers, QueryTriggerInteraction.Ignore))
			{
				SurfaceSoundSet found = SoundSurface.Find(hit.collider);
				if (found != null) soundSet = found;
			}

			return soundSet;
		}
	}
}
