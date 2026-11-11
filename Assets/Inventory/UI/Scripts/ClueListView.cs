using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
	// Вкладка "Улики": список ключей и документов (ClueCollection) — строки ClueEntryView в прокручиваемом списке.
	// Страница занимает ровно столько же места, сколько сетка предметов (Size Source): окно не меняет размер при
	// переключении вкладок, хотя оно подстраивается под содержимое.
	//
	// Вёрстка: этот объект (страница) → ScrollRect → Viewport (маска) → Content (Vertical Layout Group +
	// Content Size Fitter: Vertical = Preferred). Строки спавнятся в Content.
	//
	// Сюда можно бросить улику, перетащенную из ящика (OnDrop) — для этого на этом объекте нужен Image с
	// Raycast Target на всю страницу (например, тот же фон, что у сетки), иначе пустое место не ловит бросок.
	public class ClueListView : MonoBehaviour, ILayoutElement, IDropHandler
	{
		[Tooltip("Content списка: Vertical Layout Group + Content Size Fitter (Vertical Fit = Preferred Size)")]
		[SerializeField] private RectTransform content;
		[Tooltip("Префаб строки (ClueEntryView)")]
		[SerializeField] private ClueEntryView entryPrefab;
		[Tooltip("Сетка предметов на соседней вкладке — страница улик берёт её размер")]
		[SerializeField] private ItemGridView sizeSource;
		[Tooltip("Необязательно: прокрутка — при каждом открытии вкладки список показывается с начала")]
		[SerializeField] private ScrollRect scrollRect;
		[Tooltip("Необязательно: надпись \"Улик нет\" — видна, пока список пуст")]
		[SerializeField] private GameObject emptyLabel;

		private ClueCollection _clues;
		private readonly List<ClueEntryView> _entries = new List<ClueEntryView>();

		public event Action<ItemInstance, PointerEventData> EntryClicked;
		public event Action<ItemInstance, PointerEventData> EntryRightClicked;

		// Бросили перетаскиваемый предмет (улику из ящика) — правила знает экран инвентаря, он их сюда и подставляет
		public Func<bool> AcceptDrop;

		public void OnDrop(PointerEventData eventData)
		{
			if (!ItemDrag.IsActive) return;

			// бросили над списком — это не выброс в мир, даже если принять нельзя
			ItemDrag.MarkHandled();
			AcceptDrop?.Invoke();
		}

		internal void NotifyClick(ClueEntryView entry, PointerEventData eventData) => EntryClicked?.Invoke(entry.Item, eventData);
		internal void NotifyRightClick(ClueEntryView entry, PointerEventData eventData) => EntryRightClicked?.Invoke(entry.Item, eventData);

		public void Bind(ClueCollection clues)
		{
			if (_clues == clues) return;

			if (_clues != null) _clues.Changed -= Redraw;
			_clues = clues;
			if (_clues != null) _clues.Changed += Redraw;
			Redraw();
		}

		private void OnEnable()
		{
			if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
		}

		private void OnDestroy()
		{
			if (_clues != null) _clues.Changed -= Redraw;
		}

		private void Redraw()
		{
			foreach (ClueEntryView entry in _entries)
			{
				if (entry != null) Destroy(entry.gameObject);
			}
			_entries.Clear();

			int count = _clues != null ? _clues.Items.Count : 0;
			if (emptyLabel != null) emptyLabel.SetActive(count == 0);
			if (content == null || entryPrefab == null || _clues == null) return;

			foreach (ItemInstance item in _clues.Items)
			{
				ClueEntryView entry = Instantiate(entryPrefab, content);
				entry.Setup(item, this);
				_entries.Add(entry);
			}
		}

		// ---------- Размер (ILayoutElement) — как у сетки предметов ----------

		public float minWidth => sizeSource != null ? sizeSource.minWidth : 0f;
		public float preferredWidth => sizeSource != null ? sizeSource.preferredWidth : 0f;
		public float flexibleWidth => 0f;
		public float minHeight => sizeSource != null ? sizeSource.minHeight : 0f;
		public float preferredHeight => sizeSource != null ? sizeSource.preferredHeight : 0f;
		public float flexibleHeight => 0f;
		public int layoutPriority => 1;
		public void CalculateLayoutInputHorizontal() { }
		public void CalculateLayoutInputVertical() { }
	}
}
