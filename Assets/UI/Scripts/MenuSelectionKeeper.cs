using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI
{
	// Не даёт меню остаться без выбранной кнопки. Клик мышью мимо кнопок снимает выбор в EventSystem —
	// тогда пропадает выделение, а стрелки/геймпад перестают работать. Этот компонент сразу возвращает выбор
	// на последнюю выбранную кнопку этой страницы (или на Fallback, если её больше нет / она выключена).
	//
	// Вешается на корень страницы меню (например, MainPage паузы). Работает, пока страница включена.
	public class MenuSelectionKeeper : MonoBehaviour
	{
		[Tooltip("Что выбрать, если последней выбранной кнопки нет. Пусто — первая доступная кнопка внутри страницы")]
		[SerializeField] private Selectable fallback;

		private GameObject _last;

		private void OnDisable() => _last = null;

		private void LateUpdate()
		{
			EventSystem eventSystem = EventSystem.current;
			if (eventSystem == null) return;

			GameObject current = eventSystem.currentSelectedGameObject;
			if (current != null && current.activeInHierarchy)
			{
				if (current.transform.IsChildOf(transform)) _last = current;
				return;
			}

			GameObject target = IsSelectable(_last) ? _last : FallbackTarget();
			if (target != null) eventSystem.SetSelectedGameObject(target);
		}

		private GameObject FallbackTarget()
		{
			if (fallback != null && IsSelectable(fallback.gameObject)) return fallback.gameObject;

			foreach (Selectable selectable in GetComponentsInChildren<Selectable>())
			{
				if (selectable.IsInteractable()) return selectable.gameObject;
			}
			return null;
		}

		private static bool IsSelectable(GameObject target)
		{
			if (target == null || !target.activeInHierarchy) return false;
			Selectable selectable = target.GetComponent<Selectable>();
			return selectable != null && selectable.IsInteractable();
		}
	}
}
