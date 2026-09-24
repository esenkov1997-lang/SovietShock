using System;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Interactables
{
	// Вешается на префаб Item_Tab — одна строка списка в LeftPanel_List. Сам ничего не знает про почту:
	// показывает заголовок/подзаголовок, рисует выделение и сообщает о клике через Clicked. Поэтому тот же
	// префаб подойдёт и для будущих модулей (Файлы, Камеры) — каждый модуль сам решает, что делать по клику.
	public class ItemTabUI : MonoBehaviour
	{
		[Header("Texts")]
		[Tooltip("Основной текст — тема письма / имя файла")]
		[SerializeField] private TMP_Text titleText;
		[Tooltip("Необязательно: второстепенный текст — отправитель / дата")]
		[SerializeField] private TMP_Text subtitleText;

		[Header("Selection")]
		[Tooltip("Необязательно: фон/рамка, который перекрашивается при выделении")]
		[SerializeField] private Graphic background;
		[SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.05f);
		[SerializeField] private Color selectedColor = new Color(1f, 1f, 1f, 0.3f);
		[Tooltip("Необязательно: объект (стрелка, полоска), который включается только у выделенной строки")]
		[SerializeField] private GameObject selectedMarker;

		[Header("Click")]
		[Tooltip("Необязательно: если не задано — берётся Button с этого же объекта (если есть)")]
		[SerializeField] private Button button;

		// порядковый номер в списке — модуль по нему находит, какие данные показать
		public int Index { get; private set; }
		public bool IsSelected { get; private set; }

		// клик мышью по строке; навигацию с клавиатуры модуль ведёт сам и сюда не пропускает
		public event Action<ItemTabUI> Clicked;

		private void Awake()
		{
			if (button == null) button = GetComponent<Button>();
			if (button != null)
			{
				button.onClick.AddListener(() => Clicked?.Invoke(this));

				// своя навигация Unity по стрелкам отключена: выделение ведёт модуль (↑/↓), а не EventSystem —
				// иначе стрелки двигали бы два выделения одновременно
				button.navigation = new Navigation { mode = Navigation.Mode.None };
			}

			SetSelected(false);
		}

		// Локализованные тексты (см. LocalizedTextBinding) — обновляются сами при смене языка.
		// Пустой subtitle (запись не выбрана) прячет строку подзаголовка
		public void Setup(int index, LocalizedString title, LocalizedString subtitle = null)
		{
			Index = index;
			LocalizedTextBinding.Bind(titleText, title);

			if (subtitleText != null)
			{
				bool hasSubtitle = subtitle != null && !subtitle.IsEmpty;
				subtitleText.gameObject.SetActive(hasSubtitle);
				if (hasSubtitle) LocalizedTextBinding.Bind(subtitleText, subtitle);
			}
		}

		// Обычные (нелокализуемые) тексты — например, имена файлов или номера камер
		public void Setup(int index, string title, string subtitle = null)
		{
			Index = index;
			LocalizedTextBinding.SetPlain(titleText, title);

			if (subtitleText != null)
			{
				LocalizedTextBinding.SetPlain(subtitleText, subtitle ?? string.Empty);
				subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
			}
		}

		public void SetSelected(bool isSelected)
		{
			IsSelected = isSelected;
			if (background != null) background.color = isSelected ? selectedColor : normalColor;
			if (selectedMarker != null) selectedMarker.SetActive(isSelected);
		}
	}
}
