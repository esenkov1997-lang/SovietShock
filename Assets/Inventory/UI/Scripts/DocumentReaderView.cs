using System;
using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory
{
	// Окно чтения документа (DocumentItemData): заголовок — название документа, ниже — текст с прокруткой.
	// Открывается кликом по документу во вкладке "Улики", закрывается своим крестиком (Close) или вместе с инвентарём.
	// Пока окно открыто, экран инвентаря прячет колесо оружия — окно стоит на его месте, в центре.
	//
	// Вешается на корень окна, который включается и выключается целиком (изначально выключен).
	public class DocumentReaderView : MonoBehaviour
	{
		[SerializeField] private TMP_Text titleText;
		[Tooltip("Текст документа — внутри ScrollRect (Content с Content Size Fitter: Vertical = Preferred)")]
		[SerializeField] private TMP_Text bodyText;
		[Tooltip("Необязательно: прокрутка текста — каждый документ открывается с начала")]
		[SerializeField] private ScrollRect scrollRect;
		[Tooltip("Необязательно: крестик окна (On Click → Close назначать не нужно — подключается сам)")]
		[SerializeField] private Button closeButton;

		public DocumentItemData Current { get; private set; }
		public bool IsOpen => gameObject.activeSelf;

		// открыли / закрыли (экран инвентаря прячет и возвращает колесо)
		public event Action<bool> OpenChanged;

		private void Awake()
		{
			if (closeButton != null) closeButton.onClick.AddListener(Close);
		}

		public void Open(DocumentItemData document)
		{
			if (document == null) return;

			Current = document;
			LocalizedTextBinding.Bind(titleText, document.itemName);
			// длина текста на разных языках разная — после смены перевода пересчитываем прокрутку
			LocalizedTextBinding.Bind(bodyText, document.body, null, ResetScroll);

			bool wasOpen = IsOpen;
			gameObject.SetActive(true);
			ResetScroll();
			if (!wasOpen) OpenChanged?.Invoke(true);
		}

		public void Close()
		{
			Current = null;
			if (!IsOpen) return;

			gameObject.SetActive(false);
			OpenChanged?.Invoke(false);
		}

		private void ResetScroll()
		{
			if (scrollRect == null || !isActiveAndEnabled) return;

			Canvas.ForceUpdateCanvases();
			scrollRect.verticalNormalizedPosition = 1f;
		}
	}
}
