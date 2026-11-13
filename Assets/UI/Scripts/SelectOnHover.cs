using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI
{
	// Наведение мышью делает элемент ВЫБРАННЫМ (как стрелками с клавиатуры), а не просто подсвеченным.
	// Так у мыши и клавиатуры один общий выбор, как в терминале: выделена всегда ровно одна кнопка.
	//
	// Вешается на кнопку (рядом с Button / любым Selectable). Подсветку тогда настраивают по состоянию Selected:
	// Button → Selected Color — "выделено", Highlighted Color — как Normal (наведение отдельно не показывается).
	// В паре с MenuSelectionKeeper на странице меню — чтобы клик мимо кнопок не сбрасывал выбор.
	[RequireComponent(typeof(Selectable))]
	public class SelectOnHover : MonoBehaviour, IPointerEnterHandler
	{
		private Selectable _selectable;

		private void Awake() => _selectable = GetComponent<Selectable>();

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (!_selectable.IsInteractable() || EventSystem.current == null) return;
			if (EventSystem.current.currentSelectedGameObject == gameObject) return;
			EventSystem.current.SetSelectedGameObject(gameObject);
		}
	}
}
