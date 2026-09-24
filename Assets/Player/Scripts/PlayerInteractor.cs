using Interactables;
using StarterAssets;
using UnityEngine;
using UnityEngine.Localization;

namespace Player
{
	// Прицел + подсказка + E: рейкаст из камеры игрока находит любой IInteractable (WorldItem — любой
	// предмет, включая оружие, или что угодно ещё, реализующее интерфейс) и по нажатию E
	// вызывает IInteractable.Interact(...), передавая корневой GameObject игрока. Сам PlayerInteractor
	// не знает ни про InventoryHolder, ни про WeaponController — каждый IInteractable сам находит нужные
	// компоненты через GetComponentInParent. Замена узкоспециализированного WeaponInteractor.
	public class PlayerInteractor : MonoBehaviour
	{
		[Tooltip("Точка и направление луча — обычно камера игрока. Если не задано — используется Camera.main")]
		[SerializeField] private Transform rayOrigin;
		[SerializeField] private float interactRange = 3f;
		[SerializeField] private StarterAssetsInputs input;

		private IInteractable _lookedAt;
		// QuickOutline (необязателен) — если есть на найденном объекте, подсвечиваем вместе с подсказкой.
		// Ищем его здесь же, в PlayerInteractor, а не через сам IInteractable — так интерфейсу не нужно
		// знать про подсветку, и любой новый интерактивный объект получает её "бесплатно"
		private Outline _lookedAtOutline;
		// какая подсказка сейчас на экране — чтобы перепривязать текст, если цель та же, а подсказка сменилась
		private LocalizedString _shownPrompt;

		private void Awake()
		{
			if (rayOrigin == null && Camera.main != null) rayOrigin = Camera.main.transform;
			if (input == null) input = GetComponentInParent<StarterAssetsInputs>();

			SetPromptVisible(false);
		}

		private void Update()
		{
			UpdateLookedAt();

			// потребляем нажатие всегда, а не только когда есть цель — иначе нажатие "в пустоту"
			// останется висеть true и активирует следующий интерактивный объект перед прицелом без нового нажатия
			bool pressedThisFrame = input.interact;
			input.interact = false;

			if (_lookedAt != null && pressedThisFrame)
			{
				// сохраняем цель до вызова и сразу гасим состояние: Interact может уничтожить объект
				// (Destroy откладывается до конца кадра), но ссылку и подсказку прячем немедленно, иначе
				// рейкаст в этом же кадре снова найдёт ещё "живую" цель
				IInteractable target = _lookedAt;
				_lookedAt = null;
				_lookedAtOutline = null;
				_shownPrompt = null;
				SetPromptVisible(false);

				// не transform.root — WeaponController/InventoryHolder обычно висят на предках именно
				// этого объекта (например, WeaponHolder), а GetComponentInParent ищет только вверх по
				// иерархии. Корень (PlayerCapsule) может не иметь этих компонентов на себе напрямую
				target.Interact(gameObject);
			}
		}

		private void UpdateLookedAt()
		{
			IInteractable found = null;
			Outline foundOutline = null;

			if (rayOrigin != null && Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit hit, interactRange, ~0, QueryTriggerInteraction.Ignore))
			{
				found = hit.collider.GetComponentInParent<IInteractable>();
				if (found != null) foundOutline = hit.collider.GetComponentInParent<Outline>();
			}

			// подсказка может смениться и у той же цели (DoorButton: "открыть" → "закрыть") — сравниваем
			// и её, а не только сам объект. Сравнение по ссылке: у объекта одно поле на каждый вариант текста
			LocalizedString prompt = found?.InteractionPrompt;
			if (found == _lookedAt && prompt == _shownPrompt) return;

			if (found != _lookedAt)
			{
				// гасим контур предыдущей цели и зажигаем на новой
				if (_lookedAtOutline != null) _lookedAtOutline.enabled = false;
				_lookedAt = found;
				_lookedAtOutline = foundOutline;
				if (_lookedAtOutline != null) _lookedAtOutline.enabled = true;
			}

			// перевод подтянется сам и обновится при смене языка (см. PickupPromptUI.SetPrompt);
			// "[E] " — подпись клавиши, а не текст, поэтому остаётся в коде
			_shownPrompt = prompt;
			if (found != null) PickupPromptUI.Instance?.SetPrompt(prompt, "[E] ");
			SetPromptVisible(found != null);
		}

		private void SetPromptVisible(bool visible)
		{
			PickupPromptUI.Instance?.SetVisible(visible);
		}
	}
}
