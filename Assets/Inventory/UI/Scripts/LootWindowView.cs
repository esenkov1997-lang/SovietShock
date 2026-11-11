using System;
using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Inventory
{
	// Окно лута (содержимое открытого контейнера) — слева от окна инвентаря: заголовок с названием контейнера,
	// сетка (ItemGridView) и кнопка "Взять все". Само ничего не решает: что делать с предметами и кнопками —
	// решает экран инвентаря (InventoryUI), он же окно показывает и прячет.
	//
	// Вешается на корень окна внутри Panel; объект включается/выключается целиком (изначально выключен).
	public class LootWindowView : MonoBehaviour
	{
		[Tooltip("Сетка контейнера (ItemGridView) — такая же, как у окна инвентаря, со своим префабом предмета")]
		[SerializeField] private ItemGridView gridView;
		[Tooltip("Текст заголовка — название контейнера (LootContainer → Title) или Default Title")]
		[SerializeField] private TMP_Text titleText;
		[Tooltip("Заголовок, если у контейнера своё название не задано")]
		[SerializeField] private LocalizedString defaultTitle = new LocalizedString("UI", "inventory.loot.title");

		[Header("Buttons")]
		[Tooltip("Кнопка \"Взять все\" (On Click назначать не нужно — подключается сама)")]
		[SerializeField] private Button takeAllButton;
		[Tooltip("Текст на кнопке \"Взять все\" — сюда подставится Take All Label")]
		[SerializeField] private TMP_Text takeAllText;
		[SerializeField] private LocalizedString takeAllLabel = new LocalizedString("UI", "inventory.loot.takeAll");
		[Tooltip("Необязательно: крестик окна — закрывает весь экран (On Click назначать не нужно)")]
		[SerializeField] private Button closeButton;

		public ItemGridView GridView => gridView;
		public LootContainer Container { get; private set; }
		public bool IsOpen => gameObject.activeSelf;

		public event Action TakeAllClicked;
		public event Action CloseClicked;

		private bool _initialized;

		// окно изначально выключено, а Awake у выключенного объекта не вызывается — подключаемся при первом показе
		private void Initialize()
		{
			if (_initialized) return;
			_initialized = true;

			if (takeAllButton != null) takeAllButton.onClick.AddListener(() => TakeAllClicked?.Invoke());
			if (closeButton != null) closeButton.onClick.AddListener(() => CloseClicked?.Invoke());
			if (takeAllText != null && takeAllLabel != null && !takeAllLabel.IsEmpty) LocalizedTextBinding.Bind(takeAllText, takeAllLabel);
			if (gridView == null) Debug.LogError($"{nameof(LootWindowView)} на {name}: не назначен Grid View", this);
		}

		public void Show(LootContainer container, ItemGrid grid)
		{
			Initialize();
			Container = container;

			LocalizedString title = container != null && container.Title != null && !container.Title.IsEmpty
				? container.Title
				: defaultTitle;
			if (titleText != null) LocalizedTextBinding.Bind(titleText, title);

			if (gridView != null) gridView.Bind(grid);
			gameObject.SetActive(true);
		}

		public void Hide()
		{
			Container = null;
			if (gridView != null) gridView.Bind(null);
			gameObject.SetActive(false);
		}
	}
}
