using System;
using Interactables;
using Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Inventory
{
	// Колесо избранного оружия (WeaponLoadout) на экране: кольцо из N секторов, по часовой стрелке от верхнего
	// (сектор 0 — клавиша Weapon1). Используется дважды: в центре экрана инвентаря (перетаскивание, меню) и
	// само по себе — быстрый выбор по зажатой клавише (WeaponWheelQuickSelect).
	//
	// Какой сектор под курсором, считается по углу и расстоянию от центра этого объекта (Inner/Outer Radius),
	// а не по форме картинок — поэтому сектора можно рисовать как угодно. Для лучей нужен хотя бы один Graphic
	// с Raycast Target на всё кольцо (например, картинка кольца — внутри этого объекта).
	//
	// Вёрстка каждого сектора (Slots, по порядку слотов):
	//   - Icon — Image иконки оружия. Её прямоугольник (позиция, размер, поворот) задаёшь сам — иконка вписывается
	//     в него с сохранением пропорций. Пустой слот — иконка скрыта;
	//   - Highlight — подсветка сектора (Graphic): курсор над ним / выбор колесом / место для броска (цвет — ниже);
	//   - Selected Marker — необязательно: отметка оружия, которое сейчас в руках.
	// В центре — название оружия под курсором ("Пусто" у пустого) и его клавиша.
	public class WeaponWheelView : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler, IPointerClickHandler,
		IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
	{
		[Serializable]
		private class SlotView
		{
			public Image icon;
			public Graphic highlight;
			public GameObject selectedMarker;
		}

		[Header("Shape")]
		[Tooltip("Сектора по порядку слотов колеса: 0 — верхний (клавиша Weapon1), дальше по часовой стрелке")]
		[SerializeField] private SlotView[] slots = new SlotView[4];
		[Tooltip("Внутренний радиус кольца, в пикселях интерфейса от центра этого объекта: ближе к центру — не сектор")]
		[Min(0f)]
		[SerializeField] private float innerRadius = 40f;
		[Tooltip("Внешний радиус кольца: дальше — не сектор")]
		[Min(1f)]
		[SerializeField] private float outerRadius = 120f;

		[Header("Center")]
		[Tooltip("Название оружия в секторе под курсором")]
		[SerializeField] private TMP_Text nameText;
		[Tooltip("Подпись клавиши этого сектора")]
		[SerializeField] private TMP_Text hotkeyText;
		[SerializeField] private LocalizedString emptyLabel = new LocalizedString("UI", "inventory.wheel.empty");
		[Tooltip("Формат подписи клавиши, {0} — клавиша из Input Actions (Weapon1..WeaponN)")]
		[SerializeField] private LocalizedString hotkeyLabel = new LocalizedString("UI", "inventory.wheel.hotkey");

		[Header("Highlight Colors")]
		[SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 0.25f);
		[SerializeField] private Color dropValidColor = new Color(0.3f, 1f, 0.3f, 0.35f);
		[SerializeField] private Color dropInvalidColor = new Color(1f, 0.25f, 0.25f, 0.35f);

		private WeaponLoadout _loadout;
		private int _hoverSlot = -1;
		private int _shownLabelSlot = -2;
		private int _dragSlot = -1;

		// выбор сектора не мышью над колесом, а направлением (быстрый выбор: мышь при зажатом курсоре, стик)
		private bool _directionMode;

		public int SlotCount => slots.Length;
		// сектор под курсором (или выбранный направлением); -1 — ни один
		public int HoverSlot => _hoverSlot;

		// что сделали с сектором — решает экран инвентаря
		public event Action<int, PointerEventData> SlotRightClicked;
		public event Action<int> SlotDroppedOutside;
		// курсор перешёл на другой сектор (номер сектора; уход с секторов не сообщается) — для звука
		public event Action<int> HoverChanged;
		// предмет бросили на сектор: можно ли / положить. Правила (обмен, нет места) знает экран инвентаря
		public Func<int, bool> CanAcceptDrop;
		public Func<int, bool> AcceptDrop;

		// ---------- Данные ----------

		public void Bind(WeaponLoadout loadout)
		{
			if (_loadout == loadout) return;

			if (_loadout != null) _loadout.SlotsChanged -= Refresh;
			_loadout = loadout;
			if (_loadout != null) _loadout.SlotsChanged += Refresh;
			Refresh();
		}

		private void OnEnable() => Refresh();

		private void OnDisable()
		{
			SetHover(-1);
			if (_dragSlot >= 0) ItemDrag.Cancel();
			_dragSlot = -1;
		}

		private void OnDestroy()
		{
			if (_loadout != null) _loadout.SlotsChanged -= Refresh;
		}

		private ItemInstance ItemAt(int slot) => _loadout != null ? _loadout.GetItem(slot) : null;

		private void Refresh()
		{
			for (int i = 0; i < slots.Length; i++)
			{
				Image icon = slots[i]?.icon;
				if (icon == null) continue;

				Sprite sprite = ItemAt(i)?.Data?.icon;
				icon.sprite = sprite;
				icon.preserveAspect = true;
				icon.enabled = sprite != null;
			}
			_shownLabelSlot = -2; // подписи в центре — заново
		}

		// ---------- Каждый кадр: подсветка, отметка оружия в руках, подписи ----------

		private void LateUpdate()
		{
			// если курсор над колесом во время перетаскивания — подсвечиваем место броска
			int dropSlot = -1;
			bool dropValid = false;
			if (ItemDrag.IsActive && !_directionMode)
			{
				dropSlot = SlotAt(ItemDrag.Position, ItemDrag.EventCamera);
				dropValid = dropSlot >= 0 && CanAcceptDrop != null && CanAcceptDrop(dropSlot);
			}

			int selected = _loadout != null ? _loadout.SelectedSlot : -1;
			for (int i = 0; i < slots.Length; i++)
			{
				SlotView slot = slots[i];
				if (slot == null) continue;

				if (slot.highlight != null)
				{
					bool isDrop = i == dropSlot;
					bool on = isDrop || (i == _hoverSlot && dropSlot < 0);
					if (slot.highlight.enabled != on) slot.highlight.enabled = on;
					if (on) slot.highlight.color = isDrop ? (dropValid ? dropValidColor : dropInvalidColor) : hoverColor;
				}

				if (slot.selectedMarker != null)
				{
					bool on = i == selected && ItemAt(i) != null;
					if (slot.selectedMarker.activeSelf != on) slot.selectedMarker.SetActive(on);
				}
			}

			int labelSlot = dropSlot >= 0 ? dropSlot : _hoverSlot;
			if (labelSlot != _shownLabelSlot) ShowLabels(labelSlot);
		}

		private void ShowLabels(int slot)
		{
			_shownLabelSlot = slot;
			bool show = slot >= 0;

			if (nameText != null)
			{
				nameText.gameObject.SetActive(show);
				if (show)
				{
					ItemInstance item = ItemAt(slot);
					LocalizedTextBinding.Bind(nameText, item?.Data != null ? item.Data.itemName : emptyLabel);
				}
			}

			if (hotkeyText != null)
			{
				hotkeyText.gameObject.SetActive(show);
				if (show)
				{
					// клавиша — из реального биндинга (после переназначения в настройках подпись обновится сама)
					string key = InteractKey.GetDisplayName($"Weapon{slot + 1}", (slot + 1).ToString());
					LocalizedTextBinding.Bind(hotkeyText, hotkeyLabel, format => SafeFormat(format, key));
				}
			}
		}

		private static string SafeFormat(string format, string value)
		{
			try { return string.Format(format, value); }
			catch (FormatException) { return format; }
		}

		// ---------- Геометрия ----------

		// сектор под точкой экрана; -1 — мимо кольца
		public int SlotAt(Vector2 screenPoint, Camera eventCamera)
		{
			RectTransform rect = (RectTransform)transform;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out Vector2 local)) return -1;

			Vector2 fromCenter = local - rect.rect.center;
			float distance = fromCenter.magnitude;
			if (distance < innerRadius || distance > outerRadius) return -1;
			return SlotFromDirection(fromCenter);
		}

		// сектор по направлению от центра (вверх — сектор 0, дальше по часовой стрелке); -1 — нулевое направление
		public int SlotFromDirection(Vector2 direction)
		{
			if (slots.Length == 0 || direction.sqrMagnitude < 0.0001f) return -1;

			// угол от вертикали по часовой стрелке, 0..360
			float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
			float sector = 360f / slots.Length;
			float shifted = Mathf.Repeat(angle + sector * 0.5f, 360f);
			return Mathf.Clamp(Mathf.FloorToInt(shifted / sector), 0, slots.Length - 1);
		}

		// быстрый выбор: сектор задаётся направлением, курсор над колесом не учитывается. null — режим выключен
		public void SetDirection(Vector2? direction, float deadZone)
		{
			_directionMode = direction.HasValue;
			if (!direction.HasValue)
			{
				SetHover(-1);
				return;
			}
			SetHover(direction.Value.magnitude >= deadZone ? SlotFromDirection(direction.Value) : -1);
		}

		private void SetHover(int slot)
		{
			if (_hoverSlot == slot) return;
			_hoverSlot = slot;
			if (slot >= 0) HoverChanged?.Invoke(slot);
		}

		// ---------- Мышь (экран инвентаря) ----------

		public void OnPointerMove(PointerEventData eventData)
		{
			if (!_directionMode) SetHover(SlotAt(eventData.position, eventData.enterEventCamera));
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			if (!_directionMode) SetHover(-1);
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (_directionMode || eventData.dragging || eventData.button != PointerEventData.InputButton.Right) return;

			int slot = SlotAt(eventData.position, eventData.pressEventCamera);
			if (slot >= 0) SlotRightClicked?.Invoke(slot, eventData);
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			int slot = eventData.button == PointerEventData.InputButton.Left && !_directionMode
				? SlotAt(eventData.position, eventData.pressEventCamera)
				: -1;
			ItemInstance item = ItemAt(slot);
			if (item == null)
			{
				eventData.pointerDrag = null;
				return;
			}

			// предмет держат за середину — так он ляжет в сетку там, где его видно
			Vector2Int grabCell = new Vector2Int(item.GridSize.x / 2, item.GridSize.y / 2);
			ItemDrag.Begin(item, null, slot, grabCell, eventData, slots[slot].icon);
			_dragSlot = slot;
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (_dragSlot >= 0) ItemDrag.Move(eventData);
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (_dragSlot < 0) return;

			int slot = _dragSlot;
			_dragSlot = -1;
			if (ItemDrag.End(eventData)) SlotDroppedOutside?.Invoke(slot);
		}

		public void OnDrop(PointerEventData eventData)
		{
			if (!ItemDrag.IsActive || _directionMode) return;

			// бросили над колесом — не выброс в мир, даже если мимо секторов (в центр) или положить нельзя
			int slot = SlotAt(eventData.position, eventData.pressEventCamera);
			bool placed = slot >= 0 && CanAcceptDrop != null && CanAcceptDrop(slot) && AcceptDrop != null && AcceptDrop(slot);
			ItemDrag.Resolve(placed);
		}
	}
}
