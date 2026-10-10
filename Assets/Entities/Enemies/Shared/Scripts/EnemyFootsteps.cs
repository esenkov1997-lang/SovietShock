using Sound;
using UnityEngine;

namespace Enemies
{
	// Шаги врага по Animation Event "Footstep" — ставится в клипах ходьбы/бега на кадры, где лапа касается пола.
	// В отличие от игрока (у FPS-рига нет видимых ног, там шаги по пройденной дистанции), у врага ноги видны —
	// звук должен совпадать с анимацией, это стандартный подход.
	//
	// Blend Tree: пока Walk и Run смешиваются, события приходят из ОБОИХ клипов — шаги бы удваивались.
	// Поэтому звучат только события клипа, вес которого в смеси не меньше Min Clip Weight.
	//
	// Вешается на объект с Animator (модель) — Animation Event вызывает методы только там. Нужен свой
	// AudioSource: питч шагов меняется на каждый шаг и не должен задевать другие звуки врага.
	[RequireComponent(typeof(AudioSource))]
	public class EnemyFootsteps : MonoBehaviour
	{
		[Tooltip("Звуки лап по поверхностям (Assets/Create/Audio/Creature Footstep Set)")]
		[SerializeField] private CreatureFootstepSet footstepSet;

		[Header("Громкость от скорости")]
		[Tooltip("Множитель громкости при ходьбе")]
		[Range(0f, 1f)]
		[SerializeField] private float walkVolume = 0.5f;
		[Tooltip("Множитель громкости при беге. Между ходьбой и бегом — плавно по фактической скорости")]
		[Range(0f, 1f)]
		[SerializeField] private float runVolume = 1f;

		[Header("Фильтр событий")]
		[Tooltip("Событие звучит, только если его клип занимает в Blend Tree не меньше этой доли (0.5 — звучит только преобладающий клип)")]
		[Range(0f, 1f)]
		[SerializeField] private float minClipWeight = 0.5f;
		[Tooltip("Минимальная пауза (сек) между шагами — страховка от двух событий в один момент на стыке клипов")]
		[SerializeField] private float minInterval = 0.06f;

		[Header("Поверхность")]
		[Tooltip("Слои пола для определения материала")]
		[SerializeField] private LayerMask groundLayers = ~(1 << 2);
		[Tooltip("На сколько метров вниз от корня искать пол")]
		[SerializeField] private float rayDistance = 1f;

		private readonly RaycastHit[] _hits = new RaycastHit[8];
		private AudioSource _audioSource;
		private EnemyMovement _movement;
		private AudioClip _lastClip;
		private float _lastStepTime = float.NegativeInfinity;

		private void Awake()
		{
			_audioSource = GetComponent<AudioSource>();
			_movement = GetComponentInParent<EnemyMovement>();
			if (footstepSet == null)
				Debug.LogWarning($"{name}: у EnemyFootsteps не назначен Footstep Set — шагов не будет слышно", this);
		}

		// Animation Event "Footstep". Параметр AnimationEvent Unity передаёт сам — в окне событий ничего заполнять не нужно
		public void Footstep(AnimationEvent animationEvent)
		{
			if (footstepSet == null) return;
			if (animationEvent.animatorClipInfo.weight < minClipWeight) return;
			if (Time.time - _lastStepTime < minInterval) return;

			AudioClip clip = footstepSet.GetClip(GetSurfaceBelow(), _lastClip, out float volume);
			if (clip == null) return;

			_lastStepTime = Time.time;
			_lastClip = clip;
			_audioSource.pitch = Random.Range(footstepSet.PitchRange.x, footstepSet.PitchRange.y);
			_audioSource.PlayOneShot(clip, volume * GetSpeedVolume());
		}

		private float GetSpeedVolume()
		{
			if (_movement == null) return runVolume;
			float t = Mathf.InverseLerp(_movement.WalkSpeed, _movement.RunSpeed, _movement.CurrentSpeed);
			return Mathf.Lerp(walkVolume, runVolume, t);
		}

		private SurfaceSoundSet GetSurfaceBelow()
		{
			Transform root = _movement != null ? _movement.transform : transform;
			int count = Physics.RaycastNonAlloc(root.position + Vector3.up * 0.3f, Vector3.down, _hits,
				rayDistance, groundLayers, QueryTriggerInteraction.Ignore);

			// ближайшее попадание, не считая собственных костей-хитбоксов (лапы стоят прямо на пути луча)
			Collider ground = null;
			float nearest = float.MaxValue;
			for (int i = 0; i < count; i++)
			{
				if (_hits[i].transform.IsChildOf(root) || _hits[i].distance >= nearest) continue;
				nearest = _hits[i].distance;
				ground = _hits[i].collider;
			}

			return ground != null ? SoundSurface.Find(ground) : null;
		}
	}
}
