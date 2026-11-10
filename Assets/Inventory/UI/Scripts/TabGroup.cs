using System;
using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Inventory
{
	// Вкладки: кнопки над окном и страницы, из которых видна одна. Нажатие на кнопку — показать её страницу.
	// Выбранная вкладка отмечается своим Selected Marker (например, инвертированная плашка, как выделение в
	// терминале). Подпись кнопки — ключ локализации (Label), его ставит код.
	public class TabGroup : MonoBehaviour
	{
		[Serializable]
		private class Tab
		{
			public Button button;
			[Tooltip("Текст на кнопке — сюда подставится Label")]
			public TMP_Text labelText;
			public LocalizedString label = new LocalizedString();
			[Tooltip("Страница вкладки — включается, когда вкладка выбрана")]
			public GameObject page;
			[Tooltip("Необязательно: отметка выбранной вкладки")]
			public GameObject selectedMarker;
		}

		[SerializeField] private Tab[] tabs = new Tab[0];
		[Tooltip("Какая вкладка выбрана при старте")]
		[Min(0)]
		[SerializeField] private int defaultTab;

		public int Selected { get; private set; } = -1;
		public int Count => tabs.Length;
		public event Action<int> SelectionChanged;

		private void Awake()
		{
			for (int i = 0; i < tabs.Length; i++)
			{
				Tab tab = tabs[i];
				if (tab == null) continue;

				int index = i;
				if (tab.button != null) tab.button.onClick.AddListener(() => Select(index));
				if (tab.labelText != null && tab.label != null && !tab.label.IsEmpty) LocalizedTextBinding.Bind(tab.labelText, tab.label);
			}

			Select(Mathf.Clamp(defaultTab, 0, Mathf.Max(0, tabs.Length - 1)), force: true);
		}

		public void Select(int index) => Select(index, force: false);

		private void Select(int index, bool force)
		{
			if (index < 0 || index >= tabs.Length || (index == Selected && !force)) return;

			Selected = index;
			for (int i = 0; i < tabs.Length; i++)
			{
				Tab tab = tabs[i];
				if (tab == null) continue;

				bool on = i == index;
				if (tab.page != null) tab.page.SetActive(on);
				if (tab.selectedMarker != null) tab.selectedMarker.SetActive(on);
			}
			SelectionChanged?.Invoke(index);
		}
	}
}
