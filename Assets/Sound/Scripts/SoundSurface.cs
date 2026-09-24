using UnityEngine;

namespace Sound
{
	// Ярлык материала: "этот пол/предмет сделан из X". Вешается на объект с коллайдерами (или на родителя
	// группы коллайдеров) и ссылается на SurfaceSoundSet. Именно его читают шаги игрока (FootstepPlayer) и
	// удары предметов (CollisionSoundEmitter). Один и тот же SurfaceSoundSet переиспользуется на всех
	// объектах одного материала. Не путать с Weapons.ImpactSurface — тот отвечает только за декали попаданий.
	public class SoundSurface : MonoBehaviour
	{
		[Tooltip("Звуки материала этой поверхности")]
		public SurfaceSoundSet SoundSet;

		// материал коллайдера — ближайший SoundSurface вверх по иерархии, так что дочерние коллайдеры
		// без своего компонента наследуют материал родителя, а составной предмет может иметь разные части
		public static SurfaceSoundSet Find(Collider collider)
		{
			SoundSurface surface = collider.GetComponentInParent<SoundSurface>();
			return surface != null ? surface.SoundSet : null;
		}
	}
}
