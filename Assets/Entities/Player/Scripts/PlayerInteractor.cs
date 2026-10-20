using Interactables;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;

namespace Player
{
	// Прицел + подсказка + клавиша взаимодействия (Interact, см. InteractKey): рейкаст из камеры игрока находит любой IInteractable (WorldItem — любой
	// предмет, включая оружие, или что угодно ещё, реализующее интерфейс) и по её нажатию
	// вызывает IInteractable.Interact(...), передавая корневой GameObject игрока. Сам PlayerInteractor
	// не знает ни про InventoryHolder, ни про WeaponController — каждый IInteractable сам находит нужные
	// компоненты через GetComponentInParent. Замена узкоспециализированного WeaponInteractor.
	public class PlayerInteractor : MonoBehaviour
	{
		[Tooltip("Точка и направление луча — обычно камера игрока. Если не задано — используется Camera.main")]
		[SerializeField] private Transform rayOrigin;
		[SerializeField] private float interactRange = 3f;
		[SerializeField] private StarterAssetsInputs input;
		[Tooltip("Необязательно: удержание физических предметов. Пока предмет в руках, взаимодействие с остальным отключено, " +
			"а кнопка Interact отпускает предмет. Если не задано — ищется на этом объекте и его родителях")]
		[SerializeField] private ItemHoldController holdController;

		private IInteractable _lookedAt;
		// QuickOutline (необязателен) — если есть на найденном объекте, подсвечиваем вместе с подсказкой.
		// Ищем его здесь же, в PlayerInteractor, а не через сам IInteractable — так интерфейсу не нужно
		// знать про подсветку, и любой новый интерактивный объект получает её "бесплатно"
		private Outline _lookedAtOutline;
		// какая подсказка сейчас на экране — чтобы перепривязать текст, если цель та же, а подсказка сменилась
		private LocalizedString _shownPrompt;
		private string _shownPrefix;

		// необязателен — если есть, показываем подсказку залезть на уступ, когда использовать нечего
		private LedgeMantle _ledgeMantle;

		// корень собственных коллайдеров игрока (капсула) — луч взаимодействия их пропускает, см. TryFindInteractable
		private Transform _self;
		private readonly RaycastHit[] _hits = new RaycastHit[16];
		private const string JumpActionName = "Jump";

		private void Awake()
		{
			if (rayOrigin == null && Camera.main != null) rayOrigin = Camera.main.transform;
			if (input == null) input = GetComponentInParent<StarterAssetsInputs>();
			_ledgeMantle = GetComponentInParent<LedgeMantle>();
			FirstPersonController movement = GetComponentInParent<FirstPersonController>();
			_self = movement != null ? movement.transform : transform.root;
			if (holdController == null) holdController = GetComponentInParent<ItemHoldController>();
			InteractKey.Register(GetComponentInParent<PlayerInput>());

			SetPromptVisible(false);
		}

		private void Update()
		{
			// потребляем нажатие всегда, а не только когда есть цель — иначе нажатие "в пустоту"
			// останется висеть true и активирует следующий интерактивный объект перед прицелом без нового нажатия
			bool pressedThisFrame = input.interact;
			input.interact = false;

			// в руках физический предмет — ни подсказок, ни других взаимодействий: он сам висит перед прицелом
			// и перекрывал бы луч. Кнопка Interact в этом режиме — "отпустить"
			if (holdController != null && holdController.IsHolding)
			{
				ClearLookedAt();
				if (pressedThisFrame) holdController.Drop();
				return;
			}

			UpdateLookedAt();

			if (_lookedAt != null && pressedThisFrame)
			{
				// сохраняем цель до вызова и сразу гасим состояние: Interact может уничтожить объект
				// (Destroy откладывается до конца кадра), но ссылку и подсказку прячем немедленно, иначе
				// рейкаст в этом же кадре снова найдёт ещё "живую" цель
				IInteractable target = _lookedAt;
				ClearLookedAt();

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

			if (TryRaycastIgnoringSelf(out RaycastHit hit))
			{
				found = hit.collider.GetComponentInParent<IInteractable>();
				// объект может разрешать взаимодействие не с любой стороны (терминал — только спереди)
				if (found != null && !found.CanInteractFrom(rayOrigin.position)) found = null;
				if (found != null) foundOutline = hit.collider.GetComponentInParent<Outline>();
			}

			// подсказка может смениться и у той же цели (DoorButton: "открыть" → "закрыть") — сравниваем
			// и её, а не только сам объект. Сравнение по ссылке: у объекта одно поле на каждый вариант текста.
			// Префикс — подпись клавиши, берётся из реального биндинга (см. InteractKey)
			LocalizedString prompt = found?.InteractionPrompt;
			string prefix = found != null ? $"[{InteractKey.DisplayName}] " : null;
			bool showPrompt = found != null;

			// использовать нечего — подсказка залезть на уступ / перелезть через препятствие (что именно сделает
			// прыжок, решает LedgeMantle). У объектов приоритет: на них смотрят прицелом, а уступ просто оказался перед игроком
			LocalizedString ledgePrompt = found == null && _ledgeMantle != null ? _ledgeMantle.AvailablePrompt : null;
			if (ledgePrompt != null)
			{
				prompt = ledgePrompt;
				prefix = $"[{InteractKey.GetDisplayName(JumpActionName, "Space")}] ";
				showPrompt = true;
			}

			if (found == _lookedAt && prompt == _shownPrompt && prefix == _shownPrefix) return;

			if (found != _lookedAt)
			{
				// гасим контур предыдущей цели и зажигаем на новой
				if (_lookedAtOutline != null) _lookedAtOutline.enabled = false;
				_lookedAt = found;
				_lookedAtOutline = foundOutline;
				if (_lookedAtOutline != null) _lookedAtOutline.enabled = true;
			}

			// перевод подтянется сам и обновится при смене языка (см. PickupPromptUI.SetPrompt)
			_shownPrompt = prompt;
			_shownPrefix = prefix;
			if (showPrompt) PickupPromptUI.Instance?.SetPrompt(prompt, prefix);
			SetPromptVisible(showPrompt);
		}

		// ближайшее попадание луча от камеры, НЕ считая коллайдеры самого игрока. Камера стоит у края капсулы:
		// при движении назад капсула за кадр сдвигается так, что точка камеры оказывается перед ней, и обычный
		// Raycast упирался в собственную капсулу — подсказка мигала, а E не срабатывал. Та же логика, что
		// WeaponController.CastFromCamera
		private bool TryRaycastIgnoringSelf(out RaycastHit hit)
		{
			hit = default;
			if (rayOrigin == null) return false;

			int count = Physics.RaycastNonAlloc(rayOrigin.position, rayOrigin.forward, _hits, interactRange, ~0, QueryTriggerInteraction.Ignore);

			float closest = float.PositiveInfinity;
			for (int i = 0; i < count; i++)
			{
				RaycastHit candidate = _hits[i];
				if (candidate.distance >= closest || (_self != null && candidate.collider.transform.IsChildOf(_self))) continue;

				closest = candidate.distance;
				hit = candidate;
			}

			return closest < float.PositiveInfinity;
		}

		// забыть текущую цель: погасить её контур и спрятать подсказку
		private void ClearLookedAt()
		{
			if (_lookedAtOutline != null) _lookedAtOutline.enabled = false;
			_lookedAt = null;
			_lookedAtOutline = null;
			_shownPrompt = null;
			_shownPrefix = null;
			SetPromptVisible(false);
		}

		private void SetPromptVisible(bool visible)
		{
			PickupPromptUI.Instance?.SetVisible(visible);
		}
	}
}
