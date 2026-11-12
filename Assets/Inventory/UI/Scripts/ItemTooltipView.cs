using System;
using System.Collections.Generic;
using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Inventory
{
	// Подсказка о предмете под курсором: название, описание (ItemData → Description) и характеристики, которые
	// считаются сами из данных:
	//   - оружие — сколько заряжено (X/Y) и урон;
	//   - лечащий предмет — сколько лечит;
	//   - патроны — к какому оружию из того, что есть у игрока, они подходят.
	// Когда показывать, решает экран инвентаря (InventoryUI) — здесь только содержимое и положение у курсора.
	//
	// Вёрстка (внутри Panel инвентаря, после окон; объект изначально выключен):
	//   Tooltip        (этот компонент; рамка: Image, Vertical Layout Group + Content Size Fitter: Preferred/Preferred)
	//   ├ Title        (TextMeshPro — название)
	//   ├ Description  (TextMeshPro с переносом строк; Layout Element → Preferred Width — ширина подсказки)
	//   └ Stats        (Vertical Layout Group) — сюда спавнятся строки характеристик (Stat Line Prefab)
	// Raycast Target у всех картинок и текстов подсказки выключи — иначе она перехватит наведение у предмета под ней.
	[RequireComponent(typeof(RectTransform))]
	public class ItemTooltipView : MonoBehaviour
	{
		[SerializeField] private TMP_Text titleText;
		[Tooltip("Описание предмета. Пустое описание — строка прячется")]
		[SerializeField] private TMP_Text descriptionText;
		[Tooltip("Куда спавнятся строки характеристик (Vertical Layout Group). Нет характеристик — прячется")]
		[SerializeField] private RectTransform statsRoot;
		[Tooltip("Префаб одной строки характеристики — TextMeshPro (Raycast Target ✘)")]
		[SerializeField] private TMP_Text statLinePrefab;
		[Tooltip("Сдвиг верхнего левого угла подсказки от курсора, пиксели интерфейса (x вправо, y вверх)")]
		[SerializeField] private Vector2 pointerOffset = new Vector2(16f, -16f);

		[Header("Stat Labels")]
		[Tooltip("{0} — сколько заряжено, {1} — вместимость магазина")]
		[SerializeField] private LocalizedString loadedLabel = new LocalizedString("UI", "inventory.tooltip.loaded");
		[Tooltip("{0} — урон")]
		[SerializeField] private LocalizedString damageLabel = new LocalizedString("UI", "inventory.tooltip.damage");
		[Tooltip("{0} — сколько лечит")]
		[SerializeField] private LocalizedString healLabel = new LocalizedString("UI", "inventory.tooltip.heal");
		[Tooltip("{0} — оружие игрока, к которому подходят эти патроны (через запятую)")]
		[SerializeField] private LocalizedString ammoForLabel = new LocalizedString("UI", "inventory.tooltip.ammoFor");

		private readonly List<TMP_Text> _lines = new List<TMP_Text>();
		private ItemInstance _item;

		public bool IsShown => gameObject.activeSelf;

		// Показать подсказку о предмете (тот же — только двигается за курсором). holder — инвентарь игрока: по нему
		// ищется оружие, к которому подходят патроны; null — эта строка не показывается
		public void Show(ItemInstance item, InventoryHolder holder)
		{
			if (item?.Data == null)
			{
				Hide();
				return;
			}

			if (!gameObject.activeSelf) gameObject.SetActive(true);
			if (item != _item)
			{
				_item = item;
				Build(item, holder);
			}
			FollowPointer();
		}

		public void Hide()
		{
			_item = null;
			if (gameObject.activeSelf) gameObject.SetActive(false);
		}

		private void LateUpdate() => FollowPointer();

		private void FollowPointer()
		{
			if (Pointer.current == null) return;

			RectTransform area = transform.parent as RectTransform;
			PopupPlacement.PlaceAt(area, (RectTransform)transform, Pointer.current.position.ReadValue(),
				PopupPlacement.CanvasCamera(this), pointerOffset);
		}

		// ---------- Содержимое ----------

		private void Build(ItemInstance item, InventoryHolder holder)
		{
			if (titleText != null) LocalizedTextBinding.Bind(titleText, item.Data.itemName);

			if (descriptionText != null)
			{
				bool hasDescription = item.Data.description != null && !item.Data.description.IsEmpty;
				descriptionText.gameObject.SetActive(hasDescription);
				if (hasDescription) LocalizedTextBinding.Bind(descriptionText, item.Data.description);
			}

			ClearLines();
			AddStats(item, holder);
			if (statsRoot != null) statsRoot.gameObject.SetActive(_lines.Count > 0);

			// размер рамки нужен сразу — по нему подсказка прижимается к краю экрана
			LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
		}

		private void AddStats(ItemInstance item, InventoryHolder holder)
		{
			if (item.Weapon != null)
			{
				int loaded = item.Weapon.CurrentAmmo;
				int magazine = item.Weapon.MagazineSize;
				if (magazine > 0) AddLine(loadedLabel, format => SafeFormat(format, loaded, magazine));
				if (item.Weapon.Data != null)
				{
					int damage = item.Weapon.Data.Damage;
					AddLine(damageLabel, format => SafeFormat(format, damage));
				}
			}

			if (item.Data is HealingItemData healing)
			{
				AddLine(healLabel, format => SafeFormat(format, healing.healAmount));
			}

			if (item.Data is AmmoItemData ammo && holder != null)
			{
				List<ItemData> weapons = FindWeaponsFor(ammo, holder);
				// названия берём при каждом обновлении текста — после смены языка они тоже переведутся
				if (weapons.Count > 0) AddLine(ammoForLabel, format => SafeFormat(format, JoinNames(weapons)));
			}
		}

		// оружие игрока (колесо и сетка), которому подходят эти патроны — каждое название один раз
		private static List<ItemData> FindWeaponsFor(AmmoItemData ammo, InventoryHolder holder)
		{
			List<ItemData> result = new List<ItemData>();
			if (string.IsNullOrEmpty(ammo.ammoId)) return result;

			void Check(ItemInstance weapon)
			{
				string id = weapon?.Weapon?.Data?.Ammo?.AmmoId;
				if (id == ammo.ammoId && !result.Contains(weapon.Data)) result.Add(weapon.Data);
			}

			WeaponLoadout loadout = holder.Loadout;
			if (loadout != null)
			{
				for (int i = 0; i < loadout.SlotCount; i++) Check(loadout.GetItem(i));
			}
			foreach (GridItem item in holder.Grid.Items) Check(item.Instance);
			return result;
		}

		private static string JoinNames(List<ItemData> items)
		{
			List<string> names = new List<string>(items.Count);
			foreach (ItemData data in items)
			{
				names.Add(data.itemName != null && !data.itemName.IsEmpty ? data.itemName.GetLocalizedString() : data.name);
			}
			return string.Join(", ", names);
		}

		private void AddLine(LocalizedString label, Func<string, string> format)
		{
			if (statsRoot == null || statLinePrefab == null || label == null || label.IsEmpty) return;

			TMP_Text line = Instantiate(statLinePrefab, statsRoot);
			line.gameObject.SetActive(true);
			line.raycastTarget = false;
			LocalizedTextBinding.Bind(line, label, format);
			_lines.Add(line);
		}

		private void ClearLines()
		{
			foreach (TMP_Text line in _lines)
			{
				if (line == null) continue;
				// Destroy срабатывает в конце кадра — выключаем сразу, чтобы старые строки не попали в раскладку
				line.gameObject.SetActive(false);
				Destroy(line.gameObject);
			}
			_lines.Clear();
		}

		private static string SafeFormat(string format, params object[] values)
		{
			try { return string.Format(format, values); }
			catch (FormatException) { return format; }
		}
	}
}
