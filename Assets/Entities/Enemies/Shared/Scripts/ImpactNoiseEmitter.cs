using Sound;
using UnityEngine;

namespace Enemies
{
	// Превращает удары физического предмета (CollisionSoundEmitter) в шум для врагов через NoiseSystem:
	// брошенная банка звенит не только в динамиках — собака её слышит и идёт проверить. Классический способ отвлечь врага.
	// Вешается на тот же объект, что и CollisionSoundEmitter. Без этого компонента предмет звучит, но враги его не слышат —
	// так можно оставить "тихими" мелкие предметы, которые не должны поднимать тревогу.
	[RequireComponent(typeof(CollisionSoundEmitter))]
	public class ImpactNoiseEmitter : MonoBehaviour
	{
		[Tooltip("Радиус слышимости (м) в зависимости от силы удара. По X — сила удара (м/с, как в CollisionSoundEmitter, " +
			"с учётом Intensity Multiplier), по Y — радиус. Упала с высоты — слышно дальше, чем просто стукнулась")]
		[SerializeField] private AnimationCurve radiusBySpeed = new AnimationCurve(
			new Keyframe(1f, 3f), new Keyframe(4f, 10f), new Keyframe(8f, 18f));
		[Tooltip("Удары слабее этой силы (м/с) враги не слышат, хотя звук в динамиках может играть — отсекает перекатывание и дребезг")]
		[SerializeField] private float minSpeed = 1.5f;

		[Header("Отладка")]
		[Tooltip("Рисовать круги шума в Scene view (Gizmos должны быть включены)")]
		[SerializeField] private bool drawDebug;

		private CollisionSoundEmitter _emitter;

		private void Awake()
		{
			_emitter = GetComponent<CollisionSoundEmitter>();
		}

		private void OnEnable()
		{
			_emitter.Impacted += HandleImpact;
		}

		private void OnDisable()
		{
			_emitter.Impacted -= HandleImpact;
		}

		private void HandleImpact(Vector3 point, float speed)
		{
			if (speed < minSpeed) return;

			float radius = radiusBySpeed.Evaluate(speed);
			// чуть выше точки контакта — иначе луч проверки стен от шума до врага начинается прямо в полу
			Vector3 origin = point + Vector3.up * 0.2f;

			NoiseSystem.Emit(origin, radius, gameObject);
			if (drawDebug) NoiseSystem.DrawDebug(new NoiseEvent(origin, radius, gameObject), Color.yellow);
		}
	}
}
