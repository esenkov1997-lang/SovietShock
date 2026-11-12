using System;
using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Inventory
{
	// Окно выбора количества для части стопки ("Взять…", "Положить…", "Выбросить…"): название предмета, слайдер
	// (слева 0, справа — максимум), рядом число выбранного, кнопки подтверждения и отмены. Само ничего не делает —
	// что сделать с выбранным количеством, решает тот, кто открыл (InventoryUI).
	//
	// Числа нет в поле ввода намеренно: инвентарь открыт без паузы, и цифры с клавиатуры — это клавиши выбора оружия.
	// Стрелки тоже не используем (ими игрок ходит — навигация слайдера выключается кодом). Точная подстройка —
	// кнопками "−" / "+" (необязательны).
	//
	// Вёрстка (внутри Panel инвентаря; объект изначально выключен):
	//   AmountDialog   (этот компонент; растянут на весь экран)
	//   ├ Blocker      (Image на весь экран, альфа 0, Raycast Target ✔) — клик мимо окна = отмена
	//   └ Window       (рамка; Vertical Layout Group + Content Size Fitter: Preferred/Preferred) — ставится под курсор
	//      ├ Title     (TextMeshPro — название предмета)
	//      ├ Row       (Horizontal Layout Group): [Minus] Slider [Plus] Amount (TextMeshPro)
	//      └ Buttons   (Horizontal Layout Group): Confirm, Cancel
	public class ItemAmountDialog : MonoBehaviour, IPointerDownHandler
	{
		[Tooltip("Прозрачный фон на весь экран за окном: нажатие на него закрывает окно без действия")]
		[SerializeField] private Graphic blocker;
		[Tooltip("Рамка окна — ставится верхним левым углом под курсор")]
		[SerializeField] private RectTransform window;
		[Tooltip("Название предмета")]
		[SerializeField] private TMP_Text titleText;
		[Tooltip("Слайдер количества (Min/Max/Whole Numbers выставляет код)")]
		[SerializeField] private Slider slider;
		[Tooltip("Сколько выбрано сейчас")]
		[SerializeField] private TMP_Text amountText;
		[Tooltip("Необязательно: кнопка −1")]
		[SerializeField] private Button minusButton;
		[Tooltip("Необязательно: кнопка +1")]
		[SerializeField] private Button plusButton;

		[Header("Buttons")]
		[Tooltip("Подтвердить (подпись — название действия: \"Взять\", \"Положить\", \"Выбросить\"). При 0 неактивна")]
		[SerializeField] private Button confirmButton;
		[SerializeField] private TMP_Text confirmText;
		[SerializeField] private Button cancelButton;
		[SerializeField] private TMP_Text cancelText;
		[SerializeField] private LocalizedString cancelLabel = new LocalizedString("UI", "inventory.amount.cancel");

		private Action<int> _onConfirm;
		private bool _initialized;

		public bool IsOpen => gameObject.activeSelf;
		public int Amount => slider != null ? Mathf.RoundToInt(slider.value) : 0;

		// окно изначально выключено, а Awake у выключенного объекта не вызывается — подключаемся при первом открытии
		private void Initialize()
		{
			if (_initialized) return;
			_initialized = true;

			if (slider == null) Debug.LogError($"{nameof(ItemAmountDialog)} на {name}: не назначен Slider", this);
			else
			{
				slider.onValueChanged.AddListener(_ => Refresh());
				slider.navigation = new Navigation { mode = Navigation.Mode.None };
			}

			if (minusButton != null) minusButton.onClick.AddListener(() => Step(-1));
			if (plusButton != null) plusButton.onClick.AddListener(() => Step(1));
			if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
			if (cancelButton != null) cancelButton.onClick.AddListener(Close);
			if (cancelText != null && cancelLabel != null && !cancelLabel.IsEmpty) LocalizedTextBinding.Bind(cancelText, cancelLabel);
		}

		// Открыть под курсором. max — сколько штук можно выбрать (не больше стопки и того, что влезет);
		// в начале выбрана половина стопки (с округлением вверх), но не больше max.
		// onConfirm получает выбранное количество (> 0)
		public void Open(Vector2 screenPosition, Camera eventCamera, ItemInstance item, int max,
			LocalizedString confirmLabel, Action<int> onConfirm)
		{
			Initialize();
			Close();
			if (item?.Data == null || max <= 0 || slider == null) return;

			_onConfirm = onConfirm;
			gameObject.SetActive(true);
			transform.SetAsLastSibling();

			if (titleText != null) LocalizedTextBinding.Bind(titleText, item.Data.itemName);
			if (confirmText != null) LocalizedTextBinding.Bind(confirmText, confirmLabel);

			slider.wholeNumbers = true;
			slider.minValue = 0;
			slider.maxValue = max;
			slider.SetValueWithoutNotify(Mathf.Min(max, (item.Count + 1) / 2));
			Refresh();

			if (window != null)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(window);
				PopupPlacement.PlaceAt((RectTransform)transform, window, screenPosition, eventCamera);
			}
		}

		public void Close()
		{
			_onConfirm = null;
			if (gameObject.activeSelf) gameObject.SetActive(false);
		}

		// нажатие мимо окна (на Blocker) — отмена
		public void OnPointerDown(PointerEventData eventData)
		{
			if (blocker != null && eventData.pointerCurrentRaycast.gameObject == blocker.gameObject) Close();
		}

		private void Step(int delta)
		{
			if (slider != null) slider.value = Mathf.Clamp(Amount + delta, slider.minValue, slider.maxValue);
		}

		private void Refresh()
		{
			int amount = Amount;
			if (amountText != null) LocalizedTextBinding.SetPlain(amountText, amount.ToString());
			if (confirmButton != null) confirmButton.interactable = amount > 0;
			if (minusButton != null) minusButton.interactable = amount > slider.minValue;
			if (plusButton != null) plusButton.interactable = amount < slider.maxValue;
		}

		private void Confirm()
		{
			int amount = Amount;
			Action<int> action = _onConfirm;
			// сначала закрываем: действие перерисует инвентарь
			Close();
			if (amount > 0) action?.Invoke(amount);
		}
	}
}
