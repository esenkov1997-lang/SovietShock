using System;
using System.Collections.Generic;
using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Inventory
{
	// Маленькое меню действий под курсором (ПКМ по предмету): "Использовать", "Выбросить", позже — "В избранное",
	// "Взять" и т.п. Какие пункты показать, решает тот, кто открывает (InventoryUI), — меню просто строит кнопки.
	//
	// Вёрстка (на канвасе инвентаря, последним ребёнком — поверх окон):
	//   ContextMenu   (этот компонент; растянут на весь канвас; изначально выключен)
	//   ├ Blocker     (Image на весь экран, альфа 0, Raycast Target ✔) — клик мимо меню закрывает его
	//   └ Panel       (рамка меню; Vertical Layout Group + Content Size Fitter: Preferred/Preferred)
	// Кнопки — экземпляры Button Prefab (Button + дочерний TextMeshPro), спавнятся в Panel.
	public class ItemContextMenu : MonoBehaviour, IPointerDownHandler
	{
		// пункт меню: подпись (ключ локализации) и что сделать при нажатии
		public readonly struct Entry
		{
			public readonly LocalizedString Label;
			public readonly Action Action;
			// необязательно: собрать подпись из перевода, например "Заменить {0}" → "Заменить 2"
			public readonly Func<string, string> Format;

			public Entry(LocalizedString label, Action action, Func<string, string> format = null)
			{
				Label = label;
				Action = action;
				Format = format;
			}
		}

		[Tooltip("Прозрачный фон на весь экран за меню: нажатие на него (любой кнопкой) закрывает меню")]
		[SerializeField] private Graphic blocker;
		[Tooltip("Рамка меню с Vertical Layout Group и Content Size Fitter — сюда спавнятся кнопки")]
		[SerializeField] private RectTransform panel;
		[Tooltip("Префаб пункта: Button с дочерним TextMeshPro (подпись ставит код)")]
		[SerializeField] private Button buttonPrefab;

		private readonly List<Button> _buttons = new List<Button>();

		public bool IsOpen => gameObject.activeSelf;

		// Открыть меню так, чтобы его верхний левый угол был под курсором (у края экрана — сдвигается внутрь)
		public void Open(Vector2 screenPosition, Camera eventCamera, IReadOnlyList<Entry> entries)
		{
			Close();
			if (entries == null || entries.Count == 0 || panel == null || buttonPrefab == null) return;

			gameObject.SetActive(true);
			transform.SetAsLastSibling();

			foreach (Entry entry in entries)
			{
				Button button = Instantiate(buttonPrefab, panel);
				TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
				if (label != null) LocalizedTextBinding.Bind(label, entry.Label, entry.Format);

				Action action = entry.Action;
				button.onClick.AddListener(() =>
				{
					// сначала закрываем: действие может само открыть что-то новое или перерисовать инвентарь
					Close();
					action?.Invoke();
				});
				_buttons.Add(button);
			}

			// размер меню известен только после раскладки кнопок
			LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
			PlaceAt(screenPosition, eventCamera);
		}

		public void Close()
		{
			foreach (Button button in _buttons)
			{
				if (button != null) Destroy(button.gameObject);
			}
			_buttons.Clear();

			if (gameObject.activeSelf) gameObject.SetActive(false);
		}

		// нажатие мимо кнопок (на Blocker) — закрыть. Нажатия на сами кнопки сюда не доходят — их забирает Button
		public void OnPointerDown(PointerEventData eventData)
		{
			if (blocker != null && eventData.pointerCurrentRaycast.gameObject == blocker.gameObject) Close();
		}

		private void PlaceAt(Vector2 screenPosition, Camera eventCamera)
		{
			RectTransform area = (RectTransform)transform;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPosition, eventCamera, out Vector2 local)) return;

			Rect bounds = area.rect;
			Vector2 size = panel.rect.size;

			// угол меню — под курсором; не влезает вправо/вниз — прижимаем к краю, на целый пиксель
			float x = Mathf.Clamp(local.x, bounds.xMin, bounds.xMax - size.x);
			float y = Mathf.Clamp(local.y, bounds.yMin + size.y, bounds.yMax);

			panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
			panel.pivot = new Vector2(0f, 1f);
			Vector2 center = bounds.center;
			panel.anchoredPosition = new Vector2(Mathf.Round(x - center.x), Mathf.Round(y - center.y));
		}
	}
}
