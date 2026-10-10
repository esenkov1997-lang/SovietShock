using UnityEngine;

namespace Enemies
{
	// Необязательные настройки отдельной точки патруля. Без этого компонента точка использует
	// настройки PatrolRoute. Повесь на дочерний объект маршрута, если на этой точке враг должен
	// задержаться дольше, посмотреть в определённую сторону (синяя ось Z) или принюхаться.
	public class PatrolPoint : MonoBehaviour
	{
		[Tooltip("Использовать своё время ожидания вместо общего из PatrolRoute")]
		public bool OverrideWaitTime;
		[Tooltip("Сколько секунд стоять на этой точке")]
		public float WaitTime = 3f;
		[Tooltip("На точке развернуться туда, куда смотрит синяя ось Z этого объекта (например, в окно или на дверь)")]
		public bool FaceDirection;
		[Tooltip("Пока стоит на точке — включать анимацию осмотра/принюхивания (параметр Searching)")]
		public bool PlaySearchAnimation;
	}
}
