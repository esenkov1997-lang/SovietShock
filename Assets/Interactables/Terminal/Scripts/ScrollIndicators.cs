using UnityEngine;
using UnityEngine.UI;

namespace Interactables
{
	// Показывает игроку, что в прокручиваемом списке есть ещё содержимое: объект "Выше" включается, когда над
	// видимой областью остались сообщения, "Ниже" — когда они остались внизу. Если всё помещается — оба скрыты.
	// Что это за объекты, решает вёрстка: стрелки ▲/▼, надпись "ЕЩЁ", полоса-затемнение у края.
	//
	// Вешается на объект со ScrollRect (например, Mail_Details_Panel). Работает с любой прокруткой — колесом,
	// стрелками, PageUp/PageDown, кодом: состояние проверяется каждый кадр, а не по событиям ScrollRect.
	public class ScrollIndicators : MonoBehaviour
	{
		[Tooltip("Если не задано — ScrollRect на этом же объекте")]
		[SerializeField] private ScrollRect scrollRect;
		[Tooltip("Включается, когда есть содержимое ВЫШЕ видимой области (например, стрелка ▲ у верхнего края)")]
		[SerializeField] private GameObject moreAbove;
		[Tooltip("Включается, когда есть содержимое НИЖЕ видимой области (например, стрелка ▼ у нижнего края)")]
		[SerializeField] private GameObject moreBelow;
		[Tooltip("Сколько пикселей скрытого содержимого не считать (чтобы стрелка не мигала из-за долей пикселя у края)")]
		[SerializeField] private float edgeTolerance = 4f;

		[Header("Blink")]
		[Tooltip("Индикаторы мигают — привлекают внимание, как курсор старого терминала")]
		[SerializeField] private bool blink = true;
		[Tooltip("Период мигания: столько секунд виден и столько же скрыт")]
		[SerializeField] private float blinkInterval = 0.5f;

		private void Awake()
		{
			if (scrollRect == null) scrollRect = GetComponent<ScrollRect>();
		}

		private void OnDisable()
		{
			SetVisible(moreAbove, false);
			SetVisible(moreBelow, false);
		}

		// LateUpdate — после того, как модуль прокрутил список и layout пересчитал высоту
		private void LateUpdate()
		{
			bool above = false;
			bool below = false;

			if (scrollRect != null && scrollRect.content != null)
			{
				RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
				float hidden = scrollRect.content.rect.height - viewport.rect.height;

				if (hidden > edgeTolerance)
				{
					// 1 — в самом верху, 0 — в самом низу
					float position = Mathf.Clamp01(scrollRect.verticalNormalizedPosition);
					above = (1f - position) * hidden > edgeTolerance;
					below = position * hidden > edgeTolerance;
				}
			}

			bool blinkOn = !blink || blinkInterval <= 0f || Mathf.FloorToInt(Time.unscaledTime / blinkInterval) % 2 == 0;
			SetVisible(moreAbove, above && blinkOn);
			SetVisible(moreBelow, below && blinkOn);
		}

		private static void SetVisible(GameObject target, bool visible)
		{
			if (target != null && target.activeSelf != visible) target.SetActive(visible);
		}
	}
}
