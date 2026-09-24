using UnityEngine;

namespace Sound
{
	// Звуки ударов физического предмета о поверхности. Вешается на тот же GameObject, что и Rigidbody —
	// именно туда Unity доставляет OnCollisionEnter от всех коллайдеров тела, включая дочерние. У статичной
	// геометрии (пол, стены) этот компонент не нужен, ей достаточно SoundSurface.
	//
	// Слои: каждый удар играет звук материала самого предмета и — тише, отдельным голосом — звук материала
	// того, обо что он ударился (металл + бетон, дерево + ковёр). Материал берётся с SoundSurface коллайдера,
	// поэтому у составного предмета разные части могут звучать по-разному.
	[RequireComponent(typeof(SoundSurface))]
	public class CollisionSoundEmitter : MonoBehaviour
	{
		[Tooltip("Множитель силы удара для этого предмета: тяжёлые (бочка, шкаф) — больше 1, лёгкие (ключ, банка) — меньше 1. Сдвигает и выбор уровня Light/Medium/Heavy, и громкость")]
		[SerializeField] private float _intensityMultiplier = 1f;
		[Tooltip("Громкость слоя материала того, обо что ударились, относительно слоя самого предмета. 0 — второй слой выключен")]
		[Range(0f, 1f)]
		[SerializeField] private float _otherSurfaceVolume = 0.6f;
		[Tooltip("Минимальная пауза (с) между звуками этого предмета — гасит дребезг, когда он катится или трясётся")]
		[SerializeField] private float _cooldown = 0.1f;

		private float _nextAllowedTime;

		private void OnCollisionEnter(Collision collision)
		{
			if (Time.time < _nextAllowedTime || collision.contactCount == 0) return;

			// два предмета с эмиттерами получают OnCollisionEnter каждый — играть пару должен только один,
			// иначе один удар прозвучит дважды. Выбирается тот, у кого меньше InstanceID
			CollisionSoundEmitter otherEmitter = collision.rigidbody != null ? collision.rigidbody.GetComponent<CollisionSoundEmitter>() : null;
			if (otherEmitter != null && GetInstanceID() > otherEmitter.GetInstanceID()) return;

			ContactPoint contact = collision.GetContact(0);

			// только составляющая скорости вдоль нормали контакта: предмет, быстро скользящий по полу,
			// не "ударяется" — иначе скольжение звучало бы как серия сильных ударов
			float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal)) * _intensityMultiplier;

			SurfaceSoundManager manager = SurfaceSoundManager.Instance;
			bool played = manager.PlayImpact(SoundSurface.Find(contact.thisCollider), contact.point, speed);
			if (_otherSurfaceVolume > 0f)
				played |= manager.PlayImpact(SoundSurface.Find(contact.otherCollider), contact.point, speed, _otherSurfaceVolume);

			// пауза включается только если что-то сыграло — слабое касание не должно "съедать" следующий сильный удар
			if (played) _nextAllowedTime = Time.time + _cooldown;
		}
	}
}
