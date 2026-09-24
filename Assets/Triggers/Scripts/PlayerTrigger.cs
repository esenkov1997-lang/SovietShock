using StarterAssets;

namespace Triggers
{
	// Готовый триггер "только на игрока" — перетащи на объект с Collider(isTrigger) вместо ручной
	// настройки тега/слоя. Игрок в этом проекте определяется наличием FirstPersonController
	public class PlayerTrigger : ComponentTrigger<FirstPersonController> { }
}
