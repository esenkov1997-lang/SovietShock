using System;
using System.Collections.Generic;
using Interactables;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using Weapons;

namespace Inventory
{
	// Контейнер в мире: ящик, шкаф, сейф, труп врага. По кнопке взаимодействия открывает экран инвентаря с окном лута
	// (InventoryUI) — слева содержимое контейнера, справа инвентарь игрока.
	//
	// Размер сетки у всех контейнеров один — его задаёт экран инвентаря (InventoryUI → Loot Grid Size), поэтому сетка
	// создаётся при первом открытии, и тогда же в неё раскладывается Starting Items. Дальше содержимое живёт в
	// контейнере: забрал половину, отошёл, вернулся — лежит то, что оставил.
	//
	// Вешается на корень объекта. Взаимодействие ищет компонент вверх от коллайдера, в который попал луч, поэтому
	// у трупа подойдут и коллайдеры костей ragdoll. Труп: Require Dead = Health врага — пока враг жив, контейнера
	// "нет" (ни подсказки, ни взаимодействия).
	public class LootContainer : MonoBehaviour, IInteractable
	{
		[Tooltip("Название в заголовке окна лута (\"Ящик\", \"Собака\"). Пусто — общее название из InventoryUI")]
		[SerializeField] private LocalizedString title = new LocalizedString();
		[Tooltip("Подсказка при наведении: ящик — \"Открыть\" (prompt.loot.open), труп — \"Обыскать\" (prompt.loot.search)")]
		[SerializeField] private LocalizedString prompt = new LocalizedString("UI", "prompt.loot.open");
		[Tooltip("Что лежит внутри. x, y — верхняя левая клетка; не помещается туда — в первое свободное место")]
		[SerializeField] private List<ItemStack> startingItems = new List<ItemStack>();

		[Header("Corpse")]
		[Tooltip("Необязательно: Health врага — обыскать можно только после его смерти")]
		[SerializeField] private Health requireDead;

		[Header("Events")]
		[Tooltip("Окно лута открылось — звук, анимация крышки")]
		[SerializeField] private UnityEvent onOpened = new UnityEvent();
		[Tooltip("Окно лута закрылось")]
		[SerializeField] private UnityEvent onClosed = new UnityEvent();

		// Игрок попросил открыть контейнер — экран инвентаря этого игрока открывает окно лута.
		// interactor — корень игрока (как в IInteractable.Interact)
		public static event Action<LootContainer, GameObject> OpenRequested;

		private ItemGrid _grid;
		private Collider[] _colliders;

		public LocalizedString Title => title;
		// null — контейнер ещё ни разу не открывали
		public ItemGrid Grid => _grid;
		public bool IsOpen { get; private set; }

		// можно ли сейчас обыскивать: объект включён и (для трупа) враг мёртв
		public bool IsAvailable => isActiveAndEnabled && (requireDead == null || requireDead.IsDead);

		public LocalizedString InteractionPrompt => IsAvailable ? prompt : null;

		public bool CanInteractFrom(Vector3 viewerPosition) => IsAvailable;

		public void Interact(GameObject interactor)
		{
			if (IsAvailable) OpenRequested?.Invoke(this, interactor);
		}

		// Сетка контейнера; при первом обращении создаётся размером size и заполняется Starting Items
		public ItemGrid GetOrCreateGrid(Vector2Int size)
		{
			if (_grid != null) return _grid;

			_grid = new ItemGrid(size.x, size.y);
			foreach (ItemStack stack in startingItems)
			{
				if (stack.item == null || stack.count <= 0) continue;

				ItemInstance instance = new ItemInstance(stack.item, stack.count);
				if (_grid.TryPlace(instance, new Vector2Int(stack.x, stack.y)) || _grid.TryAdd(stack.item, stack.count)) continue;

				Debug.LogWarning($"{nameof(LootContainer)} на {name}: {stack.item.name} ×{stack.count} не поместился в сетку " +
					$"{size.x}×{size.y} (InventoryUI → Loot Grid Size)", this);
			}
			return _grid;
		}

		// Расстояние от точки до ближайшей точки контейнера (его коллайдеров) — экран закрывает лут, когда игрок
		// отошёл. Считается до коллайдеров, а не до центра объекта: и большой шкаф, и разлетевшийся ragdoll
		// "рядом", пока рядом хоть какая-то их часть
		public float DistanceTo(Vector3 point)
		{
			if (_colliders == null || _colliders.Length == 0) _colliders = GetComponentsInChildren<Collider>();

			float best = float.MaxValue;
			foreach (Collider collider in _colliders)
			{
				if (collider == null || !collider.enabled || collider.isTrigger) continue;
				best = Mathf.Min(best, Vector3.Distance(point, collider.bounds.ClosestPoint(point)));
			}
			return best < float.MaxValue ? best : Vector3.Distance(point, transform.position);
		}

		internal void NotifyOpened()
		{
			if (IsOpen) return;
			IsOpen = true;
			onOpened.Invoke();
		}

		internal void NotifyClosed()
		{
			if (!IsOpen) return;
			IsOpen = false;
			onClosed.Invoke();
		}
	}
}
